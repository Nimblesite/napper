module CtxScriptTests
// Tests [SCRIPT-CONTEXT], [SCRIPT-PROTOCOL], [SCRIPT-PROTOCOL-IN], [SCRIPT-PROTOCOL-OUT], [SCRIPT-PRE], [SCRIPT-POST], [SCRIPT-JS], [SCRIPT-PY], [NAPLIST-SCRIPT-STEP], [NAPLIST-VAR-SCOPE]
//
// BLACK-BOX, through the real napper CLI (TestHelpers.runCli shells out to the built binary).
// These tests encode the DOCUMENTED injected-`ctx` behaviour that had ZERO implementation:
//   - ctx.log(msg)          -> appears in the step's captured output
//   - ctx.fail(msg)         -> fails the step (exit 1) EVEN WHEN the process exits 0
//   - ctx.env / ctx.vars    -> readable inside the script
//   - ctx.response.*        -> readable inside a [script] post-hook
//   - ctx.set(k, v)         -> variable is visible to a DOWNSTREAM playlist step
// Each behaviour is proven for JavaScript (node) and Python (python3).

open System
open System.IO
open System.Text.Json
open Xunit

let private runCli args cwd =
    TestHelpers.runCliWithTimeout TestHelpers.ScriptTimeoutMs args cwd

let private createTempDir () =
    TestHelpers.createTempDir "nap-ctx-test"

let private cleanupDir dir = TestHelpers.cleanupDir dir

let private write (dir: string) (name: string) (content: string) =
    File.WriteAllText(Path.Combine(dir, name), content)

/// Parse a `--output json` document and return the first result element
/// (single results are objects; playlists are arrays — normalise to the element we want).
let private firstResult (stdout: string) : JsonElement =
    let root = JsonDocument.Parse(stdout).RootElement

    if root.ValueKind = JsonValueKind.Array then
        root[0]
    else
        root

let private resultAt (stdout: string) (index: int) : JsonElement =
    JsonDocument.Parse(stdout).RootElement[index]

let private logText (el: JsonElement) : string =
    // Tolerate error-shaped results that carry no "log" field: return "" so a Contains
    // assertion fails with the expected string rather than throwing KeyNotFoundException.
    match el.TryGetProperty("log") with
    | true, log when log.ValueKind = JsonValueKind.Array ->
        seq { for i in 0 .. log.GetArrayLength() - 1 -> log[i].GetString() }
        |> String.concat "\n"
    | _ -> ""

let private errorText (el: JsonElement) : string =
    match el.TryGetProperty("error") with
    | true, e -> e.GetString()
    | _ -> ""

// ─────────────────────────── ctx.log + ctx.env + ctx.vars (read) ───────────────────────────

[<Fact>]
let ``JS step can read ctx.env and ctx.vars and write ctx.log`` () =
    let dir = createTempDir ()

    try
        write dir "probe.js" "ctx.log('env=' + ctx.env + ' marker=' + ctx.vars.marker);"
        write dir "suite.naplist" "[steps]\nprobe.js\n"

        let exitCode, stdout, _ =
            runCli "run suite.naplist --output json --env staging --var marker=CTXVAR123" dir

        let r = firstResult stdout
        Assert.Equal(0, exitCode)
        Assert.True(r.GetProperty("passed").GetBoolean(), $"step should pass. error={errorText r}")
        Assert.Contains("env=staging marker=CTXVAR123", logText r)
    finally
        cleanupDir dir

[<Fact>]
let ``PY step can read ctx.env and ctx.vars and write ctx.log`` () =
    let dir = createTempDir ()

    try
        write dir "probe.py" "ctx.log('env=' + ctx.env + ' marker=' + ctx.vars['marker'])"
        write dir "suite.naplist" "[steps]\nprobe.py\n"

        let exitCode, stdout, _ =
            runCli "run suite.naplist --output json --env staging --var marker=CTXVAR123" dir

        let r = firstResult stdout
        Assert.Equal(0, exitCode)
        Assert.True(r.GetProperty("passed").GetBoolean(), $"step should pass. error={errorText r}")
        Assert.Contains("env=staging marker=CTXVAR123", logText r)
    finally
        cleanupDir dir

