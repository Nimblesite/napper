// Tests [LSP-ONE-BINARY], [LSP-DISCOVERY], [LSP-TRANSPORT]
/// Integration tests for napper-lsp.
/// Every test launches the real binary and talks JSON-RPC over stdio —
/// the exact same protocol VSCode and Zed use. These prove the shipped binary
/// works end to end; coverage of [Napper.Lsp]* comes from the in-process
/// protocol tests (LspProtocolTests / LspCommandTests) which exercise the very
/// same LspRunner loop without the process boundary.
module Napper.Lsp.Tests.LspIntegrationTests

open System.Text.Json.Nodes
open System.Threading.Tasks
open Xunit
open Napper.Lsp.Tests.LspClient
open Napper.Lsp.Tests.LspWire

/// Run a full initialize handshake (initialize request + initialized notification)
let private handshake (server: LspServerProcess) : Task<JsonNode> =
    task {
        let! response = server.SendRequest(MInitialize, 1, initializeParams ())
        do! server.SendNotification(MInitialized, JsonObject())
        return response
    }

[<Fact>]
let ``initialize handshake returns capabilities`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()

        let! response = server.SendRequest(MInitialize, 1, initializeParams ())

        Assert.NotNull(response[FResult])
        Assert.Null(response[FError])

        let result = response[FResult]
        Assert.NotNull(result["capabilities"])

        // TextDocumentSync must be Full (1 = Full in LSP spec)
        let sync = result["capabilities"]["textDocumentSync"]
        Assert.NotNull(sync)
        Assert.Equal(1, sync.GetValue<int>())

        // Server info
        let serverInfo = result["serverInfo"]
        Assert.NotNull(serverInfo)
        Assert.Equal("napper-lsp", serverInfo["name"].GetValue<string>())
        Assert.NotNull(serverInfo["version"])

        Assert.True(server.IsRunning, "Server died after initialize")
    }

[<Fact>]
let ``initialized handshake leaves the real server fully operational`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()

        let! initResponse = server.SendRequest(MInitialize, 1, initializeParams ())
        Assert.Null(initResponse[FError])
        Assert.NotNull(initResponse[FResult])
        let caps = initResponse[FResult]["capabilities"]
        Assert.NotNull(caps)
        let sync = caps["textDocumentSync"]
        Assert.Equal(1, sync.GetValue<int>())

        do! server.SendNotification(MInitialized, JsonObject())

        // A synchronous round-trip is far stronger proof of liveness than a sleep:
        // open a doc and query it back through the real binary.
        let uri = "file:///tmp/post-init.nap"

        do!
            server.SendNotification(
                MDidOpen,
                didOpenParams uri 1 "[request]\nmethod = GET\nurl = https://example.com\n"
            )

        let! symResponse = server.SendRequest(MDocumentSymbol, 2, textDocParams uri)
        Assert.Null(symResponse[FError])
        let symbols = symResponse[FResult] :?> JsonArray
        Assert.True(symbols.Count >= 1, "server must answer real requests after the initialized handshake")
        Assert.True(server.IsRunning, "Server died after initialized notification")
    }

[<Fact>]
let ``textDocument/didOpen tracks document so symbols, lenses and requestInfo all see it`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.nap"

        let content =
            "[meta]\nname = \"T\"\n\n[request]\nmethod = GET\nurl = https://example.com\n"

        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        // documentSymbol proves the opened content is actually tracked.
        let! symResponse = server.SendRequest(MDocumentSymbol, 10, textDocParams uri)
        Assert.Null(symResponse[FError])
        let symbols = symResponse[FResult] :?> JsonArray
        let names = symbols |> Seq.map (fun s -> s["name"].GetValue<string>()) |> Seq.toList
        Assert.Contains("[meta]", names)
        Assert.Contains("[request]", names)

        // codeLens proves the [request] section produced a lens with the right detail.
        let! lensResponse = server.SendRequest(MCodeLens, 11, textDocParams uri)
        Assert.Null(lensResponse[FError])
        let lenses = lensResponse[FResult] :?> JsonArray
        Assert.True(lenses.Count >= 1, "expected a code lens on the [request] section")
        let lensData = lenses[0]["data"]
        Assert.NotNull(lensData)
        Assert.Equal("GET https://example.com", lensData.GetValue<string>())

        // requestInfo proves the parsed request round-trips through the real binary.
        let! infoResponse = server.SendRequest(MExecuteCommand, 12, executeCommandParams CmdRequestInfo uri)
        Assert.Null(infoResponse[FError])
        let info = infoResponse[FResult]
        let methodNode = info["method"]
        let urlNode = info["url"]
        Assert.Equal("GET", methodNode.GetValue<string>())
        Assert.Equal("https://example.com", urlNode.GetValue<string>())
        Assert.True(server.IsRunning, "Server died after didOpen")
    }

[<Fact>]
let ``textDocument/didChange replaces tracked content and ignores stale versions`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server
        let uri = "file:///tmp/test.nap"

        do!
            server.SendNotification(
                MDidOpen,
                didOpenParams uri 1 "[request]\nmethod = GET\nurl = https://example.com\n"
            )

        // Before the change the tracked request is the GET.
        let! before = server.SendRequest(MExecuteCommand, 20, executeCommandParams CmdRequestInfo uri)
        let beforeInfo = before[FResult]
        let beforeMethod = beforeInfo["method"]
        let beforeUrl = beforeInfo["url"]
        Assert.Equal("GET", beforeMethod.GetValue<string>())
        Assert.Equal("https://example.com", beforeUrl.GetValue<string>())

        // A newer version replaces the content.
        do!
            server.SendNotification(
                MDidChange,
                didChangeParams uri 2 "[request]\nmethod = POST\nurl = https://example.com/users\n"
            )

        let! after = server.SendRequest(MExecuteCommand, 21, executeCommandParams CmdRequestInfo uri)
        let afterInfo = after[FResult]
        let afterMethod = afterInfo["method"]
        let afterUrl = afterInfo["url"]
        Assert.Equal("POST", afterMethod.GetValue<string>())
        Assert.Equal("https://example.com/users", afterUrl.GetValue<string>())

        // A stale (older version) change must be ignored — content stays at v2.
        do!
            server.SendNotification(
                MDidChange,
                didChangeParams uri 1 "[request]\nmethod = PUT\nurl = https://example.com/stale\n"
            )

        let! stale = server.SendRequest(MExecuteCommand, 22, executeCommandParams CmdRequestInfo uri)
        let staleInfo = stale[FResult]
        let staleMethod = staleInfo["method"]
        let staleUrl = staleInfo["url"]
        Assert.Equal("POST", staleMethod.GetValue<string>())
        Assert.Equal("https://example.com/users", staleUrl.GetValue<string>())
        Assert.True(server.IsRunning, "Server died after didChange")
    }

