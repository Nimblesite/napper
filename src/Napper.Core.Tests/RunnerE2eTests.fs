module RunnerE2eTests

open System
open System.IO
open Xunit
open Napper.Core

let private createTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), $"nap-runner-e2e-{Guid.NewGuid():N}")
    Directory.CreateDirectory(dir) |> ignore
    dir

let private cleanupDir (dir: string) =
    if Directory.Exists(dir) then
        Directory.Delete(dir, true)

let private writeNapFile (dir: string) (name: string) (content: string) : string =
    let filePath = Path.Combine(dir, name)
    File.WriteAllText(filePath, content)
    filePath

// ─── runNapFile: successful GET with assertions ──────────────

[<Fact>]
let ``runNapFile GET with assertions passes`` () =
    let dir = createTempDir ()

    try
        let nap = "GET " + LocalHttpServer.baseUrl + "/get"
        let filePath = writeNapFile dir "test.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, "Simple GET should pass")
        Assert.True(result.Response.IsSome, "Must have response")
        Assert.Equal(200, result.Response.Value.StatusCode)
        Assert.True(result.Error.IsNone)
    finally
        cleanupDir dir

[<Fact>]
let ``runNapFile full format GET with assertions`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\nbody.url exists"

        let filePath = writeNapFile dir "full.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, "Full format GET should pass")
        Assert.True(result.Assertions.Length >= 2, $"Must have 2+ assertions, got {result.Assertions.Length}")
        Assert.True(result.Assertions |> List.forall (fun a -> a.Passed))
    finally
        cleanupDir dir

// ─── runNapFile: POST with body ──────────────────────────────

[<Fact>]
let ``runNapFile POST with body`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = POST\nurl = "
            + LocalHttpServer.baseUrl
            + "/post\n\n[request.headers]\nContent-Type = application/json\n\n[request.body]\ncontent-type = application/json\n\"\"\"\n{\"key\": \"value\"}\n\"\"\"\n\n[assert]\nstatus = 200"

        let filePath = writeNapFile dir "post.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"POST should pass. Error: {result.Error}")
        Assert.Equal(200, result.Response.Value.StatusCode)
    finally
        cleanupDir dir

// ─── runNapFile: assertion failure ───────────────────────────

[<Fact>]
let ``runNapFile wrong status assertion fails`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 404"

        let filePath = writeNapFile dir "fail.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.False(result.Passed, "Wrong status should fail")
        Assert.True(result.Assertions.Length >= 1, "Must have assertions")
        Assert.False(result.Assertions[0].Passed)
        Assert.Equal("404", result.Assertions[0].Expected)
        Assert.Equal("200", result.Assertions[0].Actual)
    finally
        cleanupDir dir

// ─── runNapFile: variable substitution ───────────────────────

[<Fact>]
let ``runNapFile substitutes CLI variables`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/status/{{code}}\n\n[assert]\nstatus = {{code}}"

        let filePath = writeNapFile dir "vars.nap" nap
        let vars = Map.ofList [ "code", "200" ]
        let result = Runner.runNapFile filePath vars None |> Async.RunSynchronously
        Assert.True(result.Passed, $"Var substitution should work. Error: {result.Error}")
    finally
        cleanupDir dir

// ─── runNapFile: parse error ─────────────────────────────────

[<Fact>]
let ``runNapFile parse error returns error result`` () =
    let dir = createTempDir ()

    try
        let filePath = writeNapFile dir "bad.nap" "[meta]\nname = test\n"
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome, "Must have error")
    finally
        cleanupDir dir

// ─── runNapFile: request failure ─────────────────────────────

[<Fact>]
let ``runNapFile unreachable URL returns error`` () =
    let dir = createTempDir ()

    try
        let nap = "GET https://this-domain-does-not-exist-napper-test.invalid/api"
        let filePath = writeNapFile dir "bad-url.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
        Assert.Contains("Request failed", result.Error.Value)
    finally
        cleanupDir dir

// ─── runNapFile: environment loading ─────────────────────────

[<Fact>]
let ``runNapFile loads vars from napenv`` () =
    let dir = createTempDir ()

    try
        File.WriteAllText(Path.Combine(dir, ".napenv"), "code = 200\n")

        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/status/{{code}}\n\n[assert]\nstatus = {{code}}"

        let filePath = writeNapFile dir "env.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"napenv vars should resolve. Error: {result.Error}")
    finally
        cleanupDir dir

