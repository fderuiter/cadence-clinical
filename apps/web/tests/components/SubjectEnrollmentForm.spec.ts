import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import SubjectEnrollmentForm from "../../src/components/clinical/SubjectEnrollmentForm.vue";

describe("SubjectEnrollmentForm.vue - Reusable Enrollment and Screening Component", () => {
  it("renders input fields and buttons with default props", () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        canEnroll: true,
        disabled: false,
        defaultStudyId: "CADENCE-101",
      },
    });

    expect(wrapper.find(".card-title").text()).toContain("Subject Registration & Screening");
    expect(wrapper.find(".btn-enroll-action").exists()).toBe(true);
    expect(wrapper.find(".btn-screen-action").exists()).toBe(true);
  });

  it("disables actions when canEnroll is false", () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        canEnroll: false,
      },
    });

    const enrollBtn = wrapper.find(".btn-enroll-action");
    const screenBtn = wrapper.find(".btn-screen-action");

    expect(enrollBtn.attributes("disabled")).toBeDefined();
    expect(screenBtn.attributes("disabled")).toBeDefined();
  });

  it("emits screen event when Check Eligibility is clicked with valid subject_id", async () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        canEnroll: true,
        defaultStudyId: "CADENCE-101",
      },
    });

    // Enter subject_id
    const subjectInput = wrapper.find("#enroll-subject-id");
    await subjectInput.setValue("SUBJ-999");

    const screenBtn = wrapper.find(".btn-screen-action");
    await screenBtn.trigger("click");

    expect(wrapper.emitted("screen")).toBeTruthy();
    expect(wrapper.emitted("screen")![0]).toEqual([
      {
        subjectId: "SUBJ-999",
        studyId: "CADENCE-101",
      },
    ]);
  });

  it("emits enroll event with required and non-empty demographic fields", async () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        canEnroll: true,
        defaultStudyId: "CADENCE-101",
      },
    });

    await wrapper.find("#enroll-subject-id").setValue("SUBJ-888");
    await wrapper.find("#enroll-name").setValue("Jane Doe");
    await wrapper.find("#enroll-gender").setValue("Female");

    const form = wrapper.find("form");
    await form.trigger("submit.prevent");

    expect(wrapper.emitted("enroll")).toBeTruthy();
    expect(wrapper.emitted("enroll")![0][0]).toEqual({
      subject_id: "SUBJ-888",
      study_id: "CADENCE-101",
      demographics: {
        name: "Jane Doe",
        gender: "Female",
      },
    });
  });

  it("displays eligible status and badge when screeningResult is eligible", () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        screeningResult: {
          eligible: true,
          failed_criteria: [],
          indeterminate_criteria: [],
        },
      },
    });

    expect(wrapper.find(".eligible-pane").exists()).toBe(true);
    expect(wrapper.find(".screening-outcome-title").text()).toContain("Subject meets all protocol inclusion criteria");
    expect(wrapper.find(".badge-success").text()).toBe("ELIGIBLE");
  });

  it("displays failed criteria chips when screeningResult has failures", () => {
    const wrapper = mount(SubjectEnrollmentForm, {
      props: {
        screeningResult: {
          eligible: false,
          failed_criteria: ["CRIT-INCL-01: Age >= 18", "CRIT-EXCL-02: Liver function"],
          indeterminate_criteria: [],
        },
      },
    });

    expect(wrapper.find(".ineligible-pane").exists()).toBe(true);
    expect(wrapper.find(".badge-danger").text()).toBe("INELIGIBLE");
    const chips = wrapper.findAll(".failed-criteria .criterion-chip");
    expect(chips).toHaveLength(2);
    expect(chips[0].text()).toContain("CRIT-INCL-01");
  });
});
