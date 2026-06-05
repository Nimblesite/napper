# Nap Scripting Model

Scripts are external files referenced by relative path from the `[script]` section of a `.nap` file (or as a step in a `.naplist`). This keeps `.nap` files clean and makes scripts independently testable and reusable.

**Use whatever language you like.** Napper dispatches on file extension ([SCRIPT-DISPATCH]) — pick the runtime your team already runs:

- `[SCRIPT-FSX]` — F# scripts (`.fsx`) via `dotnet fsi`
- `[SCRIPT-CSX]` — C# scripts (`.csx`) via `dotnet script`
- `[SCRIPT-JS]` — JavaScript (`.js` / `.mjs` / `.cjs`) via Node.js
- `[SCRIPT-PY]` — Python (`.py`) via Python 3

There is no "preferred" language — a JavaScript or Python shop never has to touch .NET to script Napper.

## [SCRIPT-STATUS] Implementation status

This spec describes the full intended design. What ships today vs. what is planned:

| Capability | Status |
|---|---|
| Script steps + `[script]` pre/post hooks, dispatched by extension ([SCRIPT-DISPATCH], [SCRIPT-FSX], [SCRIPT-CSX], [SCRIPT-JS], [SCRIPT-PY]) | **Shipped** — pass/fail by process exit code; stdout captured |
| Injected `ctx` in **JavaScript & Python** ([SCRIPT-CONTEXT]): `env`, `vars`, `request`, `response`, `set`, `fail`, `log` | **Shipped** — injected as a global; `ctx.set` threads to downstream steps |
| Context protocol ([SCRIPT-PROTOCOL-IN] / [SCRIPT-PROTOCOL-OUT]) over `NAPPER_CONTEXT` / `NAPPER_RESULT` temp files | **Shipped** |
| `ctx` for **F# / C#** ([SCRIPT-CONTEXT]) | **Not implemented** — F#/C# scripts run as exit-code hooks; no `ctx` is injected yet |
| `nap` / runner orchestration ([SCRIPT-ORCHESTRATION], [SCRIPT-RUNNER], [SCRIPT-PROTOCOL-ORCHESTRATION]) | **Not implemented** |
| Published SDK packages + `import`-based access ([SCRIPT-SDK]) | **Partial** — `ctx` is bundled and injected as a global; there is no `import` and no published npm/PyPI package |

The shipped behaviour is exercised black-box, through the CLI, by `CtxScriptTests`, `JsPyScriptTests`, `CsxScriptTests`, and `ScriptEdgeCaseTests`.

---

## [SCRIPT-CONTEXT] Script context object

The runtime injects a `NapContext` (`ctx`) into every pre/post script. Members are identical across languages; only the spelling follows each language's conventions. **Shipped for JavaScript and Python only**; F#/C# `ctx` injection is not implemented (those run as exit-code hooks).

| Member | Available | Description |
|--------|-----------|-------------|
| `vars` | pre + post | Map of all resolved variables (mutable — see `set`) |
| `request` | pre + post | The request about to be sent (method, url, headers, body) |
| `response` | post only | Response: `status`, `headers`, `body` (raw), `json` (parsed when Content-Type is JSON), `durationMs` |
| `env` | pre + post | Current environment name |
| `set(key, value)` | pre + post | Set a variable for downstream steps |
| `fail(message)` | pre + post | Fail the test with a message (non-zero exit) |
| `log(message)` | pre + post | Write a line to test output |

Canonical data shapes (typeDiagram — see [`Types.td`](../../src/Napper.Core/Types.td) for the parsed-file models):

```typediagram
type NapResponse {
  StatusCode: Int
  Headers:    Map<String, String>
  Body:       String
  Json:       JsonElement
  Duration:   Duration
}

type NapContext {
  Vars:     Map<String, String>
  Request:  HttpRequest
  Response: Option<NapResponse>
  Env:      String
}
```

The `set` / `fail` / `log` callbacks are operations on `ctx`, not data fields (see the member table above).

---

## [SCRIPT-PRE] Pre-scripts and [SCRIPT-POST] post-scripts

