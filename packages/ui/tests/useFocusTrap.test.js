import fs from "fs";
import path from "path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { computed, effectScope, nextTick, ref } from "vue";
import {
  ClinicalModal,
  ClinicalDrawer,
  useFocusTrap,
  hasActiveModalFocusTrap,
  getTopActiveModalContainer,
  trapTabFocus,
  useInlineFocusHandoff,
  handoffFocus,
  handoffRetryFocus,
  useRetryFocusHandoff,
  useEscapeClose,
  getFocusableElements,
} from "../index.js";
import {
  isTopTrapEscapeLocked,
  hasActiveFocusTrapWithEscape,
} from "../src/composables/useFocusTrap.js";

class MockElement {
  get [Symbol.toStringTag]() {
    return "HTMLElement";
  }

  constructor(tagName = "div", attrs = {}) {
    this.tagName = tagName.toUpperCase();
    this.attributes = new Map();
    this.children = [];
    this.parentElement = null;
    this.disabled = Boolean(attrs.disabled);
    this.inert = Boolean(attrs.inert);
    this.style = { display: "", visibility: "" };
    this.offsetWidth = 100;
    this.offsetHeight = 32;

    for (const [k, v] of Object.entries(attrs)) {
      if (k === "disabled" || k === "inert") continue;
      this.attributes.set(k, String(v));
    }
  }

  setAttribute(name, val) {
    if (name === "disabled") this.disabled = true;
    if (name === "inert") this.inert = true;
    this.attributes.set(name, String(val));
  }

  removeAttribute(name) {
    if (name === "disabled") this.disabled = false;
    if (name === "inert") this.inert = false;
    this.attributes.delete(name);
  }

  hasAttribute(name) {
    if (name === "disabled") return this.disabled || this.attributes.has(name);
    if (name === "inert") return this.inert || this.attributes.has(name);
    return this.attributes.has(name);
  }

  getAttribute(name) {
    return this.attributes.has(name) ? this.attributes.get(name) : null;
  }

  appendChild(child) {
    child.parentElement = this;
    this.children.push(child);
    return child;
  }

  removeChild(child) {
    const idx = this.children.indexOf(child);
    if (idx !== -1) {
      this.children.splice(idx, 1);
      child.parentElement = null;
      if (globalThis.document?.activeElement === child) {
        globalThis.document.activeElement = globalThis.document.body;
      }
      MockMutationObserver.notifyRemoved(this, [child]);
    }
    return child;
  }

  closest(selector) {
    let cur = this;
    while (cur) {
      if (cur.matchesSelector(selector)) return cur;
      cur = cur.parentElement;
    }
    return null;
  }

  get nextElementSibling() {
    if (!this.parentElement) return null;
    const idx = this.parentElement.children.indexOf(this);
    return idx >= 0 ? this.parentElement.children[idx + 1] || null : null;
  }

  get previousElementSibling() {
    if (!this.parentElement) return null;
    const idx = this.parentElement.children.indexOf(this);
    return idx > 0 ? this.parentElement.children[idx - 1] || null : null;
  }

  contains(target) {
    if (!target) return false;
    let cur = target;
    while (cur) {
      if (cur === this) return true;
      cur = cur.parentElement;
    }
    return false;
  }

  focus() {
    if (!this.disabled && globalThis.document) {
      globalThis.document.activeElement = this;
    }
  }

  getClientRects() {
    return this.offsetWidth > 0 ? [{ width: this.offsetWidth }] : [];
  }

  matchesFocusable() {
    const tag = this.tagName;
    if (tag === "BUTTON" || tag === "SELECT" || tag === "TEXTAREA") {
      return !this.disabled;
    }
    if (tag === "INPUT") {
      return !this.disabled && this.getAttribute("type") !== "hidden";
    }
    if (this.hasAttribute("href")) {
      return true;
    }
    if (this.hasAttribute("tabindex")) {
      return this.getAttribute("tabindex") !== "-1";
    }
    return false;
  }

  matchesSelector(selector) {
    const parts = String(selector)
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    if (parts.length > 1) {
      return parts.some((part) => this.matchesSelector(part));
    }
    const single = parts[0] || "";
    if (single.startsWith("#")) {
      return this.getAttribute("id") === single.slice(1);
    }
    if (single.startsWith(".")) {
      const cls = this.getAttribute("class") || "";
      return cls.split(/\s+/).includes(single.slice(1));
    }
    if (single === '[role="alert"]') {
      return this.getAttribute("role") === "alert";
    }
    if (single === '[data-testid="canvas-error-retry"]') {
      return this.getAttribute("data-testid") === "canvas-error-retry";
    }
    if (single === 'button[id*="retry"]') {
      return (
        this.tagName === "BUTTON" &&
        String(this.getAttribute("id") || "").includes("retry")
      );
    }
    if (/^h[1-4](\[tabindex="-1"\])?$/i.test(single)) {
      const tag = single.slice(0, 2).toUpperCase();
      if (this.tagName !== tag) return false;
      if (single.includes("tabindex")) {
        return this.getAttribute("tabindex") === "-1";
      }
      return true;
    }
    if (
      ["SECTION", "ARTICLE", "ASIDE", "MAIN"].includes(single.toUpperCase())
    ) {
      return this.tagName === single.toUpperCase();
    }
    if (single === '[role="dialog"]' || single === '[role="alertdialog"]') {
      const role = single.includes("alertdialog") ? "alertdialog" : "dialog";
      return this.getAttribute("role") === role;
    }
    return this.matchesFocusable();
  }

