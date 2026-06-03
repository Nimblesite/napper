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
