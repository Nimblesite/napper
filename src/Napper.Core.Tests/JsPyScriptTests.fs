module JsPyScriptTests
// Tests [SCRIPT-JS], [SCRIPT-PY], [SCRIPT-DISPATCH], [SCRIPT-RUNTIME], [NAPLIST-SCRIPT-STEP]
//
// Guards two regressions:
//   1. Runner.runScript dispatched every non-.csx extension to `dotnet fsi`, so .js/.py died instantly.
//   2. The .naplist step classifier only treated .fsx/.csx as ScriptStep, so .js/.py were parsed as .nap files.
// These tests exercise BOTH the execution path (Runner.runScript) and the classification path (Parser.parseNapList)
// through the public surface for JavaScript and Python.

open System
open System.IO
open Xunit
open Napper.Core

let private createTempScript (extension: string) (content: string) : string =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "nap-test-%s%s" (Guid.NewGuid().ToString("N")) extension)

    File.WriteAllText(path, content)
    path

let private cleanupScript (path: string) =
    if File.Exists(path) then
        File.Delete(path)

// ─── JavaScript execution ───────────────── Spec: script-js

[<Fact>]
let ``JS script runs via node and captures output`` () =
    let path = createTempScript ".js" "console.log('hello from js');"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.True(result.Passed, $"Script should pass. Error: {result.Error}")
        Assert.Contains("hello from js", result.Log)
        Assert.True(result.Response.IsNone)
        Assert.Empty(result.Assertions)
        Assert.Equal(path, result.File)
    finally
        cleanupScript path

[<Fact>]
let ``JS script with multiple output lines`` () =
    let path =
        createTempScript ".js" "console.log('a');\nconsole.log('b');\nconsole.log('c');"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.True(result.Passed, $"Script should pass. Error: {result.Error}")
        Assert.True(result.Log.Length >= 3, $"Expected at least 3 log lines, got {result.Log.Length}")
        Assert.Contains("a", result.Log)
        Assert.Contains("c", result.Log)
    finally
        cleanupScript path

[<Fact>]
let ``JS script with process exit 1 fails and keeps prior stdout`` () =
    let path = createTempScript ".js" "console.log('before exit');\nprocess.exit(1);"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome, "Should have an error message")
        Assert.Contains("before exit", result.Log)
    finally
        cleanupScript path

[<Fact>]
let ``JS script with thrown error fails`` () =
    let path = createTempScript ".js" "throw new Error('boom');"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
    finally
        cleanupScript path

[<Fact>]
let ``MJS module extension also runs via node`` () =
    let path = createTempScript ".mjs" "console.log('from mjs');"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.True(result.Passed, $"Script should pass. Error: {result.Error}")
        Assert.Contains("from mjs", result.Log)
    finally
        cleanupScript path

// ─── Python execution ───────────────────── Spec: script-py

[<Fact>]
let ``PY script runs via python3 and captures output`` () =
    let path = createTempScript ".py" "print('hello from python')"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.True(result.Passed, $"Script should pass. Error: {result.Error}")
        Assert.Contains("hello from python", result.Log)
        Assert.True(result.Response.IsNone)
        Assert.Empty(result.Assertions)
        Assert.Equal(path, result.File)
    finally
        cleanupScript path

[<Fact>]
let ``PY script can import stdlib and compute`` () =
    let path =
        createTempScript ".py" "import json\nprint(json.dumps({'sum': sum(range(1, 11))}))"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.True(result.Passed, $"Script should pass. Error: {result.Error}")

        Assert.True(
            result.Log |> List.exists (fun l -> l.Contains("\"sum\": 55")),
            $"Should contain summed value. Log: {result.Log}"
        )
    finally
        cleanupScript path

[<Fact>]
let ``PY script with sys exit 1 fails and keeps prior stdout`` () =
    let path = createTempScript ".py" "import sys\nprint('before exit')\nsys.exit(1)"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome, "Should have an error message")
        Assert.Contains("before exit", result.Log)
    finally
        cleanupScript path

