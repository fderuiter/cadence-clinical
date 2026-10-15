import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import AdaptiveDoseEscalationConsole from "@/components/clinical/AdaptiveDoseEscalationConsole.vue";

describe("AdaptiveDoseEscalationConsole.vue", () => {
  const defaultProps = {
    studyId: "STUDY-BOIN-001",
    designType: "BOIN",
    targetToxicityRate: 0.25,
    lambdaE: 0.197,
    lambdaD: 0.298,
    activeCohortNumber: 2,
    currentDoseLevelIndex: 1,
    currentDoseName: "Dose 2 (20 mg)",
    recommendationAction: "ESCALATE",
    recommendedDoseLevelIndex: 2,
    recommendedDoseName: "Dose 3 (40 mg)",
    rationale: "Empirical DLT rate 0/3 is below lower boundary lambda_e (0.197). Escalate to next dose level.",
    isActive: true,
    doseSummaries: [
      {
        dose_level_index: 0,
        dose_name: "Level 1 - 10mg",
        dose_value: 10,
        total_enrolled: 6,
        total_dlts: 0,
        observed_dlt_rate: 0,
        posterior_dlt_rate: 0,
        is_eliminated_for_safety: false,
        overdose_probability: 0.01,
      },
      {
        dose_level_index: 1,
        dose_name: "Level 2 - 20mg",
        dose_value: 20,
        total_enrolled: 3,
        total_dlts: 0,
        observed_dlt_rate: 0,
        posterior_dlt_rate: 0,
        is_eliminated_for_safety: false,
        overdose_probability: 0.02,
      },
      {
        dose_level_index: 2,
        dose_name: "Level 3 - 40mg",
        dose_value: 40,
        total_enrolled: 0,
        total_dlts: 0,
        observed_dlt_rate: 0,
        posterior_dlt_rate: null,
        is_eliminated_for_safety: false,
        overdose_probability: null,
      },
    ],
    rarArms: [
      {
        arm_id: "ARM-CTRL",
        arm_name: "Control Arm",
        is_control: true,
        status: "ACTIVE",
        enrolled_count: 10,
        response_count: 3,
        posterior_mean: 0.3,
        allocation_probability: 0.25,
        superiority_vs_control_prob: 0,
      },
      {
        arm_id: "ARM-DOSE1",
        arm_name: "Treatment ARM-DOSE1",
        is_control: false,
        status: "ACTIVE",
        enrolled_count: 12,
        response_count: 6,
        posterior_mean: 0.5,
        allocation_probability: 0.42,
        superiority_vs_control_prob: 0.88,
      },
    ],
  };

  it("renders console header, BOIN metrics, and recommendation banner", () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    expect(wrapper.text()).toContain("Adaptive Oncology Dose Escalation & Randomization Console");
    expect(wrapper.text()).toContain("Bayesian Optimal Interval (BOIN)");
    expect(wrapper.text()).toContain("Target Toxicity (φ)");
    expect(wrapper.text()).toContain("25.0%");
    expect(wrapper.text()).toContain("λe 0.197 | λd 0.298");
    expect(wrapper.text()).toContain("Cohort #2");
    expect(wrapper.text()).toContain("RECOMMENDED ACTION:");
    expect(wrapper.text()).toContain("ESCALATE");
    expect(wrapper.text()).toContain("Target: Dose 3 (40 mg)");
  });

  it("renders dose level escalation table with summaries", () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    expect(wrapper.text()).toContain("Dose Level Escalation Tracker & Posterior Estimates");
    expect(wrapper.text()).toContain("Level 1 - 10mg");
    expect(wrapper.text()).toContain("Level 2 - 20mg");
    expect(wrapper.text()).toContain("Level 3 - 40mg");
  });

  it("renders Response-Adaptive Randomization (RAR) section when arms exist", () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    expect(wrapper.text()).toContain("Response-Adaptive Randomization (RAR) Allocation Weights");
    expect(wrapper.text()).toContain("Control Arm");
    expect(wrapper.text()).toContain("Treatment ARM-DOSE1");
  });

  it("opens transition sign-off modal on clicking 'Execute Cohort Decision'", async () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    expect(wrapper.find(".modal-backdrop").exists()).toBe(false);

    const btn = wrapper.find(".btn-primary-action");
    await btn.trigger("click");

    expect(wrapper.find(".modal-backdrop").exists()).toBe(true);
    expect(wrapper.text()).toContain("21 CFR Part 11 Cohort Transition Sign-off");
  });

  it("displays override rationale field when selected action differs from recommendation", async () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    await wrapper.find(".btn-primary-action").trigger("click");

    expect(wrapper.find("#override-reason-input").exists()).toBe(false);

    // Change selected action from ESCALATE to STAY
    const select = wrapper.find("#decision-action-select");
    await select.setValue("STAY");

    expect(wrapper.find("#override-reason-input").exists()).toBe(true);
    expect(wrapper.text()).toContain("Safety Committee Override Justification (21 CFR Part 11)");
  });

  it("emits transition-cohort event when authorization form is submitted", async () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: defaultProps,
    });

    await wrapper.find(".btn-primary-action").trigger("click");

    const esigInput = wrapper.find("#esignature-input");
    await esigInput.setValue("Dr. Jane Smith, MD");

    const submitBtn = wrapper.find(".btn-submit");
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(false);

    await submitBtn.trigger("click");

    expect(wrapper.emitted("transition-cohort")).toBeDefined();
    expect(wrapper.emitted("transition-cohort")![0][0]).toEqual({
      study_id: "STUDY-BOIN-001",
      cohort_number: 2,
      decision_action: "ESCALATE",
      target_dose_level_index: 2,
      override_reason: undefined,
      electronic_signature: "Dr. Jane Smith, MD",
    });
  });
});
