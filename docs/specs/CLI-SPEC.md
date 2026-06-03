# Nap CLI Specification

> **Nap** (Network API Protocol) — a CLI-first, test-oriented alternative to Postman, Bruno, `.http` files, and curl.

---

## Vision

Nap is a developer-first HTTP testing tool. It is as simple as curl for one-off requests, but scales to full test suites with reusable components, scripted assertions, and CI integration. It is not a GUI-first tool with a CLI bolted on — the CLI is the product.

---

## Core Principles

1. **Files are the source of truth.** All requests, tests, and playlists are plain files. Git-friendly by default.
2. **Simple things are simple.** A single HTTP call should look almost as terse as curl.
3. **Tests are reusable components.** A `.nap` file (`nap-file`) is a reusable unit. It can be composed into playlists (`naplist-file`) without modification.
4. **Scripting is opt-in, external, and language-agnostic.** Scripts live in standalone files referenced by name — F# (`.fsx`), C# (`.csx`), JavaScript (`.js`), or Python (`.py`) (`script-fsx`, `script-csx`, `script-js`, `script-py`). Every language sees the same `ctx`/`nap` surface (`script-protocol`). Simple assertions need no scripting at all.
5. **No lock-in.** The format is plain text. Scripts are standard files in standard languages run by their standard runtimes — no proprietary sandbox. Results emit standard formats.

---

## Installation

**Native-binary channels only — end users never need .NET installed.** `napper` is a
self-contained NativeAOT binary. The VS Code extension bundles the matching per-platform binary
inside the VSIX ([`vscode-cli-acquisition`](./IDE-EXTENSION-SPEC.md#vscode-cli-acquisition)), so
installing the extension needs no separate CLI install. CLI users pick a channel below.

There is deliberately **no `dotnet tool` / NuGet channel**: a dotnet tool would force users to
install the .NET runtime, which [`cli-aot-migration`](#cli-aot-migration) removed.

### `cli-install-script` — install script (macOS / Linux / Windows)

```sh
curl -fsSL https://raw.githubusercontent.com/Nimblesite/napper/main/scripts/install.sh | bash
# Windows (PowerShell):
irm https://raw.githubusercontent.com/Nimblesite/napper/main/scripts/install.ps1 | iex
```

Downloads the native binary for the host platform from the GitHub Release and verifies its
SHA-256 against `checksums-sha256.txt`.

### `cli-install-homebrew` — Homebrew tap (macOS / Linux)

```sh
brew tap Nimblesite/tap && brew install napper
```

Tracks latest only. Published by [`update-homebrew`](../../.github/workflows/release.yml) on every release.

### `cli-install-scoop` — Scoop bucket (Windows)

```sh
scoop bucket add Nimblesite https://github.com/Nimblesite/scoop-bucket && scoop install napper
```

Tracks latest only. Published by [`update-scoop`](../../.github/workflows/release.yml) on every release.

### `cli-runtime-dependency` — Runtime dependency

**None.** `napper` is published with **NativeAOT** (`-p:PublishAot=true`, see
[`cli-aot-migration`](#cli-aot-migration)) as a single statically-linked native binary per RID.
End users need neither the .NET runtime nor the SDK to install or run it. .NET is a build-time
dependency only. (Script *hooks* — `.fsx`/`.csx`/`.js`/`.py` — still need their own language
runtime, but that is the script author's choice and never a dependency of `napper` itself;
see `script-runtime`.)

### `cli-aot-migration` — NativeAOT (landed)

`napper` ships as a NativeAOT binary (`PublishAot=true`): a single statically-linked native
binary per RID with zero runtime dependencies, ~5–10 MB, ~10 ms cold start. Distribution
channels are Brew / Scoop / the install script / the VSIX-bundled binary — there is **no
`dotnet tool` channel**. The VSIX install flow needs no .NET SDK prerequisite.

**AOT constraints** (enforced): no reflection-based serialization — `printf`, quotations, and
reflection fail at publish time; all third-party deps must be AOT-compatible. Verified by the
black-box e2e suite running the real native binary, and by the release `Verify binary version
contract` step.

Tracked in [CLI-PLAN.md](../plans/CLI-PLAN.md).

---

## Usage

### `cli-run` — Run Command

```sh
# Run a single request (simplest case — as easy as curl)
napper run ./users/get-user.nap

# Run a single request with inline variable override
napper run ./users/get-user.nap --var userId=99

# Run a collection (folder)
napper run ./users/

# Run a playlist
napper run ./smoke.naplist

# Specify environment
napper run ./smoke.naplist --env staging
```

### `cli-check` — Validate Syntax

```sh
# Validate syntax without running
napper check ./smoke.naplist
```

### `cli-generate` — Generate from OpenAPI

```sh
# Generate .nap files from an OpenAPI spec
napper generate openapi ./petstore.json --output-dir ./petstore/
```

See [CLI OpenAPI Generation](./CLI-OPENAPI-GENERATION.md) for full details.

### `cli-lsp` — Language Server

```sh
# Start the Nap language server (LSP 3.17 over stdio)
napper lsp
```

`napper lsp` runs the language server in the same process as the CLI. **The LSP and CLI are one binary** ([`lsp-one-binary`](./LSP-SPEC.md#lsp-one-binary)) — there is no separate `napper-lsp`. IDE extensions spawn `napper lsp` as a child process and communicate via JSON-RPC over stdin/stdout. While `lsp` is the active subcommand, the process MUST NOT write anything to stdout outside LSP framing — all logs go to stderr or to a file. See [LSP Specification](./LSP-SPEC.md) for capabilities and protocol details.

---

## CLI Flags

| Flag | Spec ID | Description |
|------|---------|-------------|
| `--env <name>` | `cli-env` | Load environment variables from `.napenv.<name>` (`env-named`) |
| `--var <key=value>` | `cli-var` | Override a variable (repeatable). Highest priority in `env-resolution` |
| `--output <format>` | `cli-output` | Output format: `output-pretty` (default), `output-junit`, `output-json`, `output-ndjson` |
| `--output-dir <dir>` | `cli-output-dir` | Destination directory for `cli-generate` |
| `--verbose` | `cli-verbose` | Enable debug-level logging |

---

## `cli-output` — Output Formats

| Format | Spec ID | Description |
|--------|---------|-------------|
| `pretty` | `output-pretty` | Human-readable console output with ANSI colors (default) |
| `junit` | `output-junit` | JUnit XML for CI/CD integration |
| `json` | `output-json` | Single JSON object per result |
| `ndjson` | `output-ndjson` | Newline-delimited JSON for streaming |

---

## `cli-exit-codes` — Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All assertions passed |
| 1 | One or more assertions failed |
| 2 | Runtime error (network, script error, parse error) |

---

## Related Specs

- [File Formats](./FILE-FORMATS-SPEC.md) — `.nap`, `.napenv`, `.naplist` format specifications
- [Scripting](./SCRIPTING-SPEC.md) — language-agnostic scripting model (F#, C#, JavaScript, Python), NapContext, NapRunner, the context protocol
- [CLI Plan](../plans/CLI-PLAN.md) — Parser, project layout, implementation phases
- [LSP Specification](./LSP-SPEC.md) — `napper lsp` subcommand: protocol, capabilities, transport
- [LSP Plan](../plans/LSP-PLAN.md) — LSP implementation phases (same `napper` binary)
- [OpenAPI Generation (CLI)](./CLI-OPENAPI-GENERATION.md) — Test suite generation from OpenAPI specs
