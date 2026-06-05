// Pre-test step (JavaScript). `ctx` is injected as a global — no import, no npm install.
// ctx.set makes a variable visible to every later step in the playlist.
ctx.set("postId", "7");
ctx.log("[seed] chose postId=" + ctx.vars.postId + " (env=" + ctx.env + ")");
