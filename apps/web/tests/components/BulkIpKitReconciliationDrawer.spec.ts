import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { executionApi } from "../../src/api/executionApi";
import BulkIpKitReconciliationDrawer from "../../src/components/clinical/BulkIpKitReconciliationDrawer.vue";
import apiClient from "../../src/services/api";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";

describe("BulkIpKitReconciliationDrawer.vue", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Pharmacist"], {
      username: "pharmacist.site101",
      displayName: "Dr. Alex Vance, RPh",
    });

    vi.spyOn(executionApi, "getSiteIpKitInventory").mockResolvedValue([
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
      },
    ]);

    vi.spyOn(apiClient, "post").mockResolvedValue({
      sig_token: "sig-token-rtsm-1",
    });

    vi.spyOn(executionApi, "reconcileIpKitBatch").mockResolvedValue({
      certificate: {
        certificate_id: "cert-999",
        study_id: "STUDY-101",
        site_id: "SITE-01",
        processed_at: "2026-10-12T12:00:00Z",
        processed_by: "pharmacist.site101",
        total_kits_reconciled: 1,
        payload_hash: "abcd1234efgh5678ijkl", // pragma: allowlist secret
        items: [{ kit_number: "KIT-1001" }],
      },
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
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
        plugins: [pinia],
        stubs: {
          Teleport: true,
        },
      },
    });

  it("renders drawer header, badges, and context bar when open", async () => {
    const wrapper = mountDrawer();
    await flushPromises();

    expect(wrapper.text()).toContain(
      "Bulk IP Kit Reconciliation & Batch Disposition Drawer"
    );
    expect(wrapper.text()).toContain("21 CFR Part 11");
    expect(wrapper.text()).toContain("RTSM Supply Chain");
    expect(wrapper.text()).toContain("STUDY-101");
    expect(wrapper.text()).toContain("SITE-01");
    expect(wrapper.text()).toContain("Dr. Alex Vance, RPh");
  });

  it("adds scanned barcode to reconciliation buffer", async () => {
    const wrapper = mountDrawer();
    await flushPromises();

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
    await flushPromises();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Duplicate barcode scan");
    expect(wrapper.text()).toContain("Double counting prevented");
  });

  it("detects lot mismatch and flags warning badge with QUARANTINED disposition", async () => {
    const wrapper = mountDrawer();
    await flushPromises();

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
    await flushPromises();

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
    expect((selects[0].element as HTMLSelectElement).value).toBe(
      "RETURNED_TO_DEPOT"
    );
    expect((selects[1].element as HTMLSelectElement).value).toBe(
      "RETURNED_TO_DEPOT"
    );
  });

  it("enforces Part 11 step-up e-signature token verification and read-only authenticated signer identity before batch submission", async () => {
    const wrapper = mountDrawer();
    await flushPromises();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    const userIdInput = wrapper.find("#pharmacist-signature-id");
    const passwordInput = wrapper.find("#reconcile-sig-password");
    const reasonInput = wrapper.find("#reason-for-change-input");
    const submitBtn = wrapper.find(".btn-submit-batch");

    expect(userIdInput.attributes("readonly")).toBeDefined();
    expect((userIdInput.element as HTMLInputElement).value).toBe(
      "pharmacist.site101"
    );
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(true);

    await passwordInput.setValue("Password123!");
    await reasonInput.setValue("End of trial site return reconciliation");
    await wrapper.vm.$nextTick();

    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(false);

    await submitBtn.trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        username: "pharmacist.site101",
        action: "/api/v1/execution/rtsm/kits/reconcile-batch",
        semantic_action: "execution.ip_kit.reconcile_batch",
      })
    );
    expect(executionApi.reconcileIpKitBatch).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "STUDY-101",
        site_id: "SITE-01",
        reason_for_change: "End of trial site return reconciliation",
      }),
      "sig-token-rtsm-1"
    );
  });

  it("renders digital certificate modal upon successful batch commitment", async () => {
    const wrapper = mountDrawer();
    await flushPromises();

    const input = wrapper.find("#barcode-scanner-input");
    await input.setValue("KIT-1001");
    await input.trigger("keydown.enter");
    await wrapper.vm.$nextTick();

    await wrapper.find("#reconcile-sig-password").setValue("Password123!");
    await wrapper
      .find("#reason-for-change-input")
      .setValue("Routine site return");
    await wrapper.vm.$nextTick();

    await wrapper.find(".btn-submit-batch").trigger("click");
    await flushPromises();

    expect(wrapper.text()).toContain(
      "Digital IP Kit Reconciliation Certificate"
    );
    expect(wrapper.text()).toContain("cert-999");
    expect(wrapper.text()).toContain("pharmacist.site101");
    expect(wrapper.text()).toContain("abcd1234efgh5678ijkl");
  });
});
