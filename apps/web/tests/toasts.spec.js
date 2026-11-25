import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { nextTick } from "vue";
import { createPinia, setActivePinia } from "pinia";
import { mount } from "@vue/test-utils";
import { useToastStore } from "../src/stores/toasts";
import { describeApiError, ensureOk } from "../src/utils/apiErrors";
import ToastHost from "../src/components/ToastHost.vue";

describe("API error reporting (#4523)", () => {
  beforeEach(() => setActivePinia(createPinia()));

  it.each([500, 502, 504])(
    "describes an HTTP %i problem-details error",
    (status) => {
      const err = Object.assign(new Error("fallback"), {
        status,
        data: { title: "Bad Gateway", detail: "execution is unreachable" },
      });
      expect(describeApiError(err)).toBe(
        `execution is unreachable (HTTP ${status})`
      );
    }
  );

  it("describes a network error without a status", () => {
    expect(describeApiError(new TypeError("Failed to fetch"))).toBe(
      "Failed to fetch"
    );
  });

  it("ensureOk throws with the status and body of a non-2xx response", async () => {
    const response = new Response(JSON.stringify({ detail: "boom" }), {
      status: 502,
    });
    await expect(ensureOk(response)).rejects.toMatchObject({
      status: 502,
      message: "boom",
    });
  });

  it("apiError shows a persistent alert that ToastHost renders and can dismiss", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const toasts = useToastStore();
    toasts.apiError(
      "Subject screening failed",
      Object.assign(new Error("x"), { status: 503 })
    );

    const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
    expect(wrapper.text()).toContain("Subject screening failed");
    expect(wrapper.text()).toContain("x (HTTP 503)");

    await wrapper.find(".toast-dismiss").trigger("click");
    expect(toasts.toasts).toHaveLength(0);
  });

  describe("CTMS actions and audit approvals toast notifications", () => {
    it("renders a non-blocking green success toast for audit document signing and approval", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      toasts.push({
        tone: "success",
        title: "Document Signed",
        message: "Document successfully signed and approved.",
      });

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const toastEl = wrapper.find(".toast-success");
      expect(toastEl.exists()).toBe(true);
      expect(toastEl.text()).toContain("Document Signed");
      expect(toastEl.text()).toContain(
        "Document successfully signed and approved."
      );

      await toastEl.find(".toast-dismiss").trigger("click");
      expect(toasts.toasts).toHaveLength(0);
    });

    it("renders a non-blocking green success toast for eTMF document upload ingestion", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      toasts.push({
        tone: "success",
        title: "Document Ingested",
        message: "Document successfully uploaded & ingested into eTMF.",
      });

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const toastEl = wrapper.find(".toast-success");
      expect(toastEl.exists()).toBe(true);
      expect(toastEl.text()).toContain("Document Ingested");
      expect(toastEl.text()).toContain(
        "Document successfully uploaded & ingested into eTMF."
      );
    });

    it("renders a non-blocking warning toast for CTMS visit completion when no visits are scheduled", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      toasts.push({
        tone: "warning",
        title: "Monitoring Visits",
        message: "No scheduled visits to complete!",
      });

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const toastEl = wrapper.find(".toast-warning");
      expect(toastEl.exists()).toBe(true);
      expect(toastEl.text()).toContain("Monitoring Visits");
      expect(toastEl.text()).toContain("No scheduled visits to complete!");
    });

    it("renders an error toast for CTMS monitoring visit and budget action failures via apiError", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      toasts.apiError(
        "Failed to complete monitoring visit",
        new Error("Visit ID not found")
      );

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const toastEl = wrapper.find(".toast-error");
      expect(toastEl.exists()).toBe(true);
      expect(toastEl.text()).toContain("Failed to complete monitoring visit");
      expect(toastEl.text()).toContain("Visit ID not found");
    });

    it("safely handles malformed API error responses in CTMS actions", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      toasts.apiError("Failed to approve budget adjustment", {
        data: { detail: "Unauthorized signer" },
        status: 403,
      });

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const toastEl = wrapper.find(".toast-error");
      expect(toastEl.exists()).toBe(true);
      expect(toastEl.text()).toContain("Failed to approve budget adjustment");
      expect(toastEl.text()).toContain("Unauthorized signer (HTTP 403)");
    });

    it("stacks notifications concurrently when multiple actions complete", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();

      // Trigger audit approval toast and CTMS action toast concurrently
      toasts.push({
        tone: "success",
        title: "Document Signed",
        message: "Document successfully signed and approved.",
      });
      toasts.push({
        tone: "warning",
        title: "Monitoring Visits",
        message: "No scheduled visits to complete!",
      });
      toasts.apiError(
        "Failed to execute contract amendment",
        new Error("Signature required")
      );

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      const allToasts = wrapper.findAll(".toast");
      expect(allToasts).toHaveLength(3);
      expect(wrapper.find(".toast-success").exists()).toBe(true);
      expect(wrapper.find(".toast-warning").exists()).toBe(true);
      expect(wrapper.find(".toast-error").exists()).toBe(true);

      // Dismiss the middle one; the remaining two stay stacked
      await allToasts[1].find(".toast-dismiss").trigger("click");
      expect(toasts.toasts).toHaveLength(2);
      expect(wrapper.findAll(".toast")).toHaveLength(2);
    });

    it("renders an optional deep-link action button that navigates via $router and dismisses the toast", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();
      const pushedRoutes = [];

      toasts.push({
        tone: "success",
        title: "Support ticket TKT-2026-0009 submitted",
        message: "Reference ID: TKT-2026-0009 (DATA_CAPTURE_ECRF)",
        action: {
          label: "Open TKT-2026-0009",
          to: "/tickets?ref=TKT-2026-0009",
        },
      });

      const wrapper = mount(ToastHost, {
        global: {
          plugins: [pinia],
          mocks: {
            $router: {
              push: (target) => {
                pushedRoutes.push(target);
                return Promise.resolve();
              },
            },
          },
        },
      });

      const actionBtn = wrapper.find(".toast-action");
      expect(actionBtn.exists()).toBe(true);
      expect(actionBtn.text()).toContain("Open TKT-2026-0009");

      await actionBtn.trigger("click");
      expect(pushedRoutes).toEqual(["/tickets?ref=TKT-2026-0009"]);
      expect(toasts.toasts).toHaveLength(0);
    });

    it("persists all error toasts regardless of timeoutMs and splits role=alert (assertive) from role=status (polite)", async () => {
      vi.useFakeTimers();
      try {
        const pinia = createPinia();
        setActivePinia(pinia);
        const toasts = useToastStore();

        toasts.push({
          tone: "error",
          title: "Submission rejected",
          message: "Validation constraint failed.",
          timeoutMs: 3500,
        });
        toasts.push({
          tone: "info",
          title: "Draft saved",
          message: "Saved locally.",
          timeoutMs: 2000,
        });

        expect(toasts.toasts[0].timeoutMs).toBe(0);
        expect(toasts.toasts[1].timeoutMs).toBe(2000);

        const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
        const errorToast = wrapper.find(".toast-error");
        const infoToast = wrapper.find(".toast-info");

        expect(errorToast.attributes("role")).toBe("alert");
        expect(errorToast.attributes("aria-live")).toBe("assertive");
        expect(infoToast.attributes("role")).toBe("status");
        expect(infoToast.attributes("aria-live")).toBe("polite");

        vi.advanceTimersByTime(15000);
        await nextTick();

        expect(wrapper.find(".toast-info").exists()).toBe(false);
        expect(wrapper.find(".toast-error").exists()).toBe(true);
        wrapper.unmount();
      } finally {
        vi.useRealTimers();
      }
    });

    it("pauses timed toast auto-dismiss on hover and focus and resumes remaining duration on leave/blur", async () => {
      vi.useFakeTimers();
      try {
        const pinia = createPinia();
        setActivePinia(pinia);
        const toasts = useToastStore();

        toasts.push({
          tone: "success",
          title: "Saved",
          message: "Record updated.",
          timeoutMs: 3000,
        });

        const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
        const toastEl = wrapper.find(".toast-success");
        expect(toastEl.exists()).toBe(true);

        // Elapsed 1000ms -> 2000ms remaining
        vi.advanceTimersByTime(1000);
        await toastEl.trigger("mouseenter");

        // Paused while hovered even past 10s
        vi.advanceTimersByTime(10000);
        await nextTick();
        expect(wrapper.find(".toast-success").exists()).toBe(true);

        await toastEl.trigger("mouseleave");
        vi.advanceTimersByTime(1000);
        await toastEl.trigger("focusin");

        // Paused while focused
        vi.advanceTimersByTime(5000);
        await nextTick();
        expect(wrapper.find(".toast-success").exists()).toBe(true);

        // Resume remaining ~1000ms after focus leaves the toast card
        await toastEl.trigger("focusout", { relatedTarget: document.body });
        vi.advanceTimersByTime(900);
        await nextTick();
        expect(wrapper.find(".toast-success").exists()).toBe(true);

        vi.advanceTimersByTime(200);
        await nextTick();
        expect(wrapper.find(".toast-success").exists()).toBe(false);
        wrapper.unmount();
      } finally {
        vi.useRealTimers();
      }
    });

    it("omits empty .toast-title elements via v-if and provides descriptive titles in all eTMF and SSU toast calls", () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();

      toasts.push({
        tone: "info",
        message: "Untitled fallback message",
      });

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      expect(wrapper.find(".toast-title").exists()).toBe(false);
      expect(wrapper.find(".toast-message").text()).toBe(
        "Untitled fallback message"
      );
      wrapper.unmount();

      const auditedFiles = [
        "../src/components/etmf/BatchUploadDrawer.vue",
        "../src/components/etmf/CraQcQueue.vue",
        "../src/components/etmf/DocumentGrid.vue",
        "../src/components/etmf/TmfBinderTree.vue",
        "../src/components/clinical/SsuGreenlightConsole.vue",
      ];

      let totalPushCalls = 0;
      for (const relPath of auditedFiles) {
        const source = readFileSync(
          resolve(import.meta.dirname, relPath),
          "utf8"
        );
        const matches = [
          ...source.matchAll(
            /(?:toastStore|toasts|useToastStore\(\)|getToastStore\(\)\?)\.(?:push|addToast)\(\s*\{([\s\S]*?)\}\s*\)/g
          ),
        ];
        expect(matches.length).toBeGreaterThan(0);
        for (const match of matches) {
          totalPushCalls += 1;
          expect(match[1]).toMatch(/title:\s*["'`][^"'`]+["'`]/);
        }
      }
      expect(totalPushCalls).toBe(17);
    });

    it("anchors web ToastHost top-right with vector dismiss icons and batch-clears >= 4 persistent alerts", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const toasts = useToastStore();

      for (let i = 1; i <= 6; i += 1) {
        toasts.apiError(
          `Rule signature error ${i}`,
          new Error(`Constraint ${i}`)
        );
      }

      const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
      expect(wrapper.find(".toast-host").attributes("data-position")).toBe(
        "top-right"
      );
      expect(wrapper.find(".toast-host").classes()).toContain(
        "toast-host--top-right"
      );

      const firstDismiss = wrapper.find(".toast-dismiss");
      expect(firstDismiss.find("svg.clinical-icon").exists()).toBe(true);
      expect(firstDismiss.text()).not.toContain("\u00d7");

      const clearAllBtn = wrapper.find(".toast-clear-all");
      expect(clearAllBtn.exists()).toBe(true);
      expect(clearAllBtn.text()).toContain("Clear all");
      expect(clearAllBtn.text()).toContain("(6)");

      await clearAllBtn.trigger("click");
      expect(toasts.toasts).toHaveLength(0);
      expect(wrapper.findAll(".toast")).toHaveLength(0);
      wrapper.unmount();
    });
  });
});
