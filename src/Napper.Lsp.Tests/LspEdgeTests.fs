// Implements [LSP-SERVER] coverage — degenerate JSON-RPC shapes and IO failures
// the protocol guard must tolerate without ever crashing the read loop.
/// In-process protocol e2e tests for the server's defensive edges: non-object
/// params, wrong-typed / missing `method`, wrong-typed command arguments, and
/// an unreadable on-disk file. Every assertion is on the framed JSON-RPC output.
module Napper.Lsp.Tests.LspEdgeTests

open System.IO
open System.Runtime.InteropServices
open System.Text.Json.Nodes
open Xunit
open Napper.Lsp.Tests.LspWire
open Napper.Lsp.Tests.LspDriver

// The in-process tests mutate one process-wide Workspace and share document URIs
// across test classes, so they must not run concurrently with one another.
[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

[<Fact>]
let ``in-process degenerate JSON-RPC envelopes are tolerated and never crash the loop`` () =
    // params is a JSON array, not an object — every field lookup must yield "".
    let arrayParams =
        let a = JsonArray()
        a.Add(num 1)
        a.Add(str "x")
        a :> JsonNode

    // method present but not a string.
    let numericMethod =
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 201
        o[FMethod] <- num 999
        o :> JsonNode

    // no method field at all.
    let noMethod =
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 202
        o :> JsonNode

    // executeCommand whose first argument is a number, not a string.
    let numericArg =
        let args = JsonArray()
        args.Add(num 42)
        let p = JsonObject()
        p[FCommand] <- str CmdRequestInfo
        p[FArguments] <- args
        p :> JsonNode

    let responses =
        drive
            [ buildRequest MDocumentSymbol 200 (Some arrayParams) // item → non-object → ""
              numericMethod // tryStr → present-but-not-a-string → ""
              noMethod // tryStr → absent → ""
              buildRequest MExecuteCommand 203 (Some numericArg) // firstArg → non-string element → ""
              buildRequest MShutdown 204 None ]

    // Non-object params resolve to an empty uri → empty document symbols, no error.
    Assert.Equal(0, (resultArray responses 200).Count)
    Assert.Null((responseFor responses 200)[FError])

    // A wrong-typed or missing method is dispatched as unknown → method-not-found.
    for id in [ 201; 202 ] do
        let r = responseFor responses id
        Assert.NotNull(r[FError])
        Assert.Equal(-32601, r |> field FError |> field FCode |> asInt)

    // A non-string command argument coerces to "" → requestInfo finds no doc → null.
    Assert.Null(resultOf responses 203)

    // The trailing shutdown was answered — the server survived every degenerate input.
    Assert.True(hasResponse responses 204)
    Assert.Null((responseFor responses 204)[FError])
    Assert.Equal(5, responses.Length)

[<Fact>]
let ``in-process an unreadable on-disk file degrades to empty results without crashing`` () =
    // docText reads untracked files from disk; if the read throws, the server must
    // degrade to an empty result rather than crash. Deny read access (POSIX) to
    // force File.ReadAllText to throw inside the server's IO guard.
    let dir = Path.Combine(Path.GetTempPath(), $"napper-lsp-unreadable-{System.Guid.NewGuid()}")
    Directory.CreateDirectory(dir) |> ignore
    let napPath = Path.Combine(dir, "denied.nap")
    File.WriteAllText(napPath, ValidGet)

    let onWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

    // Deny read where the OS supports it, then confirm WE actually cannot read it
    // (false when running as root, where file permissions are bypassed).
    let denied =
        if onWindows then
            false
        else
            File.SetUnixFileMode(napPath, UnixFileMode.None)

            try
                File.ReadAllText napPath |> ignore
                false
            with _ ->
                true

    try
        let napUri = $"file://{napPath}"

        let responses =
            drive
                [ buildRequest MDocumentSymbol 210 (Some(textDocParams napUri))
                  buildRequest MExecuteCommand 211 (Some(executeCommandParams CmdRequestInfo napUri))
                  buildRequest MShutdown 212 None ]

        if denied then
            // The read failed inside the server → empty symbols and a null requestInfo.
            Assert.Equal(0, (resultArray responses 210).Count)
            Assert.Null(resultOf responses 211)
        else
            // Read succeeded (Windows / root) → the file's [request] section is visible.
            Assert.Equal(1, (resultArray responses 210).Count)
            Assert.Equal("GET", resultOf responses 211 |> field "method" |> asStr)

        // Either way, the loop never crashed and still answers requests.
        Assert.True(hasResponse responses 212)
        Assert.Null((responseFor responses 212)[FError])
    finally
        if not onWindows then
            File.SetUnixFileMode(napPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        Directory.Delete(dir, true)
