// Validate that required environment variables are set before running tests.
// Fails fast (in a real script) if critical config is missing.
// Pure JS — no imports, no npm, no build.

const requiredVars = ["baseUrl", "userId"];

console.log("[validate] Checking environment...");

for (const name of requiredVars) {
  const value = process.env[name];
  if (value === undefined) {
    console.log(`[validate] WARNING: ${name} not set (will use .napenv defaults)`);
  } else {
    console.log(`[validate] ${name} = ${value}`);
  }
}

console.log("[validate] Environment check complete");
