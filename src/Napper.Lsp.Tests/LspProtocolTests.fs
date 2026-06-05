// Tests [LSP-TRANSPORT], [LSP-SYMBOLS], [LSP-ERROR-HANDLING] — initialize, documents, symbols, code lens,
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
        (commandsNode :?> JsonArray)
        |> Seq.map (fun c -> c.GetValue<string>())
        |> Seq.toList

    Assert.Contains(CmdCopyCurl, commands)
    Assert.Contains(CmdListEnvironments, commands)
    Assert.Contains(CmdRequestInfo, commands)
    Assert.True(commands.Length >= 3, $"expected at least the 3 core commands, got {commands.Length}")

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

    // Every symbol is structurally well-formed (name, positive kind, ordered
    // range, mirrored selectionRange) — 6 assertions per section.
    assertWellFormedSymbols symbols

    // Sections are reported in file order, each on a strictly later line.
    let names = symbolNameKinds (resultOf responses 2) |> List.map fst

    Assert.Equal<string list>(
        [ "[meta]"
          "[vars]"
          "[request]"
          "[request.headers]"
          "[request.body]"
          "[assert]"
          "[script]" ],
        names
    )

    let startLines =
        [ for s in symbols -> s |> field "range" |> field "start" |> field "line" |> asInt ]

    Assert.Equal(0, List.head startLines)
    Assert.Equal<int list>(startLines, List.sort startLines)
    Assert.Equal(List.length startLines, List.length (List.distinct startLines))

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

    let symbols = resultArray responses 3
    let kinds = symbolNameKinds (resultOf responses 3) |> Map.ofList

    Assert.Equal(3, symbols.Count)
    Assert.Equal(KindNamespace, kinds["[meta]"])
    Assert.Equal(KindVariable, kinds["[vars]"])
    Assert.Equal(KindArray, kinds["[steps]"])

    // Every naplist symbol is structurally well-formed, reported in file order.
    assertWellFormedSymbols symbols
    let names = symbolNameKinds (resultOf responses 3) |> List.map fst
    Assert.Equal<string list>([ "[meta]"; "[vars]"; "[steps]" ], names)

    let startLines =
        [ for s in symbols -> s |> field "range" |> field "start" |> field "line" |> asInt ]

    Assert.Equal(0, List.head startLines)
    Assert.Equal<int list>(startLines, List.sort startLines)
    Assert.Equal(List.length startLines, List.length (List.distinct startLines))

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
let ``in-process malformed file uri makes every handler return an internal error and the server survives`` () =
    // A file:// uri with an invalid port makes System.Uri throw inside the
    // server's path resolution. Field reads are otherwise null-safe, so this is
    // the input that exercises the per-message internal-error guard. It must
    // surface as a JSON-RPC -32603 on every request that touches it, on every
    // handler, and must never terminate the read loop.
    let badUri = "file://h:zz/internal-error.nap" // invalid port → UriFormatException

    // textDocParams builds a fresh node each call (a JsonNode cannot have two parents).
    let responses =
        drive
            [ buildRequest MDocumentSymbol 40 (Some(textDocParams badUri))
              buildRequest MCodeLens 41 (Some(textDocParams badUri))
              buildRequest MExecuteCommand 42 (Some(executeCommandParams CmdRequestInfo badUri))
              buildRequest MExecuteCommand 43 (Some(executeCommandParams CmdCopyCurl badUri))
              buildRequest MExecuteCommand 44 (Some(executeCommandParams CmdListEnvironments badUri))
              buildRequest MShutdown 45 None ]

    // Every core handler that resolves the bad uri reports an internal error...
    for id in [ 40; 41; 42; 43; 44 ] do
        let r = responseFor responses id
        Assert.NotNull(r[FError])
        Assert.Null(r[FResult])
        Assert.Equal(-32603, r |> field FError |> field FCode |> asInt)

    // ...and the server keeps serving afterwards.
    Assert.True(hasResponse responses 45, "server must survive internal errors")
    Assert.Null((responseFor responses 45)[FError])
    Assert.Equal(6, responses.Length)