  matches(selector) {
    return this.matchesSelector(selector);
  }

  querySelectorAll(selector) {
    const out = [];
    const visit = (node) => {
      for (const child of node.children) {
        if (
          selector.includes("button:not([disabled])")
            ? child.matchesFocusable()
            : child.matchesSelector(selector)
        ) {
          out.push(child);
        }
        visit(child);
      }
    };
    visit(this);
    return out;
  }

  querySelector(selector) {
    const all = this.querySelectorAll(selector);
    return all[0] || null;
  }
}

class MockMutationObserver {
  static activeObservers = new Set();

  static notifyRemoved(targetParent, removedNodes) {
    const record = { target: targetParent, removedNodes };
    for (const entry of Array.from(MockMutationObserver.activeObservers)) {
      if (
        entry.target &&
        (entry.target === targetParent || entry.target.contains(targetParent))
      ) {
        Promise.resolve().then(() => {
          if (MockMutationObserver.activeObservers.has(entry)) {
            entry.callback([record], entry.instance);
          }
        });
      }
    }
  }

  constructor(callback) {
    this.callback = callback;
    this._entry = null;
  }

  observe(target) {
    this.disconnect();
    this._entry = { target, callback: this.callback, instance: this };
    MockMutationObserver.activeObservers.add(this._entry);
  }

  disconnect() {
    if (this._entry) {
      MockMutationObserver.activeObservers.delete(this._entry);
      this._entry = null;
    }
  }
}

function createMockDom() {
  const docListeners = new Map();
  const winListeners = new Map();
  const body = new MockElement("body");

  const addListener = (map) => (type, fn) => {
    if (!map.has(type)) map.set(type, new Set());
    map.get(type).add(fn);
  };
  const removeListener = (map) => (type, fn) => {
    map.get(type)?.delete(fn);
  };
  const dispatchTo = (map) => (event) => {
    for (const fn of Array.from(map.get(event.type) || [])) {
      fn(event);
    }
  };

  const doc = {
    body,
    activeElement: body,
    contains: (el) => body.contains(el),
    addEventListener: addListener(docListeners),
    removeEventListener: removeListener(docListeners),
    dispatchEvent: dispatchTo(docListeners),
  };

  const win = {
    getComputedStyle: (el) => el.style || { display: "", visibility: "" },
    addEventListener: addListener(winListeners),
    removeEventListener: removeListener(winListeners),
    dispatchEvent: dispatchTo(winListeners),
  };

  const dispatchKey = (key, extra = {}) => {
    const event = {
      type: "keydown",
      key,
      shiftKey: Boolean(extra.shiftKey),
      defaultPrevented: false,
      propagationStopped: false,
      preventDefault() {
        this.defaultPrevented = true;
      },
      stopPropagation() {
        this.propagationStopped = true;
      },
    };
    doc.dispatchEvent(event);
    if (!event.propagationStopped) {
      win.dispatchEvent(event);
    }
    return event;
  };

  return { doc, win, body, dispatchKey };
}

