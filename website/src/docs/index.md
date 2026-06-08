---
layout: layouts/docs.njk
title: Introduction
description: "Napper is a free, open-source, CLI-first API testing tool for VS Code. Define HTTP requests as plain text .nap files with declarative assertions and F# or C# scripting. A modern alternative to Postman, Bruno, and .http files."
keywords: "API testing, HTTP client, developer tools, VS Code extension, Postman alternative, Bruno alternative"
eleventyNavigation:
  key: Introduction
  order: 1
---

# Introduction

![Screenshot: Napper VS Code extension showing the request explorer panel, syntax-highlighted .nap file, and response viewer with JSON body and assertion results](/assets/images/docs/introduction-overview.png)

**Napper** is a free, open-source, CLI-first API testing tool for anyone testing APIs. It integrates natively with VS Code and Zed, and works in any editor through a portable language server. It is a modern alternative to Postman, Bruno, `.http` files, and curl.

Napper is built for anyone who wants:

- **Simple things to be simple** — a one-off request is nearly as terse as curl (spec: nap-minimal)
- **Complex things to be possible** — script advanced flows in JavaScript, Python, F#, or C# (spec: script-js, script-py, script-fsx, script-csx)
- **Everything in version control** — plain text files, no binary blobs (spec: nap-file, naplist-file, env-file)
- **First-class editor support** — VS Code, Cursor, Windsurf, Antigravity, and Zed extensions plus a portable LSP: syntax highlighting, Test Explorer, environment switching
- **No runtime to install** — Napper ships as a self-contained native binary, not a .NET DLL
- **Easy migration** — convert existing `.http` files with a single CLI command (spec: cli-convert)

## How does Napper work?

Every HTTP request is a `.nap` file (spec: nap-file):

```
GET https://api.example.com/health
```

That's it. One line. Run it from the CLI:

```bash
napper run ./health.nap
```

Or from VS Code with a single click.

## What happens when you need more?

Add headers, bodies, assertions, and environment variables (spec: nap-full):

```
[meta]
name = Create user

[request]
method = POST
url = {{baseUrl}}/users

[request.headers]
Content-Type = application/json
Authorization = Bearer {{token}}

[request.body]
"""
{
  "name": "Ada Lovelace",
  "email": "ada@example.com"
}
"""

[assert]
status = 201
body.id exists
duration < 500ms
```

Chain requests into test suites with `.naplist` files (spec: naplist-file). Add JavaScript, Python, F#, or C# scripts for advanced orchestration — your language, your runtime, no sandbox (spec: script-js, script-py, script-fsx, script-csx). Output JUnit XML for your CI pipeline (spec: output-junit).

## Already using .http files? (spec: cli-convert)

Napper includes a built-in converter to migrate your existing `.http` files. Both Microsoft (VS Code REST Client) and JetBrains (IntelliJ, Rider, WebStorm) dialects are supported:

```bash
napper convert http ./requests.http
```

The converter maps variables to `.napenv` files, preserves request names, and converts JetBrains `http-client.env.json` environments. See the [.http file comparison](/docs/vs-http-files/) for details.

## Why is the CLI the primary interface?

Napper is not a GUI-first tool with a CLI bolted on. The CLI is the primary interface. The VS Code extension operates on the same files and provides the same features in your editor. This means your API tests work the same way locally and in CI/CD, with no import/export step.

![Screenshot: Napper CLI running a .naplist test suite, displaying coloured pass/fail output for each assertion across multiple endpoints](/assets/images/docs/introduction-cli-output.png)

## Next steps

- [Install Napper](/docs/installation/) to get started
- Follow the [Quick Start](/docs/quick-start/) guide
- Learn about [.nap file format](/docs/nap-files/)
- [Import an OpenAPI spec](/docs/openapi-import/) to generate tests automatically
- [Migrate from .http files](/docs/vs-http-files/) with the built-in converter