// ─────────────────────────── ctx.fail honoured beyond exit code ───────────────────────────
// The script EXITS 0 but calls ctx.fail — the run must still fail. This is the strongest proof
// that the protocol (not just the process exit code) drives pass/fail.

[<Fact>]
let ``JS ctx.fail fails the step even when the process exits zero`` () =
    let dir = createTempDir ()

    try
        write dir "boom.js" "ctx.log('did some work');\nctx.fail('CTX-FAIL-JS-MARKER');\nprocess.exit(0);"
        write dir "suite.naplist" "[steps]\nboom.js\n"
        let exitCode, stdout, _ = runCli "run suite.naplist --output json" dir
        let r = firstResult stdout
        Assert.Equal(1, exitCode)
        Assert.False(r.GetProperty("passed").GetBoolean())
        Assert.Contains("CTX-FAIL-JS-MARKER", errorText r)
        Assert.Contains("did some work", logText r)
    finally
        cleanupDir dir

[<Fact>]
let ``PY ctx.fail fails the step even when the process exits zero`` () =
    let dir = createTempDir ()

    try
        write dir "boom.py" "ctx.log('did some work')\nctx.fail('CTX-FAIL-PY-MARKER')\nimport sys; sys.exit(0)"
        write dir "suite.naplist" "[steps]\nboom.py\n"
        let exitCode, stdout, _ = runCli "run suite.naplist --output json" dir
        let r = firstResult stdout
        Assert.Equal(1, exitCode)
        Assert.False(r.GetProperty("passed").GetBoolean())
        Assert.Contains("CTX-FAIL-PY-MARKER", errorText r)
        Assert.Contains("did some work", logText r)
    finally
        cleanupDir dir

// ─────────────────────────── [script] post-hook reads ctx.response ───────────────────────────
// Proves two bugs at once: (1) [script] post hooks actually execute (they were parsed-but-inert),
// (2) ctx.response is populated for post hooks.

[<Fact>]
let ``JS post-hook can read ctx.response status and json`` () =
    let dir = createTempDir ()

    try
        write
            dir
            "get.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/1\n\n[assert]\nstatus = 200\n\n[script]\npost =./check.js\n")

        write
            dir
            "check.js"
            "if (ctx.response.status !== 200) ctx.fail('bad status');\nctx.log('post-ok id=' + ctx.response.json.id + ' dur=' + ctx.response.durationMs);"

        let exitCode, stdout, _ = runCli "run get.nap --output json" dir
        let r = firstResult stdout
        Assert.Equal(0, exitCode)
        Assert.True(r.GetProperty("passed").GetBoolean(), $"should pass. error={errorText r}")
        Assert.Contains("post-ok id=1", logText r)
        Assert.Matches(@"dur=\d+", logText r) // ctx.response.durationMs is a real number
    finally
        cleanupDir dir

[<Fact>]
let ``JS post-hook ctx.fail fails an otherwise-passing request`` () =
    let dir = createTempDir ()

    try
        write
            dir
            "get.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/1\n\n[assert]\nstatus = 200\n\n[script]\npost =./reject.js\n")

        write dir "reject.js" "ctx.fail('POSTHOOK-FAIL-JS');"
        let exitCode, stdout, _ = runCli "run get.nap --output json" dir
        let r = firstResult stdout
        // HTTP 200 and the assertion passes, but the post hook rejects -> the step must fail.
        Assert.Equal(1, exitCode)
        Assert.False(r.GetProperty("passed").GetBoolean())
        Assert.Contains("POSTHOOK-FAIL-JS", errorText r)
    finally
        cleanupDir dir

[<Fact>]
let ``PY post-hook can read ctx.response status and json`` () =
    let dir = createTempDir ()

    try
        write
            dir
            "get.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/1\n\n[assert]\nstatus = 200\n\n[script]\npost =./check.py\n")

        write
            dir
            "check.py"
            "if ctx.response.status != 200:\n    ctx.fail('bad status')\nctx.log('post-ok id=' + str(ctx.response.json['id']) + ' dur=' + str(ctx.response.duration_ms))"

        let exitCode, stdout, _ = runCli "run get.nap --output json" dir
        let r = firstResult stdout
        Assert.Equal(0, exitCode)
        Assert.True(r.GetProperty("passed").GetBoolean(), $"should pass. error={errorText r}")
        Assert.Contains("post-ok id=1", logText r)
        Assert.Matches(@"dur=\d+", logText r) // ctx.response.duration_ms is a real number
    finally
        cleanupDir dir

