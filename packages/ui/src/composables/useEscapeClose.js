import {
  getCurrentInstance,
  getCurrentScope,
  onMounted,
  onScopeDispose,
  onUnmounted,
  unref,
  watchEffect,
} from "vue";
import {
  getTopActiveTrapEscapeInfo,
  nextOverlayOrder,
  setTopEscapeOrderGetter,
} from "./useFocusTrap.js";

const escapeStack = [];
const handledEscapeEvents = new WeakSet();
let globalListenersAttached = false;
let boundDoc = null;
let boundWin = null;

function getTopDefaultEscapeEntry() {
  let bestEntry = null;
  for (let i = escapeStack.length - 1; i >= 0; i -= 1) {
    const entry = escapeStack[i];
    if (entry.target) continue;
    if (!entry.isEnabled()) continue;
    if (!bestEntry || (entry.order || 0) >= (bestEntry.order || 0)) {
      bestEntry = entry;
    }
  }
  return bestEntry;
}

setTopEscapeOrderGetter(() => {
  const best = getTopDefaultEscapeEntry();
  return best ? best.order || 0 : 0;
});

function dispatchTopmostEscape(e) {
  if (!e || e.key !== "Escape" || handledEscapeEvents.has(e)) return;
  if (e.defaultPrevented) return;

  const bestEntry = getTopDefaultEscapeEntry();
  if (!bestEntry) return;

  const trapInfo = getTopActiveTrapEscapeInfo();
  if (
    trapInfo.locked ||
    (trapInfo.active &&
      trapInfo.handlesEscape &&
      trapInfo.order > (bestEntry.order || 0))
  ) {
    return;
  }

  handledEscapeEvents.add(e);
  if (typeof e.preventDefault === "function") {
    e.preventDefault();
  }
  if (typeof bestEntry.onClose === "function") {
    bestEntry.onClose(e);
  }
}

function syncGlobalEscapeListeners() {
  const hasDefaultTargetEntries = escapeStack.some((entry) => !entry.target);
  const docChanged = typeof document !== "undefined" && boundDoc !== document;
  const winChanged = typeof window !== "undefined" && boundWin !== window;
  if (globalListenersAttached && (docChanged || winChanged)) {
    if (boundDoc)
      boundDoc.removeEventListener("keydown", dispatchTopmostEscape);
    if (boundWin)
      boundWin.removeEventListener("keydown", dispatchTopmostEscape);
    boundDoc = null;
    boundWin = null;
    globalListenersAttached = false;
  }
  if (hasDefaultTargetEntries && !globalListenersAttached) {
    if (typeof document !== "undefined") {
      boundDoc = document;
      boundDoc.addEventListener("keydown", dispatchTopmostEscape);
    }
    if (typeof window !== "undefined") {
      boundWin = window;
      boundWin.addEventListener("keydown", dispatchTopmostEscape);
    }
    globalListenersAttached = Boolean(boundDoc || boundWin);
  } else if (!hasDefaultTargetEntries && globalListenersAttached) {
    if (boundDoc) {
      boundDoc.removeEventListener("keydown", dispatchTopmostEscape);
      boundDoc = null;
    }
    if (boundWin) {
      boundWin.removeEventListener("keydown", dispatchTopmostEscape);
      boundWin = null;
    }
    globalListenersAttached = false;
  }
}

export function useEscapeClose(onCloseCallback, options = {}) {
  const hasExplicitIsOpen =
    options !== null &&
    typeof options === "object" &&
    Object.prototype.hasOwnProperty.call(options, "isOpen") &&
    options.isOpen !== undefined;

  const customTarget =
    options !== null && typeof options === "object"
      ? options.target || null
      : null;

  const isEnabled = () => {
    if (!hasExplicitIsOpen) return true;
    const raw = options.isOpen;
    return Boolean(typeof raw === "function" ? raw() : unref(raw));
  };

  const entry = {
    isEnabled,
    onClose: onCloseCallback,
    target: customTarget,
    customHandler: null,
    order: isEnabled() ? nextOverlayOrder() : 0,
  };

  const register = () => {
    const existingIdx = escapeStack.indexOf(entry);
    if (existingIdx !== -1) {
      escapeStack.splice(existingIdx, 1);
    }
    if (entry.isEnabled() && !entry.order) {
      entry.order = nextOverlayOrder();
    }
    escapeStack.push(entry);

    if (customTarget && typeof customTarget.addEventListener === "function") {
      if (!entry.customHandler) {
        entry.customHandler = (e) => {
          if (!e || e.key !== "Escape" || handledEscapeEvents.has(e)) return;
          if (e.defaultPrevented) return;
          if (!entry.isEnabled()) return;
          const trapInfo = getTopActiveTrapEscapeInfo();
          if (
            trapInfo.locked ||
            (trapInfo.active &&
              trapInfo.handlesEscape &&
              trapInfo.order > (entry.order || 0))
          ) {
            return;
          }
          handledEscapeEvents.add(e);
          if (typeof e.preventDefault === "function") {
            e.preventDefault();
          }
          if (typeof entry.onClose === "function") {
            entry.onClose(e);
          }
        };
        customTarget.addEventListener("keydown", entry.customHandler);
      }
    } else {
      syncGlobalEscapeListeners();
    }
  };

  const unregister = () => {
    const idx = escapeStack.indexOf(entry);
    if (idx !== -1) {
      escapeStack.splice(idx, 1);
    }
    if (
      customTarget &&
      entry.customHandler &&
      typeof customTarget.removeEventListener === "function"
    ) {
      customTarget.removeEventListener("keydown", entry.customHandler);
      entry.customHandler = null;
    } else {
      syncGlobalEscapeListeners();
    }
  };

  let stopWatch = null;
  if (hasExplicitIsOpen && (getCurrentInstance() || getCurrentScope())) {
    let wasOpen = isEnabled();
    stopWatch = watchEffect(
      () => {
        const openNow = isEnabled();
        if (openNow && !wasOpen) {
          entry.order = nextOverlayOrder();
          if (escapeStack.includes(entry)) {
            const idx = escapeStack.indexOf(entry);
            escapeStack.splice(idx, 1);
            escapeStack.push(entry);
          }
        }
        wasOpen = openNow;
      },
      { flush: "sync" }
    );
  }

  const stop = () => {
    if (stopWatch) {
      stopWatch();
      stopWatch = null;
    }
    unregister();
  };

  if (getCurrentInstance()) {
    onMounted(register);
    onUnmounted(stop);
  } else if (getCurrentScope()) {
    register();
    onScopeDispose(stop);
  }

  return { stop };
}
