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
});