// ─────────────────────────── [script] pre-hook reads ctx.request ───────────────────────────

[<Fact>]
let ``JS pre-hook can read ctx.request method and url`` () =
    let dir = createTempDir ()

    try
        write
            dir
            "get.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/1\n\n[assert]\nstatus = 200\n\n[script]\npre =./probe-req.js\n")

        write dir "probe-req.js" "ctx.log('req ' + ctx.request.method + ' ' + ctx.request.url);"
        let exitCode, stdout, _ = runCli "run get.nap --output json" dir
        let r = firstResult stdout
        Assert.Equal(0, exitCode)
        Assert.True(r.GetProperty("passed").GetBoolean(), $"should pass. error={errorText r}")
        Assert.Contains("req GET " + LocalHttpServer.baseUrl + "/posts/1", logText r)
    finally
        cleanupDir dir

// ─────────────── [script] post-hook executes for .NET too (exit-code contract) ───────────────
// ctx injection is JS/Python-only today, but the hook EXECUTION path is language-agnostic:
// a .csx post-hook that exits non-zero must fail an otherwise-passing request.

[<Fact>]
let ``CSX post-hook nonzero exit fails an otherwise-passing request`` () =
    let dir = createTempDir ()

    try
        write
            dir
            "get.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/1\n\n[assert]\nstatus = 200\n\n[script]\npost = ./reject.csx\n")

        write dir "reject.csx" "Console.WriteLine(\"csx post hook ran\");\nEnvironment.Exit(1);"
        let exitCode, stdout, _ = runCli "run get.nap --output json" dir
        let r = firstResult stdout
        Assert.Equal(1, exitCode)
        Assert.False(r.GetProperty("passed").GetBoolean())
    finally
        cleanupDir dir

// ─────────────────────────── ctx.set threads a var to a downstream step ───────────────────────────

[<Fact>]
let ``JS ctx.set makes a variable visible to a downstream nap step`` () =
    let dir = createTempDir ()

    try
        write dir "seed.js" "ctx.set('seededId', '7');\nctx.log('seeded 7');"

        write
            dir
            "use.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/{{seededId}}\n\n[assert]\nstatus = 200\nbody.id = {{seededId}}\n")

        write dir "suite.naplist" "[steps]\nseed.js\nuse.nap\n"
        let exitCode, stdout, _ = runCli "run suite.naplist --output json" dir
        Assert.Equal(0, exitCode)
        let seedR = resultAt stdout 0
        let useR = resultAt stdout 1
        Assert.True(seedR.GetProperty("passed").GetBoolean())

        Assert.True(
            useR.GetProperty("passed").GetBoolean(),
            $"downstream step should see seededId. error={errorText useR}"
        )

        Assert.Equal(200, useR.GetProperty("statusCode").GetInt32())
    finally
        cleanupDir dir

[<Fact>]
let ``PY ctx.set makes a variable visible to a downstream nap step`` () =
    let dir = createTempDir ()

    try
        write dir "seed.py" "ctx.set('seededId', '7')\nctx.log('seeded 7')"

        write
            dir
            "use.nap"
            ("[request]\nmethod = GET\nurl = "
             + LocalHttpServer.baseUrl
             + "/posts/{{seededId}}\n\n[assert]\nstatus = 200\nbody.id = {{seededId}}\n")

        write dir "suite.naplist" "[steps]\nseed.py\nuse.nap\n"
        let exitCode, stdout, _ = runCli "run suite.naplist --output json" dir
        Assert.Equal(0, exitCode)
        let seedR = resultAt stdout 0
        let useR = resultAt stdout 1
        Assert.True(seedR.GetProperty("passed").GetBoolean())

        Assert.True(
            useR.GetProperty("passed").GetBoolean(),
            $"downstream step should see seededId. error={errorText useR}"
        )

        Assert.Equal(200, useR.GetProperty("statusCode").GetInt32())
    finally
        cleanupDir dir
