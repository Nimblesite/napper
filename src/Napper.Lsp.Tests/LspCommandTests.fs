// Implements [LSP-SERVER] coverage — workspace/executeCommand and document
// version semantics.
/// In-process protocol e2e tests for the command surface (requestInfo,
/// copyCurl, listEnvironments) and the document version/lifecycle rules.
/// All assertions are on the framed JSON-RPC responses only.
module Napper.Lsp.Tests.LspCommandTests

open System
open System.IO
open System.Text.Json.Nodes
open Xunit
open Napper.Lsp.Tests.LspWire
open Napper.Lsp.Tests.LspDriver

[<Fact>]
let ``in-process requestInfo returns method, url and projected headers`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 ValidPostWithHeader))
              buildRequest MExecuteCommand 100 (Some(executeCommandParams CmdRequestInfo NapUri)) ]

    let info = resultOf responses 100
    Assert.Equal("POST", info |> field "method" |> asStr)
    Assert.Equal("https://api.example.com/users", info |> field "url" |> asStr)
    Assert.Equal("application/json", info |> field "headers" |> field "Accept" |> asStr)

[<Fact>]
let ``in-process copyCurl returns a curl command for the request`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 ValidPostWithHeader))
              buildRequest MExecuteCommand 101 (Some(executeCommandParams CmdCopyCurl NapUri)) ]

    let curl = resultOf responses 101 |> asStr
    Assert.Contains("curl", curl)
    Assert.Contains("POST", curl)
    Assert.Contains("https://api.example.com/users", curl)

