// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { createApp, nextTick, ref } from "vue";
import { ClinicalGovernanceBanner, handoffFocus } from "../index.js";
import { resolveStickyTopOffsetPx } from "../src/composables/useInlineFocusHandoff.js";

function mountSfc(component, { attachToBody = false } = {}) {
  const container = document.createElement("div");
  if (attachToBody) {
    document.body.appendChild(container);
  }
  const app = createApp(component);
  const vm = app.mount(container);
  return {
    container,
    vm,
    unmount() {
      app.unmount();
      container.remove();
    },
  };
}

describe("ClinicalGovernanceBanner", () => {
  const cleanups = [];

  afterEach(() => {
    while (cleanups.length > 0) {
      const cleanup = cleanups.pop();
      cleanup?.();
    }
    document.documentElement.style.removeProperty("--sticky-chrome-offset");
  });

  it("renders status variants, tabular-nums gate counts, and opens/closes blocker, lock, and export drawers with Escape focus restoration", async () => {
    const activeSheet = ref(null);
    const { container, unmount } = mountSfc(
      {
        components: { ClinicalGovernanceBanner },
        setup() {
          const status = ref("blocked");
          const gates = [
            {
              key: "open_queries",
              label: "2 open queries",
              count: 2,
              testId: "gate-queries",
            },
            {
              key: "unverified_values",
              label: "0 unverified values",
              count: 0,
              testId: "gate-sdv",
            },
          ];
          return { activeSheet, status, gates };
        },
        template: `
          <div>
            <ClinicalGovernanceBanner
              id="gov-banner"
              v-model="activeSheet"
              :status="status"
              status-test-id="gov-status"
              summary="Freeze participant remains available"
              summary-test-id="gov-summary"
              :gates="gates"
              gates-test-id="gov-gates"
              :blocker-count="2"
              blocker-action-id="btn-review-blockers"
              blocker-action-test-id="gov-review-blockers"
              blocker-drawer-title="Participant readiness blockers"
              blocker-drawer-subtitle="2 unresolved gates"
              show-manage-action
              manage-action-id="btn-manage-lock"
              manage-action-label="Manage lock"
              manage-drawer-title="Manage participant lock"
              manage-drawer-subtitle="Signed scope controls"
              show-export-action
              export-action-id="btn-open-export"
              export-action-label="Export casebook"
              export-drawer-title="Export participant casebook"
              export-drawer-subtitle="Certified PDF or ODM XML"
              :teleport="false"
            >
              <template #blockers="{ close }">
                <div data-testid="blockers-sheet">
                  <p>2 open queries block Locked.</p>
                  <button id="inside-blocker-close" type="button" @click="close">
                    Done
                  </button>
                </div>
              </template>
              <template #locks>
                <div data-testid="locks-sheet">
                  <input id="inside-lock-reason" type="text" aria-label="Reason" />
                </div>
              </template>
              <template #export>
                <div data-testid="export-sheet">
                  <button id="inside-export-run" type="button">Download PDF</button>
                </div>
              </template>
              <template #outcome>
                <p data-testid="gov-outcome" role="status">Last checked now</p>
              </template>
            </ClinicalGovernanceBanner>
          </div>
        `,
      },
      { attachToBody: true }
    );
    cleanups.push(unmount);

    expect(
      container.querySelector('[data-testid="gov-status"]').textContent
    ).toContain("Blocked for Locked");
    expect(
      container.querySelector('[data-testid="gov-summary"]').textContent
    ).toContain("Freeze participant remains available");
    expect(
      container.querySelector('[data-testid="gate-queries"]').textContent
    ).toContain("2 open queries");
    expect(
      container.querySelector('[data-testid="gate-sdv"]').textContent
    ).toContain("0 unverified values");

    const blockerBtn = container.querySelector("#btn-review-blockers");
    expect(blockerBtn.textContent).toContain("Review blockers (2)");
    expect(blockerBtn.getAttribute("aria-expanded")).toBe("false");

    blockerBtn.focus();
    blockerBtn.click();
    await nextTick();
    await nextTick();

    expect(activeSheet.value).toBe("blockers");
    expect(blockerBtn.getAttribute("aria-expanded")).toBe("true");
    expect(
      container.querySelector('[data-testid="blockers-sheet"]')
    ).not.toBeNull();
    expect(
      container.querySelector(".clinical-drawer-backdrop.mobile-bottom-sheet")
    ).not.toBeNull();

    // Escape closes the blocker sheet and restores focus to the trigger button
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();

    expect(activeSheet.value).toBeNull();
    expect(document.activeElement).toBe(blockerBtn);

    // Open Manage lock sheet
    const manageBtn = container.querySelector("#btn-manage-lock");
    manageBtn.focus();
    manageBtn.click();
    await nextTick();
    await nextTick();

    expect(activeSheet.value).toBe("locks");
    expect(
      container.querySelector('[data-testid="locks-sheet"]')
    ).not.toBeNull();

    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();
    expect(activeSheet.value).toBeNull();
    expect(document.activeElement).toBe(manageBtn);

    // Open Export casebook sheet
    const exportBtn = container.querySelector("#btn-open-export");
    exportBtn.focus();
    exportBtn.click();
    await nextTick();
    await nextTick();

    expect(activeSheet.value).toBe("export");
    expect(
      container.querySelector('[data-testid="export-sheet"]')
    ).not.toBeNull();

    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    await nextTick();
    expect(activeSheet.value).toBeNull();
    expect(document.activeElement).toBe(exportBtn);
  });

  it("supports uncontrolled internal sheet state across all status labels and custom #drawer / #actions slots", async () => {
    const statuses = [
      ["locked", "Locked"],
      ["frozen", "Frozen"],
      ["ready", "Ready for Locked"],
      ["loading", "Checking readiness…"],
      ["unlocked", "Unlocked"],
    ];

    for (const [status, expectedLabel] of statuses) {
      const { container, unmount } = mountSfc(
        {
          components: { ClinicalGovernanceBanner },
          template: `
            <ClinicalGovernanceBanner
              status="${status}"
              status-test-id="status-pill"
              :teleport="false"
            >
              <template #actions="{ openSheet }">
                <button
                  id="custom-sheet-btn"
                  type="button"
                  @click="openSheet('custom', $event)"
                >
                  Inspect custom
                </button>
              </template>
              <template #drawer="{ activeSheet, close }">
                <div data-testid="fallback-drawer">
                  Sheet: {{ activeSheet }}
                  <button id="fallback-close" type="button" @click="close">
                    Close
                  </button>
                </div>
              </template>
            </ClinicalGovernanceBanner>
          `,
        },
        { attachToBody: true }
      );

      expect(
        container.querySelector('[data-testid="status-pill"]').textContent
      ).toContain(expectedLabel);

      const customBtn = container.querySelector("#custom-sheet-btn");
      customBtn.focus();
      customBtn.click();
      await nextTick();
      await nextTick();

      expect(
        container.querySelector('[data-testid="fallback-drawer"]').textContent
      ).toContain("Sheet: custom");

      container.querySelector("#fallback-close").click();
      await nextTick();
      await nextTick();

      expect(
        container.querySelector('[data-testid="fallback-drawer"]')
      ).toBeNull();
      expect(document.activeElement).toBe(customBtn);
      unmount();
    }
  });

  it("respects explicit statusLabel, activeSheet, drawerTitle/Subtitle overrides, showBlockerAction, and drawerDismissible=false", async () => {
    const activeSheet = ref("blockers");
    const { container, unmount } = mountSfc(
      {
        components: { ClinicalGovernanceBanner },
        setup() {
          return { activeSheet };
        },
        template: `
          <ClinicalGovernanceBanner
            :active-sheet="activeSheet"
            status="locked"
            status-label="Custom Locked State"
            status-test-id="override-status"
            :blocker-count="0"
            :show-blocker-action="true"
            blocker-action-id="override-blocker-btn"
            drawer-title="Explicit Drawer Heading"
            drawer-subtitle="Explicit Drawer Subheading"
            :drawer-dismissible="false"
            :teleport="false"
            @update:active-sheet="(v) => (activeSheet = v)"
          >
            <template #drawer>
              <div data-testid="non-dismissible-body">Locked in progress</div>
            </template>
          </ClinicalGovernanceBanner>
        `,
      },
      { attachToBody: true }
    );
    cleanups.push(unmount);
    await nextTick();

    expect(
      container.querySelector('[data-testid="override-status"]').textContent
    ).toContain("Custom Locked State");
    expect(
      container.querySelector("#override-blocker-btn").textContent
    ).toContain("Review blockers");
    expect(container.textContent).toContain("Explicit Drawer Heading");
    expect(container.textContent).toContain("Explicit Drawer Subheading");

    // Escape is ignored while drawerDismissible is false
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(activeSheet.value).toBe("blockers");
    expect(
      container.querySelector('[data-testid="non-dismissible-body"]')
    ).not.toBeNull();
  });

  it("scrolls focused targets and scrollTarget containers clear of --sticky-chrome-offset during handoffFocus", async () => {
    document.documentElement.style.setProperty(
      "--sticky-chrome-offset",
      "96px"
    );
    expect(resolveStickyTopOffsetPx()).toBe(96);
    expect(resolveStickyTopOffsetPx({ stickyOffset: 64 })).toBe(64);

    const origInnerHeight = window.innerHeight;
    const origScrollY = window.scrollY;
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: 768,
    });
    Object.defineProperty(window, "scrollY", {
      configurable: true,
      writable: true,
      value: 240,
    });
    const scrollBySpy = vi
      .spyOn(window, "scrollBy")
      .mockImplementation((_x, dy) => {
        window.scrollY += dy;
      });

    const wrapper = document.createElement("div");
    const formEl = document.createElement("form");
    formEl.id = "ecrf-form-new";
    let formTop = 32;
    formEl.getBoundingClientRect = () => ({
      x: 0,
      y: formTop,
      top: formTop,
      bottom: formTop + 180,
      left: 0,
      right: 520,
      width: 520,
      height: 180,
      toJSON: () => ({}),
    });

    const inputEl = document.createElement("input");
    inputEl.id = "ecrf-collected-at";
    inputEl.getBoundingClientRect = () => ({
      x: 16,
      y: formTop + 72,
      top: formTop + 72,
      bottom: formTop + 116,
      left: 16,
      right: 260,
      width: 244,
      height: 44,
      toJSON: () => ({}),
    });
    formEl.appendChild(inputEl);
    wrapper.appendChild(formEl);
    document.body.appendChild(wrapper);

    scrollBySpy.mockImplementation((_x, dy) => {
      window.scrollY += dy;
      formTop -= dy;
    });

    const focused = await handoffFocus("#ecrf-collected-at", {
      scrollIfOffscreen: true,
      scrollTarget: "#ecrf-form-new",
    });

    expect(focused).toBe(inputEl);
    expect(document.activeElement).toBe(inputEl);
    expect(scrollBySpy).toHaveBeenCalledWith(0, 32 - 96);
    expect(formTop).toBe(96);

    wrapper.remove();
    scrollBySpy.mockRestore();
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: origInnerHeight,
    });
    Object.defineProperty(window, "scrollY", {
      configurable: true,
      writable: true,
      value: origScrollY,
    });
  });
});