// ─── runNapFile: header contains assertion ───────────────────

[<Fact>]
let ``runNapFile contains assertion on header`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\nheaders.Content-Type contains json"

        let filePath = writeNapFile dir "hdr.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"Header contains should pass. Error: {result.Error}")
    finally
        cleanupDir dir

// ─── runNapFile: duration assertion ──────────────────────────

[<Fact>]
let ``runNapFile duration less than assertion`` () =
    let dir = createTempDir ()

    try
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\nduration < 30000ms"

        let filePath = writeNapFile dir "dur.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"Duration should pass. Error: {result.Error}")
    finally
        cleanupDir dir

// ─── evaluateAssertions: all operators ───────────────────────

[<Fact>]
let ``evaluateAssertions covers all assertion operators`` () =
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.ofList [ "Content-Type", "application/json" ]
          Body = "{\"id\": 42, \"name\": \"test\", \"active\": true, \"score\": null}"
          Duration = TimeSpan.FromMilliseconds(100.0) }

    let assertions =
        [ { Target = "status"; Op = Equals "200" }
          { Target = "body.id"; Op = Exists }
          { Target = "body.name"
            Op = Equals "test" }
          { Target = "body.active"
            Op = Equals "true" }
          { Target = "body.score"; Op = Exists }
          { Target = "headers.Content-Type"
            Op = Contains "json" }
          { Target = "duration"
            Op = LessThan "5000ms" }
          { Target = "duration"
            Op = GreaterThan "1ms" }
          { Target = "body.name"
            Op = Matches "t*t" }
          { Target = "body"; Op = Exists }
          { Target = "body"; Op = Contains "id" } ]

    let results = Runner.evaluateAssertions assertions response
    Assert.Equal(11, results.Length)

    for r in results do
        Assert.True(r.Passed, $"Assertion on {r.Assertion.Target} should pass: expected={r.Expected} actual={r.Actual}")

[<Fact>]
let ``evaluateAssertions missing targets all fail`` () =
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.empty
          Body = "{}"
          Duration = TimeSpan.FromMilliseconds(50.0) }

    let assertions =
        [ { Target = "body.nonexistent"
            Op = Exists }
          { Target = "body.missing"
            Op = Equals "value" }
          { Target = "headers.X-Missing"
            Op = Contains "x" }
          { Target = "body.nope"
            Op = Matches "abc" }
          { Target = "unknown_target"
            Op = Exists } ]

    let results = Runner.evaluateAssertions assertions response
    Assert.True(results |> List.forall (fun r -> not r.Passed))

[<Fact>]
let ``evaluateAssertions numeric comparison edge cases`` () =
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.empty
          Body = "{}"
          Duration = TimeSpan.FromMilliseconds(50.0) }

    let assertions =
        [ { Target = "duration"
            Op = LessThan "not-a-number" }
          { Target = "duration"
            Op = GreaterThan "999999ms" } ]

    let results = Runner.evaluateAssertions assertions response
    Assert.False(results[0].Passed, "LessThan with non-numeric should fail")
    Assert.False(results[1].Passed, "GreaterThan with huge value should fail")

// ─── evaluateAssertions: remaining branches ──────────────────

[<Fact>]
let ``evaluateAssertions descending into a non-object json path fails the target`` () =
    // body.a is a NUMBER, so body.a.b cannot resolve — the traversal must hit the
    // non-object arm and report the target missing (assertion fails, not throws).
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.empty
          Body = "{\"a\": 5}"
          Duration = TimeSpan.FromMilliseconds 10.0 }

    let results =
        Runner.evaluateAssertions [ { Target = "body.a.b"; Op = Exists } ] response

    Assert.Single(results) |> ignore
    Assert.False(results[0].Passed)

[<Fact>]
let ``evaluateAssertions glob match honours the question-mark wildcard`` () =
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.empty
          Body = "{\"name\": \"cat\"}"
          Duration = TimeSpan.FromMilliseconds 10.0 }

    let results =
        Runner.evaluateAssertions
            [ { Target = "body.name"
                Op = Matches "c?t" } // ? matches exactly one char
              { Target = "body.name"
                Op = Matches "c?" } ] // ? matches one, so "cat" must NOT match
            response

    Assert.True(results[0].Passed, "c?t should match cat")
    Assert.False(results[1].Passed, "c? should not match cat")

[<Fact>]
let ``evaluateAssertions numeric comparison on a missing target fails`` () =
    let response: NapResponse =
        { StatusCode = 200
          Headers = Map.empty
          Body = "{}"
          Duration = TimeSpan.FromMilliseconds 10.0 }

    let results =
        Runner.evaluateAssertions
            [ { Target = "body.absent"
                Op = LessThan "100" }
              { Target = "body.absent"
                Op = GreaterThan "1" } ]
            response

    Assert.False(results[0].Passed)
    Assert.False(results[1].Passed)

// ─── runScriptStep + [script] hook path (in-process) ─────────

[<Fact>]
let ``runScriptStep runs a script with the supplied variable scope`` () =
    let dir = createTempDir ()

    try
        let scriptPath = writeNapFile dir "step.fsx" "printfn \"step ran\""

        let result =
            Runner.runScriptStep (Map.ofList [ ("x", "1") ]) (Some "staging") scriptPath
            |> Async.RunSynchronously

        Assert.True(result.Passed, $"script step should pass. Error: {result.Error}")
        Assert.Contains("step ran", result.Log)
    finally
        cleanupDir dir

[<Fact>]
let ``runNapFile executes a passing post hook in-process`` () =
    let dir = createTempDir ()

    try
        writeNapFile
            dir
            "check.js"
            "if (ctx.response.status !== 200) ctx.fail('bad status');\nctx.log('post-ok ' + ctx.response.status);"
        |> ignore

        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\n\n[script]\npost = ./check.js\n"

        let filePath = writeNapFile dir "hook.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"post-hook nap should pass. Error: {result.Error}")
        Assert.Equal(200, result.Response.Value.StatusCode)
    finally
        cleanupDir dir

[<Fact>]
let ``runNapFile post hook can fail an otherwise-passing request in-process`` () =
    let dir = createTempDir ()

    try
        writeNapFile dir "reject.js" "ctx.fail('POSTHOOK-REJECT');" |> ignore

        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\n\n[script]\npost = ./reject.js\n"

        let filePath = writeNapFile dir "hook.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        // HTTP 200 and the assertion passes, but the post hook rejects -> overall fail.
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
    finally
        cleanupDir dir

// ─── runNapFile request building + pre hooks (in-process) ────

[<Fact>]
let ``runNapFile sends a non-content-type request header`` () =
    let dir = createTempDir ()

    try
        // The /headers endpoint echoes inbound headers; a custom header exercises the
        // TryAddWithoutValidation arm (Content-Type is special-cased and skipped there).
        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/headers\n\n[request.headers]\nX-Custom = abc123\n\n[assert]\nstatus = 200\nbody.headers.X-Custom contains abc123\n"

        let filePath = writeNapFile dir "hdr.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"custom header should round-trip. Error: {result.Error}")
        Assert.True(result.Assertions |> List.forall (fun a -> a.Passed))
    finally
        cleanupDir dir

[<Fact>]
let ``runNapFile runs a passing pre hook in-process`` () =
    let dir = createTempDir ()

    try
        writeNapFile dir "pre.js" "ctx.log('pre saw ' + ctx.request.method);" |> ignore

        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\n\n[script]\npre = ./pre.js\n"

        let filePath = writeNapFile dir "pre.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.True(result.Passed, $"pre-hook nap should pass. Error: {result.Error}")
        Assert.Equal(200, result.Response.Value.StatusCode)
    finally
        cleanupDir dir

[<Fact>]
let ``runNapFile pre hook failure short-circuits before the request`` () =
    let dir = createTempDir ()

    try
        writeNapFile dir "pre.js" "ctx.fail('PRE-REJECT-MARKER');" |> ignore

        let nap =
            "[request]\nmethod = GET\nurl = "
            + LocalHttpServer.baseUrl
            + "/get\n\n[assert]\nstatus = 200\n\n[script]\npre = ./pre.js\n"

        let filePath = writeNapFile dir "pre.nap" nap
        let result = Runner.runNapFile filePath Map.empty None |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
    finally
        cleanupDir dir

[<Fact>]
let ``runScript with an unsupported extension returns a dispatch error`` () =
    let dir = createTempDir ()

    try
        let scriptPath = writeNapFile dir "weird.xyz" "echo hi"
        let result = Runner.runScript scriptPath |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
    finally
        cleanupDir dir
