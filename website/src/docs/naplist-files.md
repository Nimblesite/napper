---
layout: layouts/docs.njk
title: ".naplist Playlists"
description: "Build test suites with .naplist files. Chain requests, nest playlists, and orchestrate complex test flows."
keywords: ".naplist, playlists, test suites, test orchestration"
eleventyNavigation:
  key: ".naplist Playlists"
  order: 5
---

# .naplist Playlists (spec: naplist-file)

A `.naplist` file defines an ordered sequence of steps to execute. Steps can be `.nap` files, folders, other playlists, or scripts in JavaScript, Python, F#, or C#.

## Basic format (spec: naplist-meta, naplist-steps)

```
[meta]
name = Smoke Tests
description = Quick checks for core endpoints

[steps]
./health.nap
./users/get-users.nap
./users/create-user.nap
```

## Full format (spec: naplist-meta, naplist-vars, naplist-steps)

```
[meta]
name = Full Regression Suite
description = Complete API test suite with setup and teardown
environment = staging

[vars]
baseUrl = https://staging.api.example.com
adminToken = {% raw %}{{ADMIN_TOKEN}}{% endraw %}

[steps]
# Setup
./scripts/seed-data.fsx

# Core CRUD
./users/
./posts/

# Integration tests
./integration/auth-flow.naplist
./integration/payment-flow.naplist

# Cleanup
./scripts/teardown.fsx
```

## Step types

### .nap files (spec: naplist-nap-step)

Run a single HTTP request:

```
./users/get-user.nap
```

### Folders (spec: naplist-folder-step)

Run all `.nap` files in a folder, sorted by filename (spec: collection-sort):

```
./users/
```

### Nested playlists (spec: naplist-nested)

Run another `.naplist` file:

```
./regression/core.naplist
```

Nesting is recursive — playlists can reference other playlists.

### Scripts (spec: naplist-script-step)

Run a script in any supported language — dispatch is by extension, so a single playlist can mix them freely:

```
./scripts/seed-data.js
./scripts/setup.py
./scripts/setup.fsx
./scripts/setup.csx
```

Each script runs as a step and passes or fails by exit code; its stdout is captured into the run output. In **JavaScript and Python**, an injected `ctx` object lets a step set variables for later steps and fail the run with a message. See the [Scripting Overview](/docs/scripting/), or the [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), [F#](/docs/fsharp-scripting/), and [C#](/docs/csharp-scripting/) guides.

## Variables (spec: naplist-var-scope)

Variables defined in `[vars]` are available to all steps. A JavaScript or Python step can also set a variable for downstream steps with `ctx.set(key, value)`.

## Running playlists

From the CLI:

```bash
napper run ./smoke.naplist
```

With an environment:

```bash
napper run ./smoke.naplist --env staging
```

With JUnit output:

```bash
napper run ./smoke.naplist --output junit
```

From VS Code, click the Run button next to any playlist in the Playlists panel.
