// Implements [LSP-TRANSPORT]
// JSON-RPC / LSP protocol constants — the single source of truth for every wire
// string, error code, and enum value used by the AOT-safe LSP transport.
namespace Napper.Lsp

module internal Protocol =
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

    [<Literal>]
    let FTitle = "title"

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

    [<Literal>]
    let CmdNaplistSteps = "napper.naplistSteps"

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

    [<Literal>]
    let CrashPrefix = "napper lsp crashed: "

    /// Upper bound on a single LSP frame body (bytes). Guards against a hostile or
    /// corrupt Content-Length forcing a huge allocation.
    [<Literal>]
    let MaxMessageBytes = 67108864 // 64 MiB

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

    // ─── TextDocumentSyncKind / JSON-RPC error codes ───
    [<Literal>]
    let SyncFull = 1

    [<Literal>]
    let CodeMethodNotFound = -32601

    [<Literal>]
    let CodeInternalError = -32603

    [<Literal>]
    let MsgMethodNotFound = "Method not found"
