---
layout: layouts/docs.njk
title: Python Scripting
description: "Use Python scripts for pre/post request hooks and test orchestration in Napper. Real Python 3 runtime, full PyPI access, no sandbox."
keywords: "Python scripting, Python API testing, py, pre-request script, post-request script, test orchestration, napper sdk"
eleventyNavigation:
  key: "Python Scripting"
  order: 6.2
---

# Python Scripting (spec: script-py)

Napper runs Python scripts (`.py` files) via **Python 3** for pre/post request hooks and test orchestration. Scripts run on the real Python runtime with full access to PyPI — no sandbox, no limits.

## Pre/post request hooks (spec: script-pre, script-post)

Reference scripts in your `.nap` file:

```
[script]
pre = ./scripts/setup_auth.py
post = ./scripts/validate_response.py
```

Import the injected `ctx` from the bundled `napper` module — no `pip install` required:

### Pre-request scripts (spec: script-pre)

Run before the HTTP request is sent. Use them to set up authentication, generate dynamic data, or modify variables.

```python
# setup_auth.py
from napper import ctx

token = generate_token()
ctx.set("token", token)
ctx.log(f"Token generated: {token[:8]}...")
```

### Post-request scripts (spec: script-post)

Run after the response is received. Use them for complex validation, data extraction, or chaining.

```python
# validate_response.py
from napper import ctx

body = ctx.response.json

# Extract and pass to the next step
ctx.set("userId", str(body["id"]))

# Complex validation
if body["id"] <= 0:
    ctx.fail("User ID must be positive")

ctx.log(f"Created user {body['id']}")
```

## NapContext (spec: script-context)

Scripts receive a `ctx` object with these members:

| Member | Available | Description |
|--------|-----------|-------------|
| `ctx.vars` | Pre + Post | Dict of all resolved variables |
| `ctx.request` | Pre + Post | The request about to be sent (`method`, `url`, `headers`, `body`) |
| `ctx.response` | Post only | Response with `status`, `headers`, `body`, `json`, `duration_ms` |
| `ctx.env` | Pre + Post | Current environment name |
| `ctx.set(key, value)` | Pre + Post | Set a variable for downstream steps |
| `ctx.fail(message)` | Pre + Post | Fail the test with a message |
| `ctx.log(message)` | Pre + Post | Write to test output |

## Orchestration scripts (spec: script-orchestration)

For complex flows, a `.py` file can be the entry point and drive requests directly with the injected `nap` runner:

```python
# orchestration.py
from napper import nap

# Run a request and get the result
login = nap.run("./auth/login.nap")

# Extract token from the response
nap.vars["token"] = login.response.json["token"]

# Run a suite of tests with the token
results = nap.run_list("./crud-tests.naplist")

# Data-driven testing
for user_id in (1, 2, 3, 42, 99):
    nap.vars["userId"] = str(user_id)
    result = nap.run("./users/get-user.nap")
    if result.response.status != 200:
        nap.fail(f"User {user_id} failed")
```

Reference orchestration scripts in a `.naplist`:

```
[steps]
./scripts/orchestration.py
```

## NapRunner (spec: script-runner)

Orchestration scripts receive a `nap` object:

| Member | Description |
|--------|-------------|
| `nap.run(path)` | Run a `.nap` file, returns a result (`status`, `json`, `body`, `headers`, `duration_ms`, `passed`) |
| `nap.run_list(path)` | Run a `.naplist` file, returns a list of results |
| `nap.vars` | Shared, mutable variable dict |
| `nap.log(message)` | Write to test output |
| `nap.fail(message)` | Fail the orchestration with a message |

## How it works (spec: script-protocol)

Napper hands the context to your script as JSON and reads back any `set`/`fail`/`log` calls — the bundled `napper` SDK wraps this protocol into the idiomatic `ctx` / `nap` objects above. Orchestration calls (`nap.run`) invoke the Napper binary itself with `--output json`, so script-driven and direct runs behave identically.

## Editor autocomplete

The bundled SDK ships `.pyi` type stubs, so editors give full completion on `ctx` and `nap`. For explicit vendoring or CI caching, install the published package:

```bash
pip install napper
```

## Requirements (spec: script-runtime)

Python scripts require **Python 3.9+** on the machine. The Napper CLI binary itself is self-contained; `.py` scripts are executed via `python3` (falling back to `python`), resolved from the `nap.pythonPath` setting, the `NAPPER_PYTHON` environment variable, or your `PATH`. Plain `.nap` and `.naplist` files need no runtime at all.

Prefer JavaScript? See [JavaScript Scripting](/docs/javascript-scripting/). Already in .NET? [F#](/docs/fsharp-scripting/) and [C#](/docs/csharp-scripting/) give the same surface.
