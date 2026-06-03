# Scripting Languages — JavaScript & Python Implementation Plan

> Implements [`script-js`](../specs/SCRIPTING-SPEC.md#script-js), [`script-py`](../specs/SCRIPTING-SPEC.md#script-py), [`script-protocol`](../specs/SCRIPTING-SPEC.md#script-protocol), [`script-sdk`](../specs/SCRIPTING-SPEC.md#script-sdk), [`script-runtime`](../specs/SCRIPTING-SPEC.md#script-runtime), [`script-dispatch`](../specs/SCRIPTING-SPEC.md#script-dispatch).

This plan adds **JavaScript** (`.js`/`.mjs`/`.cjs`, via Node.js) and **Python** (`.py`, via Python 3) as first-class scripting languages, alongside the existing F# (`.fsx`) and C# (`.csx`) runners. The goal: a JavaScript or Python shop can write pre/post hooks and full orchestration scripts **without ever installing .NET**, and every language sees the identical `ctx` / `nap` surface.

---

## Why

Napper is an API testing tool for *anyone* testing APIs — not only .NET developers. Today scripting is locked to `.fsx`/`.csx`, which forces a .NET SDK on every team that wants a hook. JavaScript and Python are the two most common languages among API testers, so they unlock the largest audience. `.fsx` and `.csx` remain excellent first-class options — this plan widens the door, it does not narrow it.

---

## Design: one protocol, many languages

The existing `.fsx`/`.csx` runners inject `NapContext` as **native .NET objects**. That cannot work for Node or Python. Rather than bolt on two more bespoke injection paths, we introduce a **single language-agnostic protocol** ([`script-protocol`](../specs/SCRIPTING-SPEC.md#script-protocol)) and a **thin per-language SDK** ([`script-sdk`](../specs/SCRIPTING-SPEC.md#script-sdk)) that wraps it into idiomatic `ctx`/`nap`.

```
                       ┌──────────────────────────────────────────┐
                       │            Napper.Core (F#)               │
   .nap / .naplist ───►│  Runner → ScriptDispatch (extension map)  │
                       │            │                              │
                       │            ├─ .fsx/.csx → native inject   │  (existing)
                       │            └─ .js/.py   → ScriptProtocol  │  (new, shared)
                       └────────────┬─────────────────────────────┘
                                    │  context JSON (NAPPER_CONTEXT)
                                    │  result  JSON (NAPPER_RESULT)
                                    │  binary  path (NAPPER_BIN)
                       ┌────────────▼──────────┐   ┌────────────────────────┐
                       │  node <script>.js     │   │  python3 <script>.py    │
                       │  @nimblesite/napper   │   │  napper (PyPI)          │
                       │  → ctx / nap          │   │  → ctx / nap            │
                       └───────────────────────┘   └────────────────────────┘
```

**Non-negotiable per [CLAUDE.md]:** the protocol (serialization of context in, directives out, orchestration ABI) lives **once** in `Napper.Core`. The runtime-resolution and dispatch tables live **once** in `Napper.Core`. The SDKs are the *only* per-language code, and each is a thin wrapper — no domain logic, no HTTP, no assertions, no variable scoping reimplemented per language.

### Context in / directives out

- Napper serializes the context (`phase`, `env`, `vars`, `request`, `response`) to a temp file; path in `NAPPER_CONTEXT` ([`script-protocol-in`](../specs/SCRIPTING-SPEC.md#script-protocol-in)).
- The SDK buffers `set`/`fail`/`log` calls and writes them to the file at `NAPPER_RESULT` on exit ([`script-protocol-out`](../specs/SCRIPTING-SPEC.md#script-protocol-out)).
- Napper merges `vars`, prints `logs`, and fails on `failed: true` / non-zero exit / uncaught exception — the **same** exit-code contract `runScript` already enforces today.

### Orchestration ABI

`nap.run` / `nap.runList` shell out to the Napper binary (`NAPPER_BIN`) with `--output json` and parse the result ([`script-protocol-orchestration`](../specs/SCRIPTING-SPEC.md#script-protocol-orchestration)). No IPC server, no long-lived socket — the CLI's existing machine-readable output *is* the orchestration ABI. This requires `output-json` to be stable and complete (status, headers, body, parsed json, durationMs, passed).

---

## Touch points in the current code

- [`src/Napper.Core/Runner.fs`](../../src/Napper.Core/Runner.fs) — `scriptArgs` (currently a two-branch `if csx … else fsi`) and `runScript`. These get refactored to drive a shared dispatch table and the protocol. **No regex** on paths — match on a normalized extension via a lookup, per [CLAUDE.md].
- [`src/Napper.Core/Types.fs`](../../src/Napper.Core/Types.fs) — add the protocol DTOs (`ScriptContextDto`, `ScriptResultDto`) and a `ScriptLanguage` union.
- New `src/Napper.Core/ScriptProtocol.fs` — serialize context, deserialize result, merge into `NapResult`/vars. AOT-safe `System.Text.Json` (source-generated, no reflection — `cli-aot-migration` already forbids reflection).
- New `src/Napper.Core/ScriptRuntime.fs` — the resolver table ([`script-runtime`](../specs/SCRIPTING-SPEC.md#script-runtime)): extension → (executable, arg template, resolution chain, min version, install hint).
- New SDK packages: `sdk/js/` (`@nimblesite/napper`, npm) and `sdk/python/` (`napper`, PyPI), each bundled into the CLI publish output and exposed via `NODE_PATH` / `PYTHONPATH`.
- [`Makefile`](../../Makefile) — build/test/bundle the SDKs; wire into `make test` and the publish artifacts.

Keep files under the F# 500 LOC / function 20 LOC limits — `ScriptProtocol.fs` and `ScriptRuntime.fs` exist precisely so `Runner.fs` does not grow.

---

## Implementation phases

### Phase 1 — Shared protocol & dispatch (Napper.Core)
- `ScriptLanguage` union + extension→language dispatch table (one source of truth). Refactor `scriptArgs`/`runScript` to consume it; existing `.fsx`/`.csx` behavior unchanged (covered by current e2e tests).
- Protocol DTOs + `ScriptProtocol.fs` (context-out, result-in, merge). Source-generated JSON for AOT.
- `ScriptRuntime.fs` resolver with actionable "runtime not found" errors (never silently skip a hook).
- Heavy logging at every step (start, resolved runtime, exit code, merged vars) per [CLAUDE.md].

### Phase 2 — JavaScript runner + SDK
- `node` resolution (`nap.nodePath` → `NAPPER_NODE` → `PATH`), min Node 18.
- `@nimblesite/napper`: read `NAPPER_CONTEXT`, expose `ctx` (`vars`, `request`, `response.json`, `set`, `fail`, `log`) and `nap` (`run`, `runList`, `vars`, `log`, `fail` → shell `NAPPER_BIN`). Flush to `NAPPER_RESULT` on `process.on('exit')`. Ship `.d.ts`.
- Bundle into CLI publish; set `NODE_PATH` so `import "napper"` resolves with **zero npm install**.

### Phase 3 — Python runner + SDK
- `python3`/`python` resolution (`nap.pythonPath` → `NAPPER_PYTHON` → `PATH`), min Python 3.9.
- `napper` (PyPI): same surface as the JS SDK, Pythonic spelling (`ctx.vars["x"]`, `ctx.set(...)`). Flush to `NAPPER_RESULT` via `atexit`. Ship `.pyi` stubs.
- Bundle into CLI publish; set `PYTHONPATH` so `import napper` resolves with **zero pip install**.

### Phase 4 — Tooling, docs, distribution
- IDE: editors pick up the bundled `.d.ts`/`.pyi` automatically for `ctx`/`nap` completion (no change to the Nap LSP — script files are owned by each language's own LSP). Document the path so users can point their editor at it.
- Publish `@nimblesite/napper` to npm and `napper` to PyPI (conveniences; the bundled copy stays authoritative and version-matched).
- Website + README: language-agnostic scripting story (see [website repositioning](#website--readme-repositioning)).
- Examples: add `.js` and `.py` hooks/orchestration to `examples/`.

---

## Testing strategy

Per [CLAUDE.md] — **e2e, black-box, through the real CLI** (separate files from unit tests; add assertions to existing suites where they already exercise scripting):

- **Pre-hook in JS/Python** sets a var → assert a downstream `.nap` step sees it (observe via CLI output, not internal state).
- **Post-hook in JS/Python** reads `ctx.response.json`, calls `ctx.fail` → assert the run reports failure with the message and a non-zero exit code.
- **Orchestration** `.js`/`.py` as a `.naplist` step drives multiple `.nap` files in a loop → assert each request's result in CLI output.
- **Mixed-language playlist** — one `.naplist` with `.nap`, `.fsx`, `.js`, and `.py` steps all passing → assert combined JUnit/JSON output.
- **Missing runtime** — run a `.py` hook with Python absent → assert the actionable error names Python and the extension (never a silent skip).
- **Zero-install** — run JS/Python hooks in a clean environment with no `npm install`/`pip install` → assert the bundled SDK resolves.
- Add assertions to the existing `script-*` e2e suite rather than duplicating it; never remove assertions.

---

## Risks & decisions

- **Orchestration via subprocess `--output json`** (not an IPC server): simplest, language-agnostic, reuses existing CLI output. Cost: one process spawn per `nap.run`. Acceptable; revisit only if a hot loop proves it.
- **Zero-install SDK via `NODE_PATH`/`PYTHONPATH`**: a user `node_modules`/site-packages `napper` could shadow the bundled copy. Decision: bundled copy is authoritative and version-stamped; SDK exposes `napper.version` and warns on mismatch.
- **AOT**: protocol serialization must be source-generated `System.Text.Json` — no reflection, per `cli-aot-migration`. Audit before merge.
- **`.ts`** deliberately deferred (Deno / `tsx`) — one future dispatch row, out of scope here.
- **F#/C# stay native**: we do *not* force `.fsx`/`.csx` onto the protocol in this plan (no behavior change, no regression risk). The protocol is the shared north star; migrating the .NET runners onto it is a later, optional consolidation.

---

## Website / README repositioning

Tracked alongside this plan (Phase 4) because the language story is user-facing:

- Drop "for F#/.NET developers" framing everywhere → "for anyone testing APIs." Scripting is **opt-in** and in the **language you already use**; `.fsx`/`.csx` are highlighted as genuinely nice, not required.
- Distribution: Napper ships **native binaries** (per-RID, AOT, zero runtime deps) — *not* .NET DLLs. The `dotnet tool` channel remains available as one option among Homebrew / Scoop / direct download / VSIX-bundled.
- Position Napper as a **Nimblesite IDE extension + portable LSP**, not a .NET tool.
- Add JavaScript & Python to every scripting surface: comparison tables, FAQ, feature cards, schema `featureList`, keywords, and new `/docs/javascript-scripting/` + `/docs/python-scripting/` pages.

---

## TODO

### Phase 1 — Shared protocol & dispatch (Napper.Core)
- [ ] Add `ScriptLanguage` union + extension→language dispatch table in `Napper.Core` (single source of truth) — `script-dispatch`
- [ ] Refactor `scriptArgs` / `runScript` in `Runner.fs` onto the dispatch table (no behavior change for `.fsx`/`.csx`)
- [ ] Add protocol DTOs (`ScriptContextDto`, `ScriptResultDto`) to `Types.fs` — `script-protocol`
- [ ] New `ScriptProtocol.fs` — serialize context → `NAPPER_CONTEXT`, parse `NAPPER_RESULT`, merge vars/logs/fail into `NapResult` (AOT-safe source-gen JSON)
- [ ] New `ScriptRuntime.fs` — runtime resolver table with actionable missing-runtime errors — `script-runtime`
- [ ] Heavy logging at start / resolved-runtime / exit-code / merged-vars
- [ ] e2e: existing `.fsx`/`.csx` suites still green after refactor

### Phase 2 — JavaScript runner + SDK
- [ ] `node` resolution (`nap.nodePath` → `NAPPER_NODE` → `PATH`), min Node 18 — `script-runtime`
- [ ] `sdk/js` `@nimblesite/napper`: `ctx` + `nap`, flush to `NAPPER_RESULT` on exit, orchestration via `NAPPER_BIN --output json`
- [ ] Ship `.d.ts` type declarations
- [ ] Bundle SDK into CLI publish; set `NODE_PATH` for zero-install `import "napper"`
- [ ] e2e: JS pre-hook sets var, post-hook fails, JS orchestration loop — `script-js`

### Phase 3 — Python runner + SDK
- [ ] `python3`/`python` resolution (`nap.pythonPath` → `NAPPER_PYTHON` → `PATH`), min Python 3.9 — `script-runtime`
- [ ] `sdk/python` `napper`: `ctx` + `nap`, flush via `atexit`, orchestration via `NAPPER_BIN --output json`
- [ ] Ship `.pyi` stubs
- [ ] Bundle SDK into CLI publish; set `PYTHONPATH` for zero-install `import napper`
- [ ] e2e: Python pre-hook sets var, post-hook fails, Python orchestration loop — `script-py`

### Phase 4 — Tooling, docs, distribution
- [ ] e2e: mixed-language `.naplist` (`.nap` + `.fsx` + `.js` + `.py`) → combined JUnit/JSON
- [ ] e2e: missing-runtime actionable error (never silent skip)
- [ ] e2e: zero-install resolution in a clean environment
- [ ] Publish `@nimblesite/napper` (npm) and `napper` (PyPI); CLI bundled copy stays authoritative + version-stamped
- [ ] `Makefile`: build/test/bundle both SDKs; wire into `make test` and publish artifacts
- [ ] `examples/`: add `.js` and `.py` hook + orchestration samples
- [ ] Website + README: language-agnostic repositioning + `/docs/javascript-scripting/` + `/docs/python-scripting/`
- [ ] Document the bundled SDK path for editor completion (`.d.ts` / `.pyi`)
