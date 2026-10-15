import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import BulkIpKitReconciliationDrawer from "../../src/components/clinical/BulkIpKitReconciliationDrawer.vue";

describe("BulkIpKitReconciliationDrawer.vue", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Mock global fetch for inventory and batch reconciliation endpoints
    global.fetch = vi.fn((url: string | URL | Request) => {
      const urlStr = url.toString();
      if (urlStr.includes("/api/v1/execution/rtsm/kits/inventory/")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve([
              {
                id: "item-1001",
                study_id: "STUDY-101",
                site_id: "SITE-01",
                kit_number: "KIT-1001",
                lot_number: "LOT-A100",
                expiration_date: "2027-12-31T00:00:00Z",
                status: "RETURNED_BY_PATIENT",
                residual_volume_ml: 100,
                residual_volume_percentage: 100,
                reconciliation_count: 0,
              },
              {
                id: "item-1002",
                study_id: "STUDY-101",
                site_id: "SITE-01",
                kit_number: "KIT-1002",
                lot_number: "LOT-A100",
                expiration_date: "2027-12-31T00:00:00Z",
                status: "RETURNED_BY_PATIENT",
                residual_volume_ml: 50,
                residual_volume_percentage: 50,
                reconciliation_count: 0,
              },
            ]),
        } as Response);
      }

      if (urlStr.includes("/api/v1/execution/rtsm/kits/reconcile-batch")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              batch_id: "batch-789",
              study_id: "STUDY-101",
              site_id: "SITE-01",
              reconciled_at: "2026-10-12T12:00:00Z",
              processed_by: "pharm_user",
              total_kits_reconciled: 1,
              quarantined_count: 0,
              returned_to_site_count: 1,
              returned_to_depot_count: 0,
              certificate: {
                certificate_id: "cert-999",
                batch_number: "batch-789",
                study_id: "STUDY-101",
                site_id: "SITE-01",
                issued_at: "2026-10-12T12:00:00Z",
                issued_by: "pharm_user",
                digital_signature: "abcd1234efgh5678ijkl",
                verified_by_system: true,
                kit_numbers: ["KIT-1001"],
              },
            }),
        } as Response);
      }

      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({}),
      } as Response);
    });
  });

  const mountDrawer = (props = {}) =>
    mount(BulkIpKitReconciliationDrawer, {
      props: {
        isOpen: true,
        studyId: "STUDY-101",
        siteId: "SITE-01",
        pharmacistName: "Dr. Alex Vance, RPh",
        ...props,
      },
      global: {
        stubs: {
          Teleport: true,
        },
      },
    });

  it("renders drawer header, badges, and context bar when open", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Bulk IP Kit Reconciliation & Batch Disposition Drawer");
    expect(wrapper.text()).toContain("21 CFR Part 11");
    expect(wrapper.text()).toContain("RTSM Supply Chain");
    expect(wrapper.text()).toContain("STUDY-101");
    expect(wrapper.text()).toContain("SITE-01");
    expect(wrapper.text()).toContain("Dr. Alex Vance, RPh");
  });

  it("adds scanned barcode to reconciliation buffer", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("KIT-1001");
    expect(wrapper.text()).toContain("LOT-A100");
    expect(wrapper.text()).toContain("Total Scanned Kits");
  });

  it("prevents duplicate scan entries and displays warning banner", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    // Scan second time
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Duplicate barcode scan");
    expect(wrapper.text()).toContain("Double counting prevented");
  });

  it("detects lot mismatch and flags warning badge with QUARANTINED disposition", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    // KIT-1001 expected lot in inventory is LOT-A100, scan with LOT-MISMATCH-99
    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001|LOT-MISMATCH-99");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("KIT-1001");
    expect(wrapper.text()).toContain("LOT MISMATCH");
    expect(wrapper.text()).toContain("LOT-MISMATCH-99");
    expect(wrapper.text()).toContain("LOT-A100");
  });

  it("allows bulk disposition assignment across scanned items", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    await input.setValue("KIT-1002");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    const bulkDepotBtn = wrapper.find(".bulk-actions button:nth-of-type(3)");
    expect(bulkDepotBtn.exists()).toBe(true);

    await bulkDepotBtn.trigger("click");
    await wrapper.vm.$nextTick();

    const selects = wrapper.findAll(".disposition-select");
    expect(selects.length).toBe(2);
    expect((selects[0].element as HTMLSelectElement).value).toBe("RETURNED_TO_DEPOT");
    expect((selects[1].element as HTMLSelectElement).value).toBe("RETURNED_TO_DEPOT");
  });

  it("enforces Part 11 e-signature requirements before batch submission", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    const userIdInput = wrapper.find("#pharmacist-signature-id");
    const reasonInput = wrapper.find("#reason-for-change-input");
    const submitBtn = wrapper.find(".btn-submit-batch");

    // Clear user ID to verify button is disabled when empty
    await userIdInput.setValue("");
    await wrapper.vm.$nextTick();
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(true);

    await userIdInput.setValue("pharm_user");
    await reasonInput.setValue("End of trial site return reconciliation");
    await wrapper.vm.$nextTick();

    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(false);

    await submitBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(global.fetch).toHaveBeenCalledWith(
      "/api/v1/execution/rtsm/kits/reconcile-batch",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "X-Change-Reason": "End of trial site return reconciliation",
        }),
      })
    );
  });

  it("renders digital certificate modal upon successful batch commitment", async () => {
    const wrapper = mountDrawer();
    await wrapper.vm.$nextTick();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    await wrapper.find("#pharmacist-signature-id").setValue("pharm_user");
    await wrapper.find("#reason-for-change-input").setValue("Routine site return");
    await wrapper.vm.$nextTick();

    await wrapper.find(".btn-submit-batch").trigger("click");
    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(wrapper.text()).toContain("Digital IP Kit Reconciliation Certificate");
    expect(wrapper.text()).toContain("cert-999");
    expect(wrapper.text()).toContain("abcd1234efgh5678ijkl");
  });
});
