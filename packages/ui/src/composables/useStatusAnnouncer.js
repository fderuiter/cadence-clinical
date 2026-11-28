import {
  getCurrentScope,
  isRef,
  nextTick,
  onScopeDispose,
  ref,
  watch,
} from "vue";

/**
 * Shared WCAG 2.2 SC 4.1.3 live-region status & error announcer composable.
 * @param {{
 *   debounceMs?: number,
 *   duplicateClearDelayMs?: number,
 *   initialPoliteMessage?: string,
 *   initialAssertiveMessage?: string,
 *   statusSource?: import("vue").Ref<string | null | undefined> | import("vue").ComputedRef<string | null | undefined> | (() => string | null | undefined),
 *   errorSource?: import("vue").Ref<string | null | undefined> | import("vue").ComputedRef<string | null | undefined> | (() => string | null | undefined)
 * }} [options]
 */
export function useStatusAnnouncer(options = {}) {
  const {
    debounceMs = 220,
    reannounceDelayMs = 16,
    initialPolite = "",
    initialAssertive = "",
    statusSource = null,
    errorSource = null,
    announceOnMount = false,
  } = options;

  const politeMessage = ref(String(initialPolite || ""));
  const assertiveMessage = ref(String(initialAssertive || ""));
  const lastPolite = ref(politeMessage.value);
  const lastAssertive = ref(assertiveMessage.value);

  let politeTimer = null;
  let assertiveTimer = null;
  let politeReannounceTimer = null;
  let assertiveReannounceTimer = null;

  function clearTimers(channel) {
    if (!channel || channel === "polite") {
      if (politeTimer !== null) {
        clearTimeout(politeTimer);
        politeTimer = null;
      }
      if (politeReannounceTimer !== null) {
        clearTimeout(politeReannounceTimer);
        politeReannounceTimer = null;
      }
    }
    if (!channel || channel === "assertive") {
      if (assertiveTimer !== null) {
        clearTimeout(assertiveTimer);
        assertiveTimer = null;
      }
      if (assertiveReannounceTimer !== null) {
        clearTimeout(assertiveReannounceTimer);
        assertiveReannounceTimer = null;
      }
    }
  }

  function deliver(channel, rawText) {
    const text = String(rawText ?? "").trim();
    const targetRef =
      channel === "assertive" ? assertiveMessage : politeMessage;
    const lastRef = channel === "assertive" ? lastAssertive : lastPolite;

    if (!text) {
      targetRef.value = "";
      lastRef.value = "";
      return;
    }

    if (targetRef.value === text || lastRef.value === text) {
      targetRef.value = "";
      void nextTick(() => {
        if (reannounceDelayMs <= 0) {
          targetRef.value = text;
          lastRef.value = text;
          return;
        }
        const timerId = setTimeout(() => {
          if (channel === "assertive") {
            assertiveReannounceTimer = null;
          } else {
            politeReannounceTimer = null;
          }
          targetRef.value = text;
          lastRef.value = text;
        }, reannounceDelayMs);
        if (channel === "assertive") {
          assertiveReannounceTimer = timerId;
        } else {
          politeReannounceTimer = timerId;
        }
      });
      return;
    }

    targetRef.value = text;
    lastRef.value = text;
  }

  function announce(message, announceOptions = {}) {
    const channel =
      announceOptions.politeness === "assertive" ? "assertive" : "polite";
    const immediate =
      announceOptions.immediate ?? (channel === "assertive" ? true : false);
    const waitMs = immediate
      ? 0
      : Math.max(0, Number(announceOptions.debounceMs ?? debounceMs) || 0);

    clearTimers(channel);

    if (waitMs === 0) {
      deliver(channel, message);
      return;
    }

    const timerId = setTimeout(() => {
      if (channel === "assertive") {
        assertiveTimer = null;
      } else {
        politeTimer = null;
      }
      deliver(channel, message);
    }, waitMs);

    if (channel === "assertive") {
      assertiveTimer = timerId;
    } else {
      politeTimer = timerId;
    }
  }

  function announcePolite(message, announceOptions = {}) {
    announce(message, {
      ...announceOptions,
      politeness: "polite",
    });
  }

  function announceAssertive(message, announceOptions = {}) {
    announce(message, {
      ...announceOptions,
      politeness: "assertive",
      immediate: announceOptions.immediate ?? true,
    });
  }

  function clear(channel) {
    clearTimers(channel);
    if (!channel || channel === "polite") {
      politeMessage.value = "";
      lastPolite.value = "";
    }
    if (!channel || channel === "assertive") {
      assertiveMessage.value = "";
      lastAssertive.value = "";
    }
  }

  if (
    statusSource &&
    (isRef(statusSource) || typeof statusSource === "function")
  ) {
    watch(
      statusSource,
      (nextVal) => {
        announcePolite(nextVal ?? "");
      },
      { immediate: Boolean(announceOnMount) }
    );
  }

  if (
    errorSource &&
    (isRef(errorSource) || typeof errorSource === "function")
  ) {
    watch(
      errorSource,
      (nextErr) => {
        const normalized = String(nextErr ?? "").trim();
        if (normalized) {
          announceAssertive(normalized, { immediate: true });
        } else {
          clear("assertive");
        }
      },
      { immediate: Boolean(announceOnMount) }
    );
  }

  if (getCurrentScope()) {
    onScopeDispose(() => {
      clearTimers();
    });
  }

  return {
    politeMessage,
    assertiveMessage,
    announce,
    announcePolite,
    announceAssertive,
    clear,
    clearStatus: () => clear("polite"),
    clearAssertive: () => clear("assertive"),
  };
}
