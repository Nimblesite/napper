// Pre-test setup script (JavaScript)
// Run before a playlist to seed data or configure state.
// Pure JS — no imports, no npm, no build. Uses the built-in global fetch.

(async () => {
  const payload = JSON.stringify({ title: "Seeded by JS script", body: "Setup data", userId: 1 });

  const response = await fetch("https://jsonplaceholder.typicode.com/posts", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: payload,
  });

  const body = await response.text();
  console.log(`[setup] Seeded post: ${response.status} — ${body.slice(0, 80)}`);
  console.log("[setup] Done");
})();
