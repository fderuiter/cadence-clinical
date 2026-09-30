import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CtmsView from "../src/views/CtmsView.vue";
import { apiClient, ApiError } from "../src/api/apiClient";
import { useToastStore } from "../src/stores/toasts";

/**
 * #4523: the Delegation of Authority log must show only what the DoA service returned. A failed load is an
 * error and an empty log is an empty state; no sample delegation records or PI name are invented.
 */
describe("CtmsView DoA log without fabricated delegation records (#4523)", () => {
  let pinia;
  let doaResponse;

  beforeEach(() => {
    // These CTMS tabs run only in a demo build; other builds show site activation alone (ctms_site_activation.spec.js).
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    doaResponse = () => Promise.resolve({ delegated_staff: [] });
    vi.spyOn(apiClient, "get").mockImplementation((url) => {
      if (url.includes("/doa/sites/")) return doaResponse();
      return Promise.resolve([]);
    });
    vi.spyOn(apiClient, "post").mockResolvedValue({});
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  async function openDelegationTab() {
    const wrapper = mount(CtmsView, { global: { plugins: [pinia] } });
    await flushPromises();
    wrapper.vm.activeTab = "delegation";
    await flushPromises();
    await wrapper.vm.$nextTick();
    return wrapper;
  }

  function expectNoFabricatedRecords(wrapper) {
    const text = wrapper.text();
    expect(text).not.toContain("kc-pi-001");
    expect(text).not.toContain("kc-crc-001");
    expect(text).not.toContain("Dr. Arthur Pendragon");
    expect(wrapper.vm.effectiveDelegatedStaff).toEqual([]);
  }

  it.each([500, 502, 504])(
    "shows an error and no delegation records when the DoA log returns HTTP %i",
    async (status) => {
      doaResponse = () =>
        Promise.reject(
          new ApiError("DoA service unavailable", status, "Error", {
            title: "DoA service unavailable",
          })
        );

      const wrapper = await openDelegationTab();

      expectNoFabricatedRecords(wrapper);
      const banner = wrapper.find('[data-testid="doa-load-error"]');
      expect(banner.exists()).toBe(true);
      expect(banner.text()).toContain(`HTTP ${status}`);
      expect(wrapper.text()).not.toContain("No delegation assignments logged");

      const toasts = useToastStore().toasts;
      expect(toasts.some((t) => t.tone === "error")).toBe(true);
      expect(toasts[0].title).toContain("Delegation of Authority");
    }
  );

  it("shows the empty state, not sample records, when the DoA log is empty", async () => {
    const wrapper = await openDelegationTab();

    expectNoFabricatedRecords(wrapper);
    expect(wrapper.text()).toContain("No delegation assignments logged");
    expect(wrapper.find('[data-testid="doa-load-error"]').exists()).toBe(false);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("renders the records and PI returned by the DoA service", async () => {
    doaResponse = () =>
      Promise.resolve({
        delegated_staff: [
          {
            record_id: "rec-live-1",
            staff_user_id: "live-staff-7",
            task_codes: ["CRF_DATA_ENTRY"],
            start_date: "2026-03-01",
            is_active: true,
            signed_off: false,
          },
        ],
        pi_name: "Dr. Live Investigator",
      });

    const wrapper = await openDelegationTab();

    expect(wrapper.text()).toContain("live-staff-7");
    expect(wrapper.text()).toContain("Dr. Live Investigator");
    expect(wrapper.vm.effectiveDelegatedStaff).toHaveLength(1);
  });
});
