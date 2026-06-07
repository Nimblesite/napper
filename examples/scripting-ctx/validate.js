// Post-request hook (JavaScript). Runs AFTER the request; `ctx.response` is the response.
// ctx.fail(msg) fails the step (even on a 200); ctx.log writes to the run output.
if (ctx.response.status !== 200) {
  ctx.fail("expected 200, got " + ctx.response.status);
}
const post = ctx.response.json;
if (String(post.id) !== ctx.vars.postId) {
  ctx.fail("id mismatch: " + post.id + " != " + ctx.vars.postId);
}
ctx.log("[validate] post " + post.id + " ok in " + ctx.response.durationMs + "ms");
