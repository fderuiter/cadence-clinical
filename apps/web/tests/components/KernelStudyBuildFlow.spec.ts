import { describe, it, expect, vi, beforeEach } from "vitest";
import { defineComponent, h } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory } from "vue-router";
import ProtocolImportPanel from "../../src/components/canvas/ProtocolImportPanel.vue";
import ProtocolVersionsSection from "../../src/components/canvas/ProtocolVersionsSection.vue";
import ValidationSection from "../../src/components/canvas/ValidationSection.vue";
import SiteActivationPanel from "../../src/components/clinical/SiteActivationPanel.vue";
import { ApiError } from "../../src/api/client";
import { ctmsApi } from "../../src/api/ctmsApi";
import { etmfService } from "../../src/api/etmf";
import { executionApi } from "../../src/api/executionApi";
import {
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  kernelApi,
  PROTOCOL_VERSION_APPROVE_SEMANTIC_ACTION,
} from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth";

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    verifySignature: vi.fn(),
  },
}));

vi.mock("../../src/api/ctmsApi", () => ({
  ctmsApi: {
    listSiteMilestones: vi.fn(async () => []),
  },
}));

vi.mock("../../src/api/executionApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/executionApi")>();
  return {
    ...actual,
    executionApi: {
      ...actual.executionApi,
      getSiteComplianceStatus: vi.fn(),
      activateSite: vi.fn(),
    },
  };
});

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      search: vi.fn(),
      listProtocolTemplates: vi.fn(async () => []),
      listProtocolVersions: vi.fn(),
      getProtocolDesignSummary: vi.fn(),
      listStandards: vi.fn(),
      registerStandardVersion: vi.fn(),
      approveStandardVersion: vi.fn(),
      listLibraryItems: vi.fn(),
      draftLibraryItem: vi.fn(),
      approveLibraryItem: vi.fn(),
      declareStudyStandards: vi.fn(),
      submitProtocolVersion: vi.fn(),
      returnProtocolVersionToDraft: vi.fn(),
      approveProtocolVersion: vi.fn(),
      withdrawProtocolVersion: vi.fn(),
      forkProtocolVersion: vi.fn(),
      listStudyRulePacks: vi.fn(),
      previewStudyRulePack: vi.fn(),
      declareStudyRulePacks: vi.fn(),
      listStudyValidationFindings: vi.fn(),
      getValidationFinding: vi.fn(),
      runStudyValidation: vi.fn(),
      investigateFinding: vi.fn(),
      classifyFinding: vi.fn(),
      acceptFinding: vi.fn(),
      explainFinding: vi.fn(),
      reopenFinding: vi.fn(),
      listStudySites: vi.fn(),
      registerSite: vi.fn(),
      linkSiteToStudy: vi.fn(),
      listStudyArtifacts: vi.fn(),
      registerArtifact: vi.fn(),
      declareEffectivity: vi.fn(),
      changeEffectivityStatus: vi.fn(),
      activateStudySite: vi.fn(),
    },
  };
});

const RouteStub = defineComponent({
  name: "RouteStub",
  render: () => h("div"),
});

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", name: "home", component: RouteStub },
      { path: "/studies/:studyCode?", name: "study", component: RouteStub },
      { path: "/plans", name: "plans", component: RouteStub },
    ],
  });
}

