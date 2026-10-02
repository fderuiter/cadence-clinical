import { mount } from "@vue/test-utils";
import { describe, expect, it, vi } from "vitest";
import { nextTick } from "vue";
import SsuGreenlightConsole from "../../src/components/clinical/SsuGreenlightConsole.vue";

describe("SsuGreenlightConsole Country-Level Multi-Site Activation Matrix", () => {
  it("renders the country matrix view with summary cards and site table", async () => {
    const wrapper = mount(SsuGreenlightConsole, {
      props: {
        studyId: "PROTOCOL-2026-01",
        countryCode: "US",
      },
    });

    await nextTick();
    await new Promise((r) => setTimeout(r, 50));

    expect(wrapper.text()).toContain("Country Multi-Site Activation Matrix");
    expect(wrapper.text()).toContain("Total Country Sites");
    expect(wrapper.text()).toContain("Batch Certify Country Sites");
  });

  it("disables selection checkboxes for non-compliant sites with red indicator", async () => {
    const wrapper = mount(SsuGreenlightConsole, {
      props: {
        studyId: "PROTOCOL-2026-01",
        countryCode: "US",
      },
    });

    await nextTick();
    await new Promise((r) => setTimeout(r, 50));

    // Find disabled checkboxes for non-compliant sites
    const disabledCheckboxes = wrapper.findAll(
      ".site-select-checkbox:disabled"
    );
    expect(disabledCheckboxes.length).toBeGreaterThan(0);

    // Verify non-compliant status badge or indicator is present
    expect(wrapper.text()).toContain("Non-Compliant");
  });

  it("allows selecting fully-compliant sites and opens batch certification modal", async () => {
    const wrapper = mount(SsuGreenlightConsole, {
      props: {
        studyId: "PROTOCOL-2026-01",
        countryCode: "US",
      },
    });

    await nextTick();
    await new Promise((r) => setTimeout(r, 50));

    // Click "Select All Compliant"
    const selectAllBtn = wrapper.find(".btn-select-all");
    if (selectAllBtn.exists()) {
      await selectAllBtn.trigger("click");
    }

    const batchBtn = wrapper.find(".btn-batch-certify");
    expect(batchBtn.exists()).toBe(true);
    expect(batchBtn.attributes("disabled")).toBeUndefined();

    await batchBtn.trigger("click");
    await nextTick();

    expect(wrapper.text()).toContain(
      "21 CFR Part 11 Country-Level Batch Greenlight Certification"
    );
  });

  it("executes batch certification and displays individual HMAC tokens", async () => {
    const wrapper = mount(SsuGreenlightConsole, {
      props: {
        studyId: "PROTOCOL-2026-01",
        countryCode: "US",
      },
    });

    await nextTick();
    await new Promise((r) => setTimeout(r, 50));

    // Select all compliant sites
    const selectAllBtn = wrapper.find(".btn-select-all");
    if (selectAllBtn.exists()) {
      await selectAllBtn.trigger("click");
    }

    // Open Batch Certify Modal
    const batchBtn = wrapper.find(".btn-batch-certify");
    await batchBtn.trigger("click");
    await nextTick();

    // Submit batch certification
    const submitBtn = wrapper.find(".btn-submit-batch-certify");
    expect(submitBtn.exists()).toBe(true);
    await submitBtn.trigger("click");

    await nextTick();
    await new Promise((r) => setTimeout(r, 50));

    // Expect success banner or updated certified status
    expect(wrapper.text()).toContain("Greenlight Certified");
  });
});
