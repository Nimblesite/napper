// Implements [LSP-SERVER]
// AOT-safe LSP server. Native AOT cannot use reflection-based serialization, so
// this file talks JSON-RPC over stdio using only the System.Text.Json DOM
// (JsonNode / Utf8 framing) — no StreamJsonRpc, no Newtonsoft, no reflection.
// All domain logic lives in Napper.Core; this file is protocol glue only.
namespace Napper.Lsp

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Napper.Core

/// JSON-RPC / LSP protocol constants — the single location for every wire string.
module private Protocol =
    [<Literal>]
    let JsonRpcVersion = "2.0"

    // ─── JSON-RPC envelope fields ───
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

    [<Literal>]
    let FMessage = "message"

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

    // ─── Capability / result fields ───
    [<Literal>]
    let FCapabilities = "capabilities"

    [<Literal>]
    let FTextDocumentSync = "textDocumentSync"

    [<Literal>]
    let FDocumentSymbolProvider = "documentSymbolProvider"

    [<Literal>]
    let FCodeLensProvider = "codeLensProvider"

    [<Literal>]
    let FExecuteCommandProvider = "executeCommandProvider"

    [<Literal>]
    let FResolveProvider = "resolveProvider"

    [<Literal>]
    let FCommands = "commands"

    [<Literal>]
    let FServerInfo = "serverInfo"

    [<Literal>]
    let FName = "name"

    [<Literal>]
    let FVersion = "version"

    // ─── Document / params fields ───
    [<Literal>]
    let FTextDocument = "textDocument"

    [<Literal>]
    let FUri = "uri"

    [<Literal>]
    let FText = "text"

    [<Literal>]
    let FContentChanges = "contentChanges"

    [<Literal>]
    let FCommand = "command"

    [<Literal>]
    let FArguments = "arguments"

    // ─── Symbol / lens / range fields ───
    [<Literal>]
    let FKind = "kind"

    [<Literal>]
    let FRange = "range"

    [<Literal>]
    let FSelectionRange = "selectionRange"

    [<Literal>]
    let FStart = "start"

    [<Literal>]
    let FEnd = "end"

    [<Literal>]
    let FLine = "line"

    [<Literal>]
    let FCharacter = "character"

    [<Literal>]
    let FData = "data"

    // ─── executeCommand result fields ───
    [<Literal>]
    let FUrl = "url"

    [<Literal>]
    let FHeaders = "headers"

    // ─── Commands ───
    [<Literal>]
    let CmdCopyCurl = "napper.copyCurl"

    [<Literal>]
    let CmdListEnvironments = "napper.listEnvironments"

    [<Literal>]
    let CmdRequestInfo = "napper.requestInfo"

    // ─── Section names (mirror Napper.Core.SectionScanner) ───
    [<Literal>]
    let SecMeta = "meta"

    [<Literal>]
    let SecRequest = "request"

    [<Literal>]
    let SecRequestHeaders = "request.headers"

    [<Literal>]
    let SecRequestBody = "request.body"

    [<Literal>]
    let SecAssert = "assert"

    [<Literal>]
    let SecScript = "script"

    [<Literal>]
    let SecVars = "vars"

    [<Literal>]
    let SecSteps = "steps"

    // ─── Misc ───
    [<Literal>]
    let ServerName = "napper-lsp"

    [<Literal>]
    let ServerVersion = "0.1.0"

    [<Literal>]
    let FileScheme = "file://"

    [<Literal>]
    let NapExtension = ".nap"

    [<Literal>]
    let NaplistExtension = ".naplist"

    [<Literal>]
    let HeaderContentLength = "Content-Length"

    [<Literal>]
    let HeaderTerminator = "\r\n\r\n"

    // ─── LSP SymbolKind enum values (LSP 3.17) ───
    [<Literal>]
    let KindNamespace = 3

    [<Literal>]
    let KindFunction = 12

    [<Literal>]
    let KindVariable = 13

    [<Literal>]
    let KindArray = 18

    [<Literal>]
    let KindKey = 20

    [<Literal>]
    let KindStruct = 23

    // ─── TextDocumentSyncKind / error codes ───
    [<Literal>]
    let SyncFull = 1

    [<Literal>]
    let CodeMethodNotFound = -32601

    [<Literal>]
    let CodeInternalError = -32603

    [<Literal>]
    let MsgMethodNotFound = "Method not found"

