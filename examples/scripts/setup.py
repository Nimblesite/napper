# Pre-test setup script (Python)
# Run before a playlist to seed data or configure state.
# Pure stdlib — no pip, no virtualenv. Uses urllib + json.

import json
import urllib.request

payload = json.dumps({"title": "Seeded by Python script", "body": "Setup data", "userId": 1}).encode()
request = urllib.request.Request(
    "https://jsonplaceholder.typicode.com/posts",
    data=payload,
    headers={"Content-Type": "application/json"},
    method="POST",
)

with urllib.request.urlopen(request) as response:
    body = response.read().decode()
    print(f"[setup] Seeded post: {response.status} — {body[:80]}")

print("[setup] Done")
