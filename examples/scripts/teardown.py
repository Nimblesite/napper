# Post-test teardown script (Python)
# Clean up after a playlist run.
# Pure stdlib — no pip, no virtualenv.

import datetime

print("[teardown] Cleaning up test artifacts...")
print("[teardown] Timestamp:", datetime.datetime.now(datetime.timezone.utc).isoformat())
print("[teardown] Done")
