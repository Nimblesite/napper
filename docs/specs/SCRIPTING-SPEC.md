# Nap Scripting Model

Scripts are external files referenced by relative path from the `[script]` section of a `.nap` file (or as a step in a `.naplist`). This keeps `.nap` files clean and makes scripts independently testable and reusable across many `.nap` files.

**Use whatever language you like.** Napper dispatches on file extension — pick the runtime your team already runs:

- `script-fsx` — F# scripts (`.fsx`) executed via `dotnet fsi`
- `script-csx` — C# scripts (`.csx`) executed via `dotnet script`
- `script-js` — JavaScript scripts (`.js` / `.mjs`) executed via Node.js
- `script-py` — Python scripts (`.py`) executed via Python 3

Every language sees the **same** `ctx` (request/response context) and `nap` (orchestration runner) surface, defined once by the language-agnostic context protocol (`script-protocol`) and exposed through a thin per-language client library (`script-sdk`). There is no "preferred" language — `.fsx` and `.csx` are genuinely nice, but a JavaScript or Python shop never has to touch .NET to script Napper.

---

## `script-context` — Script Context Object

The runtime injects a `NapContext` (`ctx`) object into every pre/post script. The members are identical across languages; only the spelling follows each language's conventions.

| Member | Available | Description |
|--------|-----------|-------------|
| `vars` | pre + post | Map of all resolved variables (mutable — see `set`) |
| `request` | pre + post | The request about to be sent (method, url, headers, body) |
| `response` | post only | The response: `status`, `headers`, `body` (raw), `json` (parsed when `Content-Type` is JSON), `durationMs` |
| `env` | pre + post | Current environment name |
| `set(key, value)` | pre + post | Set a variable for downstream steps |
| `fail(message)` | pre + post | Fail the test with a message (non-zero exit) |
| `log(message)` | pre + post | Write a line to test output |

The canonical shape, expressed as an F# record (the .NET runners inject this natively; other languages receive the same data via `script-protocol`):

```fsharp
type NapResponse = {
    StatusCode : int
    Headers    : Map<string, string>
    Body       : string          // raw body
    Json       : JsonElement     // parsed if Content-Type is JSON
    Duration   : TimeSpan
}

type NapContext = {
    Vars      : Map<string, string>   // mutable — scripts can set vars for downstream steps
    Request   : HttpRequestMessage    // pre-script only
    Response  : NapResponse           // post-script only (None in pre-script)
    Env       : string                // current environment name
    Fail      : string -> unit        // call to fail the test with a message
    Set       : string -> string -> unit  // set a variable for downstream steps
    Log       : string -> unit        // write to test output
}
```

---

## `script-post` — Example Post-Scripts

The same post-script — assert the returned user matches the requested id, then hand a token forward — in all four languages.

### F# (`validate-user.fsx`)

```fsharp
// ctx : NapContext is injected automatically
let user = ctx.Response.Json

if user.GetProperty("id").GetString() <> ctx.Vars["userId"] then
    ctx.Fail "User ID mismatch"

// Extract a token from the response and pass it to the next step
let token = user.GetProperty("sessionToken").GetString()
ctx.Set "token" token
```

### C# (`validate-user.csx`)

```csharp
// ctx is injected automatically
var user = ctx.Response.Json;

if (user.GetProperty("id").GetString() != ctx.Vars["userId"])
    ctx.Fail("User ID mismatch");

ctx.Set("token", user.GetProperty("sessionToken").GetString());
```

### JavaScript (`validate-user.js`)

```js
import { ctx } from "napper"; // resolved automatically — no npm install required

const user = ctx.response.json;

if (user.id !== ctx.vars.userId) ctx.fail("User ID mismatch");

ctx.set("token", user.sessionToken);
```

### Python (`validate_user.py`)

```python
from napper import ctx  # resolved automatically — no pip install required

user = ctx.response.json

if user["id"] != ctx.vars["userId"]:
    ctx.fail("User ID mismatch")

ctx.set("token", user["sessionToken"])
```

---

## `script-orchestration` — Script-Driven Execution (Inverse Model)

The relationship between `.nap` files and scripts works **both ways**:

