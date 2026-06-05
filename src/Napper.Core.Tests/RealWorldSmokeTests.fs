module RealWorldSmokeTests
// REAL-WORLD smoke tests — deliberately hit the genuine public API (jsonplaceholder)
// through the real napper CLI so a real outage or contract break TANKS the suite. That
// is the entire point of a real-world test: it proves the tool works against the wild.
//
// Kept INTENTIONALLY TINY — one or two calls per suite, per API — so we never pound the
// public server. The BROAD surface of HTTP behaviour (status/error codes, latency,
// content types, echoing) is covered hermetically by LocalHttpServer. See CLAUDE.md
// "HTTP: Local Test Server vs Real-World Smoke Tests". Implements [CLI-RUN], [NAP-ASSERT].

open System.IO
open System.Text.Json
open Xunit

[<Literal>]
let private PostUrl = "https://jsonplaceholder.typicode.com/posts/1"

[<Literal>]
let private PostsUrl = "https://jsonplaceholder.typicode.com/posts"

let private runCli args cwd =
    TestHelpers.runCliWithTimeout TestHelpers.ScriptTimeoutMs args cwd

let private writeNap (dir: string) (content: string) : unit =
    File.WriteAllText(Path.Combine(dir, "real.nap"), content)

// ─── Real GET against jsonplaceholder ───────────────────────── Spec: cli-run, nap-assert

[<Fact>]
let ``REAL jsonplaceholder GET returns post 1 with the documented shape`` () =
    let dir = TestHelpers.createTempDir "nap-realworld"

    try
        writeNap
            dir
            ("[request]\nmethod = GET\nurl = "
             + PostUrl
             + "\n\n[assert]\nstatus = 200\nbody.id = 1\nbody.userId exists\nbody.title exists\nbody.body exists\nheaders.Content-Type contains json\n")

        let exitCode, stdout, stderr = runCli "run real.nap --output json" dir
        Assert.Equal(0, exitCode)
        let root = JsonDocument.Parse(stdout).RootElement
        Assert.True(root.GetProperty("passed").GetBoolean(), $"real jsonplaceholder GET should pass. stderr={stderr}")
        Assert.Equal(200, root.GetProperty("statusCode").GetInt32())
        // Every declared assertion must be present AND passing — proves we really parsed
        // the live JSON body, not just the status line.
        let assertions = root.GetProperty("assertions")
        Assert.True(assertions.GetArrayLength() >= 6, $"expected 6+ assertions, got {assertions.GetArrayLength()}")

        for i in 0 .. assertions.GetArrayLength() - 1 do
            let target = assertions[i].GetProperty("target").GetString()

            Assert.True(
                assertions[i].GetProperty("passed").GetBoolean(),
                $"assertion {target} should pass against live data"
            )
    finally
        TestHelpers.cleanupDir dir

// ─── Real POST against jsonplaceholder ──────────────────────── Spec: cli-run, nap-body

[<Fact>]
let ``REAL jsonplaceholder POST creates a resource and echoes it`` () =
    let dir = TestHelpers.createTempDir "nap-realworld"
    let tq = "\"\"\""

    try
        writeNap
            dir
            ("[request]\nmethod = POST\nurl = "
             + PostsUrl
             + "\n\n[request.headers]\nContent-Type = application/json\n\n[request.body]\ncontent-type = application/json\n"
             + tq
             + "\n{\"title\": \"napper-smoke\", \"body\": \"hello\", \"userId\": 1}\n"
             + tq
             + "\n\n[assert]\nstatus = 201\nbody.id exists\n")

        let exitCode, stdout, stderr = runCli "run real.nap --output json" dir
        Assert.Equal(0, exitCode)
        let root = JsonDocument.Parse(stdout).RootElement
        Assert.True(root.GetProperty("passed").GetBoolean(), $"real jsonplaceholder POST should pass. stderr={stderr}")
        // jsonplaceholder returns 201 Created for POST /posts and assigns a new id.
        Assert.Equal(201, root.GetProperty("statusCode").GetInt32())
    finally
        TestHelpers.cleanupDir dir
