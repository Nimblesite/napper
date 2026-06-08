---
layout: layouts/docs.njk
title: "Napper vs Bruno"
description: "Comparing Napper and Bruno for API testing. Both are open-source alternatives to Postman, but Napper is CLI-first with real scripting in JavaScript, Python, F#, or C# while Bruno is GUI-first with sandboxed JavaScript."
keywords: "Napper vs Bruno, Bruno alternative, API testing comparison, open source API testing"
eleventyNavigation:
  key: vs Bruno
  order: 12
---

# Napper vs Bruno (spec: cli-run, nap-file)

Napper and Bruno are both free, open-source alternatives to Postman that store requests as plain text files. Here is how they differ.

## What is the main difference between Napper and Bruno?

Bruno is a GUI-first tool with a standalone desktop application. It focuses on providing a visual interface similar to Postman but with open-source, git-friendly storage. Napper is CLI-first: the command line is the primary interface, and the VS Code extension provides an editor experience without a separate application.

## How do the editors compare?

Bruno has its own standalone desktop application built with Electron. Napper integrates directly into VS Code and Zed as native extensions — and into VS Code-compatible editors like Cursor, Windsurf, and Antigravity via Open VSX, plus any editor through a portable language server — with syntax highlighting, a request explorer, environment switching, and Test Explorer integration. If you already work in an editor, Napper fits into your existing workflow without switching applications.

## How does scripting compare? (spec: script-js, script-py, script-fsx, script-csx)

Bruno provides sandboxed JavaScript for pre-request and post-request scripts, similar to Postman. Napper lets you script in JavaScript (`.js`), Python (`.py`), F# (`.fsx`), or C# (`.csx`), running on the real runtime — Node.js, Python 3, or .NET — with no sandbox. Import npm, PyPI, or NuGet packages, call databases, parse XML, generate tokens, and perform any operation the runtime supports.

## How do file formats compare? (spec: nap-file)

Both Napper and Bruno store requests as plain text files that work well with git. Bruno uses its own Bru markup language. Napper uses `.nap` files with a TOML-inspired section-based format. Both produce clean diffs in code reviews.

## How does CI/CD integration compare? (spec: cli-run, cli-output)

Bruno provides a CLI for running collections from the terminal. Napper is designed CLI-first, meaning the command line is the primary interface rather than an afterthought. Napper outputs JUnit XML, JSON, and NDJSON formats natively and requires no runtime dependencies.

## Feature comparison

| Feature | Napper | Bruno |
|---------|--------|-------|
| Primary interface | CLI + VS Code | Standalone desktop app |
| CLI design | CLI-first | CLI secondary |
| File format | `.nap` (TOML-inspired) | `.bru` (custom markup) |
| Assertions | Declarative + scripts | JavaScript scripts |
| Scripting | JavaScript, Python, F#, C# on real runtimes | Sandboxed JavaScript |
| Editor integration | VS Code, Cursor, Windsurf, Antigravity & Zed + LSP | Standalone Electron app |
| Test Explorer | Native VS Code support | No |
| CI/CD output | JUnit, JSON, NDJSON | JSON via CLI |
| OpenAPI import | URL + file + AI | Import only |
| .http file migration | Built-in converter | No |
| Pricing | Free, MIT license | Free, MIT license |

## When should you choose Napper over Bruno?

Choose Napper if you prefer working from the terminal, want to stay inside your editor (VS Code, Zed, or any editor via its language server), want to script in JavaScript, Python, F#, or C# on a real runtime, or want native JUnit output for CI/CD pipelines. Choose Bruno if you prefer a standalone GUI application with its own visual interface.

## Get started

- [Install Napper](/docs/installation/)
- [Quick Start guide](/docs/quick-start/)
