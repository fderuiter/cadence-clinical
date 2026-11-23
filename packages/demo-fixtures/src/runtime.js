const FALLBACK_DEMO_EPOCH_ISO = "2026-01-05T09:00:00Z";
const FALLBACK_DEMO_EPOCH_MS = Date.UTC(2026, 0, 5, 9, 0, 0);
const UINT32_MODULO = 2 ** 32;
const DEFAULT_UNBACKED_ACTION_DETAIL =
  "No Cadence service records this action yet, so nothing was saved.";

function hashDemoSeed(input) {
  let hash = 0x811c9dc5;
  const text = String(input);
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return hash >>> 0 || 0x6d2b79f5;
}

/**
 * Demo mode is opt-in at build time (`vite --mode demo` / `pnpm build:demo`, or `VITE_DEMO_MODE=true`).
 */
export function isDemoBuild(env) {
  if (env !== undefined) {
    return env?.MODE === "demo" || env?.VITE_DEMO_MODE === "true";
  }
  return (
    import.meta.env?.MODE === "demo" ||
    import.meta.env?.VITE_DEMO_MODE === "true"
  );
}

/**
 * Creates a deterministic demo clock and Mulberry32 PRNG runtime that delegates to
 * live clocks and Web Crypto (`crypto.getRandomValues` with a `0.5` fallback) outside demo mode.
 */
export function createDemoRuntime(options = {}) {
  const {
    isDemoActive = () => isDemoBuild(),
    epochIso = FALLBACK_DEMO_EPOCH_ISO,
    epochMs = FALLBACK_DEMO_EPOCH_MS,
    defaultTokenLength = 7,
    seedNamespace = "cadence-demo",
  } = options;

  const demoPrngStreams = new Map();
  const demoStepCounters = new Map();

  function resolveDemoEpochMs() {
    const candidate = typeof epochMs === "function" ? epochMs() : epochMs;
    return typeof candidate === "number" && candidate > 0
      ? candidate
      : FALLBACK_DEMO_EPOCH_MS;
  }

  function resolveDemoEpochIso() {
    const candidate = typeof epochIso === "function" ? epochIso() : epochIso;
    return typeof candidate === "string" && candidate.length > 0
      ? candidate
      : FALLBACK_DEMO_EPOCH_ISO;
  }

  function createDemoPrng(seedInput = "default") {
    let state =
      typeof seedInput === "number"
        ? seedInput >>> 0 || 0x6d2b79f5
        : hashDemoSeed(
            `${seedNamespace}:${resolveDemoEpochIso()}:${seedInput}`
          );
    return function nextRandom() {
      state = (state + 0x6d2b79f5) >>> 0;
      let t = Math.imul(state ^ (state >>> 15), 1 | state);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / UINT32_MODULO;
    };
  }

  function resetDemoRandom(streamKey) {
    if (streamKey !== undefined) {
      demoPrngStreams.delete(streamKey);
      demoStepCounters.delete(streamKey);
      return;
    }
    demoPrngStreams.clear();
    demoStepCounters.clear();
  }

  function nextDemoStep(counterKey = "default") {
    const current = demoStepCounters.get(counterKey) || 0;
    demoStepCounters.set(counterKey, current + 1);
    return current;
  }

  function demoNowMs(stepOffset = 0, stepSizeMs = 1000) {
    if (!isDemoActive()) {
      return Date.now();
    }
    return resolveDemoEpochMs() + Math.trunc(stepOffset) * stepSizeMs;
  }

  function demoNowDate(stepOffset = 0, stepSizeMs = 1000) {
    if (!isDemoActive()) {
      return new Date();
    }
    return new Date(demoNowMs(stepOffset, stepSizeMs));
  }

  function demoNowIso(stepOffset = 0, stepSizeMs = 1000) {
    if (!isDemoActive()) {
      return new Date().toISOString();
    }
    return demoNowDate(stepOffset, stepSizeMs).toISOString();
  }

  function demoRandom(streamKey = "default") {
    if (!isDemoActive()) {
      if (
        typeof globalThis.crypto !== "undefined" &&
        typeof globalThis.crypto.getRandomValues === "function"
      ) {
        const buf = new Uint32Array(1);
        globalThis.crypto.getRandomValues(buf);
        return buf[0] / UINT32_MODULO;
      }
      return 0.5;
    }
    let stream = demoPrngStreams.get(streamKey);
    if (!stream) {
      stream = createDemoPrng(streamKey);
      demoPrngStreams.set(streamKey, stream);
    }
    return stream();
  }

  function demoRandomInt(min, maxInclusive, streamKey = "default") {
    const lower = Math.ceil(min);
    const upper = Math.floor(maxInclusive);
    return Math.floor(lower + demoRandom(streamKey) * (upper - lower + 1));
  }

  function demoRandomToken(
    length = defaultTokenLength,
    radix = 36,
    streamKey = "default"
  ) {
    const alphabet = "0123456789abcdefghijklmnopqrstuvwxyz".slice(0, radix);
    let out = "";
    for (let i = 0; i < length; i++) {
      const idx = Math.floor(demoRandom(streamKey) * alphabet.length);
      out += alphabet[idx];
    }
    return out;
  }

  return {
    createDemoPrng,
    resetDemoRandom,
    nextDemoStep,
    demoNowMs,
    demoNowDate,
    demoNowIso,
    demoRandom,
    demoRandomInt,
    demoRandomToken,
  };
}

/**
 * Creates Part 11 guards that block unbacked regulated actions outside demo mode
 * while routing refusal notifications to each app's toast display.
 */
export function createUnbackedActionGuard(options = {}) {
  const {
    isAllowed = (env) => isDemoBuild(env),
    defaultDetail = DEFAULT_UNBACKED_ACTION_DETAIL,
    onRefuse,
  } = options;

  function refuseUnbackedAction(action, customDetail = null) {
    const title = `${action} isn't available yet`;
    const detail = customDetail || defaultDetail;
    const message = `${title}. ${detail}`;
    if (typeof onRefuse === "function") {
      return onRefuse({
        action,
        title,
        detail,
        message,
        customDetail,
      });
    }
    return message;
  }

  function allowUnbackedAction(action, env) {
    if (isAllowed(env)) {
      return true;
    }
    refuseUnbackedAction(action);
    return false;
  }

  function unbackedActionsEnabled(env) {
    return Boolean(isAllowed(env));
  }

  return {
    allowUnbackedAction,
    refuseUnbackedAction,
    unbackedActionsEnabled,
  };
}