[<Fact>]
let ``textDocument/didClose removes the document so later queries see nothing`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server
        let uri = "file:///tmp/test.nap"

        do!
            server.SendNotification(
                MDidOpen,
                didOpenParams uri 1 "[request]\nmethod = GET\nurl = https://example.com\n"
            )

        // While open: symbols are present and requestInfo resolves.
        let! openSyms = server.SendRequest(MDocumentSymbol, 30, textDocParams uri)
        Assert.Null(openSyms[FError])
        let openSymbols = openSyms[FResult] :?> JsonArray
        Assert.True(openSymbols.Count >= 1, "the [request] section should be visible while open")
        let! openInfo = server.SendRequest(MExecuteCommand, 31, executeCommandParams CmdRequestInfo uri)
        Assert.NotNull(openInfo[FResult])
        let openMethod = openInfo[FResult]["method"]
        Assert.Equal("GET", openMethod.GetValue<string>())

        // After close the document is gone: empty symbols and a null requestInfo.
        do! server.SendNotification(MDidClose, didCloseParams uri)
        let! closedSyms = server.SendRequest(MDocumentSymbol, 32, textDocParams uri)
        Assert.Null(closedSyms[FError])
        Assert.Equal(0, (closedSyms[FResult] :?> JsonArray).Count)
        let! closedInfo = server.SendRequest(MExecuteCommand, 33, executeCommandParams CmdRequestInfo uri)
        Assert.Null(closedInfo[FResult])
        Assert.True(server.IsRunning, "Server died after didClose")
    }

[<Fact>]
let ``shutdown and exit clean lifecycle`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let! shutdownResponse = server.SendRequest(MShutdown, 2)
        // Shutdown returns result (may be null for void) with no error
        Assert.Null(shutdownResponse[FError])
        Assert.True(server.IsRunning, "Server died before exit notification")

        do! server.SendNotification(MExit)
        do! Task.Delay(1000)

        Assert.False(server.IsRunning, "Server should have exited after exit notification")
    }

[<Fact>]
let ``malformed request with unknown params does not crash server`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        // Send a valid JSON-RPC request with a bogus method and garbage params
        let bogusParams = JsonObject()
        bogusParams["garbage"] <- str "nonsense"
        bogusParams["moreGarbage"] <- num 42
        let! response = server.SendRequest("textDocument/totallyBogusMethod", 999, bogusParams)

        // Should return an error, not crash
        Assert.NotNull(response[FError])
        Assert.True(server.IsRunning, "Server crashed on malformed request")

        // Verify it still responds to a valid request after the bogus one
        let! shutdownResponse = server.SendRequest(MShutdown, 100)
        Assert.Null(shutdownResponse[FError])
    }

[<Fact>]
let ``unknown method returns LSP error`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let! response = server.SendRequest("textDocument/somethingThatDoesNotExist", 42)

        Assert.NotNull(response[FError])
        Assert.True(server.IsRunning, "Server crashed on unknown method")
    }

// ─── Document Symbols ────────────────────────────────────