/// Small reflection-free helpers over the System.Text.Json DOM.
module private Json =
    let jstr (s: string) : JsonNode = JsonValue.Create(s) :> JsonNode
    let jint (n: int) : JsonNode = JsonValue.Create(n) :> JsonNode
    let jbool (b: bool) : JsonNode = JsonValue.Create(b) :> JsonNode

    /// Read a string field, or "" if absent/null.
    let strField (node: JsonNode) (key: string) : string =
        match node[key] with
        | null -> ""
        | v -> v.GetValue<string>()

    /// Read an int field, or the supplied default if absent/null.
    let intField (node: JsonNode) (key: string) (fallback: int) : int =
        match node[key] with
        | null -> fallback
        | v -> v.GetValue<int>()

    /// Build a successful JSON-RPC response. `result` may be null (→ "result":null).
    let ok (id: JsonNode) (result: JsonNode) : JsonNode =
        let o = JsonObject()
        o[Protocol.FJsonRpc] <- jstr Protocol.JsonRpcVersion
        o[Protocol.FId] <- (if isNull id then null else id.DeepClone())
        o[Protocol.FResult] <- result
        o :> JsonNode

    /// Build a JSON-RPC error response.
    let err (id: JsonNode) (code: int) (message: string) : JsonNode =
        let detail = JsonObject()
        detail[Protocol.FCode] <- jint code
        detail[Protocol.FMessage] <- jstr message
        let o = JsonObject()
        o[Protocol.FJsonRpc] <- jstr Protocol.JsonRpcVersion
        o[Protocol.FId] <- (if isNull id then null else id.DeepClone())
        o[Protocol.FError] <- detail
        o :> JsonNode

/// LSP wire framing: `Content-Length: N\r\n\r\n` + UTF-8 JSON body.
module private Wire =
    open Protocol

    /// Read raw header bytes up to and including the blank-line terminator.
    let private readHeaders (input: Stream) : string option =
        let sb = StringBuilder()
        let mutable finished = false
        let mutable eof = false

        while not finished && not eof do
            let b = input.ReadByte()

            if b = -1 then
                eof <- true
            else
                sb.Append(char b) |> ignore

                if sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) = HeaderTerminator then
                    finished <- true

        if finished then Some(sb.ToString()) else None

    /// Extract the Content-Length value from a header block.
    let private contentLength (headers: string) : int =
        headers.Split('\n')
        |> Array.tryPick (fun line ->
            match line.Split(':') with
            | [| key; value |] when key.Trim().Equals(HeaderContentLength, StringComparison.OrdinalIgnoreCase) ->
                match Int32.TryParse(value.Trim()) with
                | true, n -> Some n
                | _ -> None
            | _ -> None)
        |> Option.defaultValue 0

    /// Read one framed message body, or None at end-of-stream.
    let readMessage (input: Stream) : string option =
        match readHeaders input with
        | None -> None
        | Some headers ->
            let len = contentLength headers

            if len <= 0 then
                None
            else
                let buf = Array.zeroCreate<byte> len
                input.ReadExactly(buf, 0, len)
                Some(Encoding.UTF8.GetString(buf))

    /// Frame and write one message, then flush.
    let writeMessage (output: Stream) (json: string) : unit =
        let body = Encoding.UTF8.GetBytes(json)
        let header = Encoding.ASCII.GetBytes($"{HeaderContentLength}: {body.Length}{HeaderTerminator}")
        output.Write(header, 0, header.Length)
        output.Write(body, 0, body.Length)
        output.Flush()

