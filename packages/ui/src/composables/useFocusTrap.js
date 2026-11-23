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

const NON_CONTENT_TAGS = new Set([
  "SCRIPT",
  "STYLE",
  "LINK",
  "TEMPLATE",
  "NOSCRIPT",
]);

const activeTrapStack = [];
const handledEvents = new WeakSet();
const managedBackgroundNodes = new Map();

const resolveOption = (val) => (typeof val === "function" ? val() : unref(val));

const isExemptLiveElement = (el) => {
  if (!el || typeof el.getAttribute !== "function") return false;
  if (el.getAttribute("data-focus-trap-exempt") === "true") return true;
  if (el.getAttribute("id") === "toast-container") return true;
  const cls = el.getAttribute("class") || "";
  return /\btoast-host\b/.test(cls);
};

const containsExemptLiveElement = (el) => {
  if (!el || !el.children) return false;
  const stack = Array.from(el.children);
  while (stack.length > 0) {
    const child = stack.pop();
    if (isExemptLiveElement(child)) return true;
    if (child.children && child.children.length > 0) {
      for (let i = 0; i < child.children.length; i++) {
        stack.push(child.children[i]);
      }
    }
  }
  return false;
};

const SUPPORTS_NATIVE_INERT_PROP =
  typeof HTMLElement !== "undefined" && "inert" in HTMLElement.prototype;

const restoreManagedNode = (el, snapshot) => {
  if (!el || !snapshot) return;
  if (SUPPORTS_NATIVE_INERT_PROP) {
    el.inert = snapshot.hadInertProp;
  }
  if (snapshot.hadInertAttr) {
    if (typeof el.setAttribute === "function") {
      el.setAttribute("inert", "");
    }
  } else if (
    !snapshot.hadInertProp &&
    typeof el.removeAttribute === "function"
  ) {
    el.removeAttribute("inert");
  }
  if (snapshot.prevAriaHidden === null) {
    if (typeof el.removeAttribute === "function") {
      el.removeAttribute("aria-hidden");
    }
  } else if (typeof el.setAttribute === "function") {
    el.setAttribute("aria-hidden", snapshot.prevAriaHidden);
  }
};

const collectInertTargetsForElement = (el, targetSet) => {
  if (!el || NON_CONTENT_TAGS.has(el.tagName)) return;
  if (isExemptLiveElement(el)) return;
  if (containsExemptLiveElement(el)) {
    for (const child of Array.from(el.children || [])) {
      collectInertTargetsForElement(child, targetSet);
    }
    return;
  }
  targetSet.add(el);
};

const reconcileBackgroundInert = () => {
  const hasBody =
    typeof document !== "undefined" &&
    Boolean(document.body) &&
    typeof document.body.contains === "function";

  let topModalContainer = null;
  if (hasBody) {
    for (let i = activeTrapStack.length - 1; i >= 0; i--) {
      const trap = activeTrapStack[i];
      if (!trap.getModal()) continue;
      const candidate = trap.getContainer();
      if (
        candidate &&
        candidate !== document.body &&
        document.body.contains(candidate)
      ) {
        topModalContainer = candidate;
        break;
      }
    }
  }

  const nextTargets = new Set();
  if (topModalContainer && hasBody) {
    let branchNode = topModalContainer;
    while (
      branchNode &&
      branchNode !== document.body &&
      branchNode.parentElement
    ) {
      const parent = branchNode.parentElement;
      for (const sibling of Array.from(parent.children || [])) {
        if (sibling === branchNode) continue;
        collectInertTargetsForElement(sibling, nextTargets);
      }
      branchNode = parent;
    }
  }

  for (const [el, snapshot] of Array.from(managedBackgroundNodes.entries())) {
    if (!nextTargets.has(el)) {
      restoreManagedNode(el, snapshot);
      managedBackgroundNodes.delete(el);
    }
  }

  for (const el of nextTargets) {
    if (!managedBackgroundNodes.has(el)) {
      managedBackgroundNodes.set(el, {
        hadInertProp: SUPPORTS_NATIVE_INERT_PROP ? Boolean(el.inert) : false,
        hadInertAttr:
          typeof el.hasAttribute === "function" && el.hasAttribute("inert"),
        prevAriaHidden:
          typeof el.getAttribute === "function"
            ? el.getAttribute("aria-hidden")
            : null,
      });
    }
    if (SUPPORTS_NATIVE_INERT_PROP) {
      el.inert = true;
    }
    if (typeof el.setAttribute === "function") {
      el.setAttribute("inert", "");
      el.setAttribute("aria-hidden", "true");
    }
  }
};

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
  let removedAny = false;
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
      removedAny = true;
    }
  }
  if (removedAny) {
    reconcileBackgroundInert();
  }
};

let overlayOrderSequence = 0;
let getTopEscapeEntryOrder = () => 0;

export function nextOverlayOrder() {
  overlayOrderSequence += 1;
  return overlayOrderSequence;
}

export function setTopEscapeOrderGetter(fn) {
  getTopEscapeEntryOrder = typeof fn === "function" ? fn : () => 0;
}

