---
layout: layouts/docs.njk
title: ".nap Files"
description: "Complete reference for the .nap request file format. Sections, headers, bodies, variables, and assertions."
keywords: ".nap file format, HTTP request, API request, file format reference"
eleventyNavigation:
  key: ".nap Files"
  order: 4
---

# .nap Files (spec: nap-file)

A `.nap` file defines a single HTTP request with optional metadata, headers, body, assertions, and script hooks.

## Minimal format (spec: nap-minimal)

The simplest possible `.nap` file is just a method and URL:

```
GET https://api.example.com/health
```

## Full format (spec: nap-full)

{% raw %}
```
[meta]
name = Get user by ID
description = Fetches a single user and validates the response
tags = users, smoke

[vars]
userId = 1

[request]
method = GET
url = {{baseUrl}}/users/{{userId}}

[request.headers]
Authorization = Bearer {{token}}
Accept = application/json

[assert]
status = 200
body.id = {{userId}}
body.name exists
body.email exists
duration < 1000ms

[script]
post = ./scripts/log-response.js
```
{% endraw %}

## Sections

### `[meta]` (spec: nap-meta)

Optional metadata about the request.

| Field | Description |
|-------|-------------|
| `name` | Human-readable name displayed in explorers |
| `description` | Longer description for documentation |
| `tags` | Comma-separated tags for filtering |

### `[vars]` (spec: nap-vars)

Local variable defaults. These are overridden by environment files and CLI flags.

```
userId = 1
baseUrl = https://api.example.com
```

### `[request]` (spec: nap-request)

The HTTP method and URL. This is the only required part of a `.nap` file — give the `method` and `url` as separate keys:

{% raw %}
```
[request]
method = GET
url = {{baseUrl}}/users/{{userId}}
```
{% endraw %}

(The one-line `GET https://...` shown under [Minimal format](#minimal-format-spec-nap-minimal) is shorthand for a whole file with no other sections.)

Supported methods: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS` (spec: http-methods).

### `[request.headers]` (spec: nap-headers)

Key-value pairs for HTTP headers. Variables are interpolated.

{% raw %}
```
Authorization = Bearer {{token}}
Content-Type = application/json
X-Custom-Header = {{customValue}}
```
{% endraw %}

### `[request.body]` (spec: nap-body)

Request body for `POST`, `PUT`, and `PATCH` requests. Content is wrapped in triple quotes:

```
[request.body]
"""
{
  "name": "Ada Lovelace",
  "email": "ada@example.com"
}
"""
```

### `[assert]` (spec: nap-assert)

Declarative assertions on the response. See [Assertions](/docs/assertions/) for the full reference.

### `[script]` (spec: nap-script)

References to scripts that run before (`pre`) or after (`post`) the request. Scripts can be JavaScript, Python, F#, or C# — dispatch is by extension, and a hook fails its request step if the script exits non-zero.

```
[script]
pre = ./scripts/setup.js
post = ./scripts/validate.py
```

In JavaScript and Python, a `post` hook can read the response and chain values through the injected `ctx` object. See the [Scripting Overview](/docs/scripting/) and the [JavaScript](/docs/javascript-scripting/), [Python](/docs/python-scripting/), [F#](/docs/fsharp-scripting/), and [C#](/docs/csharp-scripting/) guides.

## Variable interpolation (spec: env-interpolation)

Use {% raw %}`{{variableName}}`{% endraw %} anywhere in the request. Variables are resolved from (highest priority first):

1. CLI `--var key=value` flags
2. `.napenv.local` (gitignored secrets)
3. `.napenv.<name>` (named environment)
4. `.napenv` (base environment)
5. `[vars]` in the `.nap` file

## Comments (spec: nap-comments)

Lines starting with `#` are comments:

```
# This is a comment
[request]
method = GET
url = https://api.example.com/health
```
