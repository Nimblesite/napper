# Post-request hook (Python). Runs AFTER the request; `ctx.response` is the response.
# ctx.fail(msg) fails the step (even on a 200); ctx.log writes to the run output.
if ctx.response.status != 200:
    ctx.fail("expected 200, got " + str(ctx.response.status))
post = ctx.response.json
if str(post["id"]) != ctx.vars["postId"]:
    ctx.fail("id mismatch: " + str(post["id"]) + " != " + ctx.vars["postId"])
ctx.log("[validate] post " + str(post["id"]) + " ok in " + str(ctx.response.duration_ms) + "ms")
