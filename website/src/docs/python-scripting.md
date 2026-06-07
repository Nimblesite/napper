---
layout: layouts/docs.njk
title: Python Scripting
description: "Script Napper in Python on the real Python 3 runtime — full PyPI access, no sandbox. An injected ctx global gives you the response, variables, and pass/fail control. No import, no install."
keywords: "Python scripting, Python API testing, py, post-request script, pre-request script, ctx, mixed language tests"
eleventyNavigation:
  key: "Python Scripting"
  order: 6.2
---

# Python Scripting (spec: script-py)

Napper runs Python scripts (`.py`) via **Python 3**, found as `python3` on your `PATH`. Scripts run on the real Python runtime with full access to PyPI — no sandbox, no limits.

A script can be a **step** in a `.naplist`, or a **pre/post hook** on a `.nap` request. It **passes when it exits `0`** and fails on any non-zero exit; everything it prints to stdout is captured into the run output.

## The injected `ctx` object (spec: script-context)

Napper injects a global named `ctx` into every Python script and hook. **There is no `import` and nothing to install** — `ctx` is already in scope.

| Member | Available | Description |
|--------|-----------|-------------|
| `ctx.env` | step + hook | Current environment name (`--env`), or `""` |
| `ctx.vars` | step + hook | Dict of all resolved variables (read) |
| `ctx.request` | hook | The request: `method`, `url`, `headers`, `body` |
| `ctx.response` | post hook | The response: `status`, `headers`, `body`, `json`, `duration_ms` |
| `ctx.set(key, value)` | step + hook | Set a variable visible to every later step |
| `ctx.fail(message)` | step + hook | Fail the step — even if the process exits 0 |
| `ctx.log(message)` | step + hook | Write a line to the run output |

## Pre/post request hooks (spec: script-pre, script-post)

Reference hooks from a `.nap` file's `[script]` block:

```
[script]
pre = ./scripts/setup_auth.py
post = ./scripts/validate_response.py
```

A **pre** hook runs before the request is sent — set up variables or log:

```python
# setup_auth.py
token = "demo-" + ctx.vars["userId"]
ctx.set("token", token)               # {% raw %}{{token}}{% endraw %} is now available to this request and later steps
ctx.log("token ready for env " + ctx.env)
```

A **post** hook runs after the response — validate, extract, or chain. If it calls `ctx.fail` (or exits non-zero), the request step fails even when the HTTP assertions passed:

```python
# validate_response.py
if ctx.response.status != 200:
    ctx.fail("expected 200, got " + str(ctx.response.status))
body = ctx.response.json              # parsed JSON, or None if the body isn't JSON
if body["id"] <= 0:
    ctx.fail("id must be positive")
ctx.set("postId", str(body["id"]))    # hand the id to the next step
ctx.log("created post " + str(body["id"]) + " in " + str(ctx.response.duration_ms) + "ms")
```

## Scripts as playlist steps (spec: naplist-script-step)

A `.py` step shares the playlist's variable scope through `ctx`, so it can seed data for the steps that follow:

```python
# seed.py
ctx.set("postId", "7")
ctx.log("seeded postId=7")
```

```
[steps]
./seed.py
./get-post.nap        # this request can use {% raw %}{{postId}}{% endraw %}
```

Runnable end-to-end: [`examples/scripting-ctx/ctx-demo-python.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/scripting-ctx/ctx-demo-python.naplist).

## Mixing with other languages (spec: script-dispatch)

Dispatch is by extension, so a `.naplist` can interleave Python with JavaScript, F#, and C# steps — see [`examples/jsonplaceholder/mixed-scripts.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/mixed-scripts.naplist) and the [Scripting Overview](/docs/scripting/).

## Requirements (spec: script-runtime)

Python scripts require **Python 3** available as `python3` on your `PATH`. The Napper CLI binary itself is self-contained; plain `.nap` and `.naplist` files need no runtime at all.

Prefer JavaScript? See [JavaScript Scripting](/docs/javascript-scripting/) — it has the same `ctx` surface. F# and C# scripts also run (as exit-code steps and hooks); see [F#](/docs/fsharp-scripting/) and [C#](/docs/csharp-scripting/).
