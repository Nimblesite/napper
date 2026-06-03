// Implements [LSP-TEST-WIRE]
/// Shared LSP / JSON-RPC wire helpers for the test assembly: the single
/// location for every wire string constant, the framing codec, and the
/// JSON-RPC envelope + param builders. Reused by BOTH the process-based client
/// (LspClient) and the in-process protocol driver (LspDriver) so there is zero
/// duplication of protocol strings or message construction.
module Napper.Lsp.Tests.LspWire

open System
open System.Text
open System.Text.Json.Nodes

// The in-process tests mutate one process-wide Workspace and share document URIs
// across test classes, so the whole assembly must run tests serially.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()

// ─── JSON-RPC envelope / version ───
[<Literal>]
let JsonRpcVersion = "2.0"

[<Literal>]
let LangNap = "nap"

[<Literal>]
let FJsonRpc = "jsonrpc"

[<Literal>]
let FId = "id"

[<Literal>]
let FMethod = "method"

[<Literal>]
let FParams = "params"

[<Literal>]
let FResult = "result"

[<Literal>]
let FError = "error"

[<Literal>]
let FCode = "code"

// ─── LSP wire framing ───
[<Literal>]
let ContentLengthHeader = "Content-Length"

[<Literal>]
let HeaderSep = "\r\n\r\n"

// ─── Methods ───
[<Literal>]
let MInitialize = "initialize"

[<Literal>]
let MInitialized = "initialized"

[<Literal>]
let MShutdown = "shutdown"

[<Literal>]
let MExit = "exit"

[<Literal>]
let MDidOpen = "textDocument/didOpen"

[<Literal>]
let MDidChange = "textDocument/didChange"

[<Literal>]
let MDidClose = "textDocument/didClose"

[<Literal>]
let MDocumentSymbol = "textDocument/documentSymbol"

[<Literal>]
let MCodeLens = "textDocument/codeLens"

[<Literal>]
let MExecuteCommand = "workspace/executeCommand"

// ─── Commands ───
[<Literal>]
let CmdRequestInfo = "napper.requestInfo"

[<Literal>]
let CmdCopyCurl = "napper.copyCurl"

[<Literal>]
let CmdListEnvironments = "napper.listEnvironments"

[<Literal>]
let CmdNaplistSteps = "napper.naplistSteps"

// ─── Param fields ───
[<Literal>]
let FTextDocument = "textDocument"

[<Literal>]
let FUri = "uri"

[<Literal>]
let FLanguageId = "languageId"

[<Literal>]
let FVersion = "version"

[<Literal>]
let FText = "text"

[<Literal>]
let FContentChanges = "contentChanges"

[<Literal>]
let FCommand = "command"

[<Literal>]
let FArguments = "arguments"

[<Literal>]
let FProcessId = "processId"

[<Literal>]
let FCapabilities = "capabilities"

[<Literal>]
let FRootUri = "rootUri"

// ─── JsonNode helpers ───
let str (s: string) : JsonNode = JsonValue.Create(s)
let num (n: int) : JsonNode = JsonValue.Create(n)

// ─── Framing codec ───

/// Encode a JSON-RPC message with a Content-Length header (the LSP wire
/// format). Public so the in-process driver reuses the exact same framing.
let encodeMessage (json: string) : byte[] =
    let body = Encoding.UTF8.GetBytes(json)
    let header = $"{ContentLengthHeader}: {body.Length}{HeaderSep}"
    Array.append (Encoding.UTF8.GetBytes(header)) body

/// First index of `pat` in `arr` at or after `from`, or -1 if absent.
let private indexOf (arr: byte[]) (pat: byte[]) (from: int) : int =
    let last = arr.Length - pat.Length
    let mutable i = from
    let mutable found = -1

    while found < 0 && i <= last do
        let mutable j = 0

        while j < pat.Length && arr[i + j] = pat[j] do
            j <- j + 1

        if j = pat.Length then found <- i else i <- i + 1

    found

