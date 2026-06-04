// Implements [SWR-IDE-RESOLUTION]. Resilient, SDK-decoupled CLI resolution.
//
// Shipwright probes the bundled binary with `napper --version` under a short deadline and
// swallows EVERY failure into a non-ok result with no path ("no resolved source"). On a
// fresh Windows Marketplace install the ~10 MB NativeAOT binary's first probe can exceed
// that deadline (Defender real-time scan of an unsigned executable) or be transiently
// blocked, which bricked the extension with an unactionable message. We retry with a
// generous timeout; the second probe runs against a warm, already-scanned file. This module
// holds only the pure retry policy — the VS Code wiring and the Shipwright import stay in
// extension.ts so the policy is unit-testable with injected probes.

import {
  LOG_MSG_CLI_RESOLVED,
  LOG_MSG_CLI_RESOLVE_FAILED,
  LOG_MSG_CLI_RESOLVE_RETRY,
} from './constants';

/** One resolution attempt's observable outcome, mapped from a Shipwright activation. */
export interface CliResolutionAttempt {
  readonly ok: boolean;
  readonly path: string | undefined;
}

/** Inputs for {@link resolveCliWithRetry} — injected so the policy is SDK-free. */
export interface ResolveCliInput {
  readonly attempt: (timeoutMs: number) => Promise<CliResolutionAttempt>;
  readonly delay: (ms: number) => Promise<void>;
  readonly log: (message: string) => void;
  readonly maxAttempts: number;
  readonly retryDelayMs: number;
  readonly timeoutMs: number;
}

/** The resolved CLI path (when found) and how many attempts it took. */
export interface CliResolution {
  readonly attempts: number;
  readonly path: string | undefined;
}

const isUsablePath = (value: string | undefined): value is string =>
  value !== undefined && value !== '';

/**
 * Probe up to `maxAttempts` times, waiting `retryDelayMs` between tries, returning the first
 * successfully-resolved CLI path. Never throws: exhausting all attempts yields a `path` of
 * `undefined` so the caller can fall back and surface an actionable message. Recurses (rather
 * than looping with an awaited body) to stay within the strict `no-await-in-loop` rule; the
 * `attempt` counter is internal and defaults to the first try.
 */
export const resolveCliWithRetry = async (
  input: ResolveCliInput,
  attempt = 1,
): Promise<CliResolution> => {
  const result = await input.attempt(input.timeoutMs);
  if (result.ok && isUsablePath(result.path)) {
    input.log(`${LOG_MSG_CLI_RESOLVED} ${result.path} (attempt ${attempt}/${input.maxAttempts})`);
    return { attempts: attempt, path: result.path };
  }
  if (attempt >= input.maxAttempts) {
    input.log(`${LOG_MSG_CLI_RESOLVE_FAILED} (${input.maxAttempts} attempts)`);
    return { attempts: attempt, path: undefined };
  }
  input.log(`${LOG_MSG_CLI_RESOLVE_RETRY} ${attempt + 1}/${input.maxAttempts}`);
  await input.delay(input.retryDelayMs);
  return resolveCliWithRetry(input, attempt + 1);
};
