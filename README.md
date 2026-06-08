<p align="center">
  <img src="src/Napper.VsCode/media/napper-icon.png" alt="Napper logo" width="100" height="100">
</p>

<h1 align="center">Napper</h1>

<p align="center">
  <strong>API Testing, Supercharged.</strong><br>
  Napper is a free, open-source API testing tool for anyone testing APIs. It runs from the command line and edits natively in VS Code, Zed, and any editor via a portable language server.
  Define HTTP requests as plain text <code>.nap</code> files, add declarative assertions, chain them into test suites, and run everything in CI/CD with JUnit output.
  As simple as curl for quick requests. As powerful as your own code &mdash; script in JavaScript, Python, F#, or C#.
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=Nimblesite.napper">VS Code Marketplace</a> &middot;
  <a href="https://napperapi.dev">Website</a> &middot;
  <a href="https://napperapi.dev/docs/">Documentation</a> &middot;
  <a href="https://github.com/Nimblesite/napper/releases">Releases</a>
</p>

---

<p align="center">
  <img src="screenshot.png" alt="Napper VS Code extension showing playlist test results with response headers and body inspection" width="720">
</p>

---

## What can Napper do?

Everything you need for API testing. Nothing you don't.

- **CLI First** (`cli-run`) &mdash; The command line is the product. Run requests, execute test suites, and integrate with CI/CD pipelines from your terminal. Napper ships as a self-contained **native binary** &mdash; not a .NET DLL &mdash; with zero runtime dependencies.
- **Editor-Native, LSP-Powered** (`vscode-extension`, `lsp`) &mdash; First-class extensions for VS Code and Zed &mdash; plus every VS Code-compatible editor (Cursor, Windsurf, Antigravity, VSCodium) via the [Open VSX Registry](https://open-vsx.org/extension/nimblesite/napper) &mdash; and a portable language server that brings completions, diagnostics, and hover to any LSP editor. Syntax highlighting (`vscode-syntax`), request explorer (`vscode-explorer`), environment switching (`vscode-env-switcher`), and Test Explorer integration (`vscode-test-explorer`). Never leave your editor.
- **Script in Any Language** (`script-js`, `script-py`, `script-fsx`, `script-csx`) &mdash; Add playlist steps and `[script]` pre/post hooks in JavaScript, Python, F#, or C# &mdash; whatever your team already runs, mixed freely in a single playlist. Real runtimes (Node.js, Python 3, .NET), full ecosystem access (npm, PyPI, NuGet), no sandbox. Scripts pass/fail by exit code; JavaScript and Python additionally get an injected `ctx` object (response, variables, `ctx.set` / `ctx.fail` / `ctx.log`).
- **Declarative Assertions** (`nap-assert`) &mdash; Assert on status codes (`assert-status`), JSON paths (`assert-equals`, `assert-exists`), headers (`assert-contains`), and response times (`assert-lt`) with a clean, readable syntax. No scripting required for simple checks.
- **Composable Playlists** (`naplist-file`) &mdash; Chain requests into test suites with `.naplist` files. Nest playlists (`naplist-nested`), reference folders (`naplist-folder-step`), pass variables between steps (`naplist-var-scope`).
- **OpenAPI Import** (`openapi-generate`) &mdash; Generate test files from any OpenAPI spec. Point it at a file, and Napper creates `.nap` files with requests, headers, bodies, and assertions. Optionally enhance with AI via GitHub Copilot (`vscode-openapi-ai`).
- **Plain Text, Git Friendly** (`nap-file`) &mdash; Every request is a `.nap` file. Every environment is a `.napenv` file (`env-file`). Version control everything. No binary blobs, no lock-in.

## Installation

### Editor Extension

**VS Code** &mdash; install from the Marketplace in one command:

```sh
code --install-extension nimblesite.napper
```

Or search **"Napper"** in the Extensions panel (`Ctrl+Shift+X` / `Cmd+Shift+X`) and click Install.

**VS Code forks &mdash; Cursor, Windsurf, Antigravity, VSCodium** &mdash; open the editor's Extensions panel and search **"Napper"**. The extension is published to the [Open VSX Registry](https://open-vsx.org/extension/nimblesite/napper) that these editors use. You can also grab the `.vsix` from [Releases](https://github.com/Nimblesite/napper/releases) and use **Install from VSIX...**.

**Zed** &mdash; install the Napper extension from Zed's extension registry.

**Any other editor** &mdash; the CLI ships a language server (`napper lsp`); point your editor's LSP client at it for completions, diagnostics, and hover.

> **Requirements:** VS Code 1.99.0 or later (or an equivalent fork). The extension shells out to the CLI, so install it too (below).

### CLI

The CLI is a self-contained native binary with **no runtime dependencies** &mdash; no .NET, Node, or Python required.

**macOS / Linux &mdash; [Homebrew](https://brew.sh):**
```sh
brew tap Nimblesite/tap
brew install napper
```

**Windows &mdash; [Scoop](https://scoop.sh):**
```powershell
scoop bucket add Nimblesite https://github.com/Nimblesite/scoop-bucket
scoop install napper
```

Verify the install:
```sh
napper --version
```

<details>
<summary><strong>Other install options</strong> &mdash; direct binary, install script, build from source</summary>

**Direct download** &mdash; grab the binary for your platform from the [latest release](https://github.com/Nimblesite/napper/releases/latest) (`napper-osx-arm64`, `napper-osx-x64`, `napper-linux-x64`, `napper-win-x64.exe`). On macOS / Linux, make it executable and put it on your `PATH`:
```sh
chmod +x napper-osx-arm64 && mv napper-osx-arm64 /usr/local/bin/napper
```

**Install script (macOS / Linux):**
```sh
curl -fsSL https://raw.githubusercontent.com/Nimblesite/napper/main/scripts/install.sh | bash
```

**Build from source** (requires the .NET SDK + `make`):
```sh
git clone https://github.com/Nimblesite/napper.git && cd napper && make install-binaries
```

</details>

> **Note:** Script hooks need a runtime only for the language you write in — JavaScript (`.js`) needs [Node.js 18+](https://nodejs.org/), Python (`.py`) needs [Python 3.9+](https://www.python.org/downloads/), and F# (`.fsx`) / C# (`.csx`) need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Runtimes are found by command name (`node`, `python3`, `dotnet`) on your `PATH`. Plain `.nap` and `.naplist` files need nothing extra. In JavaScript and Python the `ctx` object is injected automatically — nothing to `import`, no `npm install` / `pip install`.

See the [full installation guide](https://napperapi.dev/docs/installation/) for VSIX manual install, troubleshooting, and macOS Gatekeeper notes.

## How do you use Napper?

### Minimal request (`nap-minimal`)

A `.nap` file can be as simple as one line:

```
GET https://httpbin.org/get
```

### POST with body and assertions (`nap-body`, `nap-assert`)

```
[request]
method = POST
url = {{baseUrl}}/posts

[request.headers]
Content-Type = application/json
Accept = application/json

[request.body]
"""
{
  "title": "Nap Integration Test",
  "body": "This post was created by the Nap API testing tool",
  "userId": {{userId}}
}
"""

[assert]
status = 201
body.id exists
body.title = Nap Integration Test
body.userId = {{userId}}
```

### Full request with metadata and scripting (`nap-full`)

```
[meta]
name = Get user by ID
description = Fetches a single user and asserts shape
tags = users, smoke

[vars]
userId = 42

[request]
method = GET
url = https://api.example.com/users/{{userId}}

[request.headers]
Authorization = Bearer {{token}}
Accept = application/json

[assert]
status = 200
body.id = {{userId}}
body.name exists
headers.Content-Type contains "json"
duration < 500ms

[script]
pre = ./scripts/auth.js
post = ./scripts/validate-user.js
```

### Run from CLI

```sh
# Run a single request
napper run ./health.nap

# Run a full test suite
napper run ./smoke.naplist

# With environment + JUnit output
napper run ./tests/ --env staging --output junit
```

## What file formats does Napper use?

| Extension | Spec ID | Purpose | Example |
|-----------|---------|---------|---------|
| `.nap` | `nap-file` | Single HTTP request with optional assertions and scripts | `get-users.nap` |
| `.naplist` | `naplist-file` | Ordered playlist of steps (requests, scripts, nested playlists) | `smoke.naplist` |
| `.napenv` | `env-base` | Environment variables (base config, checked into git) | `.napenv` |
| `.napenv.local` | `env-local` | Local secrets (gitignored) | `.napenv.local` |
| `.napenv.<name>` | `env-named` | Named environment | `.napenv.staging` |
| `.js` / `.mjs` / `.cjs` | `script-js` | JavaScript scripts (Node.js) — playlist steps and pre/post hooks, with an injected `ctx` | `setup.js` |
| `.py` | `script-py` | Python scripts (Python 3) — playlist steps and pre/post hooks, with an injected `ctx` | `setup.py` |
| `.fsx` | `script-fsx` | F# scripts (`dotnet fsi`) — playlist steps and pre/post hooks (exit-code) | `setup.fsx` |
| `.csx` | `script-csx` | C# scripts (`dotnet script`) — playlist steps and pre/post hooks (exit-code) | `setup.csx` |

### Playlists

```
[meta]
name = JSONPlaceholder CRUD
description = Full create-read-update-delete lifecycle for posts

[steps]
../scripts/setup.fsx
./01_get-posts.nap
./02_get-post-by-id.nap
./03_create-post.nap
./04_update-post.nap
./05_patch-post.nap
./06_delete-post.nap
../scripts/teardown.fsx
```

**Runnable examples** ([`examples/`](examples/)): the same CRUD suite in each language — [`crud.naplist`](examples/jsonplaceholder/crud.naplist) (F#), [`crud-javascript.naplist`](examples/jsonplaceholder/crud-javascript.naplist), [`crud-python.naplist`](examples/jsonplaceholder/crud-python.naplist), [`crud-csharp.naplist`](examples/jsonplaceholder/crud-csharp.naplist) — plus [`mixed-scripts.naplist`](examples/jsonplaceholder/mixed-scripts.naplist) (all four languages in one playlist) and [`scripting-ctx/`](examples/scripting-ctx/) (the injected `ctx` object in JavaScript and Python: `ctx.set`, `ctx.fail`, `ctx.response`).

### Environments (`env-resolution`)

**`.napenv`** (base, checked into git):
```
baseUrl = https://jsonplaceholder.typicode.com
userId = 1
postId = 1
```

**`.napenv.local`** (secrets, gitignored):
```
token = eyJhbGci...
apiKey = sk-secret-key
```

Select a named environment with `--env`:
```sh
napper run ./smoke.naplist --env staging
```

Variable priority (highest wins):
1. `--var key=value` CLI flags (`cli-var`)
2. `.napenv.local` (`env-local`)
3. `.napenv.<name>` named environment (`env-named`)
4. `.napenv` base (`env-base`)
5. `[vars]` in `.nap`/`.naplist` files (`nap-vars`)

## OpenAPI Import

Generate `.nap` test files automatically from any OpenAPI or Swagger spec. Napper creates one file per operation, a `.naplist` playlist, and a `.napenv` environment file — giving you a working test suite in seconds.

**Supported formats:** OpenAPI 3.0.x, OpenAPI 3.1.x, Swagger 2.0 (JSON input).

### From the CLI

```sh
# Generate from a local spec file
napper generate openapi ./petstore.json --output-dir ./tests

# Output a JSON summary for scripting
napper generate openapi ./spec.json --output-dir ./tests --output json
```

### From VS Code

Open the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) and choose:

- **Napper: Import OpenAPI from URL** &mdash; paste a URL (e.g. `https://petstore3.swagger.io/api/v3/openapi.json`). Napper downloads the spec and generates files.
- **Napper: Import OpenAPI from File** &mdash; browse to a local `.json` spec file.

Both commands prompt for an output folder and offer basic or AI-enhanced generation.

### What gets generated

Endpoints are grouped into subdirectories by API tag:

```
tests/
├── pets/
│   ├── get-pets.nap
│   ├── post-pets.nap
│   └── get-pets-petId.nap
├── store/
│   └── get-store-inventory.nap
├── petstore.naplist
└── .napenv
```

Each `.nap` file includes the method, URL (with path params as `{{variables}}`), auth headers, request body (from schema), and status code assertions. The `.napenv` file contains the base URL from the spec's `servers` field and variable placeholders for auth tokens.

### AI Enhancement (optional)

With GitHub Copilot available, choose AI-enhanced generation to get:

- Semantic assertions beyond status codes (e.g. `body.email contains @`)
- Realistic test data in request bodies instead of placeholder values
- Logical playlist ordering (auth first, then CRUD in dependency order)

Falls back to basic generation automatically if Copilot is unavailable.

See the [full OpenAPI import guide](https://napperapi.dev/docs/openapi-import/) for authentication handling, `$ref` resolution, customisation tips, and troubleshooting.

## CLI Reference

```
Usage:
  napper run <file|folder>                              Run a .nap file, .naplist playlist, or folder (cli-run)
  napper check <file>                                   Validate a .nap or .naplist file (cli-check)
  napper generate openapi <spec> --output-dir <dir>     Generate .nap files from OpenAPI spec (cli-generate)
  napper help                                           Show this help

Options:
  --env <name>              Environment name (loads .napenv.<name>) (cli-env)
  --var <key=value>         Variable override (repeatable) (cli-var)
  --output <format>         Output: pretty, junit, json, ndjson (cli-output)
  --output-dir <dir>        Output directory for generate command (cli-output-dir)
  --version                 Print the installed CLI version
  --verbose                 Enable debug-level logging (cli-verbose)
```

### Exit Codes (`cli-exit-codes`)

| Exit Code | Meaning |
|-----------|---------|
| 0 | All assertions passed |
| 1 | One or more assertions failed |
| 2 | Runtime error (network, script error, parse error) |

## How does Napper compare to other API testing tools?

| Feature | Napper | Postman | Bruno | .http files |
|---------|--------|---------|-------|-------------|
| CLI-first design | Yes | No | GUI-first | No CLI |
| Editor integration | VS Code, Cursor, Windsurf, Antigravity, Zed & LSP | Separate app | Separate app | VS Code only |
| Git-friendly files | Yes | JSON blobs | Yes | Yes |
| OpenAPI import | URL + file + AI | Import only | Import only | No |
| Assertions | Declarative + scripts | JS scripts | JS scripts | None |
| Full scripting language | JS, Python, F#, C# | Sandboxed JS | Sandboxed JS | None |
| CI/CD output formats | JUnit, JSON, NDJSON | Via Newman | Via CLI | None |
| Test Explorer | Native | No | No | No |
| Free & open source | Yes | Freemium | Yes | Yes |
| No account required | Yes | Account needed | Yes | Yes |

## Built with F#

Napper's engine is written entirely in **F#**. Parsing, environment resolution, the HTTP runner, assertions, OpenAPI generation, and the language server all live in a single shared core (`Napper.Core`) &mdash; the CLI (`Napper.Cli`) and the language server (`Napper.Lsp`) are thin shells over it, so behaviour is identical in your terminal and your editor.

- **One shared core, zero duplication** &mdash; `Napper.Cli` and `Napper.Lsp` both build on `Napper.Core`. The same parser that powers `napper run` powers editor completions and diagnostics.
- **Compiled to a native binary** &mdash; the CLI publishes as a self-contained [NativeAOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) executable: no .NET runtime, ~10 ms cold start, one statically-linked binary per platform.
- **Parser combinators, not regex** &mdash; `.nap`, `.naplist`, and `.napenv` files are parsed with [FParsec](https://www.quanttec.com/fparsec/) for precise, position-aware diagnostics.
- **Functional core** &mdash; immutable models, `Result`-based error handling, and pure functions over the request/response pipeline.

Curious how it fits together? Start with [`src/Napper.Core/`](src/Napper.Core/).

## Project Structure

```
my-api/
├── .napenv                    # Base variables (checked in)
├── .napenv.local              # Secrets (gitignored)
├── .napenv.staging            # Staging environment
├── auth/
│   ├── 01_login.nap
│   └── 02_refresh-token.nap
├── users/
│   ├── 01_get-user.nap
│   ├── 02_create-user.nap
│   └── 03_delete-user.nap
├── scripts/
│   ├── setup.fsx
│   ├── setup.csx
│   └── teardown.fsx
└── smoke.naplist
```

## License

MIT