/// Request/notification handlers. All domain logic delegates to Napper.Core.
module private Handlers =
    open Protocol
    open Json

    let private isNap (uri: string) = uri.EndsWith NapExtension
    let private isNaplist (uri: string) = uri.EndsWith NaplistExtension

    let private uriToFilePath (uri: string) : string =
        if uri.StartsWith FileScheme then Uri(uri).LocalPath else uri

    let private docText (uri: string) : string option =
        Workspace.tryGetDocument uri |> Option.map _.Text

    let private parseRequest (uri: string) : NapRequest option =
        docText uri
        |> Option.bind (fun text ->
            match Parser.parseNapFile text with
            | Result.Ok napFile -> Some napFile.Request
            | Result.Error _ -> None)

    let private symbolKind (name: string) : int =
        match name with
        | SecMeta -> KindNamespace
        | SecRequest -> KindFunction
        | SecRequestHeaders -> KindStruct
        | SecRequestBody -> KindStruct
        | SecAssert -> KindFunction
        | SecScript -> KindFunction
        | SecVars -> KindVariable
        | SecSteps -> KindArray
        | _ -> KindKey

    let private position (line: int) : JsonNode =
        let o = JsonObject()
        o[FLine] <- jint line
        o[FCharacter] <- jint 0
        o :> JsonNode

    let private range (startLine: int) (endLine: int) : JsonNode =
        let o = JsonObject()
        o[FStart] <- position startLine
        o[FEnd] <- position endLine
        o :> JsonNode

    let private sectionSymbol (section: SectionScanner.SectionLocation) : JsonNode =
        let r = range section.Line section.EndLine
        let o = JsonObject()
        o[FName] <- jstr $"[{section.Name}]"
        o[FKind] <- jint (symbolKind section.Name)
        o[FRange] <- r
        o[FSelectionRange] <- r.DeepClone()
        o :> JsonNode

    /// Section scan for the given URI, choosing the scanner by extension.
    let private scanSections (uri: string) (text: string) : SectionScanner.SectionLocation list =
        if isNap uri then SectionScanner.scanNapSections text
        elif isNaplist uri then SectionScanner.scanNaplistSections text
        else []

    let documentSymbols (uri: string) : JsonNode =
        let arr = JsonArray()

        match docText uri with
        | Some text -> scanSections uri text |> List.iter (fun s -> arr.Add(sectionSymbol s))
        | None -> ()

        arr :> JsonNode

    /// A code lens at a section line, carrying optional display data.
    let private lens (line: int) (data: string option) : JsonNode =
        let o = JsonObject()
        o[FRange] <- range line line
        o[FData] <- (match data with | Some d -> jstr d | None -> null)
        o :> JsonNode

    let codeLenses (uri: string) : JsonNode =
        let arr = JsonArray()

        match docText uri with
        | Some text when isNap uri ->
            let detail =
                match Parser.parseNapFile text with
                | Result.Ok nap -> Some $"{nap.Request.Method.Name} {nap.Request.Url}"
                | Result.Error _ -> None

            SectionScanner.scanNapSections text
            |> List.filter (fun s -> s.Name = SecRequest)
            |> List.iter (fun s -> arr.Add(lens s.Line detail))
        | Some text when isNaplist uri ->
            SectionScanner.scanNaplistSections text
            |> List.filter (fun s -> s.Name = SecMeta)
            |> List.iter (fun s -> arr.Add(lens s.Line None))
        | _ -> ()

        arr :> JsonNode

    let private requestInfo (uri: string) : JsonNode =
        match parseRequest uri with
        | None -> null
        | Some req ->
            let headers = JsonObject()
            req.Headers |> Map.iter (fun k v -> headers[k] <- jstr v)
            let o = JsonObject()
            o[FMethod] <- jstr req.Method.Name
            o[FUrl] <- jstr req.Url
            o[FHeaders] <- headers
            o :> JsonNode

    let private copyCurl (uri: string) : JsonNode =
        match parseRequest uri with
        | None -> null
        | Some req -> jstr (CurlGenerator.toCurl req)

    let private listEnvironments (rootUri: string) : JsonNode =
        let arr = JsonArray()

        Environment.detectEnvironmentNames (uriToFilePath rootUri)
        |> List.iter (fun n -> arr.Add(jstr n))

        arr :> JsonNode

    /// First string argument of a workspace/executeCommand request.
    let private firstArg (p: JsonNode) : string =
        match p[FArguments] with
        | :? JsonArray as a when a.Count > 0 ->
            match a[0] with
            | null -> ""
            | v -> v.GetValue<string>()
        | _ -> ""

    let private executeCommand (p: JsonNode) : JsonNode =
        let arg = firstArg p

        match strField p FCommand with
        | CmdRequestInfo -> requestInfo arg
        | CmdCopyCurl -> copyCurl arg
        | CmdListEnvironments -> listEnvironments arg
        | _ -> null

    let private uriOf (p: JsonNode) : string =
        match p[FTextDocument] with
        | null -> ""
        | td -> strField td FUri

    let private capabilities () : JsonNode =
        let codeLens = JsonObject()
        codeLens[FResolveProvider] <- jbool false

        let commands = JsonArray()
        commands.Add(jstr CmdCopyCurl)
        commands.Add(jstr CmdListEnvironments)
        commands.Add(jstr CmdRequestInfo)
        let exec = JsonObject()
        exec[FCommands] <- commands

        let caps = JsonObject()
        caps[FTextDocumentSync] <- jint SyncFull
        caps[FDocumentSymbolProvider] <- jbool true
        caps[FCodeLensProvider] <- codeLens
        caps[FExecuteCommandProvider] <- exec
        caps :> JsonNode

    let private initializeResult () : JsonNode =
        let info = JsonObject()
        info[FName] <- jstr ServerName
        info[FVersion] <- jstr ServerVersion
        let o = JsonObject()
        o[FCapabilities] <- capabilities ()
        o[FServerInfo] <- info
        o :> JsonNode

    let private onDidOpen (p: JsonNode) : unit =
        match p[FTextDocument] with
        | null -> ()
        | td -> Workspace.openDocument (strField td FUri) (intField td FVersion 0) (strField td FText)

    let private onDidChange (p: JsonNode) : unit =
        match p[FTextDocument], p[FContentChanges] with
        | (:? JsonObject as td), (:? JsonArray as changes) when changes.Count > 0 ->
            Workspace.changeDocument (strField td FUri) (intField td FVersion 0) (strField changes[0] FText)
        | _ -> ()

    let private onDidClose (p: JsonNode) : unit =
        match p[FTextDocument] with
        | null -> ()
        | td -> Workspace.closeDocument (strField td FUri)

    /// Dispatch one message. Returns Some response for requests, None for
    /// notifications. Notifications run their side effect here.
    let handle (methodName: string) (p: JsonNode) (id: JsonNode) : JsonNode option =
        let isRequest = not (isNull id)

        match methodName with
        | MInitialize -> Some(ok id (initializeResult ()))
        | MInitialized -> None
        | MShutdown -> Some(ok id null)
        | MDidOpen ->
            onDidOpen p
            None
        | MDidChange ->
            onDidChange p
            None
        | MDidClose ->
            onDidClose p
            None
        | MDocumentSymbol -> Some(ok id (documentSymbols (uriOf p)))
        | MCodeLens -> Some(ok id (codeLenses (uriOf p)))
        | MExecuteCommand -> Some(ok id (executeCommand p))
        | _ -> if isRequest then Some(err id CodeMethodNotFound MsgMethodNotFound) else None

