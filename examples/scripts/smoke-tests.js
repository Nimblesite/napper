// Smoke test suite (JavaScript) — runs multiple HTTP tests and fails on any error.
// Use as a script step in a .naplist to run a batch of quick validations.
// Pure JS — no imports, no npm, no build. Uses the built-in global fetch.

(async () => {
  const tests = [];
  const test = (name, fn) => tests.push({ name, fn });

  const assertStatus = (expected, res) => {
    if (res.status !== expected) {
      throw new Error(`Expected status ${expected} but got ${res.status}`);
    }
  };

  // ─── Tests ───────────────────────────────────────────────────────────

  test("GET /posts returns 200", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/posts");
    assertStatus(200, res);
    const body = await res.text();
    if (body.length < 100) throw new Error("Response body unexpectedly short");
  });

  test("GET /posts/1 returns correct post", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/posts/1");
    assertStatus(200, res);
    const body = await res.text();
    if (!body.includes("userId")) throw new Error("Missing userId field");
  });

  test("POST /posts returns 201", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/posts", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title: "JS smoke test", body: "automated", userId: 1 }),
    });
    assertStatus(201, res);
    const body = await res.text();
    if (!body.includes("id")) throw new Error("Missing id in response");
  });

  test("GET /posts/1/comments returns 200", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/posts/1/comments");
    assertStatus(200, res);
  });

  test("GET /users returns 200", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/users");
    assertStatus(200, res);
    const body = await res.text();
    if (!body.includes("email")) throw new Error("Missing email field in users");
  });

  test("GET /posts/99999 returns 404", async () => {
    const res = await fetch("https://jsonplaceholder.typicode.com/posts/99999");
    assertStatus(404, res);
  });

  // ─── Runner ──────────────────────────────────────────────────────────

  console.log("\n━━━ Smoke Test Results ━━━");

  let failures = 0;
  for (const t of tests) {
    try {
      await t.fn();
      console.log(`  [PASS] ${t.name} — OK`);
    } catch (err) {
      failures++;
      console.log(`  [FAIL] ${t.name} — ${err.message}`);
    }
  }

  console.log(`\n  ${tests.length - failures}/${tests.length} passed`);
  console.log("━━━━━━━━━━━━━━━━━━━━━━━━━");

  if (failures > 0) {
    console.error(`[smoke-tests] ${failures} test(s) failed`);
    process.exit(1);
  }

  console.log("[smoke-tests] All passed");
})();
