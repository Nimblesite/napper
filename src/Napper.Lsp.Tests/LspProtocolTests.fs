// Implements [LSP-SERVER] coverage — initialize, documents, symbols, code lens,
// framing and lifecycle.
/// In-process protocol e2e tests. Each test frames real JSON-RPC messages,
/// feeds them through the actual server loop `LspRunner.run` over in-memory
/// streams, and asserts on the framed responses — the exact wire contract
/// VSCode and Zed depend on. No internal state is touched.
module Napper.Lsp.Tests.LspProtocolTests

open System.Text
open System.Text.Json.Nodes
open Xunit
open Napper.Lsp.Tests.LspWire
open Napper.Lsp.Tests.LspDriver

// LSP SymbolKind values (LSP 3.17) the server is contracted to emit.
[<Literal>]
let KindNamespace = 3

[<Literal>]
let KindFunction = 12

[<Literal>]
let KindVariable = 13

[<Literal>]
let KindArray = 18

[<Literal>]
let KindStruct = 23

[<Fact>]
let ``in-process initialize advertises capabilities, commands and serverInfo`` () =
    let responses = drive [ buildRequest MInitialize 1 (Some(initializeParams ())) ]
    let r = responseFor responses 1

    Assert.Null(r[FError])
    Assert.NotNull(r[FResult])

    let caps = r |> field FResult |> field "capabilities"
    Assert.Equal(1, caps |> field "textDocumentSync" |> asInt)
    Assert.True(caps |> field "documentSymbolProvider" |> asBool)
    Assert.False(caps |> field "codeLensProvider" |> field "resolveProvider" |> asBool)

    let commandsNode = caps |> field "executeCommandProvider" |> field "commands"

    let commands =
        (commandsNode :?> JsonArray) |> Seq.map (fun c -> c.GetValue<string>()) |> Seq.toList

    Assert.Contains(CmdCopyCurl, commands)
    Assert.Contains(CmdListEnvironments, commands)
    Assert.Contains(CmdRequestInfo, commands)
    Assert.Equal(3, commands.Length)

    let info = r |> field FResult |> field "serverInfo"
    Assert.Equal("napper-lsp", info |> field "name" |> asStr)
    Assert.Equal("0.1.0", info |> field "version" |> asStr)

[<Fact>]
let ``in-process documentSymbol maps every nap section to its LSP kind`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 AllNapSections))
              buildRequest MDocumentSymbol 2 (Some(textDocParams NapUri)) ]

    Assert.Null((responseFor responses 2)[FError])

    let symbols = resultArray responses 2
    let kinds = symbolNameKinds (resultOf responses 2) |> Map.ofList

    Assert.Equal(7, symbols.Count)
    Assert.Equal(KindNamespace, kinds["[meta]"])
    Assert.Equal(KindVariable, kinds["[vars]"])
    Assert.Equal(KindFunction, kinds["[request]"])
    Assert.Equal(KindStruct, kinds["[request.headers]"])
    Assert.Equal(KindStruct, kinds["[request.body]"])
    Assert.Equal(KindFunction, kinds["[assert]"])
    Assert.Equal(KindFunction, kinds["[script]"])

    // The first symbol ([meta]) starts on line 0 and carries a selectionRange.
    let first = symbols[0]
    Assert.Equal("[meta]", first |> field "name" |> asStr)
    Assert.Equal(0, first |> field "range" |> field "start" |> field "line" |> asInt)
    Assert.NotNull(first["selectionRange"])

[<Fact>]
let ``in-process documentSymbol maps naplist meta, vars and steps kinds`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NaplistUri 1 AllNaplistSections))
              buildRequest MDocumentSymbol 3 (Some(textDocParams NaplistUri)) ]

    let kinds = symbolNameKinds (resultOf responses 3) |> Map.ofList

    Assert.Equal(KindNamespace, kinds["[meta]"])
    Assert.Equal(KindVariable, kinds["[vars]"])
    Assert.Equal(KindArray, kinds["[steps]"])