[<Fact>]
let ``PY script with raised exception fails`` () =
    let path = createTempScript ".py" "raise Exception('boom')"

    try
        let result = Runner.runScript path |> Async.RunSynchronously
        Assert.False(result.Passed)
        Assert.True(result.Error.IsSome)
    finally
        cleanupScript path

// ─── Step classification ────────────────── Spec: naplist-script-step, script-dispatch

let private stepsOf (naplist: string) : PlaylistStep list =
    match Parser.parseNapList naplist with
    | Result.Ok playlist -> playlist.Steps
    | Result.Error msg -> failwith $"Playlist should parse: {msg}"

[<Fact>]
let ``Naplist classifies js py mjs cjs as ScriptStep not NapFileStep`` () =
    let steps =
        stepsOf "[steps]\n./a.js\n./b.py\n./c.mjs\n./d.cjs\n./e.fsx\n./f.csx\n./g.nap\n"

    // Every script extension must become a ScriptStep — this is exactly the bug that made .py/.js parse as .nap.
    Assert.Equal<PlaylistStep>(ScriptStep "./a.js", steps[0])
    Assert.Equal<PlaylistStep>(ScriptStep "./b.py", steps[1])
    Assert.Equal<PlaylistStep>(ScriptStep "./c.mjs", steps[2])
    Assert.Equal<PlaylistStep>(ScriptStep "./d.cjs", steps[3])
    Assert.Equal<PlaylistStep>(ScriptStep "./e.fsx", steps[4])
    Assert.Equal<PlaylistStep>(ScriptStep "./f.csx", steps[5])
    Assert.Equal<PlaylistStep>(NapFileStep "./g.nap", steps[6])

// ─── Dispatch table ─────────────────────── Spec: script-dispatch, script-runtime

[<Fact>]
let ``Dispatch resolves each extension to the correct runtime`` () =
    let exeOf path =
        match ScriptDispatch.resolve path with
        | Result.Ok(exe, _) -> exe
        | Result.Error msg -> failwith msg

    Assert.Equal(ScriptDispatch.RuntimeNode, exeOf "x.js")
    Assert.Equal(ScriptDispatch.RuntimeNode, exeOf "x.mjs")
    Assert.Equal(ScriptDispatch.RuntimeNode, exeOf "x.cjs")
    Assert.Equal(ScriptDispatch.RuntimePython, exeOf "x.py")
    Assert.Equal(ScriptDispatch.RuntimeDotnet, exeOf "x.fsx")
    Assert.Equal(ScriptDispatch.RuntimeDotnet, exeOf "x.csx")

[<Fact>]
let ``Dispatch passes fsi script arg prefixes and bare runtime for js py`` () =
    let argsOf path =
        match ScriptDispatch.resolve path with
        | Result.Ok(_, args) -> args
        | Result.Error msg -> failwith msg

    Assert.StartsWith("fsi ", argsOf "/tmp/x.fsx")
    Assert.StartsWith("script ", argsOf "/tmp/x.csx")
    Assert.StartsWith("\"", argsOf "/tmp/x.js") // node takes the bare quoted path, no sub-command
    Assert.StartsWith("\"", argsOf "/tmp/x.py")

[<Fact>]
let ``Dispatch is case-insensitive on extension`` () =
    Assert.True(ScriptDispatch.isScript "X.PY")
    Assert.True(ScriptDispatch.isScript "X.Js")
    Assert.False(ScriptDispatch.isScript "X.nap")

[<Fact>]
let ``Dispatch returns actionable error for unsupported extension`` () =
    match ScriptDispatch.resolve "/tmp/thing.rb" with
    | Result.Ok _ -> failwith "Expected unsupported extension to error"
    | Result.Error msg ->
        Assert.Contains(".rb", msg)
        Assert.Contains("Supported", msg)