/// Public entry point used by Napper.Cli and the integration tests.
module LspRunner =
    open Protocol

    let private tryParse (body: string) : JsonNode option =
        try
            match JsonNode.Parse(body) with
            | null -> None
            | node -> Some node
        with _ ->
            None

    /// Process one message; returns false when the server should stop (exit).
    let private processMessage (output: Stream) (msg: JsonNode) : bool =
        let methodName = Json.strField msg FMethod
        let id = msg[FId]

        if methodName = MExit then
            false
        else
            let response =
                try
                    Handlers.handle methodName msg[FParams] id
                with ex ->
                    if isNull id then None else Some(Json.err id CodeInternalError ex.Message)

            response |> Option.iter (fun r -> Wire.writeMessage output (r.ToJsonString()))
            true

    /// Start the LSP server over the given streams. Returns the exit code.
    /// Called by Napper.Cli for 'napper lsp' and by tests via the real binary.
    let run (input: Stream) (output: Stream) : int =
        try
            let mutable running = true

            while running do
                match Wire.readMessage input with
                | None -> running <- false
                | Some body ->
                    match tryParse body with
                    | None -> ()
                    | Some msg -> running <- processMessage output msg

            0
        with ex ->
            eprintfn $"napper lsp crashed: %A{ex}"
            1