[<Fact>]
let ``in-process documentSymbol is empty for unopened, non-nap and malformed params`` () =
    let noTextDocument = JsonObject() :> JsonNode

    let emptyTextDocument =
        let p = JsonObject()
        p[FTextDocument] <- JsonObject()
        p :> JsonNode

    let responses =
        drive
            [ buildRequest MDocumentSymbol 4 (Some(textDocParams UnopenedUri)) // docText None
              buildNotification MDidOpen (Some(didOpenParams TxtUri 1 ValidGet))
              buildRequest MDocumentSymbol 5 (Some(textDocParams TxtUri)) // not .nap/.naplist
              buildRequest MDocumentSymbol 6 (Some noTextDocument) // uriOf null arm
              buildRequest MDocumentSymbol 7 (Some emptyTextDocument) ] // strField null arm

    for id in [ 4; 5; 6; 7 ] do
        Assert.Equal(0, (resultArray responses id).Count)

[<Fact>]
let ``in-process codeLens emits request detail, naplist meta, and none otherwise`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 ValidGet))
              buildRequest MCodeLens 10 (Some(textDocParams NapUri))
              buildNotification MDidOpen (Some(didOpenParams BadNapUri 1 UnparseableRequest))
              buildRequest MCodeLens 11 (Some(textDocParams BadNapUri))
              buildNotification MDidOpen (Some(didOpenParams NaplistUri 1 AllNaplistSections))
              buildRequest MCodeLens 12 (Some(textDocParams NaplistUri))
              buildNotification MDidOpen (Some(didOpenParams TxtUri 1 ValidGet))
              buildRequest MCodeLens 13 (Some(textDocParams TxtUri))
              buildRequest MCodeLens 14 (Some(textDocParams UnopenedUri)) ]

    // Valid nap → one lens on line 0 with "METHOD url" detail.
    let napLenses = resultArray responses 10
    Assert.Equal(1, napLenses.Count)
    Assert.Equal(0, napLenses[0] |> field "range" |> field "start" |> field "line" |> asInt)
    Assert.Equal("GET https://example.com", napLenses[0] |> field "data" |> asStr)

    // Unparseable nap still has a [request] section → lens, but no detail.
    let badLenses = resultArray responses 11
    Assert.True(badLenses.Count >= 1)
    Assert.True(isNull (badLenses[0]["data"]))

    // Naplist → a meta lens with no detail.
    let listLenses = resultArray responses 12
    Assert.True(listLenses.Count >= 1)
    Assert.True(isNull (listLenses[0]["data"]))

    // Non-nap and unopened → no lenses.
    Assert.Equal(0, (resultArray responses 13).Count)
    Assert.Equal(0, (resultArray responses 14).Count)

[<Fact>]
let ``in-process initialized and lifecycle notifications are accepted`` () =
    // initialize → initialized → shutdown → exit; the documentSymbol after
    // exit must never be processed because exit stops the read loop.
    let responses =
        drive
            [ buildRequest MInitialize 80 (Some(initializeParams ()))
              buildNotification MInitialized (Some(JsonObject() :> JsonNode))
              buildRequest MShutdown 81 None
              buildNotification MExit None
              buildRequest MDocumentSymbol 82 (Some(textDocParams NapUri)) ]

    Assert.True(hasResponse responses 80)
    Assert.Null((responseFor responses 81)[FError])
    Assert.False(hasResponse responses 82, "messages after exit must be ignored")
    Assert.Equal(2, responses.Length)

[<Fact>]
let ``in-process notifications with missing fields are harmless no-ops`` () =
    let empty () = Some(JsonObject() :> JsonNode)

    let responses =
        drive
            [ buildNotification MDidOpen (empty ()) // onDidOpen null arm
              buildNotification MDidChange (empty ()) // onDidChange wildcard arm
              buildNotification MDidClose (empty ()) // onDidClose null arm
              buildRequest MShutdown 90 None ]

    Assert.True(hasResponse responses 90, "server must survive degenerate notifications")
    Assert.Equal(1, responses.Length)

[<Fact>]
let ``in-process unknown request errors with method-not-found, unknown notification is ignored`` () =
    let responses =
        drive
            [ buildRequest "textDocument/doesNotExist" 20 None
              buildNotification "textDocument/alsoUnknown" None
              buildRequest MShutdown 21 None ]

    let err = responseFor responses 20
    Assert.NotNull(err[FError])
    Assert.Equal(-32601, err |> field FError |> field FCode |> asInt)

    Assert.True(hasResponse responses 21, "server must keep serving after an unknown method")
    Assert.Equal(2, responses.Length)

[<Fact>]
let ``in-process malformed and null-body frames are skipped, valid requests still answered`` () =
    let bytes =
        Array.concat
            [ framesOf [ buildRequest MInitialize 30 (Some(initializeParams ())) ]
              encodeMessage "{ this is : not json" // JsonNode.Parse throws → skipped
              encodeMessage "null" // JsonNode.Parse returns null → skipped
              framesOf [ buildRequest MShutdown 31 None ] ]

    let code, responses = driveBytes bytes

    Assert.Equal(0, code)
    Assert.True(hasResponse responses 30)
    Assert.True(hasResponse responses 31)
    Assert.Equal(2, responses.Length)

[<Fact>]
let ``in-process request triggering an internal error returns -32603 and server survives`` () =
    // textDocument.uri is a number, so reading it as a string throws inside the
    // handler — the per-message guard must convert it to a JSON-RPC error.
    let badUriParams =
        let td = JsonObject()
        td[FUri] <- num 5
        let p = JsonObject()
        p[FTextDocument] <- td
        p :> JsonNode

    let responses =
        drive
            [ buildRequest MDocumentSymbol 40 (Some badUriParams)
              buildRequest MShutdown 41 None ]

    let err = responseFor responses 40
    Assert.NotNull(err[FError])
    Assert.Equal(-32603, err |> field FError |> field FCode |> asInt)
    Assert.True(hasResponse responses 41, "server must survive an internal error")

[<Fact>]
let ``in-process throwing notification is swallowed without a response`` () =
    // version is a string, so the didOpen handler throws; because it is a
    // notification (no id) the server must swallow it and keep running.
    let badVersion =
        let td = JsonObject()
        td[FUri] <- str NapUri
        td[FVersion] <- str "not-an-int"
        td[FText] <- str ValidGet
        let p = JsonObject()
        p[FTextDocument] <- td
        p :> JsonNode

    let responses =
        drive
            [ buildNotification MDidOpen (Some badVersion)
              buildRequest MShutdown 50 None ]

    Assert.True(hasResponse responses 50)
    Assert.Equal(1, responses.Length)

[<Fact>]
let ``in-process bad Content-Length terminates the read after prior messages`` () =
    let bytes =
        Array.append
            (framesOf [ buildRequest MInitialize 60 (Some(initializeParams ())) ])
            (Encoding.UTF8.GetBytes($"{ContentLengthHeader}: abc{HeaderSep}{{}}"))

    let code, responses = driveBytes bytes

    Assert.Equal(0, code)
    Assert.True(hasResponse responses 60)
    Assert.Equal(1, responses.Length)

[<Fact>]
let ``in-process empty and truncated input exit cleanly`` () =
    let emptyCode, emptyResponses = driveBytes [||]
    Assert.Equal(0, emptyCode)
    Assert.Empty(emptyResponses)

    let truncCode, truncResponses =
        driveBytes (Encoding.UTF8.GetBytes($"{ContentLengthHeader}: 5")) // header, no terminator

    Assert.Equal(0, truncCode)
    Assert.Empty(truncResponses)

[<Fact>]
let ``in-process server returns a crash code when the output stream fails`` () =
    use output = new ThrowingStream()
    let code = runWithOutput (framesOf [ buildRequest MInitialize 70 (Some(initializeParams ())) ]) output
    Assert.Equal(1, code)
