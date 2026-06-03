// Implements [LSP-TEST-DRIVER]
/// In-process driver for the real LSP server entry point `LspRunner.run`.
///
/// VSCode and Zed launch `napper lsp` as a child process and speak JSON-RPC
/// over its stdio. `LspRunner.run` IS that stdio loop — it takes an input and
/// an output Stream. Here we feed it in-memory streams instead of OS pipes, so
/// it runs inside the test host process and code-coverage instrumentation can
/// observe [Napper.Lsp]*. This is still pure black-box testing: we frame
/// JSON-RPC bytes in and assert on the framed JSON-RPC bytes out, never
/// touching the server's internal state.
module Napper.Lsp.Tests.LspDriver

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open Xunit
open Napper.Lsp
open Napper.Lsp.Tests.LspWire

/// Frame a batch of JSON-RPC messages into a single input buffer.
let framesOf (messages: JsonNode list) : byte[] =
    use buf = new MemoryStream()

    for m in messages do
        let b = encodeMessage (m.ToJsonString())
        buf.Write(b, 0, b.Length)

    buf.ToArray()

/// Run the real server over raw input bytes; return (exitCode, responses).
let driveBytes (inputBytes: byte[]) : int * JsonNode list =
    use input = new MemoryStream(inputBytes)
    use output = new MemoryStream()
    let code = LspRunner.run input output
    code, decodeFrames (output.ToArray())

/// Run the real server over a batch of messages; return the framed responses.
let drive (messages: JsonNode list) : JsonNode list = driveBytes (framesOf messages) |> snd

/// Run the server with an explicit output stream (e.g. one that fails on write,
/// to exercise the top-level crash handler). Returns the exit code.
let runWithOutput (inputBytes: byte[]) (output: Stream) : int =
    use input = new MemoryStream(inputBytes)
    LspRunner.run input output

/// Find the response with the given JSON-RPC id, asserting it exists.
let responseFor (responses: JsonNode list) (id: int) : JsonNode =
    let found =
        responses
        |> List.tryFind (fun r ->
            match r[FId] with
            | null -> false
            | v -> v.GetValue<int>() = id)

    Assert.True(found.IsSome, $"expected a JSON-RPC response for id {id}, got {responses.Length} responses")
    found.Value

/// True when a response with the given id exists.
let hasResponse (responses: JsonNode list) (id: int) : bool =
    responses
    |> List.exists (fun r ->
        match r[FId] with
        | null -> false
        | v -> v.GetValue<int>() = id)

/// The `result` array of a response as (name, kind) pairs — for documentSymbol.
let symbolNameKinds (result: JsonNode) : (string * int) list =
    (result :?> JsonArray)
    |> Seq.map (fun s -> s["name"].GetValue<string>(), s["kind"].GetValue<int>())
    |> Seq.toList

// ─── Shared sample documents (one location for the test fixtures) ───

[<Literal>]
let NapUri = "file:///tmp/req.nap"

[<Literal>]
let BadNapUri = "file:///tmp/bad.nap"

[<Literal>]
let NaplistUri = "file:///tmp/list.naplist"

[<Literal>]
let TxtUri = "file:///tmp/note.txt"

[<Literal>]
let UnopenedUri = "file:///tmp/never-opened.nap"

/// A valid GET request that parses cleanly.
[<Literal>]
let ValidGet = "[request]\nmethod = GET\nurl = https://example.com\n"

/// A valid POST request carrying a header — exercises header projection.
[<Literal>]
let ValidPostWithHeader =
    "[request]\nmethod = POST\nurl = https://api.example.com/users\n\n[request.headers]\nAccept = application/json\n"

/// Has a [request] header line but a body the parser rejects.
[<Literal>]
let UnparseableRequest = "[request]\nthis is not a valid request line\n"

/// Every known .nap section — drives documentSymbol kind coverage.
[<Literal>]
let AllNapSections =
    "[meta]\nname = \"All\"\n\n[vars]\nx = 1\n\n[request]\nmethod = GET\nurl = https://example.com\n\n[request.headers]\nAccept = application/json\n\n[request.body]\n{}\n\n[assert]\nstatus = 200\n\n[script]\npost = \"x\"\n"

/// Every known .naplist section — drives documentSymbol kind coverage.
[<Literal>]
let AllNaplistSections = "[meta]\nname = \"L\"\n\n[vars]\ny = 2\n\n[steps]\na.nap\nb.nap\n"

/// A write-only stream whose Write always throws — used to drive the server's
/// top-level crash handler (the write happens outside its per-message try).
type ThrowingStream() =
    inherit Stream()
    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = 0L
    override _.Position with get () = 0L and set _ = ()
    override _.Flush() = ()
    override _.Read(_, _, _) = 0
    override _.Seek(_, _) = 0L
    override _.SetLength _ = ()
    override _.Write(_, _, _) : unit = raise (IOException("stdout closed"))
