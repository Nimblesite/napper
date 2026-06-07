# Validate that required environment variables are set before running tests.
# Fails fast (in a real script) if critical config is missing.
# Pure stdlib — no pip, no virtualenv.

import os

required_vars = ["baseUrl", "userId"]

print("[validate] Checking environment...")

for name in required_vars:
    value = os.environ.get(name)
    if value is None:
        print(f"[validate] WARNING: {name} not set (will use .napenv defaults)")
    else:
        print(f"[validate] {name} = {value}")

print("[validate] Environment check complete")