export function getTopActiveTrapEscapeInfo() {
  pruneDisconnectedTraps();
  if (activeTrapStack.length === 0) {
    return { active: false, order: 0, locked: false, handlesEscape: false };
  }
  const topTrap = activeTrapStack[activeTrapStack.length - 1];
  const locked = topTrap.getEscapeDeactivates() === false;
  return {
    active: true,
    order: topTrap.order || 0,
    locked,
    handlesEscape: locked || typeof topTrap.onEscape === "function",
  };
}

export function isTopTrapEscapeLocked() {
  return getTopActiveTrapEscapeInfo().locked;
}

export function hasActiveFocusTrapWithEscape() {
  return getTopActiveTrapEscapeInfo().handlesEscape;
}

export function trapTabFocus(e, container) {
  if (!e || e.key !== "Tab" || !container || handledEvents.has(e)) {
    return false;
  }
  handledEvents.add(e);
  const focusableElements = getFocusableElements(container);
  if (focusableElements.length === 0) {
    e.preventDefault();
    if (typeof container.focus === "function") {
      container.focus();
    }
    return true;
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
  return true;
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
    if (e.defaultPrevented) {
      handledEvents.add(e);
      return;
    }
    if (getTopEscapeEntryOrder() > (topTrap.order || 0)) {
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
  trapTabFocus(e, container);
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
  const getModal = () =>
    options.modal === undefined ? true : Boolean(resolveOption(options.modal));
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
    getModal,
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
    reconcileBackgroundInert();
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
          reconcileBackgroundInert();
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

    trapEntry.order = nextOverlayOrder();
    activeTrapStack.push(trapEntry);
    if (container) {
      if (SUPPORTS_NATIVE_INERT_PROP) {
        container.inert = false;
      }
      if (typeof container.removeAttribute === "function") {
        container.removeAttribute("inert");
        if (container.getAttribute?.("aria-hidden") === "true") {
          container.removeAttribute("aria-hidden");
        }
      }
    }
    if (typeof document !== "undefined") {
      document.addEventListener("keydown", onTrapKeyDown);
    }
    if (typeof window !== "undefined") {
      window.addEventListener("keydown", onTrapKeyDown);
    }

    reconcileBackgroundInert();
    if (container) {
      syncContainerFocus(container);
    }
  };

  const resolveExternalFocusTarget = (source) => {
    if (!source || source === true || typeof document === "undefined") {
      return null;
    }
    const raw = resolveOption(source);
    if (!raw || raw === true) return null;
    const unwrapped = isRef(raw) ? raw.value : raw;
    if (!unwrapped) return null;
    if (typeof unwrapped === "string") {
      const queryRoot =
        typeof document.querySelector === "function" ? document : document.body;
      return typeof queryRoot?.querySelector === "function"
        ? queryRoot.querySelector(unwrapped)
        : null;
    }
    return unwrapped;
  };

  const isValidRestoreTarget = (el) => {
    if (
      !el ||
      typeof el.focus !== "function" ||
      typeof document === "undefined"
    ) {
      return false;
    }
    if (typeof document.contains === "function" && !document.contains(el)) {
      return false;
    }
    if (el.disabled || el.getAttribute?.("aria-disabled") === "true") {
      return false;
    }
    if (isElementInertOrHidden(el, null)) {
      return false;
    }
    return true;
  };

  const resolveParentTrapFocusTarget = (parentTrap) => {
    if (!parentTrap) return null;
    const parentContainer = parentTrap.getContainer?.();
    if (!parentContainer || typeof document === "undefined") return null;
    if (
      typeof document.contains === "function" &&
      !document.contains(parentContainer)
    ) {
      return null;
    }
    const focusables = getFocusableElements(parentContainer);
    if (focusables.length > 0) {
      return focusables[0];
    }
    if (
      !isElementInertOrHidden(parentContainer, null) &&
      typeof parentContainer.focus === "function"
    ) {
      return parentContainer;
    }
    return null;
  };

  const restoreFocusWithFallback = (capturedPrev, fallbackParentTrap) => {
    if (options.restoreFocus === false || typeof document === "undefined") {
      return false;
    }
    const activeParentTrap =
      activeTrapStack.length > 0
        ? activeTrapStack[activeTrapStack.length - 1]
        : fallbackParentTrap;

    const candidates = [
      resolveExternalFocusTarget(options.restoreFocus),
      capturedPrev,
      resolveExternalFocusTarget(options.fallbackFocus),
      resolveParentTrapFocusTarget(activeParentTrap),
    ];

    for (const candidate of candidates) {
      if (isValidRestoreTarget(candidate)) {
        candidate.focus();
        if (document.activeElement === candidate) {
          return true;
        }
      }
    }
    return false;
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

    reconcileBackgroundInert();

    const capturedPrev = previousActiveElement;
    const parentTrapSnapshot =
      activeTrapStack.length > 0
        ? activeTrapStack[activeTrapStack.length - 1]
        : null;
    previousActiveElement = null;

    if (options.restoreFocus !== false && typeof document !== "undefined") {
      restoreFocusWithFallback(capturedPrev, parentTrapSnapshot);
      nextTick(() => {
        if (isActive || typeof document === "undefined") return;
        const current = document.activeElement;
        const lostToBodyOrInvalid =
          !current ||
          current === document.body ||
          !isValidRestoreTarget(current);
        if (lostToBodyOrInvalid) {
          restoreFocusWithFallback(capturedPrev, parentTrapSnapshot);
        }
      });
    }
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
