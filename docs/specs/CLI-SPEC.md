# Nap CLI Specification

> **Nap** (Network API Protocol) — a CLI-first, test-oriented alternative to Postman, Bruno, `.http` files, and curl. The CLI is the product; every editor integration shells out to it.

Spec-ID convention: every section heading carries a `GROUP-TOPIC` slug in brackets (uppercase, hyphenated). Code and tests reference it in comments — e.g. `// Implements [CLI-RUN]`. In-file cross-references use the bare bracketed slug (greppable); cross-file references link the file, e.g. [CLI-RUN](./CLI-SPEC.md).

---

## [CLI-VISION] Vision

Nap is a developer-first HTTP testing tool: as terse as curl for a one-off request, but it scales to full test suites with reusable components, scripted assertions, and CI integration. It is not a GUI with a CLI bolted on.

## [CLI-PRINCIPLES] Core principles

1. **Files are the source of truth.** Requests, tests, and playlists are plain files. Git-friendly by default.
2. **Simple things stay simple.** A single HTTP call looks almost as terse as curl.
3. **Tests are reusable components.** A `.nap` file is a reusable unit; it composes into playlists (`.naplist`) without modification.
4. **Scripting is opt-in, external, and language-agnostic.** Scripts live in standalone files referenced by name — F#, C#, JavaScript, or Python. Every language sees the same `ctx`/`nap` surface. Simple assertions need no scripting. See [SCRIPTING-SPEC](./SCRIPTING-SPEC.md).
5. **No lock-in.** Plain-text format; scripts run on their standard runtimes; results emit standard formats.

---

## [CLI-INSTALL] Installation

The primary channels are **native-binary** — end users never need .NET. `napper` is a self-contained NativeAOT binary ([CLI-AOT-MIGRATION]). The VS Code extension bundles the matching per-platform binary inside the VSIX ([VSCODE-CLI-ACQUIRE](./IDE-EXTENSION-SPEC.md)), so installing the extension needs no separate CLI install. The VS Code extension never resolves the CLI via the NuGet `dotnet tool` channel ([SWR-IDE-RESOLUTION]); it only uses the bundled native binary.

The install channels below are release infrastructure (`scripts/install.*`, `.github/workflows/release.yml`) verified end-to-end by the release pipeline; they have no unit tests.

### [CLI-INSTALL-SCRIPT] Install script (macOS / Linux / Windows)

```sh
curl -fsSL https://raw.githubusercontent.com/Nimblesite/napper/main/scripts/install.sh | bash
# Windows (PowerShell):
irm https://raw.githubusercontent.com/Nimblesite/napper/main/scripts/install.ps1 | iex
```

Downloads the host-platform binary from the GitHub Release and verifies its SHA-256 against `checksums-sha256.txt`. Implemented by `scripts/install.sh` / `scripts/install.ps1`.

### [CLI-INSTALL-HOMEBREW] Homebrew tap (macOS / Linux)

```sh
brew tap Nimblesite/tap && brew install napper
```

Tracks latest only. Published by the `update-homebrew` job in `.github/workflows/release.yml` on every release.

### [CLI-INSTALL-SCOOP] Scoop bucket (Windows)

```sh
scoop bucket add Nimblesite https://github.com/Nimblesite/scoop-bucket && scoop install napper
```

Tracks latest only. Published by the `update-scoop` job in `.github/workflows/release.yml` on every release.

### [CLI-INSTALL-DOTNET-TOOL] dotnet tool (secondary, optional)

```sh
dotnet tool install -g napper                    # latest
dotnet tool install -g napper --version 0.12.0   # exact version
dotnet tool update  -g napper                    # update
```

For .NET developers who prefer it. This is the **only** channel that needs the **.NET 10 SDK**; all other channels need no .NET. Published best-effort by the non-blocking `publish-nuget` job — a NuGet failure never blocks a release, and the VS Code extension never resolves the CLI this way ([SWR-IDE-RESOLUTION]).

### [CLI-RUNTIME-DEPENDENCY] Runtime dependency

