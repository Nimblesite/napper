# Smoke test suite (Python) — runs multiple HTTP tests and fails on any error.
# Use as a script step in a .naplist to run a batch of quick validations.
# Pure stdlib — no pip, no virtualenv.

import json
import sys
import urllib.error
import urllib.request


def get(url):
    with urllib.request.urlopen(url) as response:
        return response.status, response.read().decode()


def post(url, payload):
    data = json.dumps(payload).encode()
    request = urllib.request.Request(
        url, data=data, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(request) as response:
        return response.status, response.read().decode()


def status_of(url):
    # urlopen raises HTTPError on 4xx/5xx — capture the status code either way.
    try:
        status, _ = get(url)
        return status
    except urllib.error.HTTPError as err:
        return err.code


def assert_status(expected, actual):
    if actual != expected:
        raise AssertionError(f"Expected status {expected} but got {actual}")


# ─── Tests ───────────────────────────────────────────────────────────


def test_get_posts():
    status, body = get("https://jsonplaceholder.typicode.com/posts")
    assert_status(200, status)
    if len(body) < 100:
        raise AssertionError("Response body unexpectedly short")


def test_get_single_post():
    status, body = get("https://jsonplaceholder.typicode.com/posts/1")
    assert_status(200, status)
    if "userId" not in body:
        raise AssertionError("Missing userId field")


def test_create_post():
    status, body = post(
        "https://jsonplaceholder.typicode.com/posts",
        {"title": "Python smoke test", "body": "automated", "userId": 1},
    )
    assert_status(201, status)
    if "id" not in body:
        raise AssertionError("Missing id in response")


def test_get_comments():
    status, _ = get("https://jsonplaceholder.typicode.com/posts/1/comments")
    assert_status(200, status)


def test_get_users():
    status, body = get("https://jsonplaceholder.typicode.com/users")
    assert_status(200, status)
    if "email" not in body:
        raise AssertionError("Missing email field in users")


def test_not_found():
    assert_status(404, status_of("https://jsonplaceholder.typicode.com/posts/99999"))


# ─── Runner ──────────────────────────────────────────────────────────

tests = [
    ("GET /posts returns 200", test_get_posts),
    ("GET /posts/1 returns correct post", test_get_single_post),
    ("POST /posts returns 201", test_create_post),
    ("GET /posts/1/comments returns 200", test_get_comments),
    ("GET /users returns 200", test_get_users),
    ("GET /posts/99999 returns 404", test_not_found),
]

print("\n━━━ Smoke Test Results ━━━")

failures = 0
for name, run in tests:
    try:
        run()
        print(f"  [PASS] {name} — OK")
    except Exception as err:  # noqa: BLE001 — example: report any failure
        failures += 1
        print(f"  [FAIL] {name} — {err}")

print(f"\n  {len(tests) - failures}/{len(tests)} passed")
print("━━━━━━━━━━━━━━━━━━━━━━━━━")

if failures > 0:
    print(f"[smoke-tests] {failures} test(s) failed", file=sys.stderr)
    sys.exit(1)

print("[smoke-tests] All passed")
