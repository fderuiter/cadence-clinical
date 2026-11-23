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

function tryDispatchEntryEscape(e, entry) {
  if (
    !e ||
    e.key !== "Escape" ||
    e.defaultPrevented ||
    handledEscapeEvents.has(e) ||
    !entry?.isEnabled()
  ) {
    return;
  }
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
  e.preventDefault?.();
  entry.onClose?.(e);
}

function dispatchTopmostEscape(e) {
  tryDispatchEntryEscape(e, getTopDefaultEscapeEntry());
}

function syncGlobalEscapeListeners() {
  const hasDefaultTargetEntries = escapeStack.some((entry) => !entry.target);
  const docChanged = typeof document !== "undefined" && boundDoc !== document;
  const winChanged = typeof window !== "undefined" && boundWin !== window;
  if (globalListenersAttached && (docChanged || winChanged)) {
    boundDoc?.removeEventListener("keydown", dispatchTopmostEscape);
    boundWin?.removeEventListener("keydown", dispatchTopmostEscape);
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
    boundDoc?.removeEventListener("keydown", dispatchTopmostEscape);
    boundWin?.removeEventListener("keydown", dispatchTopmostEscape);
    boundDoc = null;
    boundWin = null;
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

    if (customTarget?.addEventListener) {
      if (!entry.customHandler) {
        entry.customHandler = (e) => tryDispatchEntryEscape(e, entry);
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
    if (customTarget?.removeEventListener && entry.customHandler) {
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
