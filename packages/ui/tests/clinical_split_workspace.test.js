// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createApp, effectScope, h, nextTick, ref } from "vue";
import {
  ClinicalModal,
  ClinicalSplitWorkspace,
  SPLIT_WORKSPACE_COMPACT_BREAKPOINT,
  SPLIT_WORKSPACE_SHEET_BREAKPOINT,
  useResponsiveSplitWorkspace,
} from "../index.js";

function createViewportController(initialWidth = 1440) {
  let currentWidth = initialWidth;
  const listeners = new Set();

  const evaluate = (query) => {
    const maxMatch = query.match(/\(max-width:\s*(\d+)px\)/);
    if (maxMatch) {
      return currentWidth <= Number(maxMatch[1]);
    }
    const minMatch = query.match(/\(min-width:\s*(\d+)px\)/);
    if (minMatch) {
      return currentWidth >= Number(minMatch[1]);
    }
    return false;
  };

  const matchMediaMock = vi.fn().mockImplementation((query) => ({
    media: query,
    get matches() {
      return evaluate(query);
    },
    onchange: null,
    addEventListener: vi.fn((type, cb) => {
      if (type === "change") listeners.add(cb);
    }),
    removeEventListener: vi.fn((type, cb) => {
      if (type === "change") listeners.delete(cb);
    }),
    addListener: vi.fn((cb) => listeners.add(cb)),
    removeListener: vi.fn((cb) => listeners.delete(cb)),
    dispatchEvent: vi.fn(),
  }));

  Object.defineProperty(window, "innerWidth", {
    configurable: true,
    writable: true,
    value: currentWidth,
  });
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    writable: true,
    value: matchMediaMock,
  });

  return {
    setWidth(nextWidth) {
      currentWidth = nextWidth;
      window.innerWidth = nextWidth;
      for (const cb of Array.from(listeners)) {
        cb({ matches: true });
      }
      window.dispatchEvent(new Event("resize"));
    },
  };
}

