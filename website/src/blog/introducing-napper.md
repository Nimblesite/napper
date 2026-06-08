---
layout: layouts/blog.njk
title: "Introducing Napper: CLI-First API Testing, Scripted in Your Language"
date: 2026-02-27
author: Christian Findlay
tags: posts
category: announcements
excerpt: "Meet Napper — a free, open-source API testing tool for anyone testing APIs. The CLI is the product, everything is plain text, and you script in the language you already use: JavaScript, Python, F#, or C#."
description: "Introducing Napper, a free, open-source, CLI-first API testing tool for VS Code, Zed, and any editor. A modern alternative to Postman, Bruno, and .http files with scripting in JavaScript, Python, F#, or C#, declarative assertions, composable test suites, built-in .http file conversion, and CI/CD integration via JUnit XML."
keywords: "API testing, VS Code extension, Zed extension, Cursor, Windsurf, Antigravity, Open VSX, language server, JavaScript scripting, Python scripting, F# scripting, C# scripting, CLI API testing, Postman alternative, Bruno alternative, HTTP testing, REST API testing, CI/CD testing, JUnit XML, open source API testing tool, http file converter, convert http to nap"
---

# Introducing Napper: CLI-First API Testing, Scripted in Your Language

API testing tools have a problem. They're either too simple ([.http files](/docs/vs-http-files/) with no assertions and no CLI) or too heavy ([Postman](/docs/vs-postman/) with its mandatory accounts, cloud sync, and paid tiers). [Bruno](/docs/vs-bruno/) moved the needle with git-friendly collections, but it's still a GUI-first tool with sandboxed JavaScript.

