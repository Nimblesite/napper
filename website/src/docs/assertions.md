---
layout: layouts/docs.njk
title: Assertions
description: "Declarative assertions for HTTP responses. Check status codes, JSON paths, headers, and response times."
keywords: "assertions, testing, status code, JSON path, response validation"
eleventyNavigation:
  key: Assertions
  order: 8
---

# Assertions (spec: nap-assert)

The `[assert]` section in `.nap` files provides declarative assertions on HTTP responses. No scripting needed for common checks.

## Syntax

Each assertion is a single line in the form:

```
target operator value
```

## Status code (spec: assert-status)

```
[assert]
status = 200
```

## JSON body paths (spec: assert-equals, assert-exists, assert-gt)

Assert on values in the JSON response body using dot notation:

```
[assert]
body.id = 1
body.name = "Ada Lovelace"
body.email exists
body.users.length > 0
```

## Headers (spec: assert-contains, assert-exists)

Check response headers:

```
[assert]
headers.Content-Type contains "application/json"
headers.X-Request-Id exists
```

## Response time (spec: assert-lt)

Assert that the response completes within a time limit:

```
[assert]
duration < 500ms
duration < 2s
```

## Operators

| Operator | Description | Example | Spec |
|----------|-------------|---------|------|
| `=` | Equals | `status = 200` | (spec: assert-equals) |
| `>` | Greater than | `body.count > 0` | (spec: assert-gt) |
| `<` | Less than | `duration < 500ms` | (spec: assert-lt) |
| `exists` | Field is present | `body.id exists` | (spec: assert-exists) |
| `contains` | String contains | `headers.Content-Type contains "json"` | (spec: assert-contains) |
| `matches` | Regex match | `body.email matches "^.+@.+$"` | (spec: assert-matches) |

## Multiple assertions

Add as many assertions as you need:

```
[assert]
status = 200
body.id exists
body.name = "Ada Lovelace"
body.email contains "@"
headers.Content-Type contains "application/json"
duration < 1000ms
```

All assertions are evaluated. Napper reports each one as passed or failed.

## Complex assertions with scripts

For checks that go beyond the declarative syntax, use a [JavaScript](/docs/javascript-scripting/) or [Python](/docs/python-scripting/) post-request hook. The injected `ctx` object gives you the parsed response, and `ctx.fail(message)` fails the request:

```
[script]
post = ./scripts/validate-schema.js
```

```js
// validate-schema.js — ctx is a global, no import
for (const user of ctx.response.json) {
  if (!user.email) ctx.fail("user " + user.id + " is missing email");
}
```

F# and C# scripts can also run as post hooks, but they validate by exit code rather than the `ctx` object — see the [F#](/docs/fsharp-scripting/) and [C#](/docs/csharp-scripting/) guides.