[<Fact>]
let ``in-process notification with a non-int version is handled with no response`` () =
    // version is a string, not an int; the handler coerces it safely (no crash)
    // and, being a notification, emits no response while the server keeps running.
    let badVersion =
        let td = JsonObject()
        td[FUri] <- str NapUri
        td[FVersion] <- str "not-an-int"
        td[FText] <- str ValidGet
        let p = JsonObject()
        p[FTextDocument] <- td
        p :> JsonNode

    let responses =
        drive [ buildNotification MDidOpen (Some badVersion); buildRequest MShutdown 50 None ]

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
let ``in-process oversized and body-truncated frames end the read after prior work`` () =
    // A Content-Length beyond the 64 MiB cap ends the read (and never allocates
    // the buffer) — a prior valid message is still answered.
    let oversized =
        Array.append
            (framesOf [ buildRequest MInitialize 300 (Some(initializeParams ())) ])
            (Encoding.UTF8.GetBytes($"{ContentLengthHeader}: 100000000{HeaderSep}x"))

    let overCode, overResponses = driveBytes oversized
    Assert.Equal(0, overCode)
    Assert.True(hasResponse overResponses 300)
    Assert.Null((responseFor overResponses 300)[FError])
    Assert.Equal(1, overResponses.Length)

    // A body shorter than its declared Content-Length is treated as end-of-stream.
    let truncatedBody =
        Array.append
            (framesOf [ buildRequest MShutdown 301 None ])
            (Encoding.UTF8.GetBytes($"{ContentLengthHeader}: 4096{HeaderSep}only-a-few-bytes"))

    let truncCode, truncResponses = driveBytes truncatedBody
    Assert.Equal(0, truncCode)
    Assert.True(hasResponse truncResponses 301)
    Assert.Null((responseFor truncResponses 301)[FError])
    Assert.Equal(1, truncResponses.Length)

[<Fact>]
let ``in-process a failing output stream yields the crash code while a working stream does not`` () =
    let input = framesOf [ buildRequest MInitialize 70 (Some(initializeParams ())) ]

    // Baseline: over normal in-memory streams the run succeeds and answers.
    let okCode, responses = driveBytes input
    Assert.Equal(0, okCode)
    Assert.True(hasResponse responses 70)
    Assert.Null((responseFor responses 70)[FError])
    Assert.NotNull((responseFor responses 70)[FResult])

    // The SAME input over a stream whose Write throws drives the top-level crash
    // handler, which returns exit code 1 rather than letting the process die.
    use failing = new ThrowingStream()
    let crashCode = runWithOutput input failing
    Assert.Equal(1, crashCode)

[<Fact>]
let ``in-process degenerate envelopes and arguments are handled safely`` () =
    // method as a number → coerced to "" → unknown method.
    let numericMethod =
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 200
        o[FMethod] <- num 7
        o :> JsonNode

    // no method field at all → unknown method.
    let missingMethod =
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 201
        o :> JsonNode

    // documentSymbol whose params is NOT an object → reads nothing, empty result.
    let nonObjectParams =
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 202
        o[FMethod] <- str MDocumentSymbol
        o[FParams] <- str "not-an-object"
        o :> JsonNode

    // executeCommand requestInfo with a NUMERIC argument → coerced to "" → null.
    let numericArg =
        let args = JsonArray()
        args.Add(num 123)
        let p = JsonObject()
        p[FCommand] <- str CmdRequestInfo
        p[FArguments] <- args
        let o = JsonObject()
        o[FJsonRpc] <- str JsonRpcVersion
        o[FId] <- num 203
        o[FMethod] <- str MExecuteCommand
        o[FParams] <- p
        o :> JsonNode

    let responses = drive [ numericMethod; missingMethod; nonObjectParams; numericArg ]

    Assert.Equal(-32601, responseFor responses 200 |> field FError |> field FCode |> asInt)
    Assert.Equal(-32601, responseFor responses 201 |> field FError |> field FCode |> asInt)
    Assert.Equal(0, (resultArray responses 202).Count)
    Assert.Null(resultOf responses 203)
    Assert.Equal(4, responses.Length)

[<Fact>]
let ``in-process unreadable file on disk degrades to empty without crashing`` () =
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"napper-lsp-unreadable-{System.Guid.NewGuid()}")

    System.IO.Directory.CreateDirectory(dir) |> ignore
    let file = System.IO.Path.Combine(dir, "locked.nap")
    System.IO.File.WriteAllText(file, "plain text, no sections") // empty symbols even if it were readable
    System.IO.File.SetUnixFileMode(file, System.IO.UnixFileMode.None) // deny read → ReadAllText throws

    try
        // Not opened in the workspace → the server falls back to reading from disk.
        let responses =
            drive
                [ buildRequest MDocumentSymbol 210 (Some(textDocParams $"file://{file}"))
                  buildRequest MShutdown 211 None ]

        Assert.Equal(0, (resultArray responses 210).Count)
        Assert.True(hasResponse responses 211, "server must survive an unreadable file")
        Assert.Null((responseFor responses 211)[FError])
    finally
        System.IO.File.SetUnixFileMode(file, System.IO.UnixFileMode.UserRead ||| System.IO.UnixFileMode.UserWrite)
        System.IO.Directory.Delete(dir, true)
