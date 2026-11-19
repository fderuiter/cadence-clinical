import {
  getCurrentInstance,
  getCurrentScope,
  isRef,
  nextTick,
  onBeforeUnmount,
  onMounted,
  onScopeDispose,
  unref,
  watch,
} from "vue";

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const activeTrapStack = [];
const handledEvents = new WeakSet();

const resolveOption = (val) => (typeof val === "function" ? val() : unref(val));

const isJSDOMEnv = () =>
  typeof navigator !== "undefined" &&
  (navigator.userAgent.includes("jsdom") ||
    navigator.userAgent.includes("Node.js") ||
    (typeof window !== "undefined" && typeof window.happyDOM !== "undefined"));

const isElementInertOrHidden = (el, container) => {
  if (!el || el.disabled) return true;
  let node = el;
  while (node && node !== container) {
    if (
      node.inert ||
      (typeof node.hasAttribute === "function" && node.hasAttribute("inert")) ||
      (typeof node.getAttribute === "function" &&
        node.getAttribute("aria-hidden") === "true")
    ) {
      return true;
    }
    if (typeof window !== "undefined") {
      const style = window.getComputedStyle(node);
      if (style.display === "none" || style.visibility === "hidden") {
        return true;
      }
    }
    node = node.parentElement;
  }
  if (container) {
    if (
      container.inert ||
      (typeof container.hasAttribute === "function" &&
        container.hasAttribute("inert")) ||
      (typeof container.getAttribute === "function" &&
        container.getAttribute("aria-hidden") === "true")
    ) {
      return true;
    }
    if (typeof window !== "undefined") {
      const containerStyle = window.getComputedStyle(container);
      if (
        containerStyle.display === "none" ||
        containerStyle.visibility === "hidden"
      ) {
        return true;
      }
    }
  }
  if (typeof window === "undefined" || isJSDOMEnv()) {
    return false;
  }
  return !(
    el.offsetWidth > 0 ||
    el.offsetHeight > 0 ||
    el.getClientRects().length > 0
  );
};

export function getFocusableElements(container) {
  if (!container || typeof container.querySelectorAll !== "function") {
    return [];
  }
  return Array.from(container.querySelectorAll(FOCUSABLE_SELECTOR)).filter(
    (el) => !isElementInertOrHidden(el, container)
  );
}

const pruneDisconnectedTraps = () => {
  if (
    typeof document === "undefined" ||
    typeof document.contains !== "function"
  ) {
    return;
  }
  for (let i = activeTrapStack.length - 1; i >= 0; i--) {
    const trap = activeTrapStack[i];
    const container = trap.getContainer();
    if (container && document.contains(container)) {
      trap.wasConnected = true;
    } else if (
      trap.wasConnected &&
      (!container || !document.contains(container))
    ) {
      activeTrapStack.splice(i, 1);
    }
  }
};

export function isTopTrapEscapeLocked() {
  pruneDisconnectedTraps();
  if (activeTrapStack.length === 0) return false;
  return (
    activeTrapStack[activeTrapStack.length - 1].escapeDeactivates === false
  );
}

export function hasActiveFocusTrapWithEscape() {
  pruneDisconnectedTraps();
  if (activeTrapStack.length === 0) return false;
  const topTrap = activeTrapStack[activeTrapStack.length - 1];
  return (
    topTrap.escapeDeactivates === false ||
    typeof topTrap.onEscape === "function"
  );
}

