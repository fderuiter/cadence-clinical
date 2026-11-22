import fs from "fs";
import path from "path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { computed, effectScope, nextTick, ref } from "vue";
import {
  ClinicalModal,
  ClinicalDrawer,
  FileUploadModal,
  AiSuggestionDrawer,
  useFocusTrap,
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
    if (selector.startsWith("#")) {
      return this.getAttribute("id") === selector.slice(1);
    }
    return this.matchesFocusable();
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
  let dom;
  let activeScopes = [];

  beforeEach(() => {
    origDocument = globalThis.document;
    origWindow = globalThis.window;
    dom = createMockDom();
    globalThis.document = dom.doc;
    globalThis.window = dom.win;
    activeScopes = [];
  });

  afterEach(() => {
    for (const s of activeScopes) {
      s.stop();
    }
    globalThis.document = origDocument;
    globalThis.window = origWindow;
  });

  it("exports useFocusTrap, useEscapeClose, getFocusableElements, ClinicalModal, and ClinicalDrawer from ui", () => {
    expect(useFocusTrap).toBeTypeOf("function");
    expect(useEscapeClose).toBeTypeOf("function");
    expect(getFocusableElements).toBeTypeOf("function");
    expect(ClinicalModal).toBeDefined();
    expect(ClinicalDrawer).toBeDefined();
    expect(FileUploadModal).toBeDefined();
    expect(AiSuggestionDrawer).toBeDefined();
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

  it("enforces shared primitives across ClinicalDrawer, FileUploadModal, and inline ClinicalQueryPanel", () => {
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

    const uploadSrc = fs.readFileSync(
      path.resolve(__dirname, "../src/components/FileUploadModal.vue"),
      "utf-8"
    );
    expect(uploadSrc).toContain("<ClinicalModal");
    expect(uploadSrc).toContain(':dismissible="!isUploadLocked"');
    expect(uploadSrc).not.toContain("vuejs-accessibility");

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

  it("suppresses premature trigger restoration when restoreFocus resolves to false and preserves external trigger across modal/non-modal transitions", async () => {
    const launchBtn = dom.body.appendChild(
      new MockElement("button", { id: "tour-launch-btn" })
    );
    const popover = dom.body.appendChild(
      new MockElement("div", { id: "tour-popover", tabindex: "-1" })
    );
    const dismissBtn = popover.appendChild(
      new MockElement("button", { id: "tour-dismiss-btn" })
    );
    const nextBtn = popover.appendChild(
      new MockElement("button", { id: "tour-next-btn" })
    );

    const isTourOpen = ref(false);
    const isModalStep = ref(false);
    let trapHandle;

    const scope = effectScope();
    activeScopes.push(scope);
    scope.run(() => {
      trapHandle = useFocusTrap(ref(popover), {
        isOpen: isModalStep,
        restoreFocus: () => (isTourOpen.value ? false : launchBtn),
      });
    });

    // 1. Launch on modal step 1 from external trigger
    launchBtn.focus();
    isTourOpen.value = true;
    isModalStep.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(dismissBtn);

    // 2. Advance to non-modal step 2 while tour stays open:
    // deactivate() must NOT yank focus back to launchBtn
    nextBtn.focus();
    isModalStep.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(nextBtn);

    // 3. Re-entering a modal step while focus is already inside popover
    // preserves launchBtn as candidateTrigger and restores focus on final close
    isModalStep.value = true;
    await nextTick();
    isTourOpen.value = false;
    isModalStep.value = false;
    await nextTick();
    expect(dom.doc.activeElement).toBe(launchBtn);

    // 4. Direct deactivate({ restoreFocus: false }) override also suppresses focus return
    launchBtn.focus();
    isTourOpen.value = false;
    isModalStep.value = true;
    await nextTick();
    expect(dom.doc.activeElement).toBe(dismissBtn);
    trapHandle.deactivate({ restoreFocus: false });
    expect(dom.doc.activeElement).toBe(dismissBtn);
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
});