A `[script]` block names a `pre` script (runs before the request, `[SCRIPT-PRE]`) and/or a `post` script (runs after the response, `[SCRIPT-POST]`). The same post-script — assert the returned user matches the requested id, then hand a token forward — in all four languages:

JavaScript (`validate-user.js`) — **shipped** (`ctx` is a pre-injected global; the `import` line is the planned [SCRIPT-SDK] form and is not required today):

```js
const user = ctx.response.json;
if (user.id !== ctx.vars.userId) ctx.fail("User ID mismatch");
ctx.set("token", user.sessionToken);
```

Python (`validate_user.py`) — **shipped**:

```python
user = ctx.response.json
if user["id"] != ctx.vars["userId"]:
    ctx.fail("User ID mismatch")
ctx.set("token", user["sessionToken"])
```

F# (`validate-user.fsx`) and C# (`validate-user.csx`) — **planned**: today these run as exit-code hooks with no `ctx`. The intended surface mirrors JS/Python:

```fsharp
// PLANNED: ctx : NapContext injected automatically
let user = ctx.Response.Json
if user.GetProperty("id").GetString() <> ctx.Vars["userId"] then ctx.Fail "User ID mismatch"
ctx.Set "token" (user.GetProperty("sessionToken").GetString())
```

---

## [SCRIPT-ORCHESTRATION] Script-driven execution (inverse model)

> **Status: Not implemented.** The runner object (`nap`) is not injected and no orchestration entry point exists. This section is the intended design.

The relationship between `.nap` files and scripts is meant to work **both ways**:

- **`.nap` drives scripts** — a request file references pre/post scripts ([SCRIPT-PRE] / [SCRIPT-POST]). *(Shipped.)*
- **Script drives `.nap` files** — a script acts as the entry point, orchestrating many requests via an injected runner (`nap`). *(Planned.)*

```js
// PLANNED
const login = await nap.run("./auth/01_login.nap");
nap.vars.token = login.response.json.token;
for (const userId of [1, 2, 3]) {
  nap.vars.userId = String(userId);
  const result = await nap.run("./users/get-user.nap");
  if (result.response.status !== 200) nap.fail(`User ${userId} not found`);
}
```

### [SCRIPT-RUNNER] NapRunner

> **Status: Not implemented.**

The `NapRunner` (`nap`) injected into orchestration scripts:

| Member | Description |
|--------|-------------|
| `run(path)` | Run a `.nap` file; returns a result (`status`, `json`, `body`, `headers`, `durationMs`, `passed`) |
| `runList(path)` | Run a `.naplist`; returns a list of results |
| `vars` | Shared, mutable variable bag (carried into every `run`/`runList`) |
| `log(message)` / `fail(message)` | Write to test output / fail the orchestration |

```typediagram
type NapRunner {
  Run:     "string -> NapResult"
  RunList: "string -> List<NapResult>"
  Vars:    Map<String, String>
}
```

A `.naplist` may reference an orchestration script as a step in any language, the same as any `.nap` file ([NAPLIST-SCRIPT-STEP](./FILE-FORMATS-SPEC.md)).

---

## [SCRIPT-PROTOCOL] Language-agnostic context protocol

JavaScript and Python cannot receive .NET objects, so all non-.NET languages exchange context with Napper over a **single JSON protocol**, defined once in `Napper.Core` (no per-language reimplementation). It is the contract every [SCRIPT-SDK] client speaks.

### [SCRIPT-PROTOCOL-IN] Context handed to the script

Before launching the runtime, Napper writes the context as JSON to a temp file and exposes its path in `NAPPER_CONTEXT`:

```json
{
  "phase": "post",
  "env": "staging",
  "vars": { "userId": "42", "token": "" },
  "request": { "method": "GET", "url": "https://api.example.com/users/42", "headers": { "Accept": "application/json" }, "body": null },
  "response": { "status": 200, "headers": { "Content-Type": "application/json" }, "body": "{\"id\":\"42\",\"sessionToken\":\"abc\"}", "durationMs": 123 }
}
```

`request` is present in both phases; `response` only when `phase` is `post`. SDKs compute `json` from `body` on demand.

### [SCRIPT-PROTOCOL-OUT] Directives returned by the script

