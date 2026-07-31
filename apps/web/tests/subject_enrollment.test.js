import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import { useClinicalStore } from "../src/stores/clinical.js";
import { useAuthStore } from "../src/stores/auth.js";
import { executionService } from "../src/api/execution.js";

// Mock the execution service
vi.mock("../src/api/execution.js", () => {
  return {
    executionService: {
      createSubject: vi.fn().mockResolvedValue({ status: "success" }),
      submitForm: vi.fn().mockResolvedValue({ status: "success" }),
      getQueries: vi.fn().mockResolvedValue([]),
    },
  };
});

describe("CRC / Site Coordinator Subject Enrollment and EDC Session SPA tests", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    if (typeof window !== "undefined") {
      window.localStorage.clear();
    }
    vi.resetAllMocks();
  });

  it("renders active subject dropdown and defaults to SUBJ-001", () => {
    const clinicalStore = useClinicalStore();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(EcrfView);

    // Assert that active subject dropdown rendered
    const select = wrapper.find("select#active-subject-select");
    expect(select.exists()).toBe(true);

    // Check we have the options for default subjects
    const options = select.findAll("option");
    expect(options.length).toBe(3);
    expect(options[0].text()).toBe("SUBJ-001");
    expect(options[1].text()).toBe("SUBJ-002");
    expect(options[2].text()).toBe("SUBJ-003");

    // Check currently active display
    const display = wrapper.find(".active-subj-display");
    expect(display.exists()).toBe(true);
    expect(display.text()).toBe("SUBJ-001");
    expect(clinicalStore.activeSubjectId).toBe("SUBJ-001");
  });

  it("switches active subject and loads respective form values and queries", async () => {
    const clinicalStore = useClinicalStore();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(EcrfView);

    // Switch subject to SUBJ-002
    const select = wrapper.find("select#active-subject-select");
    await select.setValue("SUBJ-002");
    await select.trigger("change");

    expect(clinicalStore.activeSubjectId).toBe("SUBJ-002");
    expect(wrapper.find(".active-subj-display").text()).toBe("SUBJ-002");

    // Check formValues switch to SUBJ-002 data
    expect(clinicalStore.formValues.vssbp).toBe("130");
    expect(clinicalStore.formValues.sex).toBe("M");

    // Switch subject back to SUBJ-001
    await select.setValue("SUBJ-001");
    await select.trigger("change");

    expect(clinicalStore.activeSubjectId).toBe("SUBJ-001");
    expect(clinicalStore.formValues.vssbp).toBe("120");
    expect(clinicalStore.formValues.sex).toBe("F");
  });

  it("gates new subject enrollment for unauthorized roles", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["CRA"]; // CRA role cannot enroll subjects

    const wrapper = mount(EcrfView);

    // Assert that the gated error message is shown
    const gatedMsg = wrapper.find(".gated-lock-msg");
    expect(gatedMsg.exists()).toBe(true);
    expect(gatedMsg.text()).toContain("Only Clinical Research Coordinators (CRCs) or Site Investigators can enroll new study subjects");

    // Inputs must not be visible
    expect(wrapper.find("input#enroll-subject-id").exists()).toBe(false);
    expect(wrapper.find("button#btn-enroll-subject").exists()).toBe(false);
  });

  it("allows successful subject enrollment for Site Investigators / CRCs", async () => {
    const clinicalStore = useClinicalStore();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["CRC"]; // CRC role can enroll subjects

    const wrapper = mount(EcrfView);

    // Inputs must be rendered
    const subjectInput = wrapper.find("input#enroll-subject-id");
    const siteInput = wrapper.find("input#enroll-site-id");
    const reasonInput = wrapper.find("input#enroll-reason");
    const enrollBtn = wrapper.find("button#btn-enroll-subject");

    expect(subjectInput.exists()).toBe(true);
    expect(enrollBtn.exists()).toBe(true);

    // Fill the inputs
    await subjectInput.setValue("SUBJ-004");
    await siteInput.setValue("Site-02");
    await reasonInput.setValue("Participant meets inclusion criteria and signed ICF v2.0.");

    // Button should be active
    expect(enrollBtn.element.disabled).toBe(false);

    // Spy on alert to prevent actual popup in jsdom
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});

    // Click Enroll
    await enrollBtn.trigger("click");
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Verify execution service createSubject call
    expect(executionService.createSubject).toHaveBeenCalledTimes(1);
    expect(executionService.createSubject).toHaveBeenCalledWith(
      {
        subject_id: "SUBJ-004",
        site_id: "Site-02",
        birth_date: "1990-01-01",
        sex: "F",
      },
      { changeReason: "Participant meets inclusion criteria and signed ICF v2.0." }
    );

    // Verify clinical store state updates
    expect(clinicalStore.subjects).toContain("SUBJ-004");
    expect(clinicalStore.activeSubjectId).toBe("SUBJ-004");
    expect(wrapper.find(".active-subj-display").text()).toBe("SUBJ-004");

    // Verify GxP audit ledger block was generated
    const enrolledBlock = clinicalStore.ledgerBlocks.find(b => b.action === "SUBJECT_ENROLLED");
    expect(enrolledBlock).toBeTruthy();
    expect(enrolledBlock.details.subjectId).toBe("SUBJ-004");
    expect(enrolledBlock.reason).toBe("Participant meets inclusion criteria and signed ICF v2.0.");

    alertSpy.mockRestore();
  });

  it("disables the enroll button when subjectId or justification reason is missing", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(EcrfView);

    const subjectInput = wrapper.find("input#enroll-subject-id");
    const reasonInput = wrapper.find("input#enroll-reason");
    const enrollBtn = wrapper.find("button#btn-enroll-subject");

    // Initially disabled because subjectId and reason are empty
    expect(enrollBtn.element.disabled).toBe(true);

    // Enter ID only
    await subjectInput.setValue("SUBJ-005");
    expect(enrollBtn.element.disabled).toBe(true);

    // Enter reason only
    await subjectInput.setValue("");
    await reasonInput.setValue("Enrollment check");
    expect(enrollBtn.element.disabled).toBe(true);
  });
});
