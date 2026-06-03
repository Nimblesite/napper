// Implements [LSP-SERVER]
// AOT-safe LSP server. Native AOT cannot use reflection-based serialization, so
// this file talks JSON-RPC over stdio using only the System.Text.Json DOM
// (JsonNode / Utf8 framing) — no StreamJsonRpc, no Newtonsoft, no reflection.
// All domain logic lives in Napper.Core; this file is protocol glue only.
// Hardened to NEVER crash the loop on malformed input (LSP-SPEC: the server
// never crashes on malformed input).
namespace Napper.Lsp

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Napper.Core
open Protocol

/// Small reflection-free, null-safe helpers over the System.Text.Json DOM.
module private Json =
    let jstr (s: string) : JsonNode = JsonValue.Create(s) :> JsonNode
    let jint (n: int) : JsonNode = JsonValue.Create(n) :> JsonNode
    let jbool (b: bool) : JsonNode = JsonValue.Create(b) :> JsonNode

    /// Safe property access: Some only when `node` is an object that has `key`.
    /// Never throws on a null node or a non-object node.
    let item (node: JsonNode) (key: string) : JsonNode option =
        match node with
        | :? JsonObject as o ->
            match o[key] with
            | null -> None
            | v -> Some v
        | _ -> None

    /// Lenient string read: "" when absent / null / not a string. Used for the
    /// JSON-RPC envelope (`method`) and command args, which are read OUTSIDE the
    /// per-message guard — they must never throw and crash the loop.
    let tryStr (node: JsonNode) (key: string) : string =
        match item node key with
        | Some(:? JsonValue as v) ->
            match v.TryGetValue<string>() with
            | true, s -> s
            | _ -> ""
        | _ -> ""

    /// Strict string read: "" when absent, but THROWS on a present-but-wrong-type
    /// value. Used inside handlers so a malformed request field becomes a clean
    /// JSON-RPC -32603 (caught per-message), never a silent wrong result.
    let strField (node: JsonNode) (key: string) : string =
        match item node key with
        | Some v -> v.GetValue<string>()
        | None -> ""

    /// Strict int read: `fallback` when absent, THROWS on present-but-wrong-type.
    let intField (node: JsonNode) (key: string) (fallback: int) : int =
        match item node key with
        | Some v -> v.GetValue<int>()
        | None -> fallback

    /// Build a successful JSON-RPC response. `result` may be null (→ "result":null).
    let ok (id: JsonNode) (result: JsonNode) : JsonNode =
        let o = JsonObject()
        o[FJsonRpc] <- jstr JsonRpcVersion
        o[FId] <- (if isNull id then null else id.DeepClone())
        o[FResult] <- result
        o :> JsonNode

    /// Build a JSON-RPC error response.
    let err (id: JsonNode) (code: int) (message: string) : JsonNode =
        let detail = JsonObject()
        detail[FCode] <- jint code
        detail[FMessage] <- jstr message
        let o = JsonObject()
        o[FJsonRpc] <- jstr JsonRpcVersion
        o[FId] <- (if isNull id then null else id.DeepClone())
        o[FError] <- detail
        o :> JsonNode

/// LSP wire framing: `Content-Length: N\r\n\r\n` + UTF-8 JSON body.
module private Wire =

    /// A framed read result. `Skip` is a recoverable malformed/empty frame (keep
    /// the loop alive); `Eof` is genuine end-of-stream (stop).
    type Frame =
        | Eof
        | Skip
        | Body of string

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

    /// Extract the Content-Length value from a header block, or 0 when absent.
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

    /// Read exactly `len` bytes unless the stream ends first; returns bytes read.
    let private readFully (input: Stream) (buf: byte[]) (len: int) : int =
        let mutable total = 0
        let mutable n = 1

        while total < len && n > 0 do
            n <- input.Read(buf, total, len - total)
            total <- total + n

        total

    /// Read one framed message. Distinguishes EOF (stop) from a recoverable
    /// malformed/empty frame (Skip) so a bad frame never terminates the session.
    let readMessage (input: Stream) : Frame =
        match readHeaders input with
        | None -> Eof
        | Some headers ->
            let len = contentLength headers

            if len <= 0 then Skip
            elif len > MaxMessageBytes then Eof
            else
                let buf = Array.zeroCreate<byte> len
                if readFully input buf len < len then Eof else Body(Encoding.UTF8.GetString buf)

    /// Frame and write one message, then flush.
    let writeMessage (output: Stream) (json: string) : unit =
        let body = Encoding.UTF8.GetBytes(json)
        let header = Encoding.ASCII.GetBytes($"{HeaderContentLength}: {body.Length}{HeaderTerminator}")
        output.Write(header, 0, header.Length)
        output.Write(body, 0, body.Length)
        output.Flush()

