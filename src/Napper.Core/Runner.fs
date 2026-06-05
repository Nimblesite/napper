// Specs: cli-run, nap-assert, assert-status, assert-equals, assert-exists, assert-contains,
//        assert-matches, assert-lt, assert-gt, script-fsx, script-csx, script-dispatch,
//        env-interpolation, collection-folder, collection-sort, naplist-steps,
//        naplist-nap-step, naplist-folder-step, naplist-nested, naplist-script-step, naplist-var-scope
module Napper.Core.Runner

open System
open System.Diagnostics
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Napper.Core

let private httpClient = new HttpClient()

/// Execute an HTTP request from a resolved NapRequest
let executeRequest (request: NapRequest) : Async<NapResponse> =
    async {
        // .Name, not {request.Method}: interpolating the DU triggers reflective
        // structured-print (GetUnionFields) which aborts under NativeAOT.
        Logger.info $"HTTP {request.Method.Name} {request.Url}"
        Logger.debug $"Request headers: {request.Headers.Count} headers"
        let msg = new HttpRequestMessage(request.Method.ToNetMethod(), request.Url)

        // Add headers
        for kv in request.Headers do
            // Content headers need to go on the content object
            if kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) then
                ()
            else
                msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value) |> ignore

        // Add body if present
        match request.Body with
        | Some body -> msg.Content <- new StringContent(body.Content, Encoding.UTF8, body.ContentType)
        | None -> ()

        let sw = Stopwatch.StartNew()
        let! response = httpClient.SendAsync(msg) |> Async.AwaitTask
        sw.Stop()

        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        Logger.info $"HTTP {int response.StatusCode} in {sw.Elapsed.TotalMilliseconds:F0}ms"
        Logger.debug $"Response body length: {body.Length}"

        let headers =
            response.Headers
            |> Seq.append response.Content.Headers
            |> Seq.map (fun kv -> kv.Key, kv.Value |> String.concat ", ")
            |> Map.ofSeq

        return
            { StatusCode = int response.StatusCode
              Headers = headers
              Body = body
              Duration = sw.Elapsed }
    }

/// Walk a dot-delimited path into a JSON body and return the leaf value as a string.
/// e.g. tryGetJsonPath "user.name" body → Some "Alice"
/// Returns None if the path doesn't exist or the body isn't valid JSON.
let private tryGetJsonPath (path: string) (body: string) : string option =
    try
        let doc = JsonDocument.Parse(body)
        let parts = path.Split('.')
        let mutable current = doc.RootElement
        let mutable found = true

        for part in parts do
            if found then
                match current.ValueKind with
                | JsonValueKind.Object ->
                    match current.TryGetProperty(part) with
                    | true, prop -> current <- prop
                    | false, _ -> found <- false
                | _ -> found <- false

        if found then
            match current.ValueKind with
            | JsonValueKind.String -> Some(current.GetString())
            | JsonValueKind.Number -> Some(current.GetRawText())
            | JsonValueKind.True -> Some "true"
            | JsonValueKind.False -> Some "false"
            | JsonValueKind.Null -> Some "null"
            | _ -> Some(current.GetRawText())
        else
            None
    with _ ->
        None

/// Resolve an assertion target (e.g. "status", "body.id", "headers.Content-Type")
/// to the actual string value from the HTTP response.
/// Returns None when the target doesn't exist in the response.
let private resolveTarget (response: NapResponse) (target: string) : string option =
    if target = "status" then
        Some(string response.StatusCode)
    elif target = "duration" then
        // .ToString, not sprintf %f: F#'s %f path is reflection-based and aborts under NativeAOT.
        Some(response.Duration.TotalMilliseconds.ToString("F0") + "ms")
    elif target.StartsWith "headers." then
        let headerName = target.Substring(8)

        response.Headers
        |> Map.tryFind headerName
        |> Option.orElseWith (fun () ->
            response.Headers
            |> Map.tryPick (fun k v ->
                if k.Equals(headerName, StringComparison.OrdinalIgnoreCase) then
                    Some v
                else
                    None))
    elif target.StartsWith "body." then
        tryGetJsonPath (target.Substring(5)) response.Body
    elif target = "body" then
        Some response.Body
    else
        None

