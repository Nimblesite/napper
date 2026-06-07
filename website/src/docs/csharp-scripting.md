---
layout: layouts/docs.njk
title: C# Scripting
description: "Run C# scripts as Napper playlist steps and pre/post request hooks on the full .NET SDK with complete NuGet access. Scripts pass or fail by exit code."
keywords: "C# scripting, csx, dotnet script, post-request script, pre-request script, mixed language tests"
eleventyNavigation:
  key: "C# Scripting"
  order: 8
---

# C# Scripting (spec: script-csx)

Napper runs C# scripts (`.csx`) via **`dotnet script`** on your `PATH`. You get the full power of .NET and the entire NuGet ecosystem, using familiar C# syntax.

## How C# scripts run (spec: script-runtime)

A C# script can be a **step** in a `.naplist`, or a **pre/post hook** on a `.nap` request. It **passes when it exits `0`** and **fails on any non-zero exit** (including an unhandled exception). Everything the script writes with `Console.WriteLine` is captured into the run output and the JUnit/JSON reports.

```csharp
// teardown.csx — a playlist step or [script] post hook
Console.WriteLine("[teardown] cleaning up test artifacts");
Console.WriteLine($"[teardown] timestamp: {DateTime.UtcNow:O}");
// returns exit code 0 -> the step passes
```

To **fail** a step, exit non-zero or throw:

```csharp
// guard.csx
var required = Environment.GetEnvironmentVariable("API_BASE_URL");
if (string.IsNullOrEmpty(required))
{
    Console.Error.WriteLine("API_BASE_URL is not set");
    Environment.Exit(1);   // non-zero exit -> the step fails
}
Console.WriteLine("[guard] environment looks good");
```

## As a playlist step (spec: naplist-script-step)

```
[steps]
../scripts/setup.csx
./01_get-posts.nap
../scripts/teardown.csx
```

Runnable scripts: [`examples/scripts/setup.csx`](https://github.com/Nimblesite/napper/blob/main/examples/scripts/setup.csx), `validate-env.csx`, `smoke-tests.csx`, `teardown.csx`. The [`crud-csharp.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/crud-csharp.naplist) suite wraps the CRUD requests in C# setup/teardown.

## As a pre/post hook (spec: script-pre, script-post)

```
[script]
pre = ./scripts/setup.csx
post = ./scripts/teardown.csx
```

A `pre` hook runs before the request; a `post` hook runs after. If a `post` hook exits non-zero, its request step fails even when the HTTP assertions passed.

## The `ctx` object is JavaScript/Python only

The injected `ctx` object — for reading the response, setting downstream variables, and `ctx.fail`/`ctx.log` — is currently available in **JavaScript and Python only**. C# scripts do not receive `ctx`; they communicate purely through stdout and their exit code. If you need to read the response body or pass a variable from a script to a later step today, write that script in [JavaScript](/docs/javascript-scripting/) or [Python](/docs/python-scripting/).

## Mixing with other languages (spec: script-dispatch)

Dispatch is by extension, so a single `.naplist` can interleave `.csx` steps with `.js`, `.py`, `.fsx`, and `.nap` steps — see [`examples/jsonplaceholder/mixed-scripts.naplist`](https://github.com/Nimblesite/napper/blob/main/examples/jsonplaceholder/mixed-scripts.naplist) and the [Scripting Overview](/docs/scripting/).

## Requirements

C# scripts require the **.NET SDK** with `dotnet script` on your `PATH`. The Napper CLI binary itself is self-contained — you only need the .NET SDK if you actually write `.fsx`/`.csx` scripts. Prefer something else? See [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), and [F#](/docs/fsharp-scripting/).
