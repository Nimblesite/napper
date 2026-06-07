---
layout: layouts/docs.njk
title: F# Scripting
description: "Run F# scripts as Napper playlist steps and pre/post request hooks on the full .NET SDK with complete NuGet access. Scripts pass or fail by exit code."
keywords: "F# scripting, fsx, dotnet fsi, post-request script, pre-request script, mixed language tests"
eleventyNavigation:
  key: "F# Scripting"
  order: 7
---

# F# Scripting (spec: script-fsx)

Napper runs F# Interactive scripts (`.fsx`) via **`dotnet fsi`** from the .NET SDK on your `PATH`. You get the full power of .NET and the entire NuGet ecosystem for setup, teardown, and validation logic.

## How F# scripts run (spec: script-runtime)

An F# script can be a **step** in a `.naplist`, or a **pre/post hook** on a `.nap` request. It **passes when it exits `0`** and **fails on any non-zero exit** (including an unhandled exception via `failwith`). Everything the script prints with `printfn` is captured into the run output and the JUnit/JSON reports.

```fsharp
// teardown.fsx — a playlist step or [script] post hook
printfn "[teardown] cleaning up test artifacts"
printfn "[teardown] timestamp: %O" System.DateTime.UtcNow
// returns exit code 0 -> the step passes
```

To **fail** a step, return a non-zero exit code or raise:

```fsharp
// guard.fsx
let required = System.Environment.GetEnvironmentVariable "API_BASE_URL"
if System.String.IsNullOrEmpty required then
    failwith "API_BASE_URL is not set"   // non-zero exit -> the step fails
printfn "[guard] environment looks good"
```

## As a playlist step (spec: naplist-script-step)

```
[steps]
../scripts/setup.fsx
./01_get-posts.nap
../scripts/teardown.fsx
```

Runnable scripts: [`examples/scripts/setup.fsx`](https://github.com/Nimblesite/napper/blob/main/examples/scripts/setup.fsx), `validate-env.fsx`, `smoke-tests.fsx`, `teardown.fsx`. The [`crud.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud.naplist) suite uses F# setup/teardown around the CRUD requests.

## As a pre/post hook (spec: script-pre, script-post)

```
[script]
pre = ./scripts/setup.fsx
post = ./scripts/teardown.fsx
```

A `pre` hook runs before the request; a `post` hook runs after. If a `post` hook exits non-zero, its request step fails even when the HTTP assertions passed.

## The `ctx` object is JavaScript/Python only

The injected `ctx` object — for reading the response, setting downstream variables, and `ctx.fail`/`ctx.log` — is currently available in **JavaScript and Python only**. F# scripts do not receive `ctx`; they communicate purely through stdout and their exit code. If you need to read the response body or pass a variable from a script to a later step today, write that script in [JavaScript](/docs/javascript-scripting/) or [Python](/docs/python-scripting/).

## Mixing with other languages (spec: script-dispatch)

Dispatch is by extension, so a single `.naplist` can interleave `.fsx` steps with `.js`, `.py`, `.csx`, and `.nap` steps — see [`examples/jsonplaceholder/mixed-scripts.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/mixed-scripts.naplist) and the [Scripting Overview](/docs/scripting/).

## Requirements

F# scripts require the **.NET SDK** (`dotnet fsi`) on your `PATH`. The Napper CLI binary itself is self-contained — you only need the .NET SDK if you actually write `.fsx`/`.csx` scripts. Prefer something else? See [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), and [C#](/docs/csharp-scripting/).