[<Fact>]
let ``in-process requestInfo and copyCurl return null for parse errors and unopened docs`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams BadNapUri 1 UnparseableRequest))
              buildRequest MExecuteCommand 102 (Some(executeCommandParams CmdRequestInfo BadNapUri))
              buildRequest MExecuteCommand 103 (Some(executeCommandParams CmdCopyCurl BadNapUri))
              buildRequest MExecuteCommand 104 (Some(executeCommandParams CmdRequestInfo UnopenedUri)) ]

    Assert.Null(resultOf responses 102) // parse error → none
    Assert.Null(resultOf responses 103) // parse error → none
    Assert.Null(resultOf responses 104) // never opened → none

[<Fact>]
let ``in-process listEnvironments works for both file uri and plain path`` () =
    let tmpDir = Path.Combine(Path.GetTempPath(), $"napper-lsp-inproc-{Guid.NewGuid()}")
    Directory.CreateDirectory(tmpDir) |> ignore
    File.WriteAllText(Path.Combine(tmpDir, ".napenv"), "baseUrl = https://example.com")
    File.WriteAllText(Path.Combine(tmpDir, ".napenv.staging"), "baseUrl = https://staging.example.com")
    File.WriteAllText(Path.Combine(tmpDir, ".napenv.production"), "baseUrl = https://prod.example.com")
    File.WriteAllText(Path.Combine(tmpDir, ".napenv.local"), "secret = hunter2")

    try
        let responses =
            drive
                [ buildRequest MExecuteCommand 110 (Some(executeCommandParams CmdListEnvironments $"file://{tmpDir}"))
                  buildRequest MExecuteCommand 111 (Some(executeCommandParams CmdListEnvironments tmpDir)) ]

        for id in [ 110; 111 ] do
            let envs =
                (resultArray responses id)
                |> Seq.map (fun e -> e.GetValue<string>())
                |> Seq.toList

            Assert.Contains("staging", envs)
            Assert.Contains("production", envs)
            Assert.DoesNotContain("local", envs)
            Assert.Equal(2, envs.Length)
    finally
        Directory.Delete(tmpDir, true)

[<Fact>]
let ``in-process executeCommand returns null for unknown command and missing or null args`` () =
    let nullArg =
        let args = JsonArray()
        args.Add(null)
        let p = JsonObject()
        p[FCommand] <- str CmdRequestInfo
        p[FArguments] <- args
        p :> JsonNode

    let noArgs =
        let p = JsonObject()
        p[FCommand] <- str CmdRequestInfo
        p :> JsonNode

    let responses =
        drive
            [ buildRequest MExecuteCommand 120 (Some(executeCommandParams "napper.bogusCommand" NapUri)) // unknown command
              buildRequest MExecuteCommand 121 (Some nullArg) // firstArg null element
              buildRequest MExecuteCommand 122 (Some noArgs) ] // firstArg missing arguments

    Assert.Null(resultOf responses 120)
    Assert.Null(resultOf responses 121)
    Assert.Null(resultOf responses 122)

[<Fact>]
let ``in-process didChange honors version ordering, ignores stale and empty changes`` () =
    let emptyChange =
        let td = JsonObject()
        td[FUri] <- str NapUri
        td[FVersion] <- num 9
        let p = JsonObject()
        p[FTextDocument] <- td
        p[FContentChanges] <- JsonArray() // zero changes → wildcard arm
        p :> JsonNode

    let noVersionChange =
        let td = JsonObject()
        td[FUri] <- str NapUri // no version → defaults to 0 → treated as stale
        let change = JsonObject()
        change[FText] <- str "[request]\nmethod = DELETE\nurl = https://example.com/wiped\n"
        let changes = JsonArray()
        changes.Add(change)
        let p = JsonObject()
        p[FTextDocument] <- td
        p[FContentChanges] <- changes
        p :> JsonNode

    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 ValidGet))
              buildRequest MExecuteCommand 130 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildNotification
                  MDidChange
                  (Some(didChangeParams NapUri 2 "[request]\nmethod = POST\nurl = https://example.com/v2\n"))
              buildRequest MExecuteCommand 131 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildNotification
                  MDidChange
                  (Some(didChangeParams NapUri 1 "[request]\nmethod = PUT\nurl = https://example.com/stale\n"))
              buildRequest MExecuteCommand 132 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildNotification MDidChange (Some emptyChange)
              buildRequest MExecuteCommand 133 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildNotification MDidChange (Some noVersionChange)
              buildRequest MExecuteCommand 134 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildNotification MDidClose (Some(didCloseParams NapUri))
              buildRequest MExecuteCommand 135 (Some(executeCommandParams CmdRequestInfo NapUri))
              buildRequest MDocumentSymbol 136 (Some(textDocParams NapUri)) ]

    Assert.Equal("GET", resultOf responses 130 |> field "method" |> asStr)
    // Newer version applied.
    Assert.Equal("POST", resultOf responses 131 |> field "method" |> asStr)
    Assert.Equal("https://example.com/v2", resultOf responses 131 |> field "url" |> asStr)
    // Stale (older version) change ignored — still the v2 content.
    Assert.Equal("POST", resultOf responses 132 |> field "method" |> asStr)
    // Empty contentChanges ignored.
    Assert.Equal("POST", resultOf responses 133 |> field "method" |> asStr)
    // Missing version (=> 0) is stale and ignored.
    Assert.Equal("POST", resultOf responses 134 |> field "method" |> asStr)
    Assert.Equal("https://example.com/v2", resultOf responses 134 |> field "url" |> asStr)
    // After close the document is gone.
    Assert.Null(resultOf responses 135)
    Assert.Equal(0, (resultArray responses 136).Count)

[<Fact>]
let ``in-process didOpen without a version is tracked, queryable and superseded by a later version`` () =
    // A didOpen with no version field defaults the version to 0; the document
    // must still be fully tracked, queryable, and superseded by a real version.
    let uri = "file:///tmp/no-version.nap"

    let noVersionOpen =
        let td = JsonObject()
        td[FUri] <- str uri
        td[FLanguageId] <- str LangNap
        td[FText] <- str ValidGet // no version → defaults to 0
        let p = JsonObject()
        p[FTextDocument] <- td
        p :> JsonNode

    let responses =
        drive
            [ buildNotification MDidOpen (Some noVersionOpen)
              buildRequest MDocumentSymbol 140 (Some(textDocParams uri))
              buildRequest MExecuteCommand 141 (Some(executeCommandParams CmdRequestInfo uri))
              buildNotification MDidChange (Some(didChangeParams uri 5 ValidPostWithHeader))
              buildRequest MExecuteCommand 142 (Some(executeCommandParams CmdRequestInfo uri))
              buildRequest MDocumentSymbol 143 (Some(textDocParams uri)) ]

    // Tracked despite the missing version: the [request] section is visible.
    let names0 = symbolNameKinds (resultOf responses 140) |> List.map fst
    Assert.Equal(1, (resultArray responses 140).Count)
    Assert.Contains("[request]", names0)

    // The v0 content is the GET.
    let v0 = resultOf responses 141
    Assert.Equal("GET", v0 |> field "method" |> asStr)
    Assert.Equal("https://example.com", v0 |> field "url" |> asStr)

    // A real (newer) version supersedes the v0 document and its headers appear.
    let v5 = resultOf responses 142
    Assert.Equal("POST", v5 |> field "method" |> asStr)
    Assert.Equal("https://api.example.com/users", v5 |> field "url" |> asStr)
    Assert.Equal("application/json", v5 |> field "headers" |> field "Accept" |> asStr)

    let names5 = symbolNameKinds (resultOf responses 143) |> List.map fst
    Assert.Equal(2, (resultArray responses 143).Count)
    Assert.Contains("[request]", names5)
    Assert.Contains("[request.headers]", names5)

[<Fact>]
let ``in-process naplistSteps returns step paths in order and empty for stepless or unopened docs`` () =
    let steplessUri = "file:///tmp/stepless.naplist"

    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NaplistUri 1 AllNaplistSections))
              buildRequest MExecuteCommand 150 (Some(executeCommandParams CmdNaplistSteps NaplistUri))
              buildNotification MDidOpen (Some(didOpenParams steplessUri 1 "[meta]\nname = \"none\"\n"))
              buildRequest MExecuteCommand 151 (Some(executeCommandParams CmdNaplistSteps steplessUri))
              buildRequest MExecuteCommand 152 (Some(executeCommandParams CmdNaplistSteps UnopenedUri)) ]

    // The opened naplist yields its two step paths, in declaration order.
    let steps = resultArray responses 150 |> Seq.map asStr |> Seq.toList
    Assert.Equal<string list>([ "a.nap"; "b.nap" ], steps)
    // A document with no [steps] section yields an empty array.
    Assert.Equal(0, (resultArray responses 151).Count)
    // An unopened, non-existent document yields an empty array (no crash).
    Assert.Equal(0, (resultArray responses 152).Count)

[<Fact>]
let ``in-process queries read .nap and .naplist from disk when never opened in the editor`` () =
    // docText falls back to reading the file from disk for documents the IDE has
    // not opened, so the explorer can query files it never sent didOpen for.
    let dir = Path.Combine(Path.GetTempPath(), $"napper-lsp-disk-{Guid.NewGuid()}")
    Directory.CreateDirectory(dir) |> ignore
    let napPath = Path.Combine(dir, "ondisk.nap")
    let listPath = Path.Combine(dir, "ondisk.naplist")
    File.WriteAllText(napPath, ValidPostWithHeader)
    File.WriteAllText(listPath, AllNaplistSections)
    let napUri = $"file://{napPath}"
    let listUri = $"file://{listPath}"

    try
        let responses =
            drive
                [ buildRequest MDocumentSymbol 160 (Some(textDocParams napUri)) // never opened → disk
                  buildRequest MExecuteCommand 161 (Some(executeCommandParams CmdRequestInfo napUri))
                  buildRequest MExecuteCommand 162 (Some(executeCommandParams CmdCopyCurl napUri))
                  buildRequest MExecuteCommand 163 (Some(executeCommandParams CmdNaplistSteps listUri)) ]

        // Symbols come straight from the on-disk file.
        let napNames = symbolNameKinds (resultOf responses 160) |> List.map fst
        Assert.Equal(2, (resultArray responses 160).Count)
        Assert.Contains("[request]", napNames)
        Assert.Contains("[request.headers]", napNames)

        // requestInfo parsed the on-disk file.
        let info = resultOf responses 161
        Assert.Equal("POST", info |> field "method" |> asStr)
        Assert.Equal("https://api.example.com/users", info |> field "url" |> asStr)
        Assert.Equal("application/json", info |> field "headers" |> field "Accept" |> asStr)

        // copyCurl works off the same on-disk read.
        let curl = resultOf responses 162 |> asStr
        Assert.Contains("curl", curl)
        Assert.Contains("POST", curl)

        // naplist steps read from disk, in order.
        let steps = resultArray responses 163 |> Seq.map asStr |> Seq.toList
        Assert.Equal<string list>([ "a.nap"; "b.nap" ], steps)
    finally
        Directory.Delete(dir, true)