const handleTrapKeyDown = (e, trapEntry) => {
  if (!e || handledEvents.has(e)) return;
  if (e.key !== "Tab" && e.key !== "Escape") return;
  pruneDisconnectedTraps();
  if (activeTrapStack.length === 0) return;

  const topTrap = activeTrapStack[activeTrapStack.length - 1];
  if (topTrap !== trapEntry) return;

  if (e.key === "Escape") {
    if (topTrap.getEscapeDeactivates() === false) {
      handledEvents.add(e);
      e.preventDefault();
      e.stopPropagation();
      return;
    }
    const onEscape = topTrap.onEscape;
    if (typeof onEscape === "function") {
      handledEvents.add(e);
      e.preventDefault();
      e.stopPropagation();
      onEscape(e);
    }
    return;
  }

  const container = topTrap.getContainer();
  if (!container) return;

  // Tab / Shift+Tab containment
  handledEvents.add(e);
  const focusableElements = getFocusableElements(container);
  if (focusableElements.length === 0) {
    e.preventDefault();
    if (typeof container.focus === "function") {
      container.focus();
    }
    return;
  }

  const firstEl = focusableElements[0];
  const lastEl = focusableElements[focusableElements.length - 1];
  const activeEl =
    typeof document !== "undefined" ? document.activeElement : null;

  if (e.shiftKey) {
    if (activeEl === firstEl || !container.contains(activeEl)) {
      e.preventDefault();
      lastEl.focus();
    }
  } else if (activeEl === lastEl || !container.contains(activeEl)) {
    e.preventDefault();
    firstEl.focus();
  }
};

/**
 * Reactive dialog focus trap composable.
 *
 * Moves focus inside an overlay when opened, wraps Tab and Shift+Tab inside,
 * optionally closes on Escape (or blocks Escape when `escapeDeactivates: false`),
 * supports nested modal stacks, and restores focus to the trigger when closed.
 *
 * @param {import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null)} targetRef
 * @param {Object} [options]
 * @param {import("vue").Ref<boolean> | (() => boolean) | boolean} [options.isOpen]
 * @param {(event: KeyboardEvent) => void} [options.onEscape]
 * @param {import("vue").Ref<boolean> | (() => boolean) | boolean} [options.escapeDeactivates=true]
 * @param {import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | string} [options.initialFocus]
 * @param {boolean | (() => HTMLElement | null)} [options.restoreFocus=true]
 */