/// Parse a numeric value from a string, stripping a trailing "ms" duration suffix.
/// e.g. "500ms" → Some 500.0, "42" → Some 42.0, "abc" → None
let private parseNum (s: string) : float option =
    let s = s.TrimEnd('m', 's')

    match Double.TryParse(s) with
    | true, v -> Some v
    | _ -> None

/// Compare two numeric values (actual vs expected) using the given comparator.
/// Returns false if either value is missing or non-numeric.
let private compareNumeric (cmp: float -> float -> bool) (actual: string option) (expected: string) : bool =
    match actual with
    | Some a ->
        match parseNum a, parseNum expected with
        | Some av, Some ev -> cmp av ev
        | _ -> false
    | None -> false

/// Convert a glob pattern (using * and ? wildcards) to a regex and test a value against it.
let private globMatch (pattern: string) (value: string) : bool =
    let regexPattern =
        pattern.ToCharArray()
        |> Array.map (fun c ->
            match c with
            | '*' -> ".*"
            | '?' -> "."
            | c when ".+^${}()|[]\\".Contains(c) -> $"\\{c}"
            | c -> string c)
        |> String.concat ""

    Regex.IsMatch(value, $"^{regexPattern}$")

/// Build an AssertionResult from an assertion, its pass/fail state, and display strings.
let private makeResult
    (assertion: Assertion)
    (passed: bool)
    (expected: string)
    (actual: string option)
    : AssertionResult =
    { Assertion = assertion
      Passed = passed
      Expected = expected
      Actual = actual |> Option.defaultValue "<missing>" }

/// Evaluate a single assertion operator against the resolved actual value.
let private evaluateOp (assertion: Assertion) (actual: string option) : AssertionResult =
    match assertion.Op with
    | Equals expected ->
        let passed =
            actual |> Option.map (fun a -> a = expected) |> Option.defaultValue false

        makeResult assertion passed expected actual
    | Exists ->
        let passed = actual.IsSome

        { Assertion = assertion
          Passed = passed
          Expected = "exists"
          Actual = if actual.IsSome then "exists" else "<missing>" }
    | Contains expected ->
        let passed =
            actual
            |> Option.map (fun a -> a.Contains(expected, StringComparison.OrdinalIgnoreCase))
            |> Option.defaultValue false

        makeResult assertion passed $"contains \"{expected}\"" actual
    | Matches pattern ->
        let passed =
            actual |> Option.map (fun a -> globMatch pattern a) |> Option.defaultValue false

        { Assertion = assertion
          Passed = passed
          Expected = $"matches \"{pattern}\""
          Actual = actual |> Option.defaultValue "<missing>" }
    | LessThan expected -> makeResult assertion (compareNumeric (<) actual expected) $"< {expected}" actual
    | GreaterThan expected -> makeResult assertion (compareNumeric (>) actual expected) $"> {expected}" actual

/// Evaluate all assertions against an HTTP response.
/// Each assertion's target is resolved to the actual response value,
/// then the operator (=, exists, contains, matches, <, >) is applied.
let evaluateAssertions (assertions: Assertion list) (response: NapResponse) : AssertionResult list =
    assertions
    |> List.map (fun assertion -> resolveTarget response assertion.Target |> evaluateOp assertion)

/// An empty NapResult skeleton for a script invocation (scripts have no HTTP request/response of their own).
let private scriptBaseResult (scriptPath: string) : NapResult =
    { File = scriptPath
      Request =
        { Method = GET
          Url = ""
          Headers = Map.empty
          Body = None }
      Response = None
      Assertions = []
      Passed = false
      Error = None
      Log = []
      SetVars = Map.empty }

let private splitStdout (stdout: string) : string list =
    stdout.Split('\n')
    |> Array.map (fun l -> l.TrimEnd('\r'))
    |> Array.filter (fun l -> l.Length > 0)
    |> Array.toList