/// Parse the Content-Length value out of an ASCII header block.
let private contentLength (headers: string) : int =
    headers.Split('\n')
    |> Array.tryPick (fun line ->
        match line.Split(':') with
        | [| key; value |] when key.Trim().Equals(ContentLengthHeader, StringComparison.OrdinalIgnoreCase) ->
            match Int32.TryParse(value.Trim()) with
            | true, n -> Some n
            | _ -> None
        | _ -> None)
    |> Option.defaultValue 0

/// Decode every Content-Length framed JSON-RPC message in `bytes`, in order.
/// Mirrors the framing produced by encodeMessage and by the server's Wire module.
let decodeFrames (bytes: byte[]) : JsonNode list =
    let term = Encoding.ASCII.GetBytes(HeaderSep)

    let rec loop (pos: int) (acc: JsonNode list) : JsonNode list =
        let hdrEnd = indexOf bytes term pos

        if hdrEnd < 0 then
            List.rev acc
        else
            let len = contentLength (Encoding.ASCII.GetString(bytes, pos, hdrEnd - pos))
            let bodyStart = hdrEnd + term.Length

            if len <= 0 || bodyStart + len > bytes.Length then
                List.rev acc
            else
                let json = Encoding.UTF8.GetString(bytes, bodyStart, len)
                loop (bodyStart + len) (JsonNode.Parse(json) :: acc)

    loop 0 []

// ─── JSON-RPC envelope builders ───

/// Build a JSON-RPC request envelope (has an id).
let buildRequest (method: string) (id: int) (paramObj: JsonNode option) : JsonNode =
    let o = JsonObject()
    o[FJsonRpc] <- str JsonRpcVersion
    o[FId] <- num id
    o[FMethod] <- str method

    match paramObj with
    | Some p -> o[FParams] <- p
    | None -> ()

    o :> JsonNode

/// Build a JSON-RPC notification envelope (no id).
let buildNotification (method: string) (paramObj: JsonNode option) : JsonNode =
    let o = JsonObject()
    o[FJsonRpc] <- str JsonRpcVersion
    o[FMethod] <- str method

    match paramObj with
    | Some p -> o[FParams] <- p
    | None -> ()

    o :> JsonNode

// ─── LSP param builders ───

let initializeParams () : JsonNode =
    let p = JsonObject()
    p[FProcessId] <- num 1
    p[FCapabilities] <- JsonObject()
    p[FRootUri] <- str "file:///tmp/test-workspace"
    p :> JsonNode

let didOpenParams (uri: string) (version: int) (text: string) : JsonNode =
    let td = JsonObject()
    td[FUri] <- str uri
    td[FLanguageId] <- str LangNap
    td[FVersion] <- num version
    td[FText] <- str text
    let p = JsonObject()
    p[FTextDocument] <- td
    p :> JsonNode

let didChangeParams (uri: string) (version: int) (text: string) : JsonNode =
    let td = JsonObject()
    td[FUri] <- str uri
    td[FVersion] <- num version
    let change = JsonObject()
    change[FText] <- str text
    let changes = JsonArray()
    changes.Add(change)
    let p = JsonObject()
    p[FTextDocument] <- td
    p[FContentChanges] <- changes
    p :> JsonNode

let didCloseParams (uri: string) : JsonNode =
    let td = JsonObject()
    td[FUri] <- str uri
    let p = JsonObject()
    p[FTextDocument] <- td
    p :> JsonNode

/// textDocument-only params, shared by documentSymbol and codeLens requests.
let textDocParams (uri: string) : JsonNode =
    let td = JsonObject()
    td[FUri] <- str uri
    let p = JsonObject()
    p[FTextDocument] <- td
    p :> JsonNode

let executeCommandParams (command: string) (arg: string) : JsonNode =
    let args = JsonArray()
    args.Add(str arg)
    let p = JsonObject()
    p[FCommand] <- str command
    p[FArguments] <- args
    p :> JsonNode