describe("useResponsiveSplitWorkspace & ClinicalSplitWorkspace", () => {
  let originalMatchMedia;
  let originalInnerWidth;

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
    originalInnerWidth = window.innerWidth;
  });

  afterEach(() => {
    document.body.innerHTML = "";
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      writable: true,
      value: originalMatchMedia,
    });
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: originalInnerWidth,
    });
  });

  it("tracks inline (>=1280px), drawer (641px-1279px), and sheet (<=640px) modes and clears drawer open state on desktop resize", () => {
    expect(SPLIT_WORKSPACE_COMPACT_BREAKPOINT).toBe(1280);
    expect(SPLIT_WORKSPACE_SHEET_BREAKPOINT).toBe(640);

    const viewport = createViewportController(390);
    const modes = [];
    const scope = effectScope();
    let state;
    scope.run(() => {
      state = useResponsiveSplitWorkspace({
        onModeChange: (mode) => modes.push(mode),
      });
    });

    expect(state.isCompact.value).toBe(true);
    expect(state.isMobileSheet.value).toBe(true);
    expect(state.presentationMode.value).toBe("sheet");

    const btn = document.createElement("button");
    const innerSpan = document.createElement("span");
    btn.appendChild(innerSpan);
    document.body.appendChild(btn);

    state.openDetail({ target: innerSpan });
    expect(state.isDetailOpen.value).toBe(true);
    expect(state.activeTriggerEl.value).toBe(btn);

    viewport.setWidth(1024);
    expect(state.isCompact.value).toBe(true);
    expect(state.isMobileSheet.value).toBe(false);
    expect(state.presentationMode.value).toBe("drawer");
    expect(state.isDetailOpen.value).toBe(true);

    viewport.setWidth(1280);
    expect(state.isCompact.value).toBe(false);
    expect(state.presentationMode.value).toBe("inline");
    expect(state.isDetailOpen.value).toBe(false);

    state.toggleDetail();
    expect(state.isDetailOpen.value).toBe(true);
    state.toggleDetail(false);
    expect(state.isDetailOpen.value).toBe(false);

    expect(modes).toEqual(["drawer", "inline"]);
    scope.stop();
  });

  it("renders detail inline at >=1280px and inside ClinicalDrawer below 1280px with Escape trigger focus restoration", async () => {
    const viewport = createViewportController(1440);
    const host = document.createElement("div");
    document.body.appendChild(host);

    const app = createApp({
      render() {
        return h(
          ClinicalSplitWorkspace,
          {
            drawerTitle: "SOP Reader",
            drawerSubtitle: "SOP-PLN-105",
            teleport: false,
          },
          {
            master: ({ openDetail }) =>
              h(
                "button",
                {
                  id: "btn-item-1",
                  type: "button",
                  onClick: (event) => openDetail(event),
                },
                "Open SOP-PLN-105"
              ),
            detail: () =>
              h("div", { id: "detail-region" }, [
                h(
                  "button",
                  { id: "btn-inside-detail", type: "button" },
                  "Acknowledge"
                ),
              ]),
          }
        );
      },
    });

    app.mount(host);
    await nextTick();

    expect(
      host.querySelector(".clinical-split-detail #detail-region")
    ).not.toBeNull();
    expect(host.querySelector(".clinical-drawer-panel")).toBeNull();

    viewport.setWidth(768);
    await nextTick();

    expect(host.querySelector(".clinical-split-detail")).toBeNull();
    expect(host.querySelector(".clinical-drawer-panel")).toBeNull();

    const triggerBtn = host.querySelector("#btn-item-1");
    triggerBtn.focus();
    triggerBtn.click();
    await nextTick();
    await nextTick();

    const drawerPanel = host.querySelector(".clinical-drawer-panel");
    expect(drawerPanel).not.toBeNull();
    expect(drawerPanel.classList.contains("drawer-mobile-bottom-sheet")).toBe(
      true
    );
    expect(drawerPanel.contains(document.activeElement)).toBe(true);

    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();

    expect(host.querySelector(".clinical-drawer-panel")).toBeNull();
    expect(document.activeElement).toBe(triggerBtn);

    app.unmount();
  });

  it("preserves stateful editor DOM when keepMounted is enabled and coordinates LIFO Escape with nested ClinicalModal", async () => {
    createViewportController(390);
    const host = document.createElement("div");
    document.body.appendChild(host);

    const showEditor = ref(false);
    const showReasonModal = ref(false);
    const draftText = ref("");

    const app = createApp({
      render() {
        return h("div", [
          h(
            ClinicalSplitWorkspace,
            {
              modelValue: showEditor.value,
              "onUpdate:modelValue": (val) => {
                showEditor.value = val;
              },
              inlineOpen: showEditor.value,
              keepMounted: true,
              layoutVariant: "main-inspector",
              drawerTitle: "Template Editor",
              teleport: false,
            },
            {
              master: ({ openDetail }) =>
                h(
                  "button",
                  {
                    id: "btn-edit-template",
                    type: "button",
                    onClick: (event) => openDetail(event),
                  },
                  "Edit Template"
                ),
              detail: () =>
                h("div", { id: "stateful-editor" }, [
                  h("label", { for: "draft-input" }, "Template Name"),
                  h("input", {
                    id: "draft-input",
                    type: "text",
                    value: draftText.value,
                    onInput: (e) => {
                      draftText.value = e.target.value;
                    },
                  }),
                  h(
                    "button",
                    {
                      id: "btn-open-sign-modal",
                      type: "button",
                      onClick: () => {
                        showReasonModal.value = true;
                      },
                    },
                    "Save with Part 11 Reason"
                  ),
                ]),
            }
          ),
          h(
            ClinicalModal,
            {
              modelValue: showReasonModal.value,
              "onUpdate:modelValue": (val) => {
                showReasonModal.value = val;
              },
              title: "21 CFR Part 11 Change Reason",
              teleport: false,
            },
            {
              default: () => [
                h("label", { for: "reason-input" }, "Reason"),
                h("input", { id: "reason-input", type: "text" }),
              ],
            }
          ),
        ]);
      },
    });

    app.mount(host);
    await nextTick();

    // Mounted while closed via keepMounted
    expect(host.querySelector("#stateful-editor")).not.toBeNull();
    expect(
      host
        .querySelector(".clinical-drawer-backdrop")
        .classList.contains("is-closed")
    ).toBe(true);

    const editBtn = host.querySelector("#btn-edit-template");
    editBtn.focus();
    editBtn.click();
    await nextTick();
    await nextTick();

    expect(showEditor.value).toBe(true);
    const draftInput = host.querySelector("#draft-input");
    draftInput.value = "Protocol v2 Consent";
    draftInput.dispatchEvent(new Event("input", { bubbles: true }));
    await nextTick();

    // Open nested Part 11 ClinicalModal
    const openModalBtn = host.querySelector("#btn-open-sign-modal");
    openModalBtn.focus();
    openModalBtn.click();
    await nextTick();
    await nextTick();

    expect(host.querySelector(".clinical-modal-dialog")).not.toBeNull();

    // First Escape closes only nested ClinicalModal (LIFO), keeping drawer open
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();

    expect(host.querySelector(".clinical-modal-dialog")).toBeNull();
    expect(showEditor.value).toBe(true);

    // Second Escape closes drawer while preserving mounted input value
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();

    expect(showEditor.value).toBe(false);
    expect(host.querySelector("#stateful-editor")).not.toBeNull();
    expect(host.querySelector("#draft-input").value).toBe(
      "Protocol v2 Consent"
    );
    expect(document.activeElement).toBe(editBtn);

    app.unmount();
  });
});