/// Request/notification handlers. All domain logic delegates to Napper.Core.
module private Handlers =
    open Json

    let private isNap (uri: string) = uri.EndsWith NapExtension
    let private isNaplist (uri: string) = uri.EndsWith NaplistExtension

    let private uriToFilePath (uri: string) : string =
        if uri.StartsWith FileScheme then Uri(uri).LocalPath else uri

    /// The text of a tracked document, falling back to reading from disk so the
    /// LSP serves files the IDE never opened (e.g. the explorer tree). A bad URI
    /// throws here (in uriToFilePath, outside the IO guard) → JSON-RPC -32603.
    let private docText (uri: string) : string option =
        match Workspace.tryGetDocument uri with
        | Some doc -> Some doc.Text
        | None ->
            let path = uriToFilePath uri

            if File.Exists path then
                try
                    Some(File.ReadAllText path)
                with _ ->
                    None
            else
                None

    let private parseRequest (uri: string) : NapRequest option =
        docText uri
        |> Option.bind (fun text ->
            match Parser.parseNapFile text with
            | Result.Ok napFile -> Some napFile.Request
            | Result.Error _ -> None)

    /// Section name → LSP SymbolKind. KindKey is the fallback for any section a
    /// future scanner might surface that is not in this table.
    let private sectionKinds =
        Map
            [ SecMeta, KindNamespace
              SecVars, KindVariable
              SecRequest, KindFunction
              SecRequestHeaders, KindStruct
              SecRequestBody, KindStruct
              SecAssert, KindFunction
              SecScript, KindFunction
              SecSteps, KindArray ]

    let private symbolKind (name: string) : int =
        sectionKinds |> Map.tryFind name |> Option.defaultValue KindKey

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

    let private lens (line: int) (data: string option) : JsonNode =
        let o = JsonObject()
        o[FRange] <- range line line
        o[FData] <- (match data with
                     | Some d -> jstr d
                     | None -> null)
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

    /// Step file paths declared in a .naplist's [steps] section (read from the
    /// tracked doc or disk) — lets the IDE drop its own .naplist parsing.
    let private naplistSteps (uri: string) : JsonNode =
        let arr = JsonArray()

        match docText uri with
        | Some text -> SectionScanner.scanNaplistStepPaths text |> List.iter (fun p -> arr.Add(jstr p))
        | None -> ()

        arr :> JsonNode

    let private listEnvironments (rootUri: string) : JsonNode =
        let arr = JsonArray()

        Environment.detectEnvironmentNames (uriToFilePath rootUri)
        |> List.iter (fun n -> arr.Add(jstr n))

        arr :> JsonNode

    /// First string argument of a workspace/executeCommand request.
    let private firstArg (p: JsonNode) : string =
        match item p FArguments with
        | Some(:? JsonArray as a) when a.Count > 0 ->
            match a[0] with
            | :? JsonValue as v ->
                match v.TryGetValue<string>() with
                | true, s -> s
                | _ -> ""
            | _ -> ""
        | _ -> ""

    let private executeCommand (p: JsonNode) : JsonNode =
        let arg = firstArg p

        match strField p FCommand with
        | CmdRequestInfo -> requestInfo arg
        | CmdCopyCurl -> copyCurl arg
        | CmdListEnvironments -> listEnvironments arg
        | CmdNaplistSteps -> naplistSteps arg
        | _ -> null

    let private uriOf (p: JsonNode) : string =
        match item p FTextDocument with
        | Some td -> strField td FUri
        | None -> ""

    let private capabilities () : JsonNode =
        let codeLens = JsonObject()
        codeLens[FResolveProvider] <- jbool false

        let commands = JsonArray()
        commands.Add(jstr CmdCopyCurl)
        commands.Add(jstr CmdListEnvironments)
        commands.Add(jstr CmdRequestInfo)
        commands.Add(jstr CmdNaplistSteps)
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
        match item p FTextDocument with
        | Some td -> Workspace.openDocument (strField td FUri) (intField td FVersion 0) (strField td FText)
        | None -> ()

    let private onDidChange (p: JsonNode) : unit =
        match item p FTextDocument, item p FContentChanges with
        | Some td, Some(:? JsonArray as changes) when changes.Count > 0 ->
            Workspace.changeDocument (strField td FUri) (intField td FVersion 0) (strField changes[0] FText)
        | _ -> ()

    let private onDidClose (p: JsonNode) : unit =
        match item p FTextDocument with
        | Some td -> Workspace.closeDocument (strField td FUri)
        | None -> ()

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

    /// Parse a frame body; accept ONLY a JSON object root. Array/primitive roots
    /// (valid JSON a non-conformant client may send) are dropped, not crashed on.
    let private tryParse (body: string) : JsonObject option =
        try
            match JsonNode.Parse(body) with
            | :? JsonObject as o -> Some o
            | _ -> None
        with _ ->
            None

    /// Process one message; returns false when the server should stop (exit).
    let private processMessage (output: Stream) (msg: JsonObject) : bool =
        let methodName = Json.tryStr msg FMethod

        if methodName = MExit then
            false
        else
            let id = msg[FId]

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
                | Wire.Eof -> running <- false
                | Wire.Skip -> ()
                | Wire.Body body ->
                    match tryParse body with
                    | Some msg -> running <- processMessage output msg
                    | None -> ()

            0
        with ex ->
            Console.Error.WriteLine(CrashPrefix + string ex)
            1