**`.nap` file drives scripts** — a request file references one or more pre/post scripts.

**Script drives `.nap` files** — a script file can itself act as the entry point, orchestrating as many requests as needed. The runner (`nap`) is injected the same way `ctx` is.

### F# (`orchestrate.fsx`)

```fsharp
// ctx : NapContext injected; nap : NapRunner also injected
let loginResult = nap.Run "./auth/01_login.nap"
ctx.Set "token" (loginResult.Response.Json.GetProperty("token").GetString())

for userId in [1; 2; 3] do
    ctx.Set "userId" (string userId)
    let result = nap.Run "./users/get-user.nap"
    if result.Response.StatusCode <> 200 then
        ctx.Fail $"User {userId} not found"
```

### JavaScript (`orchestrate.js`)

```js
import { nap } from "napper";

const login = await nap.run("./auth/01_login.nap");
nap.vars.token = login.response.json.token;

for (const userId of [1, 2, 3]) {
  nap.vars.userId = String(userId);
  const result = await nap.run("./users/get-user.nap");
  if (result.response.status !== 200) nap.fail(`User ${userId} not found`);
}
```

### Python (`orchestrate.py`)

```python
from napper import nap

login = nap.run("./auth/01_login.nap")
nap.vars["token"] = login.response.json["token"]

for user_id in (1, 2, 3):
    nap.vars["userId"] = str(user_id)
    result = nap.run("./users/get-user.nap")
    if result.response.status != 200:
        nap.fail(f"User {user_id} not found")
```

This enables arbitrarily complex test flows — loops, branching, data-driven runs — without any special playlist syntax, in any supported language.

### `script-runner` — NapRunner

The `NapRunner` (`nap`) object injected into orchestration scripts:

| Member | Description |
|--------|-------------|
| `run(path)` | Run a `.nap` file, returns a result (`status`, `json`, `body`, `headers`, `durationMs`, `passed`) |
| `runList(path)` | Run a `.naplist` file, returns a list of results |
| `vars` | Shared, mutable variable bag (carried into every `run`/`runList`) |
| `log(message)` | Write a line to test output |
| `fail(message)` | Fail the orchestration with a message |

Canonical F# shape:

```fsharp
type NapRunner = {
    Run     : string -> NapResult          // run a .nap file, returns result
    RunList : string -> NapResult list     // run a .naplist file
    Vars    : Map<string, string>          // shared variable bag
}
```

A `.naplist` can reference an orchestration script as a step in **any** language, the same as any `.nap` file:

```naplist
[steps]
./auth/01_login.nap
./scripts/parametrized-user-tests.py   # Python script drives multiple .nap files
./scripts/seed-data.js                 # JavaScript step in the same playlist
./teardown/cleanup.nap
```

---

## `script-protocol` — Language-Agnostic Context Protocol

The .NET runners (`.fsx`/`.csx`) can inject `NapContext` as native objects. JavaScript and Python cannot receive .NET objects, so all non-.NET languages — and, for uniformity, the SDKs in every language — exchange context with Napper over a **single JSON protocol**. The protocol is defined once in `Napper.Core` (no per-language reimplementation) and is the contract every `script-sdk` client speaks.

### `script-protocol-in` — Context handed to the script

Before launching the runtime, Napper writes the context as a JSON document to a temp file and exposes its path in the `NAPPER_CONTEXT` environment variable:

```json
{
  "phase": "post",
  "env": "staging",
  "vars": { "userId": "42", "token": "" },
  "request": {
    "method": "GET",
    "url": "https://api.example.com/users/42",
    "headers": { "Accept": "application/json" },
    "body": null
  },
  "response": {
    "status": 200,
    "headers": { "Content-Type": "application/json" },
    "body": "{\"id\":\"42\",\"sessionToken\":\"abc\"}",
    "json": { "id": "42", "sessionToken": "abc" },
    "durationMs": 123
  }
}
```

`request` is present in both phases; `response` is present only when `phase` is `post`.

### `script-protocol-out` — Directives returned by the script

The SDK accumulates every `set`/`fail`/`log` call and writes them as a single JSON document to the path in the `NAPPER_RESULT` environment variable when the script exits:

```json
{
  "vars": { "token": "abc" },
  "failed": false,
  "failMessage": null,
  "logs": ["Created user 42"]
}
```

Napper merges `vars` into the downstream variable scope, prints `logs` to test output, and treats `failed: true` (or a non-zero exit code, or an uncaught exception) as a test failure — identical to the F#/C# exit-code contract that already exists.

### `script-protocol-orchestration` — Orchestration callbacks

`nap.run` / `nap.runList` do **not** use an IPC channel. The SDK invokes the Napper binary itself — its absolute path is injected as `NAPPER_BIN` — and parses standard `--output json`:

```
$NAPPER_BIN run ./auth/01_login.nap --output json --env staging --var userId=42
```

This reuses the CLI's own machine-readable output (`output-json`) as the orchestration ABI, so orchestration behaves identically whether driven by a script or invoked directly, in any language, with zero bespoke transport.

---

## `script-sdk` — Per-Language Client Libraries

Each non-.NET language gets a tiny client library that wraps `script-protocol` into the idiomatic `ctx` / `nap` surface from `script-context` and `script-runner`. The SDKs are the **only** per-language code; all behavior lives behind the shared protocol.

| Language | Package | Import |
|----------|---------|--------|
| JavaScript / TypeScript | `@nimblesite/napper` (npm) | `import { ctx, nap } from "napper"` |
| Python | `napper` (PyPI) | `from napper import ctx, nap` |

**Zero-install resolution.** Users should not need a package manager just to write a hook. The Napper binary **bundles** a copy of each SDK and prepends it to the runtime's module search path before launching:

- JavaScript: `NODE_PATH` is set so `require("napper")` / `import "napper"` resolves the bundled copy.
- Python: `PYTHONPATH` is set so `import napper` resolves the bundled copy.

Publishing to npm / PyPI is purely a convenience for editor tooling (type stubs, autocomplete) and for users who prefer to vendor the SDK explicitly — the bundled copy is authoritative and always version-matched to the CLI.

TypeScript `.d.ts` declarations and Python `.pyi` stubs ship with the SDK so editors give full completion on `ctx` and `nap`.

---

## `script-runtime` — Runtime Resolution

The Napper binary itself is a self-contained native binary with **zero runtime dependencies** for plain `.nap` / `.naplist` execution. Script hooks require the relevant language runtime, resolved per language (first match wins):

| Extensions | Runtime | Minimum | Resolution order |
|------------|---------|---------|------------------|
| `.fsx` | `dotnet fsi` | .NET 10 SDK | `nap.dotnetPath` → `DOTNET_ROOT` → `PATH` |
| `.csx` | `dotnet script` | .NET 10 SDK | `nap.dotnetPath` → `DOTNET_ROOT` → `PATH` |
| `.js` `.mjs` `.cjs` | `node` | Node.js 18 | `nap.nodePath` → `NAPPER_NODE` → `PATH` |
| `.py` | `python3` (fallback `python`) | Python 3.9 | `nap.pythonPath` → `NAPPER_PYTHON` → `PATH` |

If the required runtime is missing, Napper fails the step with an actionable message (which runtime, which extension, how to install) — it never silently skips a hook. The runtime is only needed by users who actually write script hooks in that language; a JavaScript shop never installs .NET, and a .NET shop never installs Node.

---

## `script-dispatch` — Language Extensibility

The `[script]` section and `.naplist` steps specify a file path. The runtime dispatches on file extension through a single mapping table (one source of truth, no scattered extension literals):

| Extension | Runner | Spec |
|-----------|--------|------|
| `.fsx` | F# Interactive (`dotnet fsi`) | `script-fsx` |
| `.csx` | C# scripting (`dotnet script`) | `script-csx` |
| `.js` `.mjs` `.cjs` | Node.js (`node`) | `script-js` |
| `.py` | Python 3 (`python3`) | `script-py` |
| `.ts` | Future — Deno / `tsx` | — |

Adding a language is: (1) one row in the dispatch table, (2) a `script-sdk` client speaking `script-protocol`, (3) a `script-runtime` resolver entry. The HTTP engine, variable scoping, assertions, and result handling are entirely language-agnostic and shared.