describe("Shared reactive useFocusTrap, useEscapeClose, and reduced-motion tokens", () => {
  let origDocument;
  let origWindow;
  let origMutationObserver;
  let dom;
  let activeScopes = [];

  beforeEach(() => {
    origDocument = globalThis.document;
    origWindow = globalThis.window;
    origMutationObserver = globalThis.MutationObserver;
    MockMutationObserver.activeObservers.clear();
    dom = createMockDom();
    globalThis.document = dom.doc;
    globalThis.window = dom.win;
    globalThis.MutationObserver = MockMutationObserver;
    activeScopes = [];
  });

  afterEach(() => {
    for (const s of activeScopes) {
      s.stop();
    }
    MockMutationObserver.activeObservers.clear();
    globalThis.document = origDocument;
    globalThis.window = origWindow;
    globalThis.MutationObserver = origMutationObserver;
  });

  it("exports useFocusTrap, useInlineFocusHandoff, handoffFocus, useEscapeClose, getFocusableElements, ClinicalModal, and ClinicalDrawer from ui", () => {
    expect(useFocusTrap).toBeTypeOf("function");
    expect(useInlineFocusHandoff).toBeTypeOf("function");
    expect(handoffFocus).toBeTypeOf("function");
    expect(useEscapeClose).toBeTypeOf("function");
    expect(getFocusableElements).toBeTypeOf("function");
    expect(ClinicalModal).toBeDefined();
    expect(ClinicalDrawer).toBeDefined();
  });

  it("marks background siblings inert and aria-hidden while keeping ToastHost live alerts active", async () => {
    const appShell = dom.body.appendChild(
      new MockElement("div", { id: "app" })
    );
    const openBtn = appShell.appendChild(
      new MockElement("button", { id: "open-modal-btn" })
    );
    const closedDrawer = dom.body.appendChild(
      new MockElement("aside", {
        id: "sync-drawer",
        inert: true,
        "aria-hidden": "true",
      })
    );
    const toastHost = dom.body.appendChild(
      new MockElement("div", {
        class: "toast-host",
        "data-focus-trap-exempt": "true",
        "aria-live": "assertive",
      })
    );
    const toastDismiss = toastHost.appendChild(
      new MockElement("button", { id: "toast-dismiss" })
    );
    const modalBackdrop = dom.body.appendChild(
      new MockElement("div", { class: "clinical-modal-backdrop" })
    );
    const modalDialog = modalBackdrop.appendChild(
      new MockElement("div", { id: "reason-modal", tabindex: "-1" })
    );
    const modalInput = modalDialog.appendChild(
      new MockElement("input", { id: "reason-input", type: "text" })
    );

    const isOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(modalDialog), {
        isOpen,
        onEscape: () => {
          isOpen.value = false;
        },
      });
    });

    openBtn.focus();
    isOpen.value = true;
    await nextTick();

    expect(dom.doc.activeElement).toBe(modalInput);
    // Background #app is inert + aria-hidden
    expect(appShell.inert).toBe(true);
    expect(appShell.getAttribute("aria-hidden")).toBe("true");
    // ToastHost remains live, audible, and interactive
    expect(toastHost.inert).toBe(false);
    expect(toastHost.hasAttribute("aria-hidden")).toBe(false);
    expect(getFocusableElements(toastHost)).toEqual([toastDismiss]);
    // Active modal branch is not inert
    expect(modalBackdrop.inert).toBe(false);
    expect(modalDialog.inert).toBe(false);

    // Closing restores #app while preserving pre-existing inert state on closedDrawer
    dom.dispatchKey("Escape");
    await nextTick();

    expect(isOpen.value).toBe(false);
    expect(appShell.inert).toBe(false);
    expect(appShell.hasAttribute("aria-hidden")).toBe(false);
    expect(closedDrawer.inert).toBe(true);
    expect(closedDrawer.getAttribute("aria-hidden")).toBe("true");
    expect(dom.doc.activeElement).toBe(openBtn);
  });

  it("descends into wrapper ancestors containing exempt live alerts while inerting non-exempt siblings", async () => {
    const appRoot = dom.body.appendChild(new MockElement("div", { id: "app" }));
    const mainShell = appRoot.appendChild(
      new MockElement("div", { id: "main-shell" })
    );
    const triggerBtn = mainShell.appendChild(
      new MockElement("button", { id: "trigger" })
    );
    const nestedToast = appRoot.appendChild(
      new MockElement("div", { class: "toast-host" })
    );
    const dialog = dom.body.appendChild(
      new MockElement("div", { id: "teleported-dialog", tabindex: "-1" })
    );
    dialog.appendChild(new MockElement("button", { id: "dialog-ok" }));

    const isOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(dialog), { isOpen });
    });

    triggerBtn.focus();
    isOpen.value = true;
    await nextTick();

    expect(appRoot.inert).toBe(false);
    expect(mainShell.inert).toBe(true);
    expect(mainShell.getAttribute("aria-hidden")).toBe("true");
    expect(nestedToast.inert).toBe(false);
    expect(nestedToast.hasAttribute("aria-hidden")).toBe(false);

    isOpen.value = false;
    await nextTick();
    expect(mainShell.inert).toBe(false);
    expect(mainShell.hasAttribute("aria-hidden")).toBe(false);
    expect(dom.doc.activeElement).toBe(triggerBtn);
  });

  it("dismisses only the topmost unlocked overlay via LIFO useEscapeClose stack and supports custom target/stop()", async () => {
    const outerOpen = ref(true);
    const innerOpen = ref(false);
    const onOuterEscape = vi.fn(() => {
      outerOpen.value = false;
    });
    const onInnerEscape = vi.fn(() => {
      innerOpen.value = false;
    });

    const scope = effectScope();
    activeScopes.push(scope);
    let customHandle;
    const customTarget = createMockDom();
    const onCustomEscape = vi.fn();

    scope.run(() => {
      useEscapeClose(onOuterEscape, { isOpen: outerOpen });
      useEscapeClose(onInnerEscape, { isOpen: innerOpen });
      customHandle = useEscapeClose(onCustomEscape, {
        isOpen: () => innerOpen.value,
        target: customTarget.win,
      });
    });

    innerOpen.value = true;
    await nextTick();

    // First Escape closes ONLY the topmost (inner) non-trapped overlay
    const firstEsc = dom.dispatchKey("Escape");
    expect(firstEsc.defaultPrevented).toBe(true);
    expect(onInnerEscape).toHaveBeenCalledTimes(1);
    expect(onOuterEscape).not.toHaveBeenCalled();

    // Second Escape closes the outer overlay
    const secondEsc = dom.dispatchKey("Escape");
    expect(secondEsc.defaultPrevented).toBe(true);
    expect(onOuterEscape).toHaveBeenCalledTimes(1);

    // Custom target + stop()
    innerOpen.value = true;
    await nextTick();
    customTarget.dispatchKey("Enter");
    expect(onCustomEscape).not.toHaveBeenCalled();
    const customEsc = customTarget.dispatchKey("Escape");
    expect(customEsc.defaultPrevented).toBe(true);
    expect(onCustomEscape).toHaveBeenCalledTimes(1);

    customHandle.stop();
    customTarget.dispatchKey("Escape");
    expect(onCustomEscape).toHaveBeenCalledTimes(1);
  });

  it("enforces shared primitives across ClinicalDrawer and inline ClinicalQueryPanel", () => {
    const drawerSrc = fs.readFileSync(
      path.resolve(__dirname, "../src/components/clinical/ClinicalDrawer.vue"),
      "utf-8"
    );
    expect(drawerSrc).toContain(':role="role"');
    expect(drawerSrc).toContain('default: "dialog"');
    expect(drawerSrc).toContain('aria-modal="true"');
    expect(drawerSrc).toContain("useFocusTrap");
    expect(drawerSrc).not.toContain("<main");
    expect(drawerSrc).not.toContain("vuejs-accessibility");

    const queryPanelSrc = fs.readFileSync(
      path.resolve(
        __dirname,
        "../src/components/clinical/ClinicalQueryPanel.vue"
      ),
      "utf-8"
    );
    expect(queryPanelSrc).not.toContain("useFocusTrap");
    expect(queryPanelSrc).toContain("useEscapeClose");
  });

  it("excludes inert, aria-hidden, disabled, and visibility:hidden descendants from getFocusableElements", () => {
    expect(getFocusableElements(null)).toEqual([]);

    const dialog = dom.body.appendChild(new MockElement("div"));
    const visibleBtn = dialog.appendChild(
      new MockElement("button", { id: "visible-btn" })
    );
    dialog.appendChild(
      new MockElement("button", { id: "disabled-btn", disabled: true })
    );
    dialog.appendChild(
      new MockElement("input", { id: "hidden-input", type: "hidden" })
    );

    const inertGroup = dialog.appendChild(
      new MockElement("div", { inert: true })
    );
    inertGroup.appendChild(new MockElement("button", { id: "inert-btn" }));

    const ariaHiddenGroup = dialog.appendChild(
      new MockElement("div", { "aria-hidden": "true" })
    );
    ariaHiddenGroup.appendChild(
      new MockElement("button", { id: "aria-hidden-btn" })
    );

    const cssHiddenBtn = dialog.appendChild(
      new MockElement("button", { id: "css-hidden-btn" })
    );
    cssHiddenBtn.style.visibility = "hidden";

    expect(getFocusableElements(dialog)).toEqual([visibleBtn]);

    dialog.inert = true;
    expect(getFocusableElements(dialog)).toEqual([]);
  });

  it("traps Tab and Shift+Tab, handles Escape, and restores focus to trigger for persistent isOpen drawers", async () => {
    const trigger = dom.body.appendChild(
      new MockElement("button", { id: "drawer-trigger" })
    );
    const drawer = dom.body.appendChild(
      new MockElement("aside", { id: "sync-drawer", tabindex: "-1" })
    );
    const closeBtn = drawer.appendChild(
      new MockElement("button", { id: "close-btn" })
    );
    const actionBtn = drawer.appendChild(
      new MockElement("button", { id: "sync-now-btn" })
    );

    const isOpen = ref(false);
    const onEscape = vi.fn(() => {
      isOpen.value = false;
    });

    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(drawer), {
        isOpen,
        onEscape,
      });
    });

    trigger.focus();
    expect(dom.doc.activeElement).toBe(trigger);

    isOpen.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(closeBtn);
    expect(hasActiveFocusTrapWithEscape()).toBe(true);

    // Shift+Tab from first element wraps to last element
    const shiftTabEvt = dom.dispatchKey("Tab", { shiftKey: true });
    expect(shiftTabEvt.defaultPrevented).toBe(true);
    expect(dom.doc.activeElement).toBe(actionBtn);

    // Tab from last element wraps to first element
    const tabEvt = dom.dispatchKey("Tab");
    expect(tabEvt.defaultPrevented).toBe(true);
    expect(dom.doc.activeElement).toBe(closeBtn);

    // Escape closes and restores focus to the trigger button
    const escEvt = dom.dispatchKey("Escape");
    expect(escEvt.defaultPrevented).toBe(true);
    expect(onEscape).toHaveBeenCalledTimes(1);
    await nextTick();
    expect(dom.doc.activeElement).toBe(trigger);
    expect(hasActiveFocusTrapWithEscape()).toBe(false);
  });

  it("supports 3-argument (targetRef, isOpen, options) overload, initialFocus selector, and stacked overlays", async () => {
    const outerTrigger = dom.body.appendChild(
      new MockElement("button", { id: "outer-trigger" })
    );
    const outerDialog = dom.body.appendChild(
      new MockElement("div", { id: "outer-dialog" })
    );
    const outerFirst = outerDialog.appendChild(
      new MockElement("button", { id: "outer-first" })
    );
    const openInnerBtn = outerDialog.appendChild(
      new MockElement("button", { id: "open-inner" })
    );

    const innerDialog = dom.body.appendChild(
      new MockElement("div", { id: "inner-dialog" })
    );
    innerDialog.appendChild(new MockElement("button", { id: "inner-close" }));
    const innerPin = innerDialog.appendChild(
      new MockElement("input", { id: "inner-pin", type: "password" })
    );

    const outerOpen = ref(false);
    const innerOpen = ref(false);

    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(outerDialog), outerOpen, {
        onEscape: () => {
          outerOpen.value = false;
        },
      });
      useFocusTrap(
        ref(innerDialog),
        computed(() => innerOpen.value),
        {
          initialFocus: "#inner-pin",
          onEscape: () => {
            innerOpen.value = false;
          },
        }
      );
    });

    outerTrigger.focus();
    outerOpen.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(outerFirst);

    openInnerBtn.focus();
    innerOpen.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(innerPin);

    // Escape closes ONLY the top (inner) dialog and restores focus to openInnerBtn
    dom.dispatchKey("Escape");
    await nextTick();
    expect(innerOpen.value).toBe(false);
    expect(outerOpen.value).toBe(true);
    expect(dom.doc.activeElement).toBe(openInnerBtn);

    // Second Escape closes the outer dialog and restores focus to outerTrigger
    dom.dispatchKey("Escape");
    await nextTick();
    expect(outerOpen.value).toBe(false);
    expect(dom.doc.activeElement).toBe(outerTrigger);
  });

  it("blocks Escape dismissal and useEscapeClose fallback when escapeDeactivates is false (mandatory PIN lock)", async () => {
    const pinModal = dom.body.appendChild(
      new MockElement("div", { id: "pin-modal" })
    );
    const pinInput = pinModal.appendChild(
      new MockElement("input", { id: "pin-input", type: "password" })
    );
    const unlockBtn = pinModal.appendChild(
      new MockElement("button", { id: "unlock-btn" })
    );

    const isPinLocked = ref(true);
    const fallbackEscapeSpy = vi.fn();

    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(pinModal), {
        isOpen: isPinLocked,
        escapeDeactivates: false,
      });
      useEscapeClose(fallbackEscapeSpy);
    });

    await nextTick();
    expect(dom.doc.activeElement).toBe(pinInput);
    expect(isTopTrapEscapeLocked()).toBe(true);

    const escEvt = dom.dispatchKey("Escape");
    expect(escEvt.defaultPrevented).toBe(true);
    expect(isPinLocked.value).toBe(true);
    expect(fallbackEscapeSpy).not.toHaveBeenCalled();

    // Tab still cycles inside the mandatory PIN modal
    unlockBtn.focus();
    dom.dispatchKey("Tab");
    expect(dom.doc.activeElement).toBe(pinInput);

    // Unlocking releases the trap so useEscapeClose can handle subsequent Escape presses
    isPinLocked.value = false;
    await nextTick();
    expect(isTopTrapEscapeLocked()).toBe(false);

    dom.dispatchKey("Escape");
    expect(fallbackEscapeSpy).toHaveBeenCalledTimes(1);
  });

  it("falls back to focusing container when no focusable children exist", async () => {
    const emptyModal = dom.body.appendChild(
      new MockElement("div", { id: "empty-modal", tabindex: "-1" })
    );
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(() => emptyModal);
    });
    await nextTick();
    expect(dom.doc.activeElement).toBe(emptyModal);

    const tabEvt = dom.dispatchKey("Tab");
    expect(tabEvt.defaultPrevented).toBe(true);
    expect(dom.doc.activeElement).toBe(emptyModal);
  });

  it("exposes hasActiveModalFocusTrap and getTopActiveModalContainer with excludeId and excludeElement filtering", async () => {
    expect(hasActiveModalFocusTrap()).toBe(false);
    expect(getTopActiveModalContainer()).toBeNull();

    const paletteDialog = dom.body.appendChild(
      new MockElement("div", { id: "canvas-palette", tabindex: "-1" })
    );
    paletteDialog.appendChild(
      new MockElement("input", { id: "canvas-palette-input", type: "text" })
    );
    const modalDialog = dom.body.appendChild(
      new MockElement("div", { id: "confirm-modal", tabindex: "-1" })
    );
    modalDialog.appendChild(new MockElement("button", { id: "btn-cancel" }));

    const paletteOpen = ref(false);
    const modalOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(paletteDialog), { isOpen: paletteOpen });
      useFocusTrap(ref(modalDialog), { isOpen: modalOpen });
    });

    paletteOpen.value = true;
    await nextTick();
    expect(hasActiveModalFocusTrap()).toBe(true);
    expect(hasActiveModalFocusTrap({ excludeId: "canvas-palette" })).toBe(
      false
    );
    expect(hasActiveModalFocusTrap({ excludeElement: paletteDialog })).toBe(
      false
    );
    expect(getTopActiveModalContainer()).toBe(paletteDialog);

    modalOpen.value = true;
    await nextTick();
    expect(hasActiveModalFocusTrap({ excludeId: "canvas-palette" })).toBe(true);
    expect(getTopActiveModalContainer()).toBe(modalDialog);
  });

  it("bridges forward Tab and reverse Shift+Tab across modal controls and [data-focus-trap-exempt='true'] toast buttons", async () => {
    const appShell = dom.body.appendChild(
      new MockElement("div", { id: "app" })
    );
    appShell.appendChild(new MockElement("button", { id: "bg-btn" }));

    const toastHost = dom.body.appendChild(
      new MockElement("div", {
        class: "toast-host",
        "data-focus-trap-exempt": "true",
      })
    );
    const toastAction = toastHost.appendChild(
      new MockElement("button", { id: "toast-action" })
    );
    const toastDismiss = toastHost.appendChild(
      new MockElement("button", { id: "toast-dismiss" })
    );

    const modal = dom.body.appendChild(
      new MockElement("div", { id: "active-modal", tabindex: "-1" })
    );
    const cancelBtn = modal.appendChild(
      new MockElement("button", { id: "modal-cancel" })
    );
    const confirmBtn = modal.appendChild(
      new MockElement("button", { id: "modal-confirm" })
    );

    const isOpen = ref(true);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(modal), { isOpen });
    });
    await nextTick();

    expect(dom.doc.activeElement).toBe(cancelBtn);

    // Tab from last modal control bridges into first exempt toast button
    confirmBtn.focus();
    dom.dispatchKey("Tab");
    expect(dom.doc.activeElement).toBe(toastAction);

    // Tab advances within exempt toast buttons
    dom.dispatchKey("Tab");
    expect(dom.doc.activeElement).toBe(toastDismiss);

    // Tab from last exempt toast button wraps back to first modal control
    dom.dispatchKey("Tab");
    expect(dom.doc.activeElement).toBe(cancelBtn);

    // Shift+Tab from first modal control wraps to last exempt toast button
    dom.dispatchKey("Tab", { shiftKey: true });
    expect(dom.doc.activeElement).toBe(toastDismiss);

    // Shift+Tab steps backward through exempt toast buttons and back into modal
    dom.dispatchKey("Tab", { shiftKey: true });
    expect(dom.doc.activeElement).toBe(toastAction);
    dom.dispatchKey("Tab", { shiftKey: true });
    expect(dom.doc.activeElement).toBe(confirmBtn);
  });

  it("restores focus to visible controller or #main-content instead of document.body when trigger hides or detaches", async () => {
    const mobileToggle = dom.body.appendChild(
      new MockElement("button", {
        id: "btn-mobile-menu-toggle",
        "aria-controls": "mobile-nav-menu",
      })
    );
    const mobileMenu = dom.body.appendChild(
      new MockElement("nav", { id: "mobile-nav-menu" })
    );
    const mobileLink = mobileMenu.appendChild(
      new MockElement("a", { id: "mobile-link", href: "/studies" })
    );
    const mainLandmark = dom.body.appendChild(
      new MockElement("main", { id: "main-content" })
    );
    const confirmModal = dom.body.appendChild(
      new MockElement("div", { id: "leave-confirm", tabindex: "-1" })
    );
    confirmModal.appendChild(new MockElement("button", { id: "stay-on-form" }));

    const isOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(confirmModal), { isOpen });
    });

    // 1. Trigger inside #mobile-nav-menu hides via hidden attribute before dialog closes -> restores #btn-mobile-menu-toggle
    mobileLink.focus();
    isOpen.value = true;
    await nextTick();
    mobileMenu.setAttribute("hidden", "");
    isOpen.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(mobileToggle);

    // 2. Detached trigger with hidden toggle -> falls back to #main-content with tabindex="-1"
    mobileToggle.style.display = "none";
    const detachedBtn = new MockElement("button", { id: "detached-item" });
    dom.doc.activeElement = detachedBtn;
    isOpen.value = true;
    await nextTick();
    isOpen.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(mainLandmark);
    expect(mainLandmark.getAttribute("tabindex")).toBe("-1");
  });

  it("defines prefers-reduced-motion rules in tokens.css while keeping default tokens intact", () => {
    const tokensCss = fs.readFileSync(
      path.resolve(__dirname, "../tokens.css"),
      "utf-8"
    );
    expect(tokensCss).toContain("@media (prefers-reduced-motion: reduce)");
    expect(tokensCss).toContain("--transition-fast: 0s");
    expect(tokensCss).toContain("--transition-normal: 0s");
    expect(tokensCss).toContain("animation-duration: 0.01ms !important");
    expect(tokensCss).toContain("transition-duration: 0.01ms !important");
    // Default non-reduced motion tokens remain intact outside the media query
    expect(tokensCss).toContain("--transition-fast: 0.15s ease");
    expect(tokensCss).toContain("--transition-normal: 0.25s ease");
  });

  it("handoffFocus resolves first visible target, skips hidden/inert/disabled candidates, and falls back to #canvas-workspace or #main-content", async () => {
    const workspace = dom.body.appendChild(
      new MockElement("main", { id: "canvas-workspace", tabindex: "-1" })
    );
    const hiddenHeading = workspace.appendChild(
      new MockElement("h2", { id: "hidden-heading", tabindex: "-1" })
    );
    hiddenHeading.style.display = "none";

    const inertSection = workspace.appendChild(
      new MockElement("section", { id: "inert-section", inert: true })
    );
    const inertBtn = inertSection.appendChild(
      new MockElement("button", { id: "inert-btn" })
    );

    const disabledBtn = workspace.appendChild(
      new MockElement("button", { id: "disabled-btn", disabled: true })
    );

    const visibleHeading = workspace.appendChild(
      new MockElement("h2", { id: "visible-heading" })
    );

    const disconnectedBtn = new MockElement("button", { id: "detached-btn" });

    const resolved = await handoffFocus([
      disconnectedBtn,
      "#hidden-heading",
      inertBtn,
      disabledBtn,
      visibleHeading,
    ]);

    expect(resolved).toBe(visibleHeading);
    expect(dom.doc.activeElement).toBe(visibleHeading);
    // Programmatic non-interactive headings receive tabindex="-1" (never "0")
    expect(visibleHeading.getAttribute("tabindex")).toBe("-1");
    expect(workspace.hasAttribute("inert")).toBe(false);

    // Unmodified Tab is never trapped by inline handoffFocus
    const tabEvt = dom.dispatchKey("Tab");
    expect(tabEvt.defaultPrevented).toBe(false);

    // When all explicit candidates are hidden, falls back to #canvas-workspace
    visibleHeading.style.visibility = "hidden";
    const fallbackResolved = await handoffFocus([
      "#hidden-heading",
      visibleHeading,
    ]);
    expect(fallbackResolved).toBe(workspace);
    expect(dom.doc.activeElement).toBe(workspace);
  });

  it("useInlineFocusHandoff remembers triggers across open/close swaps and falls back when original trigger unmounts", async () => {
    const main = dom.body.appendChild(
      new MockElement("main", { id: "main-content", tabindex: "-1" })
    );
    const openBtn = main.appendChild(
      new MockElement("button", { id: "btn-open-inline" })
    );
    const filterSelect = main.appendChild(
      new MockElement("select", { id: "filter-select" })
    );
    const inlineInput = main.appendChild(
      new MockElement("input", { id: "inline-reason", type: "text" })
    );
    inlineInput.style.display = "none";

    const isOpen = ref(false);
    let inlineHandoff;
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      inlineHandoff = useInlineFocusHandoff({
        isOpen,
        openTarget: "#inline-reason",
        closeFallback: "#filter-select",
      });
    });

    openBtn.focus();
    expect(dom.doc.activeElement).toBe(openBtn);

    // Open inline disclosure: focuses #inline-reason without trapping Tab
    inlineInput.style.display = "";
    isOpen.value = true;
    await nextTick();
    await nextTick();
    expect(dom.doc.activeElement).toBe(inlineInput);

    const tabEvt = dom.dispatchKey("Tab");
    expect(tabEvt.defaultPrevented).toBe(false);

    // Close inline disclosure while openBtn is still visible: restores openBtn
    inlineInput.style.display = "none";
    isOpen.value = false;
    await nextTick();
    await nextTick();
    expect(dom.doc.activeElement).toBe(openBtn);

    // Ephemeral button unmount scenario: trigger is hidden/removed before restoreTrigger runs
    const clearChip = main.appendChild(
      new MockElement("button", { id: "btn-clear-chip" })
    );
    clearChip.focus();
    inlineHandoff.rememberTrigger();
    clearChip.style.display = "none";

    const restoredAfterUnmount =
      await inlineHandoff.restoreTrigger("#filter-select");
    expect(restoredAfterUnmount).toBe(filterSelect);
    expect(dom.doc.activeElement).toBe(filterSelect);
  });

  it("falls back to fallbackFocus when previousActiveElement unmounts or becomes disabled", async () => {
    const appShell = dom.body.appendChild(
      new MockElement("div", { id: "app" })
    );
    const refreshBtn = appShell.appendChild(
      new MockElement("button", { id: "btn-refresh-qc" })
    );
    const batchAcceptBtn = appShell.appendChild(
      new MockElement("button", { id: "btn-batch-accept" })
    );
    const rowInspectBtn = appShell.appendChild(
      new MockElement("button", { id: "btn-row-inspect" })
    );
    const modalDialog = dom.body.appendChild(
      new MockElement("div", { id: "qc-modal", tabindex: "-1" })
    );
    modalDialog.appendChild(
      new MockElement("button", { id: "btn-confirm-qc" })
    );

    const isOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(modalDialog), {
        isOpen,
        fallbackFocus: "#btn-refresh-qc",
      });
    });

    // Case 1: invoking row button unmounts before close
    rowInspectBtn.focus();
    isOpen.value = true;
    await nextTick();

    appShell.children = appShell.children.filter((c) => c !== rowInspectBtn);
    rowInspectBtn.parentElement = null;

    isOpen.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(refreshBtn);

    // Case 2: invoking batch button becomes disabled on nextTick right as modal closes
    batchAcceptBtn.focus();
    isOpen.value = true;
    await nextTick();

    batchAcceptBtn.disabled = true;
    isOpen.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(refreshBtn);
  });

  it("falls back to the active parent trap on the stack when a nested modal trigger unmounts on nextTick", async () => {
    const appShell = dom.body.appendChild(
      new MockElement("div", { id: "app" })
    );
    const openDrawerBtn = appShell.appendChild(
      new MockElement("button", { id: "btn-open-drawer" })
    );
    const drawerPanel = dom.body.appendChild(
      new MockElement("aside", { id: "ticket-drawer", tabindex: "-1" })
    );
    const drawerCloseBtn = drawerPanel.appendChild(
      new MockElement("button", { id: "ticket-drawer-close-btn" })
    );
    const signTriggerBtn = drawerPanel.appendChild(
      new MockElement("button", { id: "btn-open-part11-sign" })
    );
    const signModal = dom.body.appendChild(
      new MockElement("div", { id: "sign-modal", tabindex: "-1" })
    );
    signModal.appendChild(
      new MockElement("button", { id: "btn-submit-part11" })
    );

    const isDrawerOpen = ref(false);
    const isSignOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(drawerPanel), { isOpen: isDrawerOpen });
      useFocusTrap(ref(signModal), { isOpen: isSignOpen });
    });

    openDrawerBtn.focus();
    isDrawerOpen.value = true;
    await nextTick();

    signTriggerBtn.focus();
    isSignOpen.value = true;
    await nextTick();

    // Close modal synchronously first, then unmount signTriggerBtn before nextTick completes
    // (mirrors Vue patching parent v-if="canSignTicket" after child emit("close"))
    isSignOpen.value = false;
    drawerPanel.children = drawerPanel.children.filter(
      (c) => c !== signTriggerBtn
    );
    signTriggerBtn.parentElement = null;
    dom.doc.activeElement = dom.body;

    await nextTick();
    expect(dom.doc.activeElement).toBe(drawerCloseBtn);
  });

  it("recovers focus inside the active top modal after nextTick when a focused child unmounts onto BODY, while yielding to handoffFocus and child modals (ADR-2676)", async () => {
    const appShell = dom.body.appendChild(
      new MockElement("div", { id: "app" })
    );
    const openBtn = appShell.appendChild(
      new MockElement("button", { id: "btn-open" })
    );
    const dialog = dom.body.appendChild(
      new MockElement("div", {
        id: "help-drawer",
        role: "dialog",
        tabindex: "-1",
      })
    );
    const closeBtn = dialog.appendChild(
      new MockElement("button", { id: "btn-close-drawer" })
    );
    const targetInput = dialog.appendChild(
      new MockElement("input", { id: "qc-next-input", type: "text" })
    );
    const removableBtn = dialog.appendChild(
      new MockElement("button", { id: "btn-unmounting-action" })
    );

    const isOpen = ref(false);
    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      useFocusTrap(ref(dialog), { isOpen });
    });

    openBtn.focus();
    isOpen.value = true;
    await nextTick();

    // 1. Focused child unmounts with no explicit handoff -> recovers inside dialog after nextTick
    removableBtn.focus();
    expect(dom.doc.activeElement).toBe(removableBtn);
    dialog.removeChild(removableBtn);
    expect(dom.doc.activeElement).toBe(dom.body);

    await Promise.resolve();
    await nextTick();
    await Promise.resolve();
    expect(dialog.contains(dom.doc.activeElement)).toBe(true);
    expect(dom.doc.activeElement).toBe(closeBtn);

    // 2. Focused child unmounts alongside explicit handoffFocus -> yields to handoffFocus target
    const secondRemovable = dialog.appendChild(
      new MockElement("button", { id: "btn-second-unmount" })
    );
    secondRemovable.focus();
    dialog.removeChild(secondRemovable);
    const handoffPromise = handoffFocus(targetInput, {
      container: dialog,
      includeDefaultFallbacks: false,
    });
    await handoffPromise;
    await Promise.resolve();
    await nextTick();
    await Promise.resolve();
    expect(dom.doc.activeElement).toBe(targetInput);

    // 3. Child modal stacked on top -> parent subtree removal yields to child modal
    const thirdRemovable = dialog.appendChild(
      new MockElement("button", { id: "btn-third-unmount" })
    );
    const childModal = dom.body.appendChild(
      new MockElement("div", {
        id: "child-sign-modal",
        role: "dialog",
        tabindex: "-1",
      })
    );
    const childConfirmBtn = childModal.appendChild(
      new MockElement("button", { id: "btn-child-confirm" })
    );
    const isChildOpen = ref(false);
    scope.run(() => {
      useFocusTrap(ref(childModal), { isOpen: isChildOpen });
    });
    isChildOpen.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(childConfirmBtn);

    dialog.removeChild(thirdRemovable);
    await Promise.resolve();
    await nextTick();
    await Promise.resolve();
    expect(dom.doc.activeElement).toBe(childConfirmBtn);
  });

  it("handoffRetryFocus and useRetryFocusHandoff focus the retry control on error or recovered content / heading on success (ADR-2676)", async () => {
    expect(handoffRetryFocus).toBeTypeOf("function");
    expect(useRetryFocusHandoff).toBeTypeOf("function");

    const section = dom.body.appendChild(
      new MockElement("section", { id: "queries-section" })
    );
    const heading = section.appendChild(
      new MockElement("h2", { id: "queries-heading", tabindex: "-1" })
    );
    const alertBox = section.appendChild(
      new MockElement("div", { class: "canvas-error-alert", role: "alert" })
    );
    const retryBtn = alertBox.appendChild(
      new MockElement("button", {
        id: "btn-retry-queries",
        "data-testid": "canvas-error-retry",
      })
    );

    // 1. Repeated failure keeps focus on the retry button
    retryBtn.focus();
    const { retryWithFocus } = useRetryFocusHandoff({
      container: section,
      includeDefaultFallbacks: false,
    });
    await retryWithFocus(async () => {
      section.removeChild(alertBox);
      const nextAlert = section.appendChild(
        new MockElement("div", { class: "canvas-error-alert", role: "alert" })
      );
      nextAlert.appendChild(retryBtn);
    });
    expect(dom.doc.activeElement).toBe(retryBtn);

    // 2. Recovery with interactive content after anchor focuses recovered control
    const recoveredAction = new MockElement("button", {
      id: "btn-recovered-item",
    });
    await retryWithFocus(async () => {
      const currentAlert = retryBtn.parentElement;
      if (currentAlert) section.removeChild(currentAlert);
      section.appendChild(recoveredAction);
    });
    expect(dom.doc.activeElement).toBe(recoveredAction);

    // 3. Recovery with empty state (no interactive controls) focuses section heading
    section.removeChild(recoveredAction);
    const thirdAlert = section.appendChild(
      new MockElement("div", { class: "canvas-error-alert", role: "alert" })
    );
    thirdAlert.appendChild(retryBtn);
    retryBtn.focus();
    await handoffRetryFocus(
      async () => {
        section.removeChild(thirdAlert);
      },
      { container: section, includeDefaultFallbacks: false }
    );
    expect(dom.doc.activeElement).toBe(heading);
  });
});