**[Napper](https://github.com/Nimblesite/napper)** takes a different approach. It's a free, open-source API testing tool for *anyone* testing APIs: the CLI is the primary interface, everything is stored as plain text, and you script in the language you already use — **JavaScript, Python, F#, or C#** — on a real runtime, with no sandbox. Napper ships as a self-contained native binary (not a .NET DLL) and edits natively in [VS Code](https://code.visualstudio.com/), [Zed](https://zed.dev/), the VS Code-compatible editors Cursor, Windsurf, and Antigravity, and any editor via a portable language server.

## The CLI is the product

Napper is not a GUI with a CLI bolted on. The command line is the primary interface. Every feature works from the terminal first, then from [VS Code](https://code.visualstudio.com/) second.

```bash
# Run a single request
napper run ./health.nap

# Run a full test suite
napper run ./smoke.naplist

# Run with a specific environment and JUnit XML output for CI/CD
napper run ./tests/ --env staging --output junit > results.xml
```

The CLI binary is self-contained with no runtime dependencies. It runs on Windows, macOS, and Linux. Install it with [Homebrew](https://brew.sh) (`brew tap Nimblesite/tap && brew install napper`) or [Scoop](https://scoop.sh) (`scoop bucket add Nimblesite https://github.com/Nimblesite/scoop-bucket && scoop install napper`), or download it from [GitHub Releases](https://github.com/Nimblesite/napper/releases).

## Plain text everything — git-friendly by design

Every request is a [.nap file](/docs/nap-files/). Every test suite is a [.naplist file](/docs/naplist-files/). Every environment is a [.napenv file](/docs/environments/). All plain text. All in your repo. Diffs are readable. Code reviews are meaningful. No binary blobs, no JSON dumps, no proprietary formats.

Here's what a `.nap` file looks like:

{% raw %}
```
[meta]
name = Create a new post
tags = posts, crud

[request]
method = POST
url = {{baseUrl}}/posts

[request.headers]
Content-Type = application/json
Authorization = Bearer {{token}}

[request.body]
"""
{
  "title": "Nap Integration Test",
  "body": "Created by Napper",
  "userId": 1
}
"""

[assert]
status = 201
body.id exists
body.title = Nap Integration Test
duration < 2s
```
{% endraw %}

That's a complete HTTP request with headers, a JSON body, and [declarative assertions](/docs/assertions/) — all in one readable file. No scripting needed for the common cases.

## Scripting in your language — real runtimes, no sandbox

This is where Napper breaks away from every other API testing tool. [Postman](/docs/vs-postman/) and [Bruno](/docs/vs-bruno/) give you a sandboxed JavaScript environment with limited APIs. Napper lets you script in **JavaScript, Python, F#, or C#** — whichever your team already runs — on the real runtime, with full access to npm, PyPI, and NuGet. In JavaScript and Python, Napper injects a global `ctx` object that exposes the request/response and lets a script pass variables to later steps.

Here's the same post-request hook — extract a user id, chain it forward, validate — in [JavaScript](/docs/javascript-scripting/) and [Python](/docs/python-scripting/):

```js
// validate-response.js
// ctx is injected as a global — no import, no npm install
const body = ctx.response.json;
ctx.set("userId", String(body.id));
if (body.id <= 0) ctx.fail("User ID must be positive");
ctx.log(`Created user ${body.id}`);
```

```python
# validate_response.py
# ctx is injected as a global — no import, no pip install
body = ctx.response.json
ctx.set("userId", str(body["id"]))
if body["id"] <= 0:
    ctx.fail("User ID must be positive")
ctx.log(f"Created user {body['id']}")
```

### F# and C# run too — on the full .NET SDK

Prefer .NET? F# (`.fsx`) and C# (`.csx`) scripts run as playlist steps and `[script]` pre/post hooks via `dotnet fsi` and `dotnet script`, with the entire NuGet ecosystem. Today they communicate through stdout and their exit code — the injected `ctx` object is JavaScript and Python only — so a `.csx` step that exits non-zero fails the run:

```csharp
// guard.csx — a playlist step or a [script] post hook
var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
if (string.IsNullOrEmpty(baseUrl))
{
    Console.Error.WriteLine("API_BASE_URL is not set");
    Environment.Exit(1);   // non-zero exit -> the step fails
}
Console.WriteLine("[guard] environment looks good");
```

C# and F# scripts can use `HttpClient`, `System.Text.Json`, `System.Security.Cryptography`, LINQ, `async`/`await` — everything .NET offers — and any [NuGet](https://www.nuget.org/) package. No sandbox. The runnable [`crud-csharp.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud-csharp.naplist) example wraps the CRUD requests in C# setup and teardown steps.

You can mix languages in the same project. A single `.naplist` can reference `.js`, `.py`, `.csx`, and `.fsx` files as steps. Choose whichever language your team already tests with — or use several. See the [Scripting Overview](/docs/scripting/) for the full picture.

## Declarative assertions — no scripting needed for the common cases

Most API tests check the same things: status codes, JSON values, headers, and response times. Napper's [assertion syntax](/docs/assertions/) handles all of this declaratively — no scripting required:

```
[assert]
status = 200
body.id = 1
body.name exists
body.email contains "@"
headers.Content-Type contains "application/json"
duration < 500ms
```

All assertions are evaluated and reported individually. When the declarative syntax isn't enough, drop into [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), [C#](/docs/csharp-scripting/), or [F#](/docs/fsharp-scripting/) for complex validation logic.

## Composable test suites with .naplist files

Chain requests into ordered test suites with [.naplist files](/docs/naplist-files/). Nest playlists inside other playlists, reference entire folders, and mix `.nap` requests with `.js`, `.py`, `.csx`, and `.fsx` scripts:

```
[meta]
name = Full API Suite

[steps]
./scripts/setup.csx
./auth/login.nap
./crud-tests.naplist
./edge-cases/
./scripts/teardown.csx
```

## Built for CI/CD from day one

Napper is designed for [continuous integration](/docs/ci-integration/). The CLI binary is self-contained with no runtime dependencies. It outputs [JUnit XML](https://github.com/testmoapp/junitxml), JSON, and NDJSON formats natively (`cli-output`).

### GitHub Actions

```yaml
name: API Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Download Napper CLI
        run: |
          curl -L -o napper https://github.com/Nimblesite/napper/releases/latest/download/napper-linux-x64
          chmod +x napper
          sudo mv napper /usr/local/bin/

      - name: Run API tests
        run: napper run ./tests/ --env ci --output junit > results.xml

      - name: Upload results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: api-test-results
          path: results.xml
```

Napper exits with code `0` when all assertions pass and `1` when any assertion fails. This integrates natively with [GitHub Actions](https://github.com/features/actions), [GitLab CI](https://docs.gitlab.com/ci/), [Jenkins](https://www.jenkins.io/), [Azure DevOps](https://azure.microsoft.com/en-us/products/devops), and any CI platform that fails on non-zero exit codes.

## Migrate from .http files with one command

Already using `.http` files with VS Code REST Client or JetBrains IDEs? Napper includes a **built-in converter** that transforms your existing `.http` files into `.nap` format:

```bash
# Convert a single file
napper convert http ./requests.http

# Convert an entire directory
napper convert http ./api-tests/ --output-dir ./nap-tests/
```

The converter supports both **Microsoft** (VS Code REST Client) and **JetBrains** (IntelliJ, Rider, WebStorm) `.http` dialects. It maps variables to `.napenv` files, preserves request names, converts JetBrains `http-client.env.json` environments, and warns about unsupported features like WebSocket or gRPC requests.

Migration is non-destructive — your original `.http` files are untouched. Use `--dry-run` to preview what will be generated before writing any files. Once converted, you get all the benefits of Napper: declarative assertions, composable test suites, scripting in JavaScript, Python, F#, or C#, and CI/CD integration.

See [Napper vs .http files](/docs/vs-http-files/) for a full comparison.

## Editor-native, LSP-powered

Napper meets you in your editor. There are first-class extensions for [VS Code](https://marketplace.visualstudio.com/items?itemName=nimblesite.napper) and [Zed](https://zed.dev/) — and because the extension is published to the [Open VSX Registry](https://open-vsx.org/extension/nimblesite/napper), it installs in every VS Code-compatible editor too: **Cursor**, **Windsurf**, **Antigravity**, and **VSCodium**. A portable **language server** brings completions, diagnostics, and hover to any other editor that speaks LSP. The [Napper VS Code extension](https://marketplace.visualstudio.com/items?itemName=nimblesite.napper) brings the full experience into your editor:

- **Syntax highlighting** for `.nap`, `.naplist`, and `.napenv` files
- **Request explorer** in the sidebar with a tree view of all requests and playlists
- **Run requests** directly from the editor with a single click
- **Environment switching** between dev, staging, production, and custom environments
- **Test Explorer integration** with native VS Code test results
- **Response inspection** with headers, body, and timing information
- **Copy as curl** to share requests with teammates who don't use Napper

The extension relies on the CLI binary to run requests — [install the CLI](/docs/installation/) first, then install the extension from the [VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=nimblesite.napper) (or search **Napper** in the Extensions panel of Cursor, Windsurf, or Antigravity):

```bash
code --install-extension nimblesite.napper
```

## How does Napper compare?

| Feature | Napper | [Postman](/docs/vs-postman/) | [Bruno](/docs/vs-bruno/) | [.http files](/docs/vs-http-files/) |
|---------|--------|---------|-------|-------------|
| CLI-first design | Yes | No | GUI-first | No CLI |
| Editor integration | VS Code, Cursor, Windsurf, Antigravity, Zed & LSP | Separate app | Separate app | REST Client |
| Git-friendly files | Plain text | JSON blobs | Yes | Yes |
| Assertions | Declarative + scripts | JS scripts | JS scripts | None |
| Scripting language | **JS, Python, F#, C#** | Sandboxed JS | Sandboxed JS | None |
| CI/CD output | JUnit, JSON, NDJSON | Via Newman | Via CLI | None |
| Test Explorer | Native | No | No | No |
| OpenAPI import | URL + file + AI | Import only | Import only | No |
| .http file migration | Built-in converter | Import only | No | N/A |
| Account required | No | Yes | No | No |
| Price | Free (MIT) | Freemium | Free (MIT) | Free |

## Get started in 5 minutes

1. [Install the CLI or VS Code extension](/docs/installation/)
2. Follow the [Quick Start guide](/docs/quick-start/) to create your first request
3. [Migrate existing .http files](/docs/vs-http-files/) with `napper convert http`
4. Add [assertions](/docs/assertions/) to validate responses
5. Set up [environments](/docs/environments/) for different targets
6. Write scripts in [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), [C#](/docs/csharp-scripting/), or [F#](/docs/fsharp-scripting/) for advanced flows
7. Run everything in [CI/CD](/docs/ci-integration/) with JUnit XML output

Napper is free, open source, and [MIT licensed](https://github.com/Nimblesite/napper/blob/main/LICENSE). Browse the source code and examples on [GitHub](https://github.com/Nimblesite/napper).
