import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import CellGeneTherapyCocConsole from "@/components/clinical/CellGeneTherapyCocConsole.vue";

describe("CellGeneTherapyCocConsole.vue - Domain Formatting Specification", () => {
  it("renders human-readable clinical labels instead of raw database keys or UUID labels", () => {
    const wrapper = mount(CellGeneTherapyCocConsole);

    expect(wrapper.text()).toContain("Subject Identifier");
    expect(wrapper.text()).not.toContain("Subject ID / UUID");

    expect(wrapper.text()).toContain(
      "Enter a valid Study Protocol and Subject Identifier above"
    );
    expect(wrapper.text()).not.toContain("Subject UUID");
  });

  it("displays human readable labels for facility codes when ledger data is loaded", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.cocData = {
      id: "coc-1",
      study_id: "STUDY-801",
      subject_id: "SUBJ-001",
      din: "DIN-12345",
      batch_id: "BATCH-A1",
      current_stage: "HARVEST",
      status: "ACTIVE",
      tamper_evident_barcode: "BARCODE",
      barcode_signature: "SIG",
      collection_site_id: "SITE-ALPHA",
      collection_timestamp: "2026-10-01",
      manufacturing_facility_id: "CMO-BETA",
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      infusion_unlocked: false,
    };
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Collection Site: SITE-ALPHA");
    expect(wrapper.text()).toContain("Manufacturing Facility: CMO-BETA");
  });

  it("opens transit milestone modal without errors when clicking '+ Log Transit Milestone'", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.cocData = {
      id: "coc-1",
      study_id: "STUDY-CGT-801",
      subject_id: "SUBJ-101-0042",
      din: "DIN-12345",
      batch_id: null,
      current_stage: "COURIER_TRANSIT_OUTBOUND",
      status: "IN_TRANSIT",
      tamper_evident_barcode: null,
      barcode_signature: null,
      collection_site_id: "SITE-ALPHA",
      collection_timestamp: null,
      manufacturing_facility_id: null,
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      highest_temp_celsius: -70.0,
      infusion_unlocked: false,
      infused_at: null,
      reconciled_at: null,
    };
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".modal-xl").exists()).toBe(false);

    const logBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("+ Log Transit Milestone"));
    expect(logBtn).toBeDefined();
    await logBtn?.trigger("click");

    expect(wrapper.find(".modal-xl").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Log Transit Milestone & Telemetry Upload"
    );
  });

  it("parses valid telemetry file and displays excursion preview drawer with minimum temp and sample count", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const sampleCsv = `
Timestamp,Temperature (°C)
2026-10-12T10:00:00Z,-195.0
2026-10-12T10:15:00Z,-192.0
2026-10-12T10:30:00Z,-188.5
    `.trim();

    const file = new File([sampleCsv], "Sensitech_Transit_Log.csv", {
      type: "text/csv",
    });
    await wrapper.vm.handleFileUpload(file);
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".excursion-drawer").exists()).toBe(true);
    expect(wrapper.text()).toContain("Excursion Preview Drawer");
    expect(wrapper.text()).toContain("-195.0°C");
    expect(wrapper.text()).toContain("3");
    expect(wrapper.text()).toContain("OPTIMAL (0 Excursions)");
  });

  it("highlights temperature readings warmer than -70.0°C as EXCURSION in preview drawer", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const sampleCsv = `
Date Time,Temp (°C)
2026-10-12T12:00:00Z,-190.0
2026-10-12T12:15:00Z,-65.0
2026-10-12T12:30:00Z,-50.0
2026-10-12T12:45:00Z,-185.0
    `.trim();

    const file = new File([sampleCsv], "TempTale_Excursion.csv", {
      type: "text/csv",
    });
    await wrapper.vm.handleFileUpload(file);
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".excursion-drawer").exists()).toBe(true);
    expect(wrapper.text()).toContain("EXCURSION");
    expect(wrapper.text()).toContain("Detected Excursion Events");
    expect(wrapper.text()).toContain("WARM EXCURSION (> -70.0°C)");
    expect(wrapper.text()).toContain("-50.0°C");
  });

  it("displays error message in modal when invalid file format uploads", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const invalidFile = new File(
      ["Invalid unparseable payload text"],
      "corrupted.txt",
      { type: "text/plain" }
    );
    await wrapper.vm.handleFileUpload(invalidFile);
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".excursion-drawer").exists()).toBe(false);
    expect(wrapper.find(".alert-danger").exists()).toBe(true);
    expect(wrapper.text()).toContain("Parsing Error");
  });

  it("submits parsed milestone data to POST /api/v1/execution/rtsm/cgt/milestone and refreshes traceability", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: async () => ({
        id: "m-101",
        stage: "COURIER_TRANSIT_OUTBOUND",
        temperature_celsius: -195.0,
      }),
    } as Response);

    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.cocData = {
      id: "coc-123",
      study_id: "STUDY-CGT-801",
      subject_id: "SUBJ-101-0042",
      din: "DIN-123",
      batch_id: null,
      current_stage: "COURIER_TRANSIT_OUTBOUND",
      status: "IN_TRANSIT",
      tamper_evident_barcode: null,
      barcode_signature: null,
      collection_site_id: "SITE-1",
      collection_timestamp: null,
      manufacturing_facility_id: null,
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      highest_temp_celsius: -70.0,
      infusion_unlocked: false,
      infused_at: null,
      reconciled_at: null,
    };

    wrapper.vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const sampleCsv = `
Timestamp,Temperature (°C)
2026-10-12T10:00:00Z,-195.0
    `.trim();
    const file = new File([sampleCsv], "Sensitech.csv", { type: "text/csv" });
    await wrapper.vm.handleFileUpload(file);

    await wrapper.vm.submitMilestone();

    expect(fetchSpy).toHaveBeenCalledWith(
      "/api/v1/execution/rtsm/cgt/milestone",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "Content-Type": "application/json",
        }),
      })
    );

    fetchSpy.mockRestore();
  });

  it("initializes without hardcoded fake signature tokens and binds shared alphanumeric step-up password attributes", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    expect(wrapper.vm.bedsideForm.primary_signature_token).toBe("");
    expect(wrapper.vm.bedsideForm.secondary_signature_token).toBe("");
    expect(wrapper.vm.destructionForm.witness_1_signature_token).toBe("");
    expect(wrapper.vm.destructionForm.witness_2_signature_token).toBe("");

    wrapper.vm.openBedsideModal();
    await wrapper.vm.$nextTick();

    const primaryInput = wrapper.find(
      '[data-testid="primary-stepup-password"]'
    );
    const secondaryInput = wrapper.find(
      '[data-testid="secondary-stepup-password"]'
    );
    expect(primaryInput.exists()).toBe(true);
    expect(secondaryInput.exists()).toBe(true);
    expect(primaryInput.attributes("type")).toBe("password");
    expect(primaryInput.attributes("inputmode")).toBe("text");
    expect(secondaryInput.attributes("inputmode")).toBe("text");
  });

  it("acquires dual step-up tokens bound to target record and posts CgtBedsideVerificationRequest shape", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    await new Promise((resolve) => setTimeout(resolve, 0));

    let issuedCounter = 0;
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input) => {
        const url = String(input);
        if (url === "/api/v1/auth/signature-verification") {
          issuedCounter += 1;
          return {
            ok: true,
            json: async () => ({
              sig_token: `issued-jwt-${issuedCounter}`,
            }),
          } as Response;
        }
        if (url === "/api/v1/execution/rtsm/cgt/bedside-verify") {
          return {
            ok: true,
            json: async () => ({
              unlock_token: "UNLOCK-SERVER-TOKEN",
              is_match: true,
            }),
          } as Response;
        }
        return {
          ok: true,
          json: async () => ({
            chain_of_custody: wrapper.vm.cocData,
            milestones: [],
          }),
        } as Response;
      });

    wrapper.vm.cocData = {
      id: "coc-900",
      study_id: "STUDY-CGT-801",
      site_id: "SITE-ALPHA",
      subject_id: "SUBJ-101-0042",
      din: "=W12342698765400",
      batch_id: "BATCH-88",
      current_stage: "BEDSIDE_VERIFICATION",
      status: "READY_FOR_INFUSION",
      tamper_evident_barcode: "CGT1|STUDY-CGT-801|...",
      barcode_signature: "SIG",
      collection_site_id: "SITE-ALPHA",
      collection_timestamp: null,
      manufacturing_facility_id: "CMO-BETA",
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      infusion_unlocked: false,
    };

    wrapper.vm.openBedsideModal();
    wrapper.vm.bedsideForm.primary_verifier_id = "nurse-01";
    wrapper.vm.bedsideForm.primary_verifier_name = "Sarah Smith, RN";
    wrapper.vm.bedsideForm.primary_password = "NursePassword!1"; // pragma: allowlist secret
    wrapper.vm.bedsideForm.secondary_verifier_id = "pi-02";
    wrapper.vm.bedsideForm.secondary_verifier_name = "Dr. Robert Johnson, MD";
    wrapper.vm.bedsideForm.secondary_password = "PiPassword!2"; // pragma: allowlist secret

    await wrapper.vm.submitBedsideVerify();

    const stepUpCalls = fetchSpy.mock.calls.filter(
      ([url]) => url === "/api/v1/auth/signature-verification"
    );
    expect(stepUpCalls).toHaveLength(2);
    expect(JSON.parse(String(stepUpCalls[0][1]?.body))).toEqual({
      username: "nurse-01",
      password: "NursePassword!1", // pragma: allowlist secret
      action: "/api/v1/execution/rtsm/cgt/bedside-verify",
      semantic_action: "execution.cgt.bedside_verify",
      batch_id: "STUDY-CGT-801:SUBJ-101-0042:=W12342698765400:BATCH-88",
    });
    expect(JSON.parse(String(stepUpCalls[1][1]?.body))).toEqual({
      username: "pi-02",
      password: "PiPassword!2", // pragma: allowlist secret
      action: "/api/v1/execution/rtsm/cgt/bedside-verify",
      semantic_action: "execution.cgt.bedside_verify",
      batch_id: "STUDY-CGT-801:SUBJ-101-0042:=W12342698765400:BATCH-88",
    });

    const bedsideCall = fetchSpy.mock.calls.find(
      ([url]) => url === "/api/v1/execution/rtsm/cgt/bedside-verify"
    );
    expect(bedsideCall).toBeDefined();
    expect(JSON.parse(String(bedsideCall?.[1]?.body))).toEqual({
      study_id: "STUDY-CGT-801",
      site_id: "SITE-ALPHA",
      subject_id: "SUBJ-101-0042",
      din: "=W12342698765400",
      batch_id: "BATCH-88",
      bag_barcode_payload: "CGT1|STUDY-CGT-801|...",
      patient_wristband_id: "SUBJ-101-0042",
      primary_verifier_id: "nurse-01",
      primary_verifier_name: "Sarah Smith, RN",
      primary_verifier_role: "Bedside Nurse",
      primary_signature_token: "issued-jwt-1",
      secondary_verifier_id: "pi-02",
      secondary_verifier_name: "Dr. Robert Johnson, MD",
      secondary_verifier_role: "Principal Investigator",
      secondary_signature_token: "issued-jwt-2",
    });

    // Sensitive credentials must be scrubbed from reactive state after submission.
    expect(wrapper.vm.bedsideForm.primary_password).toBe("");
    expect(wrapper.vm.bedsideForm.secondary_password).toBe("");
    expect(wrapper.vm.bedsideForm.primary_signature_token).toBe("");
    expect(wrapper.vm.bedsideForm.secondary_signature_token).toBe("");

    fetchSpy.mockRestore();
  });

  it("surfaces inline alert and keeps modal open when step-up or bedside verification fails", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({
        code: "REAUTHENTICATION_REQUIRED",
        detail: "Step-up electronic signature token is invalid or expired.",
      }),
    } as Response);

    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.cocData = {
      id: "coc-901",
      study_id: "STUDY-CGT-801",
      site_id: "SITE-ALPHA",
      subject_id: "SUBJ-101-0042",
      din: "=W12342698765400",
      batch_id: "BATCH-88",
      current_stage: "BEDSIDE_VERIFICATION",
      status: "READY_FOR_INFUSION",
      tamper_evident_barcode: "CGT1|STUDY-CGT-801|...",
      barcode_signature: "SIG",
      collection_site_id: "SITE-ALPHA",
      collection_timestamp: null,
      manufacturing_facility_id: "CMO-BETA",
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      infusion_unlocked: false,
    };

    wrapper.vm.openBedsideModal();
    wrapper.vm.bedsideForm.primary_verifier_id = "nurse-01";
    wrapper.vm.bedsideForm.primary_verifier_name = "Sarah Smith, RN";
    wrapper.vm.bedsideForm.primary_password = "WrongPassword"; // pragma: allowlist secret
    wrapper.vm.bedsideForm.secondary_verifier_id = "pi-02";
    wrapper.vm.bedsideForm.secondary_verifier_name = "Dr. Robert Johnson, MD";
    wrapper.vm.bedsideForm.secondary_password = "PiPassword!2"; // pragma: allowlist secret

    await wrapper.vm.submitBedsideVerify();
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showBedsideModal).toBe(true);
    const errorAlert = wrapper.find('[data-testid="bedside-verify-error"]');
    expect(errorAlert.exists()).toBe(true);
    expect(errorAlert.text()).toContain("Primary Verifier:");

    fetchSpy.mockRestore();
  });
});
