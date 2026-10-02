import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AuditorExportModal from "@/components/auditor/AuditorExportModal.vue";
import { useAuditorStore } from "@/stores/auditor";

describe("AuditorExportModal component", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "true");
  });

  it("renders export format options and modal buttons", () => {
    const wrapper = mount(AuditorExportModal);
    expect(wrapper.text()).toContain("Export Inspection Log");
    expect(wrapper.find("input[value='CSV']").exists()).toBe(true);
    expect(wrapper.find("input[value='PDF']").exists()).toBe(true);
    expect(wrapper.find("input[value='JSON']").exists()).toBe(true);
  });

  it("shows stage progress during export and supports cancellation", async () => {
    const auditorStore = useAuditorStore();
    vi.spyOn(auditorStore, "fetchAuditLogs").mockImplementation(async () => {
      await new Promise((resolve) => setTimeout(resolve, 200));
    });
    vi.spyOn(auditorStore, "exportAuditTrail").mockImplementation(
      async () => {}
    );

    const wrapper = mount(AuditorExportModal);
    const downloadBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Download Export"));

    downloadBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".export-progress-panel").exists()).toBe(true);
    expect(wrapper.find(".btn-cancel-auditor-export").exists()).toBe(true);

    await wrapper.find(".btn-cancel-auditor-export").trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".export-progress-panel").exists()).toBe(false);
  });
});