/// Run a script (.fsx, .csx, .js, .mjs, .cjs, .py) with the [script-protocol] context injected.
/// Dispatch lives in [script-dispatch]; the `ctx` object is wired in by [script-sdk] (ScriptContext).
/// A script fails when it exits non-zero OR calls ctx.fail; its ctx.log lines and ctx.set vars
/// are merged into the result. Implements [script-context], [script-protocol-in], [script-protocol-out].
let private runScriptInternal (inv: ScriptContext.Invocation) (scriptPath: string) : Async<NapResult> =
    async {
        Logger.info $"Script start: {scriptPath}"
        let baseResult = scriptBaseResult scriptPath

        match ScriptDispatch.resolve scriptPath with
        | Error msg ->
            Logger.error $"Script dispatch failed: {msg}"
            return { baseResult with Error = Some msg }
        | Ok(exe, args) ->
            let plan = ScriptContext.prepare inv scriptPath exe args
            let psi = ProcessStartInfo()
            psi.FileName <- plan.Exe
            psi.Arguments <- plan.Args
            psi.WorkingDirectory <- System.IO.Path.GetDirectoryName(scriptPath)
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true

            for k, v in plan.Env do
                psi.Environment[k] <- v

            try
                use proc = Process.Start(psi)
                let! stdout = proc.StandardOutput.ReadToEndAsync() |> Async.AwaitTask
                let! stderr = proc.StandardError.ReadToEndAsync() |> Async.AwaitTask
                do! proc.WaitForExitAsync() |> Async.AwaitTask
                let directives = ScriptContext.readDirectives plan.ResultPath
                ScriptContext.cleanup plan
                Logger.info $"Script exit code: {proc.ExitCode}; ctx.failed={directives.Failed}"
                let passed = proc.ExitCode = 0 && not directives.Failed

                let error =
                    if directives.Failed then
                        directives.FailMessage |> Option.orElse (Some "Script called ctx.fail")
                    elif proc.ExitCode = 0 then
                        None
                    elif stderr.Length > 0 then
                        Some stderr
                    else
                        Some $"Script exited with code {proc.ExitCode}"

                return
                    { baseResult with
                        Passed = passed
                        Error = error
                        Log = splitStdout stdout @ directives.Logs
                        SetVars = directives.Vars }
            with ex ->
                ScriptContext.cleanup plan
                Logger.error $"Script failed: {ex.Message}"

                return
                    { baseResult with
                        Error = Some $"Could not start runtime '{plan.Exe}' for {scriptPath}: {ex.Message}" }
    }

/// Run a standalone script with no surrounding context (back-compat entry point).
let runScript (scriptPath: string) : Async<NapResult> =
    runScriptInternal (ScriptContext.stepInvocation Map.empty None) scriptPath

/// Run a naplist script step, handing it the current variable scope and environment as `ctx`.
let runScriptStep (vars: Map<string, string>) (env: string option) (scriptPath: string) : Async<NapResult> =
    runScriptInternal (ScriptContext.stepInvocation vars env) scriptPath

/// Right-biased map merge: every key in `b` overrides `a`. Used to fold ctx.set vars forward.
let mergeVars (a: Map<string, string>) (b: Map<string, string>) : Map<string, string> =
    Map.fold (fun acc k v -> Map.add k v acc) a b

/// Run a `.nap` file's [script] pre/post hook, handing it the request (and, for post, the response) as `ctx`.
/// Implements [script-pre], [script-post].
let private runHook
    (phase: ScriptContext.Phase)
    (dir: string)
    (scriptRel: string)
    (vars: Map<string, string>)
    (env: string option)
    (request: NapRequest)
    (response: NapResponse option)
    : Async<NapResult> =
    runScriptInternal
        { Phase = phase
          Vars = vars
          Env = env
          Request = Some request
          Response = response }
        (System.IO.Path.Combine(dir, scriptRel))