describe("Kernel study-build UI workflows", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("rejects .pdf selection in ProtocolImportPanel with ODM XML / USDM JSON guidance (@req:PRD-CRF-006)", async () => {
    const router = makeRouter();
    const wrapper = mount(ProtocolImportPanel, {
      props: {
        studyRef: "study:01950000-0000-7000-8000-000000000010",
        studyCode: "STUDY-201",
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const input = wrapper.find("#protocol-import-file");
    const pdf = new File(["%PDF-1.7"], "protocol.pdf", {
      type: "application/pdf",
    });
    Object.defineProperty(input.element, "files", {
      value: [pdf],
      configurable: true,
    });
    await input.trigger("change");
    await flushPromises();

    expect(wrapper.find("#protocol-import-error").text()).toContain(
      "PDF protocol digitization is not yet available for 'protocol.pdf'. Upload an ODM v1.3.2 XML (.xml) or USDM v4 JSON (.json) file."
    );
  });

  it("drives ProtocolVersionsSection SDTM mapping, submission error handling, step-up approval, and amendment forking (@req:PRD-CRF-003 @req:PRD-CRF-012)", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["designer"];
    auth.user = {
      id: "user-designer-1",
      username: "designer@cadence.example",
      email: "designer@cadence.example",
      firstName: "Dana",
      lastName: "Designer",
      tenantId: "tenant-201",
    };

    const studyUuid = "01950000-0000-7000-8000-000000000010";
    const v1Uuid = "01950000-0000-7000-8000-000000000021";
    const v2Uuid = "01950000-0000-7000-8000-000000000022";
    const stdUuid = "01950000-0000-7000-8000-000000000031";
    const mapUuid = "01950000-0000-7000-8000-000000000041";

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "STUDY-201",
        title: "Founder Trial",
        exact_code: true,
      },
    ]);

    let currentStatus = "Draft";
    vi.mocked(kernelApi.listProtocolVersions).mockImplementation(async () => [
      {
        ref: `protocol_version:${v1Uuid}@1`,
        study: `study:${studyUuid}@1`,
        number: 1,
        parent_version: null,
        status: currentStatus,
        content_hash: currentStatus === "Draft" ? null : "hash-v1",
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
    ]);

    vi.mocked(kernelApi.getProtocolDesignSummary).mockResolvedValue({
      version: {
        ref: `protocol_version:${v1Uuid}@1`,
        study: `study:${studyUuid}@1`,
        number: 1,
        parent_version: null,
        status: "Draft",
        content_hash: null,
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
      title: "Founder Trial v1",
      form_count: 1,
      item_count: 1,
      forms: [
        {
          id: "VS",
          display_order: 1,
          name: "Vital Signs",
          item_count: 1,
          required_item_count: 1,
          items: [
            {
              id: "SYSBP",
              display_order: 1,
              prompt: "Systolic Blood Pressure",
              concept: "concept:01950000-0000-7000-8000-000000000091@1",
              required: true,
              value_kind: "Quantity",
              unit: "mm[Hg]",
            },
          ],
        },
      ],
      import_notes: [],
    });

    vi.mocked(kernelApi.listStandards).mockResolvedValue({
      standards: [
        {
          ref: `standard_version:${stdUuid}@2`,
          standard_code: "SDTMIG",
          version: "3.4",
          name: "SDTMIG v3.4",
          publisher: "CDISC",
          source: null,
          effective_date: "2021-11-29",
          content_hash: "stdhash",
          status: "Approved",
          approval_attestation_ref: null,
          deprecation_attestation_ref: null,
          controlled_terminology: [],
          datasets: [],
        },
      ],
      bundled_seeds: [],
    });

    let currentMappingSpecs: Awaited<
      ReturnType<typeof kernelApi.listLibraryItems>
    > = [];
    vi.mocked(kernelApi.listLibraryItems).mockImplementation(
      async () => currentMappingSpecs
    );
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      study_ref: `study:${studyUuid}@1`,
      profile_ref: null,
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });

    const router = makeRouter();
    const wrapper = mount(ProtocolVersionsSection, {
      props: {
        studyCode: "STUDY-201",
        selectedRef: `protocol_version:${v1Uuid}@1`,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // 1. Attempt submit before mapping -> surfaces K.Standards.MappingMissing
    vi.mocked(kernelApi.submitProtocolVersion).mockRejectedValueOnce(
      new ApiError(
        "Every collected concept needs a mapping (K-STD-1).",
        422,
        "Unprocessable Entity",
        {
          code: "K.Standards.MappingMissing",
          detail:
            "Every collected concept needs a mapping (K-STD-1). Unmapped: VS.SYSBP.",
        }
      )
    );
    await wrapper.find("#btn-submit-protocol-version").trigger("click");
    await flushPromises();
    expect(wrapper.find("#protocol-governance-error").text()).toContain(
      "Unmapped: VS.SYSBP"
    );

    // 2. Draft SDTM concept mapping over collected concepts
    const draftedMapping = {
      ref: `library_item:${mapUuid}@2`,
      kind: "MappingSpecification",
      key: "study-201-sdtm-mapping",
      version: 1,
      title: "STUDY-201 SDTMIG 3.4 Mapping Specification",
      status: "Approved",
      pinned: false,
      content_hash: "maphash",
      standard_version_refs: [`standard_version:${stdUuid}@2`],
      approval_attestation_ref: null,
      deprecation_attestation_ref: null,
      content: {},
    };
    currentMappingSpecs = [draftedMapping];
    vi.mocked(kernelApi.draftLibraryItem).mockResolvedValue(draftedMapping);

    await wrapper.find("#btn-draft-mapping-spec").trigger("click");
    await flushPromises();
    expect(kernelApi.draftLibraryItem).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: "MappingSpecification",
        standardVersionRefs: [`standard_version:${stdUuid}@2`],
      })
    );

    // 3. Pin mapping & SDTMIG 3.4 on study
    currentMappingSpecs = [{ ...draftedMapping, pinned: true }];
    vi.mocked(kernelApi.declareStudyStandards).mockResolvedValue({
      study_ref: `study:${studyUuid}@1`,
      profile_ref:
        "study_standards_profile:01950000-0000-7000-8000-000000000061@1",
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });
    await wrapper
      .find("#btn-pin-mapping-study-201-sdtm-mapping-1")
      .trigger("click");
    await flushPromises();
    expect(kernelApi.declareStudyStandards).toHaveBeenCalledWith(
      expect.objectContaining({
        standardVersionRefs: [`standard_version:${stdUuid}@2`],
        mappingSpecRefs: [`library_item:${mapUuid}@2`],
      })
    );

    // 4. Submit version for review -> transitions to InReview
    currentStatus = "InReview";
    vi.mocked(kernelApi.submitProtocolVersion).mockResolvedValueOnce({
      ref: `protocol_version:${v1Uuid}@2`,
      study: `study:${studyUuid}@1`,
      number: 1,
      parent_version: null,
      status: "InReview",
      content_hash: "hash-v1",
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    });
    await wrapper.find("#btn-submit-protocol-version").trigger("click");
    await flushPromises();
    expect(
      wrapper.find("[data-testid='selected-protocol-status']").text()
    ).toBe("InReview");

    // 5. Sign & approve protocol version via step-up modal
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "sig-approve-v1",
    });
    currentStatus = "Approved";
    vi.mocked(kernelApi.approveProtocolVersion).mockResolvedValue({
      ref: `protocol_version:${v1Uuid}@3`,
      study: `study:${studyUuid}@1`,
      number: 1,
      parent_version: null,
      status: "Approved",
      content_hash: "hash-v1",
      amendment_intent: null,
      approval_attestation:
        "attestation:01950000-0000-7000-8000-000000000099@1",
      withdrawal_reason: null,
      mask: null,
    });

    await wrapper.find("#btn-approve-protocol-version").trigger("click");
    await flushPromises();
    await wrapper.find("#sig-password").setValue("Password123!");
    await wrapper.find("#sig-reason").setValue("APPROVAL");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: `/api/v1/kernel/protocol-versions/protocol_version:${v1Uuid}/approve`,
        semanticAction: PROTOCOL_VERSION_APPROVE_SEMANTIC_ACTION,
      })
    );
    expect(kernelApi.approveProtocolVersion).toHaveBeenCalledWith(
      expect.objectContaining({
        meaning: "Approved",
        sigToken: "sig-approve-v1",
      })
    );

    // 6. Fork amendment draft from Approved version
    vi.mocked(kernelApi.forkProtocolVersion).mockResolvedValue({
      ref: `protocol_version:${v2Uuid}@1`,
      study: `study:${studyUuid}@1`,
      number: 2,
      parent_version: `protocol_version:${v1Uuid}@3`,
      status: "Draft",
      content_hash: null,
      amendment_intent: "Add safety check",
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    });
    await wrapper.find("#btn-toggle-amendment-form").trigger("click");
    await wrapper.find("#amendment-intent").setValue("Add safety check");
    await wrapper.find("#amendment-rationale").setValue("DSMB recommendation");
    await wrapper.find("#amendment-summary").setValue("Added standing BP");
    await wrapper.find("#amendment-elements").setValue("VS");
    await wrapper.find("#protocol-amendment-form").trigger("submit.prevent");
    await flushPromises();

    expect(kernelApi.forkProtocolVersion).toHaveBeenCalledWith(
      expect.objectContaining({
        intent: "Add safety check",
        impactedElements: ["VS"],
      })
    );
  });

  it("orchestrates SiteActivationPanel kernel site linking, approval evidence, and protocol effectivity (@req:PRD-CTMS-005 @req:PRD-SSU-003)", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["sponsor_dm"];
    auth.assignedStudies = ["STUDY-201"];
    auth.assignedSites = ["SITE-201"];

    const studyUuid = "01950000-0000-7000-8000-000000000010";
    const siteUuid = "01950000-0000-7000-8000-000000000071";
    const studySiteUuid = "01950000-0000-7000-8000-000000000072";
    const artifactUuid = "01950000-0000-7000-8000-000000000073";
    const artifactVerUuid = "01950000-0000-7000-8000-000000000074";
    const v1Uuid = "01950000-0000-7000-8000-000000000021";
    const effUuid = "01950000-0000-7000-8000-000000000075";

    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue({
      study_id: "STUDY-201",
      site_id: "SITE-201",
      milestone: "SITE_ACTIVATION",
      is_complete: true,
      is_activated: false,
      activated_at: null,
      activated_by: null,
      missing_documents: [],
    });

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "STUDY-201",
        title: "Founder Trial",
        exact_code: true,
      },
    ]);

    let linkedSites: Awaited<ReturnType<typeof kernelApi.listStudySites>> = [];
    let recordedArtifacts: Awaited<
      ReturnType<typeof kernelApi.listStudyArtifacts>
    > = [];
    vi.mocked(kernelApi.listStudySites).mockImplementation(
      async () => linkedSites
    );
    vi.mocked(kernelApi.listStudyArtifacts).mockImplementation(
      async () => recordedArtifacts
    );
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      {
        ref: `protocol_version:${v1Uuid}@3`,
        study: `study:${studyUuid}@1`,
        number: 1,
        parent_version: null,
        status: "Approved",
        content_hash: "hash-v1",
        amendment_intent: null,
        approval_attestation:
          "attestation:01950000-0000-7000-8000-000000000099@1",
        withdrawal_reason: null,
        mask: null,
      },
    ]);

    const router = makeRouter();
    const wrapper = mount(SiteActivationPanel, {
      props: { studyCode: "STUDY-201", siteId: "SITE-201" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // 1. Register & link site
    vi.mocked(kernelApi.registerSite).mockResolvedValue({
      ref: `site:${siteUuid}@1`,
      site_code: "SITE-201",
      name: "SITE-201 Clinical Research Center",
      country: "US",
      institution_ref: "organization:01950000-0000-7000-8000-000000000079@1",
      institution_code: "INST-SITE-201",
      institution_name: "SITE-201 Medical Institution",
    });
    const linkedSummary = {
      ref: `study_site:${studySiteUuid}@1`,
      study_ref: `study:${studyUuid}@1`,
      study_code: "STUDY-201",
      site_ref: `site:${siteUuid}@1`,
      site_code: "SITE-201",
      site_name: "SITE-201 Clinical Research Center",
      country: "US",
      institution_ref: "organization:01950000-0000-7000-8000-000000000079@1",
      institution_name: "SITE-201 Medical Institution",
      status: "Pending",
      effective_versions: [] as {
        protocol_version_ref: string;
        protocol_version_number: number;
        scope_level: string;
      }[],
    };
    linkedSites = [linkedSummary];
    vi.mocked(kernelApi.linkSiteToStudy).mockResolvedValue(linkedSummary);

    await wrapper.find("#btn-kernel-link-site").trigger("click");
    await flushPromises();
    expect(kernelApi.registerSite).toHaveBeenCalled();
    expect(kernelApi.linkSiteToStudy).toHaveBeenCalled();

    // 2. Record EthicsApproval evidence
    const createdArtifact = {
      ref: `artifact:${artifactUuid}@1`,
      kind: "EthicsApproval",
      study_ref: `study:${studyUuid}@1`,
      title: "IRB / Ethics approval letter",
      latest_version: {
        ref: `artifact_version:${artifactVerUuid}@1`,
        number: 1,
        content_hash: "arthash",
        size_bytes: 64,
        media_type: "text/plain",
        storage_ref: "inline:sha256:arthash",
        recorded_at: "2026-11-22T10:00:00Z",
      },
      versions: [],
    };
    recordedArtifacts = [createdArtifact];
    vi.mocked(kernelApi.registerArtifact).mockResolvedValue(createdArtifact);

    await wrapper.find("#btn-kernel-register-artifact").trigger("click");
    await flushPromises();
    expect(kernelApi.registerArtifact).toHaveBeenCalledWith(
      expect.objectContaining({ kind: "EthicsApproval" })
    );

    // 3. Declare & mark protocol effective at site
    vi.mocked(kernelApi.declareEffectivity).mockResolvedValue({
      ref: `protocol_effectivity:${effUuid}@1`,
      study_ref: `study:${studyUuid}@1`,
      protocol_version_ref: `protocol_version:${v1Uuid}@3`,
      protocol_version_number: 1,
      scope_level: "Site",
      scope_country: null,
      scope_study_site_ref: `study_site:${studySiteUuid}@1`,
      status: "Planned",
      status_since: "2026-11-22T10:01:00Z",
      evidence_version_refs: [],
    });
    linkedSites = [
      {
        ...linkedSummary,
        effective_versions: [
          {
            protocol_version_ref: `protocol_version:${v1Uuid}@3`,
            protocol_version_number: 1,
            scope_level: "Site",
          },
        ],
      },
    ];
    vi.mocked(kernelApi.changeEffectivityStatus).mockResolvedValue({
      ref: `protocol_effectivity:${effUuid}@2`,
      study_ref: `study:${studyUuid}@1`,
      protocol_version_ref: `protocol_version:${v1Uuid}@3`,
      protocol_version_number: 1,
      scope_level: "Site",
      scope_country: null,
      scope_study_site_ref: `study_site:${studySiteUuid}@1`,
      status: "Effective",
      status_since: "2026-11-22T10:02:00Z",
      evidence_version_refs: [`artifact_version:${artifactVerUuid}@1`],
    });

    await wrapper.find("#btn-kernel-make-effective").trigger("click");
    await flushPromises();
    expect(kernelApi.declareEffectivity).toHaveBeenCalled();
    expect(kernelApi.changeEffectivityStatus).toHaveBeenCalledWith(
      expect.objectContaining({
        status: "Effective",
        evidenceVersionRefs: [`artifact_version:${artifactVerUuid}@1`],
      })
    );
    expect(
      wrapper.find("[data-testid='kernel-effective-versions']").text()
    ).toContain("Version 1 effective at SITE-201");
    expect(ctmsApi.listSiteMilestones).toBeDefined();
  });

  it("runs on-demand study validation and triages findings through investigate, classify, signed accept/explain, and reopen (@req:PRD-CRF-004 @req:PRD-SYS-001)", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["data_manager"];
    auth.user = {
      id: "user-dm-1",
      username: "dm@cadence.example",
      email: "dm@cadence.example",
      firstName: "Devon",
      lastName: "Manager",
      tenantId: "tenant-201",
    };

    const studyUuid = "01950000-0000-7000-8000-000000000010";
    const findingUuid = "01950000-0000-7000-8000-000000000081";

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "STUDY-201",
        title: "Founder Trial",
        exact_code: true,
      },
    ]);
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      study_ref: `study:${studyUuid}@1`,
      profile_ref: null,
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });

    const baseFinding = {
      ref: `validation_finding:${findingUuid}@1`,
      id: findingUuid,
      stage: "Collection",
      status: "Open",
      severity: "Warning",
      rule_pack_key: "study-201-checks",
      rule_id: "VS_SYSBP_MAX",
      rule_version: 1,
      message: "Systolic BP exceeds 220 mm[Hg].",
      remediation_guidance: "Verify cuff reading.",
      target_kind: "Record",
      dataset: "VS",
      record_key: "01950000-0000-7000-8000-000000000095",
      variable: "SYSBP",
      classification: null as string | null,
      rationale: null as string | null,
      reviewer_explanation: null as string | null,
      detected_at: "2026-11-22T10:05:00Z",
      last_seen_at: "2026-11-22T10:05:00Z",
      resolved_at: null,
      observed_values: [],
    };
    let findingsList = [baseFinding];
    vi.mocked(kernelApi.listStudyValidationFindings).mockImplementation(
      async () => ({
        total: findingsList.length,
        findings: findingsList,
      })
    );

    const makeDetail = (overrides: Partial<typeof baseFinding>) => ({
      finding: { ...baseFinding, ...overrides },
      explanation: {
        subject: `validation_finding:${findingUuid}@1`,
        claim: "Rule VS_SYSBP_MAX raised Warning.",
        producer: "ValidationLedger",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    });

    vi.mocked(kernelApi.getValidationFinding).mockResolvedValue(makeDetail({}));
    vi.mocked(kernelApi.runStudyValidation).mockResolvedValue({
      run_ref: "validation_run:01950000-0000-7000-8000-000000000089@1",
      detected: 1,
      new: 1,
      resolved: 0,
      findings: [baseFinding],
    });

    const router = makeRouter();
    const wrapper = mount(ValidationSection, {
      props: { studyCode: "STUDY-201", canManageRules: true },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // Verify Explained filter option exists
    expect(
      wrapper
        .find("#validation-status-filter option[value='Explained']")
        .exists()
    ).toBe(true);

    // 1. Run on-demand study validation
    await wrapper.find("#btn-run-study-validation").trigger("click");
    await flushPromises();
    expect(kernelApi.runStudyValidation).toHaveBeenCalled();
    expect(
      wrapper.find("[data-testid='study-validation-outcome']").text()
    ).toContain("Validation run complete: 1 detected");

    // 2. Inspect finding & start investigation
    await wrapper
      .find("[data-testid='finding-VS_SYSBP_MAX-Open'] button")
      .trigger("click");
    await flushPromises();

    await wrapper
      .find(`#finding-triage-reason-${findingUuid}`)
      .setValue("Confirmed transient white-coat hypertension with site PI");
    vi.mocked(kernelApi.investigateFinding).mockResolvedValue(
      makeDetail({ status: "Investigating" })
    );
    await wrapper.find("#btn-investigate-finding").trigger("click");
    await flushPromises();
    expect(kernelApi.investigateFinding).toHaveBeenCalled();

    // 3. Classify finding as NotAnIssue
    await wrapper
      .find(`#finding-classification-${findingUuid}`)
      .setValue("NotAnIssue");
    vi.mocked(kernelApi.classifyFinding).mockResolvedValue(
      makeDetail({ status: "Investigating", classification: "NotAnIssue" })
    );
    await wrapper.find("#btn-classify-finding").trigger("click");
    await flushPromises();
    expect(kernelApi.classifyFinding).toHaveBeenCalledWith(
      expect.objectContaining({ classification: "NotAnIssue" })
    );

    // 4. Sign & accept finding (Responsibility meaning)
    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-accept-1",
    });
    findingsList = [{ ...baseFinding, status: "Accepted" }];
    vi.mocked(kernelApi.acceptFinding).mockResolvedValue(
      makeDetail({
        status: "Accepted",
        classification: "NotAnIssue",
        rationale: "Confirmed transient white-coat hypertension with site PI",
      })
    );
    await wrapper.find("#btn-accept-finding").trigger("click");
    await flushPromises();
    await wrapper.find("#sig-password").setValue("Password123!");
    await wrapper.find("#sig-reason").setValue("CLINICAL_QC");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        semanticAction: FINDING_ACCEPT_SEMANTIC_ACTION,
      })
    );
    expect(kernelApi.acceptFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        meaning: "Responsibility",
        sigToken: "sig-accept-1",
      })
    );

    // 5. Reopen finding -> Open -> Sign reviewer explanation (Approved meaning)
    findingsList = [{ ...baseFinding, status: "Open" }];
    vi.mocked(kernelApi.reopenFinding).mockResolvedValue(
      makeDetail({ status: "Open", classification: "NotAnIssue" })
    );
    await wrapper.find("#btn-reopen-finding").trigger("click");
    await flushPromises();
    expect(kernelApi.reopenFinding).toHaveBeenCalled();

    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-explain-1",
    });
    findingsList = [{ ...baseFinding, status: "Explained" }];
    vi.mocked(kernelApi.explainFinding).mockResolvedValue(
      makeDetail({
        status: "Explained",
        classification: "NotAnIssue",
        reviewer_explanation:
          "Confirmed transient white-coat hypertension with site PI",
      })
    );
    await wrapper.find("#btn-explain-finding-sign").trigger("click");
    await flushPromises();
    await wrapper.find("#sig-password").setValue("Password123!");
    await wrapper.find("#sig-reason").setValue("APPROVAL");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        semanticAction: FINDING_EXPLAIN_SEMANTIC_ACTION,
      })
    );
    expect(kernelApi.explainFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        meaning: "Approved",
        sigToken: "sig-explain-1",
      })
    );
  });
});
