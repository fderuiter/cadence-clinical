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
    const vm = wrapper.vm as Record<string, any>;
    vm.cocData = {
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
    const vm = wrapper.vm as Record<string, any>;
    vm.cocData = {
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
    const vm = wrapper.vm as Record<string, any>;
    vm.openMilestoneModal();
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
    await vm.handleFileUpload(file);
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".excursion-drawer").exists()).toBe(true);
    expect(wrapper.text()).toContain("Excursion Preview Drawer");
    expect(wrapper.text()).toContain("-195.0°C");
    expect(wrapper.text()).toContain("3");
    expect(wrapper.text()).toContain("OPTIMAL (0 Excursions)");
  });

  it("highlights temperature readings warmer than -70.0°C as EXCURSION in preview drawer", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    const vm = wrapper.vm as Record<string, any>;
    vm.openMilestoneModal();
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
    await vm.handleFileUpload(file);
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".excursion-drawer").exists()).toBe(true);
    expect(wrapper.text()).toContain("EXCURSION");
    expect(wrapper.text()).toContain("Detected Excursion Events");
    expect(wrapper.text()).toContain("WARM EXCURSION (> -70.0°C)");
    expect(wrapper.text()).toContain("-50.0°C");
  });

  it("displays error message in modal when invalid file format uploads", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    const vm = wrapper.vm as Record<string, any>;
    vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const invalidFile = new File(
      ["Invalid unparseable payload text"],
      "corrupted.txt",
      { type: "text/plain" }
    );
    await vm.handleFileUpload(invalidFile);
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
    const vm = wrapper.vm as Record<string, any>;
    vm.cocData = {
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

    vm.openMilestoneModal();
    await wrapper.vm.$nextTick();

    const sampleCsv = `
Timestamp,Temperature (°C)
2026-10-12T10:00:00Z,-195.0
    `.trim();
    const file = new File([sampleCsv], "Sensitech.csv", { type: "text/csv" });
    await vm.handleFileUpload(file);

    await vm.submitMilestone();

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

  function buildReadyCoc(overrides: Record<string, unknown> = {}) {
    return {
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
      ...overrides,
    };
  }

  it("initializes without hardcoded fake signature tokens and binds shared alphanumeric step-up password attributes", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    const vm = wrapper.vm as Record<string, any>;
    expect(vm.bedsideForm.primary_signature_token).toBe("");
    expect(vm.bedsideForm.secondary_signature_token).toBe("");
    expect(vm.destructionForm.witness_1_signature_token).toBe("");
    expect(vm.destructionForm.witness_2_signature_token).toBe("");

    vm.openBedsideModal();
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

  it("acquires dual step-up tokens bound to target record and posts CgtBedsideVerificationRequest and CgtDestructionCertificateCreateRequest shapes", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    const vm = wrapper.vm as Record<string, any>;
    await new Promise((resolve) => setTimeout(resolve, 0));

    let issuedCounter = 0;
    const stepUpBodies: Array<Record<string, unknown>> = [];
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input, init) => {
        const url =
          input instanceof Request
            ? new URL(input.url).pathname
            : String(input);
        if (url === "/api/v1/auth/signature-verification") {
          const parsedBody =
            input instanceof Request
              ? await input.clone().json()
              : JSON.parse(String(init?.body));
          stepUpBodies.push(parsedBody);
          issuedCounter += 1;
          return new Response(
            JSON.stringify({ sig_token: `issued-jwt-${issuedCounter}` }),
            {
              status: 200,
              headers: { "Content-Type": "application/json" },
            }
          );
        }
        if (url === "/api/v1/execution/rtsm/cgt/bedside-verify") {
          return new Response(
            JSON.stringify({
              unlock_token: "UNLOCK-SERVER-TOKEN",
              is_match: true,
            }),
            {
              status: 200,
              headers: { "Content-Type": "application/json" },
            }
          );
        }
        if (url === "/api/v1/execution/rtsm/cgt/destruction-certificate") {
          return new Response(
            JSON.stringify({
              certificate_number: "COD-2026-001",
              status: "COMPLETED",
            }),
            {
              status: 200,
              headers: { "Content-Type": "application/json" },
            }
          );
        }
        return new Response(
          JSON.stringify({
            chain_of_custody: vm.cocData,
            milestones: [],
          }),
          {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }
        );
      });

    vm.cocData = buildReadyCoc();

    vm.openBedsideModal();
    vm.bedsideForm.primary_verifier_id = "nurse-01";
    vm.bedsideForm.primary_verifier_name = "Sarah Smith, RN";
    vm.bedsideForm.primary_password = "NursePassword!1"; // pragma: allowlist secret
    vm.bedsideForm.secondary_verifier_id = "pi-02";
    vm.bedsideForm.secondary_verifier_name = "Dr. Robert Johnson, MD";
    vm.bedsideForm.secondary_password = "PiPassword!2"; // pragma: allowlist secret

    await vm.submitBedsideVerify();

    expect(stepUpBodies).toHaveLength(2);
    expect(stepUpBodies[0]).toEqual({
      username: "nurse-01",
      password: "NursePassword!1", // pragma: allowlist secret
      action: "/api/v1/execution/rtsm/cgt/bedside-verify",
      semantic_action: "execution.cgt.bedside_verify",
      batch_id: "STUDY-CGT-801:SUBJ-101-0042:=W12342698765400:BATCH-88",
    });
    expect(stepUpBodies[1]).toEqual({
      username: "pi-02",
      password: "PiPassword!2", // pragma: allowlist secret
      action: "/api/v1/execution/rtsm/cgt/bedside-verify",
      semantic_action: "execution.cgt.bedside_verify",
      batch_id: "STUDY-CGT-801:SUBJ-101-0042:=W12342698765400:BATCH-88",
    });

    const bedsideCall = fetchSpy.mock.calls.find(
      ([url]) => String(url) === "/api/v1/execution/rtsm/cgt/bedside-verify"
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

    expect(vm.bedsideForm.primary_password).toBe("");
    expect(vm.bedsideForm.secondary_password).toBe("");
    expect(vm.bedsideForm.primary_signature_token).toBe("");
    expect(vm.bedsideForm.secondary_signature_token).toBe("");

    // Now verify dual-witness destruction certificate step-up binding & request shape
    vm.openDestructionModal();
    vm.destructionForm.witness_1_id = "pharm-01";
    vm.destructionForm.witness_1_name = "David Lee, PharmD";
    vm.destructionForm.witness_1_password = "PharmPassword!1"; // pragma: allowlist secret
    vm.destructionForm.witness_2_id = "pi-02";
    vm.destructionForm.witness_2_name = "Dr. Robert Johnson, MD";
    vm.destructionForm.witness_2_password = "PiPassword!2"; // pragma: allowlist secret

    await vm.submitDestruction();

    expect(stepUpBodies).toHaveLength(4);
    expect(stepUpBodies[2]).toEqual({
      username: "pharm-01",
      password: "PharmPassword!1", // pragma: allowlist secret
      action: "/api/v1/execution/rtsm/cgt/destruction-certificate",
      semantic_action: "execution.cgt.destruction_certificate",
      batch_id: "STUDY-CGT-801:SITE-ALPHA:LOT-CAR-T-2026-01:=W12342698765400",
    });

    const destructionCall = fetchSpy.mock.calls.find(
      ([url]) =>
        String(url) === "/api/v1/execution/rtsm/cgt/destruction-certificate"
    );
    expect(destructionCall).toBeDefined();
    expect(JSON.parse(String(destructionCall?.[1]?.body))).toMatchObject({
      study_id: "STUDY-CGT-801",
      site_id: "SITE-ALPHA",
      witness_1_id: "pharm-01",
      witness_1_signature_token: "issued-jwt-3",
      witness_2_id: "pi-02",
      witness_2_signature_token: "issued-jwt-4",
    });

    expect(vm.destructionForm.witness_1_password).toBe("");
    expect(vm.destructionForm.witness_2_password).toBe("");
    expect(vm.destructionForm.witness_1_signature_token).toBe("");
    expect(vm.destructionForm.witness_2_signature_token).toBe("");

    fetchSpy.mockRestore();
  });

  it("surfaces inline alert and keeps modal open when step-up or bedside verification fails", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockImplementation(
      async () =>
        new Response(
          JSON.stringify({
            code: "REAUTHENTICATION_REQUIRED",
            detail: "Step-up electronic signature token is invalid or expired.",
          }),
          {
            status: 401,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
    );

    const wrapper = mount(CellGeneTherapyCocConsole);
    const vm = wrapper.vm as Record<string, any>;
    vm.cocData = buildReadyCoc({ id: "coc-901" });

    vm.openBedsideModal();
    vm.bedsideForm.primary_verifier_id = "nurse-01";
    vm.bedsideForm.primary_verifier_name = "Sarah Smith, RN";
    vm.bedsideForm.primary_password = "WrongPassword"; // pragma: allowlist secret
    vm.bedsideForm.secondary_verifier_id = "pi-02";
    vm.bedsideForm.secondary_verifier_name = "Dr. Robert Johnson, MD";
    vm.bedsideForm.secondary_password = "PiPassword!2"; // pragma: allowlist secret

    await vm.submitBedsideVerify();
    await wrapper.vm.$nextTick();

    expect(vm.showBedsideModal).toBe(true);
    const errorAlert = wrapper.find('[data-testid="bedside-verify-error"]');
    expect(errorAlert.exists()).toBe(true);
    expect(errorAlert.text()).toContain("Primary Verifier:");

    fetchSpy.mockRestore();
  });
});
