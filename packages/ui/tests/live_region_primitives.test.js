// @vitest-environment jsdom
import path from "node:path";
import { ESLint } from "eslint";
import { createApp, effectScope, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import {
  ClinicalDataTable,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalLiveRegion,
  EmptyState,
  useStatusAnnouncer,
} from "../index.js";

describe("Centralized Live-Region Primitives & Vue AST Gates (WCAG 2.2 SC 4.1.3)", () => {
  const mountedApps = [];

  afterEach(() => {
    vi.useRealTimers();
    while (mountedApps.length > 0) {
      const { app, host } = mountedApps.pop();
      app.unmount();
      host.remove();
    }
  });

  function mountHost(setupFn) {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({ setup: setupFn });
    app.mount(host);
    mountedApps.push({ app, host });
    return host;
  }

  describe("useStatusAnnouncer composable", () => {
    it("debounces rapid search updates, re-announces consecutive identical messages across ticks, and cleans up timers on dispose", async () => {
      vi.useFakeTimers();
      const searchSource = ref("");
      const errorSource = ref("");
      const scope = effectScope();
      let announcer;

      scope.run(() => {
        announcer = useStatusAnnouncer({
          debounceMs: 180,
          statusSource: searchSource,
          errorSource,
        });
      });

      // Rapid filter keystrokes collapse into the final debounced polite announcement
      announcer.announcePolite("Showing 12 of 24 documents matching 'P'");
      vi.advanceTimersByTime(60);
      announcer.announcePolite("Showing 5 of 24 documents matching 'Pro'");
      vi.advanceTimersByTime(60);
      announcer.announcePolite("Showing 2 of 24 documents matching 'Protocol'");
      expect(announcer.politeMessage.value).toBe("");

      await vi.advanceTimersByTimeAsync(180);
      await nextTick();
      expect(announcer.politeMessage.value).toBe(
        "Showing 2 of 24 documents matching 'Protocol'"
      );

      // Identical consecutive polite announcement clears to "" then repopulates so AT speaks again
      announcer.announcePolite(
        "Showing 2 of 24 documents matching 'Protocol'",
        { immediate: true }
      );
      expect(announcer.politeMessage.value).toBe("");
      await nextTick();
      await vi.advanceTimersByTimeAsync(40);
      expect(announcer.politeMessage.value).toBe(
        "Showing 2 of 24 documents matching 'Protocol'"
      );

      // Immediate assertive channel & duplicate re-announcement
      errorSource.value = "Invalid Security PIN. Please try again.";
      await nextTick();
      expect(announcer.assertiveMessage.value).toBe(
        "Invalid Security PIN. Please try again."
      );

      announcer.announceAssertive("Invalid Security PIN. Please try again.");
      expect(announcer.assertiveMessage.value).toBe("");
      await nextTick();
      await vi.advanceTimersByTimeAsync(40);
      expect(announcer.assertiveMessage.value).toBe(
        "Invalid Security PIN. Please try again."
      );

      announcer.clearStatus();
      announcer.clearAssertive();
      expect(announcer.politeMessage.value).toBe("");
      expect(announcer.assertiveMessage.value).toBe("");

      // Pending timer cancelled on scope disposal
      announcer.announcePolite("Stale queued status");
      scope.stop();
      await vi.advanceTimersByTimeAsync(250);
      expect(announcer.politeMessage.value).toBe("");
    });
  });

  describe("ClinicalLiveRegion primitive", () => {
    it("keeps polite and assertive containers mounted before updates, places sibling pills/actions outside atomic text, and passes axe-core", async () => {
      const message = ref("");
      const politeness = ref("polite");
      const tone = ref("neutral");
      const active = ref(false);

      const host = mountHost(
        () => () =>
          h(
            ClinicalLiveRegion,
            {
              id: "docs-status-strip",
              variant: "strip",
              politeness: politeness.value,
              tone: tone.value,
              active: active.value,
              message: message.value,
            },
            {
              pills: () =>
                h("span", { class: "study-pill" }, "Study: CADENCE-101"),
              actions: () =>
                h(
                  "button",
                  { type: "button", id: "btn-reset-filters" },
                  "Reset All Filters"
                ),
            }
          )
      );

      await nextTick();
      const root = host.querySelector(".clinical-live-region");
      const statusNode = root.querySelector('[role="status"]');
      const alertNode = root.querySelector('[role="alert"]');

      // Pre-mounted in DOM even while inactive (ARIA22)
      expect(statusNode).not.toBeNull();
      expect(alertNode).not.toBeNull();
      expect(statusNode.getAttribute("aria-live")).toBe("polite");
      expect(alertNode.hasAttribute("aria-live")).toBe(false);
      expect(root.classList.contains("clinical-live-region--inactive")).toBe(
        true
      );
      expect(statusNode.classList.contains("sr-only")).toBe(true);
      expect(alertNode.classList.contains("sr-only")).toBe(true);
      expect(host.querySelector("#docs-status-strip")).toBeNull();

      // Activate filtered status strip
      active.value = true;
      message.value = "Showing 4 of 18 documents matching 'Protocol'";
      await nextTick();

      expect(root.querySelector('[role="status"]')).toBe(statusNode);
      expect(root.querySelector('[role="alert"]')).toBe(alertNode);
      expect(host.querySelector("#docs-status-strip")).toBe(statusNode);
      expect(root.classList.contains("clinical-live-region--inactive")).toBe(
        false
      );
      expect(statusNode.classList.contains("sr-only")).toBe(false);
      expect(root.classList.contains("tabular-nums")).toBe(true);
      expect(statusNode.textContent).toContain(
        "Showing 4 of 18 documents matching 'Protocol'"
      );

      // Sibling pills & Reset button sit OUTSIDE the atomic status and alert containers
      const resetBtn = root.querySelector("#btn-reset-filters");
      expect(resetBtn).not.toBeNull();
      expect(statusNode.contains(resetBtn)).toBe(false);
      expect(alertNode.contains(resetBtn)).toBe(false);
      expect(statusNode.contains(root.querySelector(".study-pill"))).toBe(
        false
      );

      await toBeAccessible(host);

      // Escalate dynamically to assertive error without unmounting either live node
      politeness.value = "assertive";
      tone.value = "error";
      message.value = "Lock precheck failed: 2 open queries block freeze.";
      await nextTick();

      expect(root.querySelector('[role="status"]')).toBe(statusNode);
      expect(root.querySelector('[role="alert"]')).toBe(alertNode);
      expect(host.querySelector("#docs-status-strip")).toBe(alertNode);
      expect(statusNode.classList.contains("sr-only")).toBe(true);
      expect(alertNode.classList.contains("sr-only")).toBe(false);
      expect(alertNode.textContent).toContain(
        "Lock precheck failed: 2 open queries block freeze."
      );
      await toBeAccessible(host);
    });
  });

  describe("ClinicalDataTable, EmptyState, ClinicalDropzone & ClinicalFieldLayout ARIA22/ARIA19 contracts", () => {
    it("sets aria-busy while loading and keeps .clinical-table-live-summary[role='status'] mounted across single-page, external, #pagination, and empty states", async () => {
      const loading = ref(true);
      const externalPagination = ref(false);
      const totalRecords = ref(null);
      const rows = ref([{ id: "DOC-1", title: "Protocol v2.0" }]);
      const useCustomSlot = ref(false);
      const columns = [
        { key: "id", label: "ID", sortable: true },
        { key: "title", label: "Title", sortable: true },
      ];

      const host = mountHost(
        () => () =>
          h(
            ClinicalDataTable,
            {
              title: "Clinical Documents",
              columns,
              data: rows.value,
              loading: loading.value,
              externalPagination: externalPagination.value,
              totalRecords: totalRecords.value,
              pageSize: 10,
            },
            useCustomSlot.value
              ? {
                  pagination: () =>
                    h("div", { class: "custom-pager" }, "Cursor page 1"),
                }
              : {}
          )
      );

      await nextTick();
      const wrapper = host.querySelector(".table-wrapper");
      const table = host.querySelector("table.clinical-table");
      const liveSummary = host.querySelector(
        '.clinical-table-live-summary[role="status"][aria-live="polite"]'
      );

      expect(liveSummary).not.toBeNull();
      expect(wrapper.getAttribute("aria-busy")).toBe("true");
      expect(table.getAttribute("aria-busy")).toBe("true");
      expect(liveSummary.textContent).toContain("Loading");

      // Single-page client state (totalPages === 1 hides footer controls, but liveSummary stays mounted!)
      loading.value = false;
      await nextTick();
      expect(wrapper.hasAttribute("aria-busy")).toBe(false);
      expect(table.hasAttribute("aria-busy")).toBe(false);
      expect(host.querySelector(".clinical-table-live-summary")).toBe(
        liveSummary
      );
      expect(liveSummary.textContent).toContain("Showing 1 record");

      // External pagination with totalRecords
      externalPagination.value = true;
      totalRecords.value = 1;
      await nextTick();
      expect(host.querySelector(".clinical-table-live-summary")).toBe(
        liveSummary
      );
      expect(liveSummary.textContent).toContain("Showing 1 of 1 records");

      // Zero-row empty state
      rows.value = [];
      totalRecords.value = 0;
      await nextTick();
      expect(host.querySelector(".clinical-table-live-summary")).toBe(
        liveSummary
      );
      expect(liveSummary.textContent).toContain("No clinical records found.");

      // Custom #pagination slot state
      rows.value = [{ id: "DOC-1", title: "Protocol v2.0" }];
      totalRecords.value = null;
      useCustomSlot.value = true;
      await nextTick();
      expect(host.querySelector(".clinical-table-live-summary")).toBe(
        liveSummary
      );
      expect(host.querySelector(".custom-pager")).not.toBeNull();
    });

    it("supports polite role='status' on filtered EmptyState while keeping role='region' for static placeholders", async () => {
      const staticHost = mountHost(
        () => () =>
          h(EmptyState, {
            title: "No studies configured",
            message: "Create a study to begin.",
          })
      );
      await nextTick();
      const staticEl = staticHost.querySelector(".empty-state-container");
      expect(staticEl.getAttribute("role")).toBe("region");
      expect(staticEl.getAttribute("aria-label")).toBe("Empty State");
      expect(staticEl.hasAttribute("aria-live")).toBe(false);

      const liveHost = mountHost(
        () => () =>
          h(EmptyState, {
            title: "No matching documents",
            message: "Try clearing active filters.",
            live: true,
          })
      );
      await nextTick();
      const liveEl = liveHost.querySelector(".empty-state-container");
      expect(liveEl.getAttribute("role")).toBe("status");
      expect(liveEl.getAttribute("aria-live")).toBe("polite");
      expect(liveEl.hasAttribute("aria-label")).toBe(false);
    });

    it("keeps .clinical-dropzone-selected[role='status'] mounted across file selection and removal", async () => {
      const selectedName = ref("");
      const host = mountHost(
        () => () =>
          h(
            ClinicalDropzone,
            {
              inputId: "protocol-dropzone",
              hasSelection: Boolean(selectedName.value),
              removable: true,
              buttonLabel: "Select Protocol File",
              onFileSelected: (file) => {
                selectedName.value = file.name;
              },
              onRemove: () => {
                selectedName.value = "";
              },
            },
            selectedName.value
              ? {
                  selected: () => h("span", `Selected: ${selectedName.value}`),
                }
              : {}
          )
      );

      await nextTick();
      const statusEl = host.querySelector(
        '.clinical-dropzone-selected[role="status"][aria-live="polite"]'
      );
      expect(statusEl).not.toBeNull();
      expect(statusEl.classList.contains("sr-only")).toBe(true);

      const observedTexts = [];
      const observer = new MutationObserver(() => {
        observedTexts.push(statusEl.textContent.trim());
      });
      observer.observe(statusEl, {
        childList: true,
        subtree: true,
        characterData: true,
      });

      const input = host.querySelector("#protocol-dropzone");
      const mockFile = new File(["pdf"], "CADENCE-101-Protocol.pdf", {
        type: "application/pdf",
      });
      Object.defineProperty(input, "files", {
        value: [mockFile],
        configurable: true,
      });
      input.dispatchEvent(new Event("change"));
      await nextTick();

      // Same DOM element persists across selection
      expect(host.querySelector(".clinical-dropzone-selected")).toBe(statusEl);
      expect(statusEl.classList.contains("sr-only")).toBe(false);
      expect(statusEl.textContent).toContain(
        "Selected: CADENCE-101-Protocol.pdf"
      );

      // Remove file and verify same DOM element announces removal politely
      const removeBtn = host.querySelector(".clinical-dropzone-remove-btn");
      removeBtn.click();
      await nextTick();

      expect(host.querySelector(".clinical-dropzone-selected")).toBe(statusEl);
      expect(statusEl.textContent).toContain("Selected file removed");
      expect(observedTexts.length).toBeGreaterThanOrEqual(2);
      observer.disconnect();
    });

    it("renders ClinicalFieldLayout validation errors inside a persistent role='alert' node without aria-live='polite'", async () => {
      const errorMsg = ref("");
      const host = mountHost(
        () => () =>
          h(
            ClinicalFieldLayout,
            {
              id: "VS.SYSBP",
              label: "Systolic Blood Pressure",
              error: errorMsg.value,
            },
            {
              default: () => h("input", { id: "VS.SYSBP", type: "number" }),
            }
          )
      );

      await nextTick();
      const errEl = host.querySelector('[id="validation-error-VS.SYSBP"]');
      expect(errEl).not.toBeNull();
      expect(errEl.getAttribute("role")).toBe("alert");
      expect(errEl.hasAttribute("aria-live")).toBe(false);
      expect(errEl.classList.contains("sr-only")).toBe(true);

      errorMsg.value = "Value exceeds physiological ceiling (300 mmHg).";
      await nextTick();
      expect(host.querySelector('[id="validation-error-VS.SYSBP"]')).toBe(
        errEl
      );
      expect(errEl.classList.contains("validation-error-msg")).toBe(true);
      expect(errEl.textContent).toContain("300 mmHg");
    });
  });

  describe("Custom ESLint Vue AST Live-Region Gates", () => {
    const repoRoot = path.resolve(import.meta.dirname, "../../..");
    const eslint = new ESLint({ cwd: repoRoot });

    it("flags role='alert' downgraded with aria-live='polite' while permitting synchronized toast ternaries", async () => {
      const [downgradedStatic] = await eslint.lintText(
        '<template><div role="alert" aria-live="polite">Locked</div></template>\n',
        { filePath: path.join(repoRoot, "apps/web/src/views/ProbeAlert.vue") }
      );
      expect(
        downgradedStatic.messages.some(
          (m) => m.ruleId === "custom-vue/no-conflicting-live-region"
        )
      ).toBe(true);

      const [downgradedDynamic] = await eslint.lintText(
        `<template><div :role="hasErr ? 'alert' : 'status'" aria-live="polite">Outcome</div></template>\n<script setup>\nconst hasErr = true;\n</script>\n`,
        { filePath: path.join(repoRoot, "apps/web/src/views/ProbeOutcome.vue") }
      );
      expect(
        downgradedDynamic.messages.some(
          (m) => m.ruleId === "custom-vue/no-conflicting-live-region"
        )
      ).toBe(true);

      const [syncedTernary] = await eslint.lintText(
        `<template><div :role="isErr ? 'alert' : 'status'" :aria-live="isErr ? 'assertive' : 'polite'">Toast</div></template>\n<script setup>\nconst isErr = false;\n</script>\n`,
        { filePath: path.join(repoRoot, "apps/web/src/views/ProbeToast.vue") }
      );
      expect(
        syncedTernary.messages.some(
          (m) => m.ruleId === "custom-vue/no-conflicting-live-region"
        )
      ).toBe(false);
    });

    it("flags role='status' on error banners and new v-if status mounts in unallowlisted or ceiling-exceeding files", async () => {
      const [statusErr] = await eslint.lintText(
        '<template><div id="val-reason-error" class="error-banner" role="status">Reason required</div></template>\n',
        { filePath: path.join(repoRoot, "apps/web/src/views/ProbeErr.vue") }
      );
      expect(
        statusErr.messages.some(
          (m) => m.ruleId === "custom-vue/no-status-role-on-errors"
        )
      ).toBe(true);

      const [newVIfStatus] = await eslint.lintText(
        `<template><p v-if="ready" role="status" aria-live="polite">24 rows</p></template>\n<script setup>\nconst ready = true;\n</script>\n`,
        {
          filePath: path.join(
            repoRoot,
            "apps/web/src/views/DocumentsHubView.vue"
          ),
        }
      );
      expect(
        newVIfStatus.messages.some(
          (m) => m.ruleId === "custom-vue/no-conditional-status-mount"
        )
      ).toBe(true);

      // Exceeding an allowlisted file's ratcheted count (PortalHeader.vue has maxAllowed = 1) fails
      const [exceededAllowlist] = await eslint.lintText(
        `<template><div><span v-if="a" role="status">One</span><span v-if="b" role="status">Two</span></div></template>\n<script setup>\nconst a = true;\nconst b = true;\n</script>\n`,
        {
          filePath: path.join(
            repoRoot,
            "apps/subject-portal/src/components/PortalHeader.vue"
          ),
        }
      );
      expect(
        exceededAllowlist.messages.some(
          (m) => m.ruleId === "custom-vue/no-conditional-status-mount"
        )
      ).toBe(true);
    });
  });
});
