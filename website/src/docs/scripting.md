---
layout: layouts/docs.njk
title: Scripting Overview
description: "Napper scripts in JavaScript, Python, F#, or C#, run by real runtimes — no sandbox. Mix languages freely in one playlist. JavaScript and Python get an injected ctx object."
keywords: "API testing scripting, JavaScript scripting, Python scripting, F# scripting, C# scripting, post-request script, pre-request script, mixed language tests"
eleventyNavigation:
  key: "Scripting Overview"
  order: 6
---

# Scripting Overview

Most checks need no code at all — the declarative [`[assert]` block](/docs/assertions/) covers status codes, JSON paths, headers, and timing. When you need real logic, Napper runs a script in **the language you already use**.

| Language | Extension | Runtime | Guide |
|----------|-----------|---------|-------|
| JavaScript | `.js` / `.mjs` / `.cjs` | Node.js (`node`) | [JavaScript Scripting](/docs/javascript-scripting/) |
| Python | `.py` | Python 3 (`python3`) | [Python Scripting](/docs/python-scripting/) |
| F# | `.fsx` | .NET SDK (`dotnet fsi`) | [F# Scripting](/docs/fsharp-scripting/) |
| C# | `.csx` | .NET SDK (`dotnet script`) | [C# Scripting](/docs/csharp-scripting/) |

Dispatch is purely by file extension, so there is no "preferred" language and no configuration — drop in a `.js`, `.py`, `.fsx`, or `.csx` file and Napper runs it with the matching runtime.

## Two ways to run a script (spec: naplist-script-step, nap-script)

**As a playlist step.** List a script file as a step in a `.naplist`. It runs in order, in the same way a `.nap` request step does:

```
[steps]
./scripts/setup.py        # Python step
./01_get-posts.nap
./scripts/teardown.js      # JavaScript step
```

**As a pre/post hook on a request.** Reference a script from a `.nap` file's `[script]` block. `pre` runs before the request is sent; `post` runs after the response comes back:

```
[script]
pre = ./scripts/setup-auth.js
post = ./scripts/validate-response.js
```

## Pass / fail and output (spec: script-runtime)

A script — step or hook — **passes when its process exits `0`** and **fails on any non-zero exit**. When a `post` hook fails, its request step fails too, even if the HTTP assertions passed. Everything the script writes to stdout is captured into the run output and the JUnit/JSON reports.

## Mix and match languages (spec: script-dispatch)

Because dispatch is by extension, a single `.naplist` can interleave every language. This playlist drives the same CRUD requests through Python, JavaScript, F#, and C# steps:

```
[steps]
../scripts/setup.py
./01_get-posts.nap
../scripts/validate-env.js
./03_create-post.nap
../scripts/validate-env.fsx
./06_delete-post.nap
../scripts/teardown.js
```

Runnable: [`examples/jsonplaceholder/mixed-scripts.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/mixed-scripts.naplist), plus single-language suites [`crud-javascript.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud-javascript.naplist), [`crud-python.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud-python.naplist), and [`crud-csharp.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud-csharp.naplist).

## The `ctx` object — JavaScript and Python (spec: script-context)

In **JavaScript and Python**, Napper injects a global object named `ctx` into every script and hook. **There is nothing to import or install** — `ctx` is simply in scope.

| Member | Available | Description |
|--------|-----------|-------------|
| `ctx.env` | step + hook | Current environment name (`--env`), or `""` |
| `ctx.vars` | step + hook | All resolved variables (read) |
| `ctx.request` | hook | The request (`method`, `url`, `headers`, `body`) |
| `ctx.response` | post hook | The response: `status`, `headers`, `body`, `json`, `durationMs` (JS) / `duration_ms` (Python) |
| `ctx.set(key, value)` | step + hook | Set a variable visible to every later step |
| `ctx.fail(message)` | step + hook | Fail the step with a message (even if the process exits 0) |
| `ctx.log(message)` | step + hook | Write a line to the run output |

The same post hook in JavaScript and Python — assert the response, then hand a value forward:

```js
// validate-response.js — ctx is a global, no import
if (ctx.response.status !== 200) ctx.fail("expected 200, got " + ctx.response.status);
ctx.set("postId", String(ctx.response.json.id));
ctx.log("validated post " + ctx.response.json.id);
```

```python
# validate_response.py — ctx is a global, no import
if ctx.response.status != 200:
    ctx.fail("expected 200, got " + str(ctx.response.status))
ctx.set("postId", str(ctx.response.json["id"]))
ctx.log("validated post " + str(ctx.response.json["id"]))
```

A variable set with `ctx.set` flows into every later step. Runnable end-to-end: [`examples/scripting-ctx/ctx-demo.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/scripting-ctx/ctx-demo.naplist) (JavaScript) and [`ctx-demo-python.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/scripting-ctx/ctx-demo-python.naplist) (Python).

## F# and C# scripts (spec: script-fsx, script-csx)

F# (`.fsx`) and C# (`.csx`) scripts run on the full .NET SDK with complete NuGet access. They work as playlist steps and as `[script]` pre/post hooks, and they pass or fail by **exit code** — write to stdout to log, and return a non-zero exit code (or throw) to fail.

The injected `ctx` object is currently **JavaScript and Python only**. F#/C# scripts do not receive `ctx`; if you need to read the response or pass variables between steps from a script today, use JavaScript or Python. See the [F#](/docs/fsharp-scripting/) and [C#](/docs/csharp-scripting/) guides.

## Real runtimes, no sandbox

Unlike the sandboxed JavaScript in Postman and Bruno, Napper scripts run on **real runtimes** — Node.js, Python 3, or .NET — with full access to npm, PyPI, and NuGet. Parse XML, call a database, generate a JWT, validate a schema, or pull in any package your runtime can load.

## Requirements (spec: script-runtime)

The Napper binary itself is self-contained. A script needs only the runtime for the language it's written in, found on your `PATH`: JavaScript needs [`node`](https://nodejs.org/), Python needs [`python3`](https://www.python.org/downloads/), and F#/C# need the [.NET SDK](https://dotnet.microsoft.com/download). A JavaScript shop never installs .NET; a .NET shop never installs Node. Plain `.nap` and `.naplist` files with no scripts need no extra runtime at all.
