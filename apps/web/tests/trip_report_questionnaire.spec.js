import { describe, it, expect, beforeEach, vi } from "vitest";
import { setActivePinia, createPinia } from "pinia";
import { mount } from "@vue/test-utils";
import { useClinicalStore } from "../src/stores/clinical";
import TripReportConsole from "../src/components/clinical/TripReportConsole.vue";

describe("Trip Report & Structured Visit Questionnaire Engine", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.restoreAllMocks();
  });

  it("initializes form across visit types (PSV, SIV, IMV, COV) and populates default sections", async () => {
    const store = useClinicalStore();

    for (const vType of ["PSV", "SIV", "IMV", "COV"]) {
      const visitId = `VISIT-${vType}-TEST`;
      const form = await store.fetchTripReport(visitId);

      expect(form).toBeDefined();
      expect(form.visitId).toBe(visitId);
      expect(form.visitType).toBe(vType);
      expect(form.sections).toHaveLength(4);

      const sectionKeys = form.sections.map((s) => s.sectionKey);
      expect(sectionKeys).toContain("protocol_compliance");
      expect(sectionKeys).toContain("ip_accountability");
      expect(sectionKeys).toContain("regulatory_binder");
      expect(sectionKeys).toContain("facilities");
    }
  });

  it("handles draft saving and dirty state tracking", async () => {
    const store = useClinicalStore();
    await store.fetchTripReport("VISIT-IMV-101");

    expect(store.isTripReportDirty).toBe(false);

    store.markTripReportDirty(true);
    expect(store.isTripReportDirty).toBe(true);

    const savePayload = {
      tripReportId: "VISIT-IMV-101",
      sections: [
        {
          sectionKey: "protocol_compliance",
          isVerified: true,
          responses: [
            {
              questionKey: "pc_irb_approval",
              questionText: "Is IRB approval current?",
              responseType: "boolean",
              value: "PASS",
              commentary: "Approved by WCG IRB",
            },
          ],
        },
      ],
      reasonForChange: "Test draft save",
    };

    await store.saveTripReportDraft(savePayload);
    expect(store.isTripReportDirty).toBe(false);

    const pcSec = store.activeTripReport?.sections.find(
      (s) => s.sectionKey === "protocol_compliance"
    );
    expect(pcSec?.isVerified).toBe(true);
  });

  it("attaches findings linked to specific section and question item", async () => {
    const store = useClinicalStore();
    await store.fetchTripReport("VISIT-SIV-101");

    const finding = await store.attachTripReportFinding({
      visitId: "VISIT-SIV-101",
      sectionKey: "ip_accountability",
      questionKey: "ip_temp_logs",
      text: "Temperature log missing for July 12",
      severity: "MAJOR",
      resolutionStatus: "OPEN",
    });

    expect(finding).toBeDefined();
    expect(finding.text).toContain("Temperature log missing");

    const ipSec = store.activeTripReport?.sections.find(
      (s) => s.sectionKey === "ip_accountability"
    );
    expect(ipSec?.findings).toHaveLength(1);
    expect(ipSec?.findings[0].questionKey).toBe("ip_temp_logs");
  });

  it("submits 21 CFR Part 11 electronic signature and finalizes report with eTMF filing", async () => {
    const store = useClinicalStore();
    await store.fetchTripReport("VISIT-IMV-101");

    const submitResult = await store.submitTripReport({
      tripReportId: "VISIT-IMV-101",
      finalize: true,
      signerName: "Frank de Ruiter",
      signingPurpose:
        "21 CFR Part 11 Electronic Signature / Visit Report Sign-off",
      reasonForChange: "Final submission",
    });

    expect(submitResult.status).toBe("FINALIZED");
    expect(store.activeTripReport?.status).toBe("FINALIZED");
    expect(store.activeTripReport?.isSigned).toBe(true);
    expect(store.activeTripReport?.signer).toBe("Frank de Ruiter");
    expect(store.activeTripReport?.etmfDocumentId).toBeDefined();
  });

  it("renders TripReportConsole UI component with sectional navigation and verification indicators", async () => {
    const store = useClinicalStore();
    await store.fetchTripReport("VISIT-IMV-101");

    const wrapper = mount(TripReportConsole, {
      props: { visitIdProp: "VISIT-IMV-101" },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("Structured Visit Questionnaire");
    expect(wrapper.text()).toContain("Protocol Compliance");
    expect(wrapper.text()).toContain("IP Accountability");
    expect(wrapper.text()).toContain("Regulatory Binder");
    expect(wrapper.text()).toContain("Facilities");

    // Click on Finalize & Sign button to open 21 CFR Part 11 modal
    const signBtn = wrapper.find("#btn-finalize-sign");
    expect(signBtn.exists()).toBe(true);
    await signBtn.trigger("click");

    expect(wrapper.text()).toContain("21 CFR Part 11 Electronic Signature");
  });
});