[<Fact>]
let ``documentSymbol returns sections for nap file`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.nap"

        let content =
            "[meta]\nname = \"Test\"\n\n[request]\nmethod = GET\nurl = https://example.com\n\n[assert]\nstatus = 200\n"

        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        let! response = server.SendRequest(MDocumentSymbol, 10, textDocParams uri)

        Assert.Null(response[FError])
        Assert.NotNull(response[FResult])

        let symbols = response[FResult] :?> JsonArray
        Assert.True(symbols.Count >= 3, $"Expected at least 3 symbols (meta, request, assert), got {symbols.Count}")

        // Check section names
        let names = symbols |> Seq.map (fun s -> s["name"].GetValue<string>()) |> Seq.toList
        Assert.Contains("[meta]", names)
        Assert.Contains("[request]", names)
        Assert.Contains("[assert]", names)
    }

[<Fact>]
let ``documentSymbol returns sections for naplist file`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.naplist"

        let content =
            "[meta]\nname = \"Smoke tests\"\n\n[steps]\nauth/login.nap\nusers/get-user.nap\n"

        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        let! response = server.SendRequest(MDocumentSymbol, 11, textDocParams uri)

        Assert.Null(response[FError])
        Assert.NotNull(response[FResult])

        let symbols = response[FResult] :?> JsonArray
        Assert.True(symbols.Count >= 2, $"Expected at least 2 symbols (meta, steps), got {symbols.Count}")

        let names = symbols |> Seq.map (fun s -> s["name"].GetValue<string>()) |> Seq.toList
        Assert.Contains("[meta]", names)
        Assert.Contains("[steps]", names)
    }

// ─── Code Lens ───────────────────────────────────────────

[<Fact>]
let ``codeLens returns lenses for nap file with request section`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.nap"
        let content = "[request]\nmethod = GET\nurl = https://example.com\n"
        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        let! response = server.SendRequest(MCodeLens, 12, textDocParams uri)

        Assert.Null(response[FError])
        Assert.NotNull(response[FResult])

        let lenses = response[FResult] :?> JsonArray
        Assert.True(lenses.Count >= 1, $"Expected at least 1 code lens, got {lenses.Count}")

        // First lens should be on line 0 (where [request] is)
        let firstLens = lenses[0]
        Assert.NotNull(firstLens["range"])
        let rangeNode = firstLens["range"]
        let startNode = rangeNode["start"]
        let startLine = startNode["line"].GetValue<int>()
        Assert.Equal(0, startLine)
    }

// ─── Execute Command: requestInfo ────────────────────────

[<Fact>]
let ``executeCommand requestInfo returns method and URL`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.nap"
        let content = "[request]\nmethod = POST\nurl = https://api.example.com/users\n"
        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        let! response = server.SendRequest(MExecuteCommand, 20, executeCommandParams CmdRequestInfo uri)

        Assert.Null(response[FError])
        Assert.NotNull(response[FResult])

        let result = response[FResult]
        Assert.Equal("POST", result["method"].GetValue<string>())
        Assert.Equal("https://api.example.com/users", result["url"].GetValue<string>())
    }

// ─── Execute Command: copyCurl ───────────────────────────

[<Fact>]
let ``executeCommand copyCurl returns curl string`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        let uri = "file:///tmp/test.nap"
        let content = "[request]\nmethod = GET\nurl = https://example.com/api\n"
        do! server.SendNotification(MDidOpen, didOpenParams uri 1 content)

        let! response = server.SendRequest(MExecuteCommand, 21, executeCommandParams CmdCopyCurl uri)

        Assert.Null(response[FError])
        Assert.NotNull(response[FResult])

        let curl = response[FResult].GetValue<string>()
        Assert.Contains("curl", curl)
        Assert.Contains("GET", curl)
        Assert.Contains("https://example.com/api", curl)
    }

// ─── Execute Command: listEnvironments ───────────────────

[<Fact>]
let ``executeCommand listEnvironments returns env names`` () : Task =
    task {
        use server = new LspServerProcess()
        server.Start()
        let! _ = handshake server

        // Create temp .napenv files
        let tmpDir =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"napper-lsp-test-{System.Guid.NewGuid()}")

        System.IO.Directory.CreateDirectory(tmpDir) |> ignore
        System.IO.File.WriteAllText(System.IO.Path.Combine(tmpDir, ".napenv"), "baseUrl = https://example.com")

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(tmpDir, ".napenv.staging"),
            "baseUrl = https://staging.example.com"
        )

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(tmpDir, ".napenv.production"),
            "baseUrl = https://prod.example.com"
        )

        System.IO.File.WriteAllText(System.IO.Path.Combine(tmpDir, ".napenv.local"), "secret = hunter2")

        try
            let rootUri = $"file://{tmpDir}"

            let! response = server.SendRequest(MExecuteCommand, 22, executeCommandParams CmdListEnvironments rootUri)

            Assert.Null(response[FError])
            Assert.NotNull(response[FResult])

            let envs = response[FResult] :?> JsonArray
            let envNames = envs |> Seq.map (fun e -> e.GetValue<string>()) |> Seq.toList

            // Should find staging and production, NOT base (.napenv) or local (.napenv.local)
            Assert.Contains("staging", envNames)
            Assert.Contains("production", envNames)
            Assert.DoesNotContain("local", envNames)
            Assert.Equal(2, envs.Count)
        finally
            System.IO.Directory.Delete(tmpDir, true)
    }
