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

    let info = (responseFor responses 100)[FResult]
    Assert.Equal("POST", info["method"].GetValue<string>())
    Assert.Equal("https://api.example.com/users", info["url"].GetValue<string>())
    Assert.Equal("application/json", info["headers"]["Accept"].GetValue<string>())

[<Fact>]
let ``in-process copyCurl returns a curl command for the request`` () =
    let responses =
        drive
            [ buildNotification MDidOpen (Some(didOpenParams NapUri 1 ValidPostWithHeader))
              buildRequest MExecuteCommand 101 (Some(executeCommandParams CmdCopyCurl NapUri)) ]

    let curl = (responseFor responses 101)[FResult].GetValue<string>()
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

    Assert.Null((responseFor responses 102)[FResult]) // parse error → none
    Assert.Null((responseFor responses 103)[FResult]) // parse error → none
    Assert.Null((responseFor responses 104)[FResult]) // never opened → none

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
                ((responseFor responses id)[FResult] :?> JsonArray)
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

    Assert.Null((responseFor responses 120)[FResult])
    Assert.Null((responseFor responses 121)[FResult])
    Assert.Null((responseFor responses 122)[FResult])

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

    let info responses id = (responseFor responses id)[FResult]

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

    Assert.Equal("GET", (info responses 130)["method"].GetValue<string>())
    // Newer version applied.
    Assert.Equal("POST", (info responses 131)["method"].GetValue<string>())
    Assert.Equal("https://example.com/v2", (info responses 131)["url"].GetValue<string>())
    // Stale (older version) change ignored — still the v2 content.
    Assert.Equal("POST", (info responses 132)["method"].GetValue<string>())
    // Empty contentChanges ignored.
    Assert.Equal("POST", (info responses 133)["method"].GetValue<string>())
    // Missing version (=> 0) is stale and ignored.
    Assert.Equal("POST", (info responses 134)["method"].GetValue<string>())
    Assert.Equal("https://example.com/v2", (info responses 134)["url"].GetValue<string>())
    // After close the document is gone.
    Assert.Null((info responses 135))
    Assert.Equal(0, ((responseFor responses 136)[FResult] :?> JsonArray).Count)

[<Fact>]
let ``in-process didOpen without a version still tracks the document`` () =
    let noVersionOpen =
        let td = JsonObject()
        td[FUri] <- str NapUri
        td[FLanguageId] <- str LangNap
        td[FText] <- str AllNapSections // no version → defaults to 0
        let p = JsonObject()
        p[FTextDocument] <- td
        p :> JsonNode

    let responses =
        drive
            [ buildNotification MDidOpen (Some noVersionOpen)
              buildRequest MDocumentSymbol 140 (Some(textDocParams NapUri)) ]

    Assert.Equal(7, ((responseFor responses 140)[FResult] :?> JsonArray).Count)
