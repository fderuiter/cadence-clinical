import { ref } from "vue";

/**
 * Shared WCAG 2.2 SC 2.2.2 & SC 4.1.3 milestone countdown controller.
 *
 * Separates visual 1-second `remainingSeconds` updates (intended for `aria-hidden="true"`
 * tabular-nums indicators) from discrete assistive technology `milestoneText` transitions
 * so polite `role="status"` live regions announce once per attempt, pause, resume,
 * completion, or failure rather than chattering every second.
 *
 * @param {object} [options]
 * @param {number} [options.tickIntervalMs=1000]
 * @param {(payload: { remainingSeconds: number, remainingMs: number, attempt: number, maxRetries: number }) => void} [options.onTick]
 * @param {(payload: { milestone: string, message: string, attempt: number, maxRetries: number }) => void} [options.onMilestone]
 * @param {() => void} [options.onExpire]
 * @param {() => number} [options.now]
 */
export function createMilestoneCountdown(options = {}) {
  const {
    tickIntervalMs = 1000,
    onTick,
    onMilestone,
    onExpire,
    now = () => Date.now(),
  } = options;

  const remainingSeconds = ref(0);
  const remainingMs = ref(0);
  const isPaused = ref(false);
  const isRunning = ref(false);
  const attempt = ref(0);
  const maxRetries = ref(0);
  const milestoneText = ref("");

  let deadlineMs = 0;
  let intervalId = null;
  let expireTimeoutId = null;
  let lastAnnouncedAttemptKey = "";

  function clearTimers() {
    if (intervalId !== null) {
      clearInterval(intervalId);
      intervalId = null;
    }
    if (expireTimeoutId !== null) {
      clearTimeout(expireTimeoutId);
      expireTimeoutId = null;
    }
  }

  function emitMilestone(milestone, message, opts = {}) {
    if (typeof message !== "string") return;
    const trimmed = message.trim();
    if (!trimmed) return;

    if (opts.attemptKey) {
      if (lastAnnouncedAttemptKey === opts.attemptKey && !opts.force) {
        return;
      }
      lastAnnouncedAttemptKey = opts.attemptKey;
    } else if (!opts.preserveAttemptKey) {
      lastAnnouncedAttemptKey = "";
    }

    if (milestoneText.value === trimmed && !opts.force) {
      return;
    }

    milestoneText.value = trimmed;
    if (typeof onMilestone === "function") {
      onMilestone({
        milestone,
        message: trimmed,
        attempt: attempt.value,
        maxRetries: maxRetries.value,
      });
    }
  }

  function syncRemainingFromClock() {
    const remMs = Math.max(0, deadlineMs - now());
    remainingMs.value = remMs;
    remainingSeconds.value = Math.max(0, Math.ceil(remMs / 1000));
    if (typeof onTick === "function") {
      onTick({
        remainingSeconds: remainingSeconds.value,
        remainingMs: remMs,
        attempt: attempt.value,
        maxRetries: maxRetries.value,
      });
    }
    return remMs;
  }

  function finalizeExpiry() {
    if (!isRunning.value) return;
    clearTimers();
    remainingMs.value = 0;
    remainingSeconds.value = 0;
    isRunning.value = false;
    if (typeof onTick === "function") {
      onTick({
        remainingSeconds: 0,
        remainingMs: 0,
        attempt: attempt.value,
        maxRetries: maxRetries.value,
      });
    }
    if (typeof onExpire === "function") {
      onExpire();
    }
  }

  function scheduleTimers(durationMs) {
    clearTimers();
    const safeMs = Math.max(0, Number(durationMs) || 0);
    remainingMs.value = safeMs;
    remainingSeconds.value = Math.max(0, Math.ceil(safeMs / 1000));
    if (safeMs <= 0) {
      isRunning.value = false;
      if (typeof onExpire === "function") {
        onExpire();
      }
      return;
    }

    isRunning.value = true;
    deadlineMs = now() + safeMs;

    intervalId = setInterval(() => {
      if (isPaused.value || !isRunning.value) return;
      const remMs = syncRemainingFromClock();
      if (remMs <= 0) {
        finalizeExpiry();
      }
    }, tickIntervalMs);
    if (intervalId && typeof intervalId.unref === "function") {
      intervalId.unref();
    }

    expireTimeoutId = setTimeout(() => {
      if (isPaused.value) return;
      finalizeExpiry();
    }, safeMs);
    if (expireTimeoutId && typeof expireTimeoutId.unref === "function") {
      expireTimeoutId.unref();
    }
  }

  function announceAttempt({
    attempt: nextAttempt = 1,
    maxRetries: nextMaxRetries = 5,
    message,
  } = {}) {
    attempt.value = Number(nextAttempt) || 1;
    maxRetries.value = Number(nextMaxRetries) || 5;
    const text =
      message ||
      `Retrying sync: attempt ${attempt.value} of ${maxRetries.value}.`;
    emitMilestone("attempt", text, {
      attemptKey: `${attempt.value}/${maxRetries.value}`,
    });
  }

  function start({
    durationMs = 0,
    attempt: nextAttempt = attempt.value || 1,
    maxRetries: nextMaxRetries = maxRetries.value || 5,
    milestoneMessage,
  } = {}) {
    attempt.value = Number(nextAttempt) || 1;
    maxRetries.value = Number(nextMaxRetries) || 5;
    const safeMs = Math.max(0, Number(durationMs) || 0);
    remainingMs.value = safeMs;
    remainingSeconds.value = Math.max(0, Math.ceil(safeMs / 1000));

    if (milestoneMessage !== null) {
      const text =
        milestoneMessage ||
        `Retrying sync: attempt ${attempt.value} of ${maxRetries.value}.`;
      emitMilestone("attempt", text, {
        attemptKey: `${attempt.value}/${maxRetries.value}`,
      });
    }

    if (isPaused.value) {
      clearTimers();
      isRunning.value = false;
      if (typeof onTick === "function") {
        onTick({
          remainingSeconds: remainingSeconds.value,
          remainingMs: remainingMs.value,
          attempt: attempt.value,
          maxRetries: maxRetries.value,
        });
      }
      return;
    }

    scheduleTimers(safeMs);
  }

  function pause(pauseMessage = "Automatic sync updates paused.") {
    if (isPaused.value) return;
    if (isRunning.value) {
      const remMs = Math.max(0, deadlineMs - now());
      remainingMs.value = remMs;
      remainingSeconds.value = Math.max(0, Math.ceil(remMs / 1000));
    }
    clearTimers();
    isRunning.value = false;
    isPaused.value = true;
    if (pauseMessage) {
      emitMilestone("paused", pauseMessage, { preserveAttemptKey: true });
    }
  }

  function resume(resumeMessage = "Automatic sync updates resumed.") {
    if (!isPaused.value) return false;
    isPaused.value = false;
    if (resumeMessage) {
      emitMilestone("resumed", resumeMessage, { preserveAttemptKey: true });
    }
    if (remainingMs.value > 0) {
      scheduleTimers(remainingMs.value);
      return true;
    }
    return false;
  }

  function togglePause({
    pauseMessage = "Automatic sync updates paused.",
    resumeMessage = "Automatic sync updates resumed.",
  } = {}) {
    if (isPaused.value) {
      resume(resumeMessage);
      return false;
    }
    pause(pauseMessage);
    return true;
  }

  function complete(completeMessage = "Online. Sync complete.") {
    clearTimers();
    isRunning.value = false;
    remainingMs.value = 0;
    remainingSeconds.value = 0;
    attempt.value = 0;
    if (completeMessage) {
      emitMilestone("complete", completeMessage);
    }
  }

  function fail(failMessage) {
    clearTimers();
    isRunning.value = false;
    remainingMs.value = 0;
    remainingSeconds.value = 0;
    if (failMessage) {
      emitMilestone("failed", failMessage);
    }
  }

  function setMilestone(milestone, message) {
    emitMilestone(milestone, message);
  }

  function stop() {
    clearTimers();
    isRunning.value = false;
    remainingMs.value = 0;
    remainingSeconds.value = 0;
  }

  function reset() {
    stop();
    isPaused.value = false;
    attempt.value = 0;
    maxRetries.value = 0;
    milestoneText.value = "";
    lastAnnouncedAttemptKey = "";
  }

  return {
    remainingSeconds,
    remainingMs,
    isPaused,
    isRunning,
    attempt,
    maxRetries,
    milestoneText,
    announceAttempt,
    start,
    pause,
    resume,
    togglePause,
    complete,
    fail,
    setMilestone,
    stop,
    reset,
  };
}
