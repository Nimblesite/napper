module ScriptContextTests
// In-process unit tests for [SCRIPT-PROTOCOL-IN], [SCRIPT-PROTOCOL-OUT], [SCRIPT-SDK].
// The subprocess ctx tests in CtxScriptTests PROVE the end-to-end behaviour, but coverage
// only instruments the in-process test host — the library code the CLI subprocess runs is
// not measured. These call ScriptContext directly so the protocol writer/reader and the
// launch-plan builder are actually exercised (and asserted) in-process.

open System
open System.IO
open System.Text.Json
open Xunit
open Napper.Core
open Napper.Core.ScriptContext

let private sampleRequest: NapRequest =
    { Method = POST
      Url = "https://example.com/things"
      Headers = Map.ofList [ ("Accept", "application/json") ]
      Body =
        Some
            { ContentType = "application/json"
              Content = "{\"a\":1}" } }

let private sampleResponse: NapResponse =
    { StatusCode = 201
      Headers = Map.ofList [ ("Content-Type", "application/json") ]
      Body = "{\"id\":7}"
      Duration = TimeSpan.FromMilliseconds 123.0 }

let private ctxFileOf (plan: LaunchPlan) : string =
    plan.TempFiles |> List.find (fun f -> f.EndsWith "context.json")

let private readCtx (plan: LaunchPlan) : JsonElement =
    JsonDocument.Parse(File.ReadAllText(ctxFileOf plan)).RootElement

// ─── prepare: launch-plan + inbound context JSON ───────────── [SCRIPT-PROTOCOL-IN], [SCRIPT-SDK]

[<Fact>]
let ``prepare for a Step js script writes a step context and a require preload`` () =
    let inv = stepInvocation (Map.ofList [ ("marker", "M1") ]) (Some "staging")
    let plan = prepare inv "probe.js" "node" "probe.js"

    try
        Assert.Contains("--require", plan.Args)
        Assert.Equal(3, plan.TempFiles.Length)
        Assert.True(plan.Env |> List.exists (fun (k, _) -> k = EnvContext))
        Assert.True(plan.Env |> List.exists (fun (k, _) -> k = EnvResult))
        let ctx = readCtx plan
        Assert.Equal("step", ctx.GetProperty("phase").GetString())
        Assert.Equal("staging", ctx.GetProperty("env").GetString())
        Assert.Equal("M1", ctx.GetProperty("vars").GetProperty("marker").GetString())
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("request").ValueKind)
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("response").ValueKind)
    finally
        cleanup plan

    // cleanup must have removed every temp file it created.
    Assert.False(File.Exists(ctxFileOf plan))

[<Fact>]
let ``prepare for a Pre python hook embeds the request and uses a boot wrapper`` () =
    let inv =
        { Phase = Pre
          Vars = Map.empty
          Env = None
          Request = Some sampleRequest
          Response = None }

    let plan = prepare inv "hook.py" "python3" "hook.py"

    try
        Assert.DoesNotContain("--require", plan.Args)
        Assert.Contains("boot.py", plan.Args)
        let ctx = readCtx plan
        Assert.Equal("pre", ctx.GetProperty("phase").GetString())
        Assert.Equal("", ctx.GetProperty("env").GetString()) // None env serialises to ""
        let req = ctx.GetProperty("request")
        Assert.Equal("POST", req.GetProperty("method").GetString())
        Assert.Equal("https://example.com/things", req.GetProperty("url").GetString())
        Assert.Equal("application/json", req.GetProperty("headers").GetProperty("Accept").GetString())
        Assert.Equal("{\"a\":1}", req.GetProperty("body").GetString())
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("response").ValueKind)
    finally
        cleanup plan

[<Fact>]
let ``prepare for a Post hook embeds the response and a body-less request`` () =
    let inv =
        { Phase = Post
          Vars = Map.empty
          Env = Some "prod"
          Request = Some { sampleRequest with Body = None }
          Response = Some sampleResponse }

    let plan = prepare inv "check.js" "node" "check.js"

    try
        let ctx = readCtx plan
        Assert.Equal("post", ctx.GetProperty("phase").GetString())
        // Body = None must serialise the request body as JSON null.
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("request").GetProperty("body").ValueKind)
        let resp = ctx.GetProperty("response")
        Assert.Equal(201, resp.GetProperty("status").GetInt32())
        Assert.Equal("{\"id\":7}", resp.GetProperty("body").GetString())
        Assert.Equal("application/json", resp.GetProperty("headers").GetProperty("Content-Type").GetString())
        Assert.Equal(123L, resp.GetProperty("durationMs").GetInt64())
    finally
        cleanup plan

[<Fact>]
let ``prepare for a non js or py script passes args through with only context and result temps`` () =
    let plan =
        prepare (stepInvocation Map.empty None) "setup.csx" "dotnet-script" "setup.csx --flag"

    try
        Assert.Equal("setup.csx --flag", plan.Args) // no shim wrapping for other runtimes
        Assert.Equal(2, plan.TempFiles.Length)
        Assert.True(plan.Env |> List.exists (fun (k, _) -> k = EnvContext))
    finally
        cleanup plan

// ─── readDirectives: outbound directives JSON ──────────────── [SCRIPT-PROTOCOL-OUT]

let private writeTemp (content: string) : string =
    let path =
        Path.Combine(Path.GetTempPath(), "nap-dir-" + Guid.NewGuid().ToString("N") + ".json")

    File.WriteAllText(path, content)
    path

[<Fact>]
let ``readDirectives parses vars, failed, failMessage and logs`` () =
    let path =
        writeTemp
            "{\"vars\":{\"seededId\":\"7\",\"token\":\"abc\"},\"failed\":true,\"failMessage\":\"boom\",\"logs\":[\"a\",\"b\"]}"

    try
        let d = readDirectives path
        Assert.True(d.Failed)
        Assert.Equal(Some "boom", d.FailMessage)
        Assert.Equal("7", d.Vars["seededId"])
        Assert.Equal("abc", d.Vars["token"])
        Assert.Equal<string list>([ "a"; "b" ], d.Logs)
    finally
        File.Delete path

[<Fact>]
let ``readDirectives returns empty directives when the file is missing`` () =
    let d =
        readDirectives (Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".json"))

    Assert.False(d.Failed)
    Assert.True(d.Vars.IsEmpty)
    Assert.True(d.Logs.IsEmpty)
    Assert.Equal(None, d.FailMessage)

[<Fact>]
let ``readDirectives swallows malformed json and returns empty directives`` () =
    let path = writeTemp "{ this is not valid json ]"

    try
        let d = readDirectives path
        Assert.False(d.Failed)
        Assert.True(d.Vars.IsEmpty)
        Assert.True(d.Logs.IsEmpty)
    finally
        File.Delete path

[<Fact>]
let ``readDirectives tolerates a result with no vars and no failMessage`` () =
    let path = writeTemp "{\"failed\":false}"

    try
        let d = readDirectives path
        Assert.False(d.Failed)
        Assert.Equal(None, d.FailMessage)
        Assert.True(d.Vars.IsEmpty)
        Assert.True(d.Logs.IsEmpty)
    finally
        File.Delete path
