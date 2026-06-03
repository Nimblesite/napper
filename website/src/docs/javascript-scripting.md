---
layout: layouts/docs.njk
title: JavaScript Scripting
description: "Use JavaScript scripts for pre/post request hooks and test orchestration in Napper. Real Node.js runtime, full npm access, no sandbox."
keywords: "JavaScript scripting, Node.js API testing, js, pre-request script, post-request script, test orchestration, napper sdk"
eleventyNavigation:
  key: "JavaScript Scripting"
  order: 6.1
---

# JavaScript Scripting (spec: script-js)

Napper runs JavaScript scripts (`.js` / `.mjs` files) via **Node.js** for pre/post request hooks and test orchestration. Scripts run on the real Node runtime with full access to npm — no sandbox, no limits.

## Pre/post request hooks (spec: script-pre, script-post)

Reference scripts in your `.nap` file:

```
[script]
pre = ./scripts/setup-auth.js
post = ./scripts/validate-response.js
```

Import the injected `ctx` from the bundled `napper` module — no `npm install` required:

### Pre-request scripts (spec: script-pre)

Run before the HTTP request is sent. Use them to set up authentication, generate dynamic data, or modify variables.

```js
// setup-auth.js
import { ctx } from "napper";

const token = generateToken();
ctx.set("token", token);
ctx.log(`Token generated: ${token.slice(0, 8)}...`);
```

### Post-request scripts (spec: script-post)

Run after the response is received. Use them for complex validation, data extraction, or chaining.

```js
// validate-response.js
import { ctx } from "napper";

const body = ctx.response.json;

// Extract and pass to the next step
ctx.set("userId", String(body.id));

// Complex validation
if (body.id <= 0) ctx.fail("User ID must be positive");

ctx.log(`Created user ${body.id}`);
```

## NapContext (spec: script-context)

Scripts receive a `ctx` object with these members:

| Member | Available | Description |
|--------|-----------|-------------|
| `ctx.vars` | Pre + Post | Object of all resolved variables |
| `ctx.request` | Pre + Post | The request about to be sent (`method`, `url`, `headers`, `body`) |
| `ctx.response` | Post only | Response with `status`, `headers`, `body`, `json`, `durationMs` |
| `ctx.env` | Pre + Post | Current environment name |
| `ctx.set(key, value)` | Pre + Post | Set a variable for downstream steps |
| `ctx.fail(message)` | Pre + Post | Fail the test with a message |
| `ctx.log(message)` | Pre + Post | Write to test output |

## Orchestration scripts (spec: script-orchestration)

For complex flows, a `.js` file can be the entry point and drive requests directly with the injected `nap` runner:

```js
// orchestration.js
import { nap } from "napper";

// Run a request and get the result
const login = await nap.run("./auth/login.nap");

// Extract token from the response
nap.vars.token = login.response.json.token;

// Run a suite of tests with the token
const results = await nap.runList("./crud-tests.naplist");

// Data-driven testing
for (const userId of [1, 2, 3, 42, 99]) {
  nap.vars.userId = String(userId);
  const result = await nap.run("./users/get-user.nap");
  if (result.response.status !== 200) nap.fail(`User ${userId} failed`);
}
```

Reference orchestration scripts in a `.naplist`:

```
[steps]
./scripts/orchestration.js
```

## NapRunner (spec: script-runner)

Orchestration scripts receive a `nap` object:

| Member | Description |
|--------|-------------|
| `nap.run(path)` | Run a `.nap` file, returns a result (`status`, `json`, `body`, `headers`, `durationMs`, `passed`) |
| `nap.runList(path)` | Run a `.naplist` file, returns a list of results |
| `nap.vars` | Shared, mutable variable object |
| `nap.log(message)` | Write to test output |
| `nap.fail(message)` | Fail the orchestration with a message |

## How it works (spec: script-protocol)

Napper hands the context to your script as JSON and reads back any `set`/`fail`/`log` calls — the bundled `napper` SDK wraps this protocol into the idiomatic `ctx` / `nap` objects above. Orchestration calls (`nap.run`) invoke the Napper binary itself with `--output json`, so script-driven and direct runs behave identically.

## Editor autocomplete

The bundled SDK ships TypeScript `.d.ts` declarations, so editors give full completion on `ctx` and `nap`. For explicit vendoring or CI caching, install the published package:

```bash
npm install --save-dev @nimblesite/napper
```

## Requirements (spec: script-runtime)

JavaScript scripts require **Node.js 18+** on the machine. The Napper CLI binary itself is self-contained; `.js` scripts are executed via `node`, resolved from the `nap.nodePath` setting, the `NAPPER_NODE` environment variable, or your `PATH`. Plain `.nap` and `.naplist` files need no runtime at all.

Prefer Python? See [Python Scripting](/docs/python-scripting/). Already in .NET? [F#](/docs/fsharp-scripting/) and [C#](/docs/csharp-scripting/) give the same surface.