**None.** `napper` ships as a single statically-linked NativeAOT binary per RID ([CLI-AOT-MIGRATION]). End users need neither the .NET runtime nor the SDK. .NET is a build-time dependency only. (Script *hooks* still need their own language runtime — that is the script author's choice, never a dependency of `napper` itself; see [SCRIPT-RUNTIME](./SCRIPTING-SPEC.md).)

### [CLI-AOT-MIGRATION] NativeAOT (landed)

`napper` ships as a NativeAOT binary (`-p:PublishAot=true`): one statically-linked native binary per RID, zero runtime dependencies, ~5–10 MB, ~10 ms cold start. Primary distribution is the native binary — Brew / Scoop / install script / VSIX-bundled. The secondary `dotnet tool` ([CLI-INSTALL-DOTNET-TOOL]) is best-effort.

**AOT constraints (enforced):** no reflection-based serialization — `printf`, quotations, and reflection fail at publish time; all third-party deps must be AOT-compatible. Verified by the black-box e2e suite running the real native binary and by the release "Verify binary version contract" step.

---

## [CLI-USAGE] Usage

### [CLI-RUN] Run command

```sh
napper run ./users/get-user.nap                 # single request — as easy as curl
napper run ./users/get-user.nap --var userId=99 # with an inline variable override
napper run ./users/                             # a collection (folder)
napper run ./smoke.naplist                      # a playlist
napper run ./smoke.naplist --env staging        # with an environment
```

### [CLI-CHECK] Validate syntax

```sh
napper check ./smoke.naplist   # parse and validate without running
```

### [CLI-GENERATE] Generate from OpenAPI

```sh
napper generate openapi ./petstore.json --output-dir ./petstore/
```

See [OpenAPI Generation (CLI)](./CLI-OPENAPI-GENERATION.md) for full details.

### [CLI-CONVERT] Convert .http files

```sh
napper convert http ./requests.http --output-dir ./nap-requests/
```

Converts Microsoft / JetBrains `.http` files to `.nap`. See [HTTP Files](./HTTP-FILES-SPEC.md).

### [CLI-LSP] Language server

```sh
napper lsp   # start the Nap language server (LSP 3.17 over stdio)
```

`napper lsp` runs the language server in the same process as the CLI. **The LSP and CLI are one binary** ([LSP-ONE-BINARY](./LSP-SPEC.md)) — there is no separate `napper-lsp`. IDE extensions spawn `napper lsp` and communicate via JSON-RPC over stdin/stdout. While `lsp` is the active subcommand the process MUST NOT write to stdout outside LSP framing — all logs go to stderr or a file. See [LSP Specification](./LSP-SPEC.md).

---

## [CLI-FLAGS] CLI flags

| Flag | Spec ID | Description |
|------|---------|-------------|
| `--env <name>` | `[CLI-ENV]` | Load environment variables from `.napenv.<name>` ([ENV-NAMED](./FILE-FORMATS-SPEC.md)) |
| `--var <key=value>` | `[CLI-VAR]` | Override a variable (repeatable). Highest priority in [ENV-RESOLUTION](./FILE-FORMATS-SPEC.md) |
| `--output <format>` | `[CLI-OUTPUT]` | Output format: `[OUTPUT-PRETTY]` (default), `[OUTPUT-JUNIT]`, `[OUTPUT-JSON]`, `[OUTPUT-NDJSON]` |
| `--output-dir <dir>` | `[CLI-OUTPUT-DIR]` | Destination directory for `[CLI-GENERATE]` / `[CLI-CONVERT]` |
| `--verbose` | `[CLI-VERBOSE]` | Enable debug-level logging |

---

## [CLI-OUTPUT] Output formats

| Value | Spec ID | Description |
|-------|---------|-------------|
| `pretty` | `[OUTPUT-PRETTY]` | Human-readable console output with ANSI colors (default) |
| `junit` | `[OUTPUT-JUNIT]` | JUnit XML for CI integration |
| `json` | `[OUTPUT-JSON]` | One JSON object/array per result |
| `ndjson` | `[OUTPUT-NDJSON]` | Newline-delimited JSON for streaming |

---

## [CLI-EXIT-CODES] Exit codes

| Code | Meaning |
|------|---------|
| 0 | All assertions passed |
| 1 | One or more assertions failed |
| 2 | Runtime error (network, script error, parse error) |

---

## Related specs

- [File Formats](./FILE-FORMATS-SPEC.md) — `.nap`, `.napenv`, `.naplist` formats
- [Scripting](./SCRIPTING-SPEC.md) — language-agnostic scripting model, `NapContext`, the context protocol
- [LSP Specification](./LSP-SPEC.md) — the `napper lsp` subcommand
- [OpenAPI Generation (CLI)](./CLI-OPENAPI-GENERATION.md) — test-suite generation from OpenAPI specs
- [HTTP Files](./HTTP-FILES-SPEC.md) — `.http` conversion
- [CLI Plan](../plans/CLI-PLAN.md) — parser, project layout, implementation phases
