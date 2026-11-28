import {
  computed,
  getCurrentInstance,
  getCurrentScope,
  onMounted,
  onScopeDispose,
  ref,
  shallowRef,
} from "vue";

export const SPLIT_WORKSPACE_COMPACT_BREAKPOINT = 1280;
export const SPLIT_WORKSPACE_SHEET_BREAKPOINT = 640;

function resolveInteractiveTrigger(eventOrEl) {
  if (typeof HTMLElement !== "undefined" && eventOrEl instanceof HTMLElement) {
    return eventOrEl;
  }
  if (eventOrEl && typeof eventOrEl === "object") {
    if (
      typeof HTMLElement !== "undefined" &&
      eventOrEl.currentTarget instanceof HTMLElement
    ) {
      return eventOrEl.currentTarget;
    }
    if (
      typeof HTMLElement !== "undefined" &&
      eventOrEl.target instanceof HTMLElement
    ) {
      const interactive = eventOrEl.target.closest(
        "button, [role='button'], [role='treeitem'], [role='tab'], a[href], input, select, textarea, [tabindex]"
      );
      return interactive instanceof HTMLElement
        ? interactive
        : eventOrEl.target;
    }
  }
  if (
    typeof document !== "undefined" &&
    typeof HTMLElement !== "undefined" &&
    document.activeElement instanceof HTMLElement &&
    document.activeElement !== document.body &&
    document.activeElement !== document.documentElement
  ) {
    return document.activeElement;
  }
  return null;
}

function subscribeMediaQuery(mql, listener) {
  if (!mql) return () => {};
  if (typeof mql.addEventListener === "function") {
    mql.addEventListener("change", listener);
    return () => {
      if (typeof mql.removeEventListener === "function") {
        mql.removeEventListener("change", listener);
      }
    };
  }
  if (typeof mql.addListener === "function") {
    mql.addListener(listener);
    return () => {
      if (typeof mql.removeListener === "function") {
        mql.removeListener(listener);
      }
    };
  }
  return () => {};
}

export function useResponsiveSplitWorkspace(options = {}) {
  const compactBreakpoint =
    typeof options.compactBreakpoint === "number" &&
    options.compactBreakpoint > 0
      ? options.compactBreakpoint
      : SPLIT_WORKSPACE_COMPACT_BREAKPOINT;
  const sheetBreakpoint =
    typeof options.sheetBreakpoint === "number" && options.sheetBreakpoint > 0
      ? options.sheetBreakpoint
      : SPLIT_WORKSPACE_SHEET_BREAKPOINT;

  const compactQuery = `(max-width: ${compactBreakpoint - 1}px)`;
  const sheetQuery = `(max-width: ${sheetBreakpoint}px)`;

  const evaluateQuery = (query, maxWidthPx, inclusive) => {
    if (typeof window === "undefined") return false;
    if (typeof window.matchMedia === "function") {
      const mql = window.matchMedia(query);
      if (mql && typeof mql.matches === "boolean") {
        return mql.matches;
      }
    }
    if (typeof window.innerWidth === "number") {
      return inclusive
        ? window.innerWidth <= maxWidthPx
        : window.innerWidth < maxWidthPx;
    }
    return false;
  };

  const evaluateCompact = () =>
    evaluateQuery(compactQuery, compactBreakpoint, false);
  const evaluateSheet = () => evaluateQuery(sheetQuery, sheetBreakpoint, true);

  const isCompact = ref(evaluateCompact());
  const isMobileSheet = ref(evaluateSheet());
  const isDetailOpen = ref(Boolean(options.initialOpen));
  const activeTriggerEl = shallowRef(null);

  const presentationMode = computed(() => {
    if (!isCompact.value) return "inline";
    return isMobileSheet.value ? "sheet" : "drawer";
  });

  const syncViewport = () => {
    const wasCompact = isCompact.value;
    const prevMode = presentationMode.value;
    const nextCompact = evaluateCompact();
    const nextSheet = evaluateSheet();

    isCompact.value = nextCompact;
    isMobileSheet.value = nextSheet;

    if (wasCompact && !nextCompact && isDetailOpen.value) {
      isDetailOpen.value = false;
    }

    const nextMode = presentationMode.value;
    if (prevMode !== nextMode && typeof options.onModeChange === "function") {
      options.onModeChange(nextMode);
    }
  };

  const captureTrigger = (eventOrEl) => {
    const resolved = resolveInteractiveTrigger(eventOrEl);
    if (resolved) {
      activeTriggerEl.value = resolved;
    }
    return activeTriggerEl.value;
  };

  const openDetail = (eventOrEl) => {
    captureTrigger(eventOrEl);
    isDetailOpen.value = true;
  };

  const closeDetail = () => {
    isDetailOpen.value = false;
  };

  const toggleDetail = (next, eventOrEl) => {
    const targetOpen = typeof next === "boolean" ? next : !isDetailOpen.value;
    if (targetOpen) {
      openDetail(eventOrEl);
    } else {
      closeDetail();
    }
  };

  let cleanupFns = [];
  let attached = false;

  const stop = () => {
    for (const fn of cleanupFns) {
      fn();
    }
    cleanupFns = [];
    attached = false;
  };

  const attachListeners = () => {
    if (attached || typeof window === "undefined") return;
    attached = true;
    syncViewport();

    if (typeof window.matchMedia === "function") {
      const compactMql = window.matchMedia(compactQuery);
      const sheetMql = window.matchMedia(sheetQuery);
      cleanupFns.push(subscribeMediaQuery(compactMql, syncViewport));
      cleanupFns.push(subscribeMediaQuery(sheetMql, syncViewport));
    }

    if (typeof window.addEventListener === "function") {
      window.addEventListener("resize", syncViewport);
      cleanupFns.push(() => {
        if (typeof window.removeEventListener === "function") {
          window.removeEventListener("resize", syncViewport);
        }
      });
    }
  };

  if (getCurrentScope()) {
    attachListeners();
    onScopeDispose(stop);
  }

  if (getCurrentInstance()) {
    onMounted(() => {
      attachListeners();
      syncViewport();
    });
  }

  return {
    isCompact,
    isMobileSheet,
    presentationMode,
    isDetailOpen,
    activeTriggerEl,
    captureTrigger,
    openDetail,
    closeDetail,
    toggleDetail,
    syncViewport,
    stop,
  };
}