The SDK accumulates every `set`/`fail`/`log` call and writes them as JSON to the path in `NAPPER_RESULT` on exit:

```json
{ "vars": { "token": "abc" }, "failed": false, "failMessage": null, "logs": ["Created user 42"] }
```

Napper merges `vars` into the downstream scope, prints `logs`, and treats `failed: true` (or a non-zero exit, or an uncaught exception) as a failure — identical to the F#/C# exit-code contract.

### [SCRIPT-PROTOCOL-ORCHESTRATION] Orchestration callbacks

> **Status: Not implemented** (depends on [SCRIPT-RUNNER]).

`nap.run` / `nap.runList` are intended to invoke the Napper binary directly — its absolute path injected as `NAPPER_BIN` — and parse `--output json` ([OUTPUT-JSON](./CLI-SPEC.md)):

```
$NAPPER_BIN run ./auth/01_login.nap --output json --env staging --var userId=42
```

This reuses the CLI's machine-readable output as the orchestration ABI, so orchestration behaves identically whether driven by a script or invoked directly.

---

## [SCRIPT-SDK] Per-language client libraries

> **Status: Partial.** The SDK ships as a copy bundled inside the binary and injected as a global; there is no published npm/PyPI package and no `import`-based resolution yet.

Each non-.NET language gets a small client that wraps [SCRIPT-PROTOCOL] into the idiomatic `ctx` / `nap` surface.

| Language | Package (planned) | Import (planned) |
|----------|-------------------|------------------|
| JavaScript / TypeScript | `@nimblesite/napper` (npm) | `import { ctx, nap } from "napper"` |
| Python | `napper` (PyPI) | `from napper import ctx, nap` |

**Zero-install resolution (shipped form):** Napper bundles a copy of each SDK and prepends it to the runtime's module search path before launching — `NODE_PATH` for Node, `PYTHONPATH` for Python — and injects `ctx` as a global. Publishing to npm / PyPI (planned) is purely a convenience for editor tooling (type stubs, autocomplete) and explicit vendoring; the bundled copy is authoritative and always version-matched to the CLI.

---

## [SCRIPT-RUNTIME] Runtime resolution

The Napper binary itself has **zero runtime dependencies** for plain `.nap` / `.naplist` execution. Script hooks require the relevant language runtime, resolved per language (first match wins):

| Extensions | Runtime | Minimum | Resolution order |
|------------|---------|---------|------------------|
| `.fsx` | `dotnet fsi` | .NET 10 SDK | `nap.dotnetPath` → `DOTNET_ROOT` → `PATH` |
| `.csx` | `dotnet script` | .NET 10 SDK | `nap.dotnetPath` → `DOTNET_ROOT` → `PATH` |
| `.js` `.mjs` `.cjs` | `node` | Node.js 18 | `nap.nodePath` → `NAPPER_NODE` → `PATH` |
| `.py` | `python3` (fallback `python`) | Python 3.9 | `nap.pythonPath` → `NAPPER_PYTHON` → `PATH` |

If the required runtime is missing, Napper fails the step with an actionable message (which runtime, which extension, how to install) — it never silently skips a hook.

---

## [SCRIPT-DISPATCH] Language extensibility

The `[script]` section and `.naplist` steps name a file path. The runtime dispatches on file extension through a single mapping table (one source of truth):

| Extension | Runner | Spec |
|-----------|--------|------|
| `.fsx` | F# Interactive (`dotnet fsi`) | `[SCRIPT-FSX]` |
| `.csx` | C# scripting (`dotnet script`) | `[SCRIPT-CSX]` |
| `.js` `.mjs` `.cjs` | Node.js (`node`) | `[SCRIPT-JS]` |
| `.py` | Python 3 (`python3`) | `[SCRIPT-PY]` |
| `.ts` | Future — Deno / `tsx` | — |

Adding a language is: (1) one row in the dispatch table, (2) a [SCRIPT-SDK] client speaking [SCRIPT-PROTOCOL], (3) a [SCRIPT-RUNTIME] resolver entry. The HTTP engine, variable scoping, assertions, and result handling are language-agnostic and shared.