export function useFocusTrap(
  targetRef,
  isOpenOrOptions = {},
  maybeOptions = {}
) {
  const options =
    isRef(isOpenOrOptions) ||
    typeof isOpenOrOptions === "function" ||
    typeof isOpenOrOptions === "boolean"
      ? { ...(maybeOptions || {}), isOpen: isOpenOrOptions }
      : isOpenOrOptions || {};

  const hasExplicitIsOpen =
    options !== null &&
    typeof options === "object" &&
    Object.prototype.hasOwnProperty.call(options, "isOpen") &&
    options.isOpen !== undefined;

  let candidateTrigger =
    typeof document !== "undefined" ? document.activeElement : null;
  let previousActiveElement = null;
  let isActive = false;

  const getContainer = () => resolveOption(targetRef) || null;
  const getIsOpen = () =>
    hasExplicitIsOpen ? Boolean(resolveOption(options.isOpen)) : true;
  const getEscapeDeactivates = () =>
    options.escapeDeactivates === undefined
      ? true
      : Boolean(resolveOption(options.escapeDeactivates));

  const resolveInitialFocusTarget = (container) => {
    if (!container) return null;
    if (options.initialFocus) {
      const raw = resolveOption(options.initialFocus);
      const resolved =
        typeof raw === "string" ? container.querySelector(raw) : raw;
      if (
        resolved &&
        typeof resolved.focus === "function" &&
        !isElementInertOrHidden(resolved, container)
      ) {
        return resolved;
      }
    }
    const focusables = getFocusableElements(container);
    if (focusables.length > 0) {
      return focusables[0];
    }
    if (
      !isElementInertOrHidden(container, null) &&
      typeof container.focus === "function"
    ) {
      return container;
    }
    return null;
  };

  const focusIntoContainer = (container) => {
    if (!container || typeof document === "undefined") return false;
    if (
      document.activeElement &&
      document.activeElement !== container &&
      container.contains(document.activeElement) &&
      !isElementInertOrHidden(document.activeElement, container)
    ) {
      return true;
    }
    const target = resolveInitialFocusTarget(container);
    if (target) {
      target.focus();
      return document.activeElement === target;
    }
    return false;
  };

  const trapEntry = {
    getContainer,
    getEscapeDeactivates,
    get escapeDeactivates() {
      return getEscapeDeactivates();
    },
    onEscape: options.onEscape,
    wasConnected: false,
  };

  const onTrapKeyDown = (e) => {
    handleTrapKeyDown(e, trapEntry);
  };

  const syncContainerFocus = (container) => {
    if (!container) return;
    if (
      typeof document !== "undefined" &&
      typeof document.contains === "function" &&
      document.contains(container)
    ) {
      trapEntry.wasConnected = true;
    }
    focusIntoContainer(container);
    nextTick(() => {
      if (
        isActive &&
        activeTrapStack[activeTrapStack.length - 1] === trapEntry
      ) {
        const latestContainer = getContainer();
        if (latestContainer) {
          if (
            typeof document !== "undefined" &&
            typeof document.contains === "function" &&
            document.contains(latestContainer)
          ) {
            trapEntry.wasConnected = true;
          }
          focusIntoContainer(latestContainer);
        }
      }
    });
  };

  const activate = (container) => {
    if (isActive) return;
    if (!hasExplicitIsOpen && !container) return;
    isActive = true;
    trapEntry.wasConnected = Boolean(
      container &&
      typeof document !== "undefined" &&
      typeof document.contains === "function" &&
      document.contains(container)
    );

    if (typeof document !== "undefined") {
      const currentActive = document.activeElement;
      if (
        currentActive &&
        currentActive !== document.body &&
        (!container || !container.contains(currentActive))
      ) {
        previousActiveElement = currentActive;
      } else {
        previousActiveElement = candidateTrigger;
      }
    }

    activeTrapStack.push(trapEntry);
    if (typeof document !== "undefined") {
      document.addEventListener("keydown", onTrapKeyDown);
    }
    if (typeof window !== "undefined") {
      window.addEventListener("keydown", onTrapKeyDown);
    }

    if (container) {
      syncContainerFocus(container);
    }
  };

  const deactivate = () => {
    if (!isActive) return;
    isActive = false;

    if (typeof document !== "undefined") {
      document.removeEventListener("keydown", onTrapKeyDown);
    }
    if (typeof window !== "undefined") {
      window.removeEventListener("keydown", onTrapKeyDown);
    }

    const idx = activeTrapStack.lastIndexOf(trapEntry);
    if (idx !== -1) {
      activeTrapStack.splice(idx, 1);
    }

    if (options.restoreFocus !== false && typeof document !== "undefined") {
      const customRestore =
        typeof options.restoreFocus === "function"
          ? options.restoreFocus()
          : null;
      const targetToFocus = customRestore || previousActiveElement;
      if (
        targetToFocus &&
        typeof targetToFocus.focus === "function" &&
        document.contains(targetToFocus)
      ) {
        targetToFocus.focus();
      }
    }
    previousActiveElement = null;
  };

  let stopOpenWatch = null;
  if (hasExplicitIsOpen) {
    stopOpenWatch = watch(
      () => getIsOpen(),
      (open, prevOpen) => {
        if (open && !prevOpen && typeof document !== "undefined") {
          if (
            document.activeElement &&
            document.activeElement !== document.body
          ) {
            candidateTrigger = document.activeElement;
          }
        }
      },
      { flush: "sync", immediate: true }
    );
  }

  const stopActiveWatch = watch(
    () => {
      const open = getIsOpen();
      const container = getContainer();
      if (!open) return { active: false, container: null };
      if (hasExplicitIsOpen) return { active: true, container };
      return { active: Boolean(container), container };
    },
    (state, prevState) => {
      if (state.active) {
        if (!isActive) {
          activate(state.container);
        } else if (
          state.container &&
          state.container !== prevState?.container
        ) {
          syncContainerFocus(state.container);
        }
      } else if (isActive) {
        deactivate();
      }
    },
    { flush: "sync", immediate: true }
  );

  if (getCurrentInstance()) {
    onMounted(() => {
      const container = getContainer();
      if (getIsOpen() && container && !isActive) {
        activate(container);
      }
    });

    onBeforeUnmount(() => {
      if (stopOpenWatch) stopOpenWatch();
      stopActiveWatch();
      deactivate();
    });
  } else if (getCurrentScope()) {
    onScopeDispose(() => {
      if (stopOpenWatch) stopOpenWatch();
      stopActiveWatch();
      deactivate();
    });
  }

  return {
    activate: () => activate(getContainer()),
    deactivate,
  };
}
