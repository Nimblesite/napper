---
layout: layouts/docs.njk
title: Scripting Overview
description: "Napper scripts in JavaScript, Python, F#, or C#. Same ctx and nap surface in every language, run by real runtimes — no sandbox. Pick the language you already test with."
keywords: "API testing scripting, JavaScript scripting, Python scripting, F# scripting, C# scripting, pre-request script, post-request script, test orchestration"
eleventyNavigation:
  key: "Scripting Overview"
  order: 6
---

# Scripting Overview

Most checks need no code at all — the declarative [`[assert]` block](/docs/assertions/) covers status codes, JSON paths, headers, and timing. When you need real logic, Napper lets you **script in the language you already use**.

| Language | Extension | Runtime | Guide |
|----------|-----------|---------|-------|
| JavaScript | `.js` / `.mjs` | Node.js 18+ | [JavaScript Scripting](/docs/javascript-scripting/) |
| Python | `.py` | Python 3.9+ | [Python Scripting](/docs/python-scripting/) |
| F# | `.fsx` | .NET 10 SDK | [F# Scripting](/docs/fsharp-scripting/) |
| C# | `.csx` | .NET 10 SDK | [C# Scripting](/docs/csharp-scripting/) |

There is no "preferred" language. Use whatever your team already tests with. `.fsx` and `.csx` happen to be genuinely lovely — concise, strongly typed, immutable by default — but they are never required.

## One surface, every language (spec: script-context, script-runner)

Every language sees the **same two objects**:

- **`ctx`** — the request/response context for pre/post hooks: `ctx.vars`, `ctx.request`, `ctx.response`, `ctx.set(...)`, `ctx.fail(...)`, `ctx.log(...)`.
- **`nap`** — the orchestration runner for script-driven flows: `nap.run(...)`, `nap.runList(...)`, `nap.vars`, `nap.fail(...)`.

The same post-script in four languages:

```js
// validate-user.js
import { ctx } from "napper";
const user = ctx.response.json;
if (user.id !== ctx.vars.userId) ctx.fail("User ID mismatch");
ctx.set("token", user.sessionToken);
```

```python
# validate_user.py
from napper import ctx
user = ctx.response.json
if user["id"] != ctx.vars["userId"]:
    ctx.fail("User ID mismatch")
ctx.set("token", user["sessionToken"])
```

```fsharp
// validate-user.fsx
let user = ctx.Response.Json
if user.GetProperty("id").GetString() <> ctx.Vars["userId"] then ctx.Fail "User ID mismatch"
ctx.Set "token" (user.GetProperty("sessionToken").GetString())
```

```csharp
// validate-user.csx
var user = ctx.Response.Json;
if (user.GetProperty("id").GetString() != ctx.Vars["userId"]) ctx.Fail("User ID mismatch");
ctx.Set("token", user.GetProperty("sessionToken").GetString());
```

## Real runtimes, no sandbox

Unlike the sandboxed JavaScript in Postman and Bruno, Napper scripts run on **real runtimes** — Node.js, Python 3, or .NET — with full access to npm, PyPI, and NuGet. Parse XML, call a database, generate a JWT, validate a schema, or pull in any package.

## How to reference a script (spec: nap-file, script-dispatch)

Reference scripts from a `.nap` file's `[script]` block, or use a script file as a `.naplist` step. Dispatch is by extension, so a single playlist can mix languages:

```
[steps]
./auth/01_login.nap
./scripts/seed-data.js          # JavaScript step
./scripts/parametrized-tests.py # Python step
./teardown/cleanup.nap
```

## Zero install (spec: script-sdk)

The Napper binary **bundles** the JavaScript and Python SDKs and puts them on the runtime's module path, so `import { ctx } from "napper"` (JS) and `from napper import ctx` (Python) just work — no `npm install`, no `pip install`. The published `@nimblesite/napper` (npm) and `napper` (PyPI) packages are conveniences for editor autocomplete and explicit vendoring.

## Requirements (spec: script-runtime)

The Napper binary itself is self-contained with no runtime dependencies. Script hooks need the relevant runtime only for the language you actually script in — a JavaScript shop never installs .NET, and a .NET shop never installs Node. See [Installation](/docs/installation/#prerequisites).