/// Send the request, evaluate assertions, then run the post hook. The step passes only when the
/// assertions pass AND the post hook passes; ctx.log/ctx.set from both hooks are folded in.
let private executeWithPost
    (filePath: string)
    (dir: string)
    (envName: string option)
    (postRel: string option)
    (preLogs: string list)
    (preVars: Map<string, string>)
    (vars: Map<string, string>)
    (resolved: NapFile)
    : Async<NapResult> =
    async {
        let! response = executeRequest resolved.Request
        let assertionResults = evaluateAssertions resolved.Assertions response
        let assertionsPassed = assertionResults |> List.forall (fun r -> r.Passed)

        Logger.info
            $"Assertions: {assertionResults |> List.filter (fun r -> r.Passed) |> List.length}/{assertionResults.Length} passed"

        let! post =
            match postRel with
            | Some rel ->
                async {
                    let! p = runHook ScriptContext.Post dir rel vars envName resolved.Request (Some response)
                    return Some p
                }
            | None -> async { return None }

        let postPassed = post |> Option.forall (fun p -> p.Passed)
        let postLogs = post |> Option.map (fun p -> p.Log) |> Option.defaultValue []

        let postVars =
            post |> Option.map (fun p -> p.SetVars) |> Option.defaultValue Map.empty

        let postError = post |> Option.bind (fun p -> if p.Passed then None else p.Error)

        return
            { File = filePath
              Request = resolved.Request
              Response = Some response
              Assertions = assertionResults
              Passed = assertionsPassed && postPassed
              Error = postError
              Log = preLogs @ postLogs
              SetVars = mergeVars preVars postVars }
    }

/// Run a single .nap file end-to-end (optional [script] pre/post hooks included).
let runNapFile (filePath: string) (vars: Map<string, string>) (envName: string option) : Async<NapResult> =
    async {
        Logger.info $"File: {filePath}"
        let dir = System.IO.Path.GetDirectoryName(filePath)
        let content = System.IO.File.ReadAllText(filePath)

        match Parser.parseNapFile content with
        | Error msg ->
            Logger.error $"Parse error in {filePath}: {msg}"

            return
                { File = filePath
                  Request =
                    { Method = GET
                      Url = ""
                      Headers = Map.empty
                      Body = None }
                  Response = None
                  Assertions = []
                  Passed = false
                  Error = Some $"Parse error: {msg}"
                  Log = []
                  SetVars = Map.empty }
        | Ok napFile ->
            let allVars = Environment.loadEnvironment dir envName vars napFile.Vars
            Logger.debug $"Resolved {allVars.Count} variables"

            // PRE hook (sees the about-to-be-sent request). Its ctx.set vars feed the request.
            let! pre =
                match napFile.Script.Pre with
                | Some rel ->
                    async {
                        let req = (Environment.resolveNapFile allVars napFile).Request
                        let! p = runHook ScriptContext.Pre dir rel allVars envName req None
                        return Some p
                    }
                | None -> async { return None }

            match pre with
            | Some p when not p.Passed ->
                Logger.error $"Pre-script failed for {filePath}"

                return
                    { (scriptBaseResult filePath) with
                        File = filePath
                        Error = p.Error
                        Log = p.Log
                        SetVars = p.SetVars }
            | _ ->
                let preVars =
                    pre |> Option.map (fun p -> p.SetVars) |> Option.defaultValue Map.empty

                let preLogs = pre |> Option.map (fun p -> p.Log) |> Option.defaultValue []
                let mergedVars = mergeVars allVars preVars
                let resolved = Environment.resolveNapFile mergedVars napFile

                try
                    return! executeWithPost filePath dir envName napFile.Script.Post preLogs preVars mergedVars resolved
                with ex ->
                    Logger.error $"Request failed: {ex.Message}"

                    return
                        { File = filePath
                          Request = resolved.Request
                          Response = None
                          Assertions = []
                          Passed = false
                          Error = Some $"Request failed: {ex.Message}"
                          Log = preLogs
                          SetVars = preVars }
    }
