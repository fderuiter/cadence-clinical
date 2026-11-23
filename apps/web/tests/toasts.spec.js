import { describe, it, expect, beforeEach } from "vitest";
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
  });
});
