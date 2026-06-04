// Isolates the [SWR-IDE-RESOLUTION] retry policy that fixes the Windows "no resolved source"
// brick: a single probe under a tight deadline (first-run AV scan of the NativeAOT binary)
// must NOT be the end of the story. These tests drive the pure policy with injected probes,
// asserting the observable outcome (resolved path, attempt count, retry delays, log trail).
import * as assert from 'assert';
import { resolveCliWithRetry, type CliResolutionAttempt } from '../../cliResolver';
import {
  LOG_MSG_CLI_RESOLVED,
  LOG_MSG_CLI_RESOLVE_FAILED,
  LOG_MSG_CLI_RESOLVE_RETRY,
} from '../../constants';

const MAX_ATTEMPTS = 3;
const RETRY_DELAY_MS = 50;
const TIMEOUT_MS = 9000;
const RESOLVED_PATH = '/ext/bin/win32-x64/napper.exe';

interface Probe {
  readonly attempt: (timeoutMs: number) => Promise<CliResolutionAttempt>;
  readonly timeouts: number[];
}

// A probe that replays a fixed sequence of outcomes, recording every timeout it was given.
// Past the sequence it keeps reporting failure, mirroring Shipwright's "no resolved source".
const probeReturning = (outcomes: CliResolutionAttempt[]): Probe => {
  const timeouts: number[] = [];
  return {
    timeouts,
    attempt: async (timeoutMs: number): Promise<CliResolutionAttempt> => {
      const next = outcomes[timeouts.length] ?? { ok: false, path: undefined };
      timeouts.push(timeoutMs);
      await Promise.resolve();
      return next;
    },
  };
};

const run = async (
  probe: Probe,
): Promise<{
  resolution: Awaited<ReturnType<typeof resolveCliWithRetry>>;
  delays: number[];
  logs: string[];
}> => {
  const delays: number[] = [];
  const logs: string[] = [];
  const resolution = await resolveCliWithRetry({
    attempt: probe.attempt,
    delay: async (ms: number): Promise<void> => {
      delays.push(ms);
      await Promise.resolve();
    },
    log: (message: string): void => {
      logs.push(message);
    },
    maxAttempts: MAX_ATTEMPTS,
    retryDelayMs: RETRY_DELAY_MS,
    timeoutMs: TIMEOUT_MS,
  });
  return { resolution, delays, logs };
};

suite('cliResolver', () => {
  test('resolves on the first attempt without retrying or delaying', async () => {
    const probe = probeReturning([{ ok: true, path: RESOLVED_PATH }]);
    const { resolution, delays, logs } = await run(probe);

    assert.strictEqual(resolution.path, RESOLVED_PATH, 'should return the resolved path');
    assert.strictEqual(resolution.attempts, 1, 'should stop after one successful attempt');
    assert.strictEqual(delays.length, 0, 'must not delay when the first probe succeeds');
    assert.strictEqual(probe.timeouts.length, 1, 'must probe exactly once');
    assert.strictEqual(probe.timeouts[0], TIMEOUT_MS, 'must pass the generous deadline through');
    assert.ok(
      logs.some((l) => l.startsWith(LOG_MSG_CLI_RESOLVED)),
      'should log the resolved path',
    );
    assert.ok(
      logs.every((l) => !l.startsWith(LOG_MSG_CLI_RESOLVE_FAILED)),
      'must not log a failure on success',
    );
  });

  test('retries after transient failures and succeeds on a later attempt', async () => {
    const probe = probeReturning([
      { ok: false, path: undefined },
      { ok: false, path: undefined },
      { ok: true, path: RESOLVED_PATH },
    ]);
    const { resolution, delays, logs } = await run(probe);

    assert.strictEqual(resolution.path, RESOLVED_PATH, 'should resolve once a probe succeeds');
    assert.strictEqual(resolution.attempts, 3, 'should take three attempts');
    assert.strictEqual(probe.timeouts.length, 3, 'should probe three times');
    assert.strictEqual(delays.length, 2, 'should wait between the three attempts');
    assert.deepStrictEqual(
      delays,
      [RETRY_DELAY_MS, RETRY_DELAY_MS],
      'each wait uses the retry delay',
    );
    const retries = logs.filter((l) => l.startsWith(LOG_MSG_CLI_RESOLVE_RETRY));
    assert.strictEqual(retries.length, 2, 'should announce two retries');
    assert.ok(
      logs.some((l) => l.startsWith(LOG_MSG_CLI_RESOLVED)),
      'should log the eventual resolution',
    );
  });

  test('gives up with no path after exhausting every attempt', async () => {
    const probe = probeReturning([]);
    const { resolution, delays, logs } = await run(probe);

    assert.strictEqual(resolution.path, undefined, 'should report no path on total failure');
    assert.strictEqual(resolution.attempts, MAX_ATTEMPTS, 'should use the full attempt budget');
    assert.strictEqual(probe.timeouts.length, MAX_ATTEMPTS, 'should probe maxAttempts times');
    assert.strictEqual(delays.length, MAX_ATTEMPTS - 1, 'should delay between each attempt only');
    assert.ok(
      logs.some((l) => l.startsWith(LOG_MSG_CLI_RESOLVE_FAILED)),
      'should log the final give-up message so the output channel explains the failure',
    );
  });

  test('treats an ok result with an empty path as a failure and keeps retrying', async () => {
    const probe = probeReturning([
      { ok: true, path: '' },
      { ok: true, path: RESOLVED_PATH },
    ]);
    const { resolution } = await run(probe);

    assert.strictEqual(resolution.path, RESOLVED_PATH, 'an empty path must not be accepted');
    assert.strictEqual(resolution.attempts, 2, 'should retry past the empty-path result');
  });
});
