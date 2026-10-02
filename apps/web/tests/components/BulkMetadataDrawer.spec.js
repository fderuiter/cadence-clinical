import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import BulkMetadataDrawer from "../../src/components/etmf/BulkMetadataDrawer.vue";
import { useFileshareStore } from "../../src/stores/fileshare.js";

describe("BulkMetadataDrawer.vue", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  const sampleFiles = [
    {
      id: "DOC-101",
      filename: "doc1.pdf",
      zone_code: "1",
      section_code: "01.01",
      folder_code: "01.01.01",
      artifact_type: "Protocol",
      site_id: "SITE-101",
      is_on_hold: false,
    },
    {
      id: "DOC-102",
      filename: "doc2.pdf",
      zone_code: "1",
      section_code: "01.01",
      folder_code: "01.01.01",
      artifact_type: "Protocol",
      site_id: "SITE-101",
      is_on_hold: false,
    },
    {
      id: "DOC-103",
      filename: "doc3_hold.pdf",
      zone_code: "1",
      section_code: "01.01",
      folder_code: "01.01.01",
      artifact_type: "Protocol",
      site_id: "SITE-101",
      is_on_hold: true,
      status: "ON_HOLD",
    },
  ];

  it("renders overlay and preview summary when isOpen is true", () => {
    const wrapper = mount(BulkMetadataDrawer, {
      props: {
        isOpen: true,
        selectedFiles: sampleFiles,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find(".drawer-panel").exists()).toBe(true);
    expect(wrapper.text()).toContain("Bulk Edit Document Metadata");
    expect(wrapper.text()).toContain("Total Selected");
    expect(wrapper.text()).toContain("3"); // Total selected
    expect(wrapper.text()).toContain("2"); // Editable files
    expect(wrapper.text()).toContain("Retention Hold (Excluded)");
    expect(wrapper.text()).toContain("1"); // Hold count
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(BulkMetadataDrawer, {
      props: {
        isOpen: false,
        selectedFiles: sampleFiles,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find(".drawer-panel").exists()).toBe(false);
  });

  it("enforces validation error when no fields are checked or change reason is too short", async () => {
    const wrapper = mount(BulkMetadataDrawer, {
      props: {
        isOpen: true,
        selectedFiles: sampleFiles,
      },
      global: {
        plugins: [pinia],
      },
    });

    // Initially no fields checked
    expect(wrapper.text()).toContain(
      "Please select at least one metadata field to update."
    );
    expect(
      wrapper.find("button.btn-save").attributes("disabled")
    ).toBeDefined();

    // Check Zone toggle
    const zoneCheckbox = wrapper.find('input[aria-label="Update Zone"]');
    await zoneCheckbox.setValue(true);

    // Now validation should complain about reason for change
    expect(wrapper.text()).toContain(
      "Reason for change must be at least 10 characters long"
    );
    expect(
      wrapper.find("button.btn-save").attributes("disabled")
    ).toBeDefined();

    // Enter valid change reason
    const textarea = wrapper.find("#bulk-change-reason");
    await textarea.setValue(
      "Updating DIA zone metadata across active documents"
    );

    // Button should now be enabled
    expect(
      wrapper.find("button.btn-save").attributes("disabled")
    ).toBeUndefined();
  });

  it("calls fileshareStore.bulkUpdateMetadata and emits submitted when form is submitted", async () => {
    const store = useFileshareStore();
    store.files = [...sampleFiles];
    const spy = vi
      .spyOn(store, "bulkUpdateMetadata")
      .mockResolvedValue({ success: true });

    const wrapper = mount(BulkMetadataDrawer, {
      props: {
        isOpen: true,
        selectedFiles: sampleFiles,
      },
      global: {
        plugins: [pinia],
      },
    });

    // Toggle Zone and Site fields
    await wrapper.find('input[aria-label="Update Zone"]').setValue(true);
    await wrapper.find('input[aria-label="Update Site"]').setValue(true);

    // Set change reason
    await wrapper
      .find("#bulk-change-reason")
      .setValue("Bulk edit zone 2 and site 101 for compliance");

    // Submit form
    await wrapper.find("form").trigger("submit.prevent");
    await flushPromises();

    expect(spy).toHaveBeenCalledWith(
      expect.objectContaining({
        documentIds: ["DOC-101", "DOC-102"], // excludes hold file
        reasonForChange: "Bulk edit zone 2 and site 101 for compliance",
        zone: 1,
        siteId: "SITE-101",
      })
    );

    expect(wrapper.emitted("submitted")).toBeTruthy();
  });
});
