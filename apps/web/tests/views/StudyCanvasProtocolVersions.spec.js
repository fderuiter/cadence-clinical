import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { kernelGovernanceApi } from "../../src/api/kernelGovernanceApi";
import { etmfService } from "../../src/api/etmf";
import { ApiError } from "../../src/api/client";
import { useAuthStore } from "../../src/stores/auth";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    verifySignature: vi.fn(),
    signDocument: vi.fn(),
  },
}));

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSubjectCounts: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(),
    listProtocolVersions: vi.fn(),
    getProtocolVersion: vi.fn(),
    getCommands: vi.fn(),
    trace: vi.fn(),
    dependents: vi.fn(),
    amendmentImpact: vi.fn(),
    simulate: vi.fn(),
    listProtocolTemplates: vi.fn(async () => []),
    getProtocolDesignSummary: vi.fn(async () => null),
  },
  isMasked: (object) => object.mask !== null,
  describeKernelError: (err) => err?.message ?? String(err),
}));

vi.mock("../../src/api/kernelGovernanceApi", () => ({
  kernelGovernanceApi: {
    submitProtocolVersionForReview: vi.fn(),
    returnProtocolVersionToDraft: vi.fn(),
    approveProtocolVersion: vi.fn(),
    listStudyArtifacts: vi.fn(async () => []),
    registerStudyArtifact: vi.fn(),
    listStudyEffectivities: vi.fn(async () => []),
    declareProtocolEffectivity: vi.fn(),
    changeEffectivityStatus: vi.fn(),
    listStudySites: vi.fn(async () => []),
  },
  PROTOCOL_APPROVE_SEMANTIC_ACTION: "kernel.protocol_version.approve",
  protocolVersionApproveAction: (versionRef) => {
    const clean = String(versionRef ?? "").split("@")[0];
    return `/api/v1/kernel/protocol-versions/${clean}/approve`;
  },
}));

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
const V1_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b1";
const V2_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b2";
const V1 = `protocol_version:${V1_ID}@3`;
const V2 = `protocol_version:${V2_ID}@1`;

const version = (overrides) => ({
  ref: null,
  study: STUDY,
  number: null,
  parent_version: null,
  status: null,
  content_hash: null,
  amendment_intent: null,
  approval_attestation: null,
  withdrawal_reason: null,
  mask: null,
  ...overrides,
});

const FIRST = version({
  ref: V1,
  number: 1,
  status: "Approved",
  content_hash: "sha256:aa",
});
const AMENDMENT = version({
  ref: V2,
  number: 2,
  status: "Draft",
  // The parent is named without its revision; the list still finds it.
  parent_version: `protocol_version:${V1_ID}`,
  amendment_intent: "Add a PK visit",
});
const HIDDEN = version({ mask: "NotPermitted" });

describe("Clinical Canvas protocol versions and amendment impact (ADR-2540, K-PROT-2)", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 0,
      counts: {},
    });
    for (const method of Object.values(kernelApi))
      vi.mocked(method).mockReset();
    vi.mocked(kernelApi.listProposals).mockResolvedValue([]);
    vi.mocked(kernelApi.listProtocolTemplates).mockResolvedValue([]);
    vi.mocked(kernelGovernanceApi.listStudyArtifacts).mockResolvedValue([]);
    vi.mocked(kernelGovernanceApi.listStudyEffectivities).mockResolvedValue([]);
    vi.mocked(kernelGovernanceApi.listStudySites).mockResolvedValue([]);
    vi.mocked(etmfService.verifySignature).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY,
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      AMENDMENT,
      HIDDEN,
      FIRST,
    ]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      ref === V2 ? AMENDMENT : FIRST
    );
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(path) {
    await router.push(path);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  it("lists the study's versions from the kernel, a hidden one in place", async () => {
    await open("/studies/CAD-101?stage=design");
    await wrapper.find("#nav-protocol-versions").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(kernelApi.search).toHaveBeenCalledWith({
      text: "CAD-101",
      kinds: ["study"],
      limit: 5,
    });
    expect(kernelApi.listProtocolVersions).toHaveBeenCalledWith(STUDY);
    expect(wrapper.find("#protocol-versions-status").text()).toBe(
      "3 versions, 1 of them hidden from you."
    );
    const rows = wrapper.findAll("#protocol-versions-section tbody tr");
    expect(
      rows.map((r) => r.findAll("th, td").map((cell) => cell.text().trim()))
    ).toEqual([
      ["2", "Draft", "Version 1", "Add a PK visit"],
      ["A version you may not see (not permitted)"],
      ["1", "Approved", "Nothing (first version)", "None recorded"],
    ]);
  });

  it("says in plain terms when the study wasn't found or isn't visible", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);

    await open("/studies/CAD-101?section=protocol-versions");

    expect(wrapper.find("#protocol-versions-status").text()).toBe(
      "Study CAD-101 wasn't found or isn't visible to your account."
    );
    expect(kernelApi.listProtocolVersions).not.toHaveBeenCalled();
  });

  it("presents a plain status message and collapsible support details when versions cannot be read", async () => {
    vi.mocked(kernelApi.listProtocolVersions).mockRejectedValue(
      new ApiError("Not visible", 403)
    );

    await open("/studies/CAD-101?section=protocol-versions");

    expect(wrapper.find("#protocol-versions-status").text()).toContain(
      "Protocol versions couldn't be loaded right now."
    );
    expect(wrapper.find("#protocol-versions-section details").text()).toContain(
      "Not visible"
    );
  });

  it("shows an amendment in the inspector, and Impact lists what it changes and what depends on the version it amends", async () => {
    vi.mocked(kernelApi.amendmentImpact).mockResolvedValue({
      amended: V2,
      parent: `protocol_version:${V1_ID}`,
      impacted_elements: ["schedule_of_activities"],
      changed_elements: [
        {
          element_id: "act-pk",
          instance_type: "Activity",
          change: "Added",
          properties: [],
        },
        {
          element_id: "enc-v3",
          instance_type: "Encounter",
          change: "Changed",
          properties: ["/window/upper"],
        },
      ],
      dependents: {
        start: V1,
        depth: 10,
        groups: [
          {
            kind: "participant",
            count: 2,
            objects: [
              {
                handle: "masked:1",
                kind: "participant",
                ref: null,
                mask: "NotPermitted",
                distance: 1,
              },
            ],
          },
        ],
        total: 2,
        truncated: false,
      },
    });
    await open("/studies/CAD-101?section=protocol-versions");

    await wrapper.find("#protocol-version-2").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(V2);
    expect(kernelApi.getProtocolVersion).toHaveBeenCalledWith(V2);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("Version 2");
    expect(wrapper.find("#inspector-version-status").text()).toBe("Draft");

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(kernelApi.amendmentImpact).toHaveBeenCalledWith(V2);
    expect(kernelApi.dependents).not.toHaveBeenCalled();
    expect(wrapper.find("#kernel-impact-title").text()).toBe(
      "What this amendment changes"
    );
    const changes = wrapper.findAll("#kernel-amendment-changes li");
    expect(changes.map((c) => c.text().replace(/\s+/g, " "))).toEqual([
      "Added activity act-pk",
      "Changed encounter enc-v3 (Window · Upper)",
    ]);
    expect(wrapper.find("#kernel-impact-total").text()).toBe(
      "2 dependent objects"
    );
    expect(wrapper.find("#kernel-impact").text()).toContain(
      "Hidden participant (not permitted)"
    );
  });

  it("uses the plain dependents read for a study's first version, which amends nothing", async () => {
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      start: V1,
      depth: 10,
      groups: [],
      total: 0,
      truncated: false,
    });
    await open(`/studies/CAD-101?section=protocol-versions&node=${V1}`);

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(kernelApi.dependents).toHaveBeenCalledWith(V1);
    expect(kernelApi.amendmentImpact).not.toHaveBeenCalled();
    expect(wrapper.find("#kernel-impact-title").text()).toBe(
      "What depends on this"
    );
  });

  it("traces a protocol version through the kernel's lineage", async () => {
    vi.mocked(kernelApi.trace).mockResolvedValue({
      nodes: [],
      edges: [],
      truncated: false,
    });
    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    await wrapper.find("#verb-trace").trigger("click");
    await flushPromises();

    expect(kernelApi.trace).toHaveBeenCalledWith(V2);
  });

  it("presents a plain status message and support details when a version named in the URL couldn't be read", async () => {
    vi.mocked(kernelApi.getProtocolVersion).mockRejectedValue(
      new ApiError(`${V2} does not exist in your tenant.`, 404)
    );

    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    expect(wrapper.find("#inspector-version-error").text()).toContain(
      "This protocol version couldn't be loaded."
    );
    expect(wrapper.find("#canvas-inspector details").text()).toContain(
      "does not exist in your tenant"
    );
  });

  it("enables Simulate on protocol versions, renders hash seals and matrix gap findings, and resets on version switch", async () => {
    vi.mocked(kernelApi.simulate).mockResolvedValue({
      scope: STUDY,
      as_of: {
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
      },
      candidate: { source: "stored", version_ref: V2 },
      engine_versions: [],
      input_hash: "sha256:input-v2",
      result_hash: "sha256:result-v2",
      structural_diff: [],
      schedule_changes: [],
      masked: [],
      matrix_gaps: [
        {
          kind: "UnboundCollection",
          severity: "Warning",
          json_pointer_path: "/schedule/activities/2",
          description: "PK Blood Draw has no scheduled visit window.",
          recommendation: "Bind PK Blood Draw to Visit 3.",
        },
      ],
    });

    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    const simBtn = wrapper.find("#verb-simulate");
    expect(simBtn.attributes("aria-disabled")).toBeUndefined();

    await simBtn.trigger("click");
    await flushPromises();

    expect(kernelApi.simulate).toHaveBeenCalledWith({
      storedVersionRef: V2,
    });
    expect(wrapper.find("#kernel-simulate-hashes").text()).toContain(
      "sha256:input-v2"
    );
    expect(wrapper.find("#kernel-simulate-hashes").text()).toContain(
      "sha256:result-v2"
    );
    expect(wrapper.find("#kernel-simulate-gaps").text()).toContain(
      "PK Blood Draw has no scheduled visit window."
    );

    // Switching to Version 1 resets the active verb to Details and clears previous simulation results.
    await wrapper.find("#protocol-version-1").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(wrapper.find("#verb-details").attributes("aria-pressed")).toBe(
      "true"
    );
    expect(wrapper.find("#kernel-simulate").exists()).toBe(false);
  });

  it("ignores stale in-flight simulation responses when switching versions and surfaces kernel simulation errors", async () => {
    let resolveV2Sim;
    vi.mocked(kernelApi.simulate)
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveV2Sim = resolve;
          })
      )
      .mockRejectedValueOnce(
        new ApiError("Base protocol version is not frozen.", 422)
      );

    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);
    await wrapper.find("#verb-simulate").trigger("click");
    await flushPromises();

    // Switch to V1 before V2's simulation finishes.
    await wrapper.find("#protocol-version-1").trigger("click");
    await router.isReady();
    await flushPromises();

    resolveV2Sim({
      scope: STUDY,
      as_of: {
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
      },
      candidate: { source: "stored", version_ref: V2 },
      engine_versions: [],
      input_hash: "sha256:stale-v2",
      result_hash: "sha256:stale-v2-out",
      structural_diff: [],
      schedule_changes: [],
      masked: [],
      matrix_gaps: [],
    });
    await flushPromises();

    expect(wrapper.text()).not.toContain("sha256:stale-v2");

    await wrapper.find("#verb-simulate").trigger("click");
    await flushPromises();

    expect(kernelApi.simulate).toHaveBeenLastCalledWith({
      storedVersionRef: V1,
    });
    expect(wrapper.find("#kernel-simulate-error").text()).toBe(
      "Base protocol version is not frozen."
    );

    // Simulation error Explain opens #kernel-explain; Retry re-runs simulation
    await wrapper
      .find('#kernel-simulate [data-testid="canvas-error-explain"]')
      .trigger("click");
    await flushPromises();
    expect(wrapper.find("#kernel-explain").exists()).toBe(true);

    vi.mocked(kernelApi.simulate)
      .mockRejectedValueOnce(
        new ApiError("Base protocol version is not frozen.", 409)
      )
      .mockResolvedValueOnce({
        input_hash: "sha256:v1-retry",
        result_hash: "sha256:v1-out",
        structural_diff: [],
        schedule_changes: [],
        masked: [],
        matrix_gaps: [],
      });
    await wrapper.find("#verb-simulate").trigger("click");
    await flushPromises();
    expect(wrapper.find("#kernel-simulate-error").exists()).toBe(true);
    await wrapper
      .find('#kernel-simulate [data-testid="canvas-error-retry"]')
      .trigger("click");
    await flushPromises();
    expect(wrapper.find("#kernel-simulate-error").exists()).toBe(false);

    // Protocol version load error Retry & Explain
    const missingRef = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-000000000999";
    vi.mocked(kernelApi.getProtocolVersion)
      .mockRejectedValueOnce(new ApiError("Transient read timeout", 503))
      .mockResolvedValueOnce(
        version({ ref: `${missingRef}@1`, number: 1, status: "Approved" })
      );
    await router.push(
      `/studies/CAD-101?stage=design&section=protocol-versions&node=${missingRef}`
    );
    await flushPromises();

    const versionErr = wrapper.find("#inspector-version-error");
    expect(versionErr.exists()).toBe(true);
    await versionErr
      .find('[data-testid="canvas-error-explain"]')
      .trigger("click");
    await flushPromises();
    expect(wrapper.find("#kernel-explain").exists()).toBe(true);

    await versionErr
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await flushPromises();
    expect(wrapper.find("#inspector-version-error").exists()).toBe(false);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("Version 1");
  });

  it("drives Draft -> InReview -> Part 11 Approved -> Artifact registration -> Effectivity Effective and refreshes study schedule options", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = { username: "designer.lead", fullName: "Dr. Elena Vance" };
    auth.rawRoles = ["Sponsor Designer"];

    const FROZEN_HASH =
      "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    const IRB_HASH =
      "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    const ARTIFACT_VER_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000c1";
    const EFFECTIVITY_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000d1";

    let currentV2 = { ...AMENDMENT };
    const artifactsState = [];
    const effectivitiesState = [];

    vi.mocked(kernelApi.listProtocolVersions).mockImplementation(async () => [
      currentV2,
      FIRST,
    ]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      String(ref).startsWith(`protocol_version:${V2_ID}`) ? currentV2 : FIRST
    );
    vi.mocked(kernelGovernanceApi.listStudyArtifacts).mockImplementation(
      async () => [...artifactsState]
    );
    vi.mocked(kernelGovernanceApi.listStudyEffectivities).mockImplementation(
      async () => [...effectivitiesState]
    );

    vi.mocked(
      kernelGovernanceApi.submitProtocolVersionForReview
    ).mockImplementation(async () => {
      currentV2 = {
        ...currentV2,
        status: "InReview",
        content_hash: FROZEN_HASH,
      };
      return currentV2;
    });
    vi.mocked(
      kernelGovernanceApi.returnProtocolVersionToDraft
    ).mockImplementation(async () => {
      currentV2 = { ...currentV2, status: "Draft", content_hash: null };
      return currentV2;
    });
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "step-up-sig-token-60s",
    });
    vi.mocked(kernelGovernanceApi.approveProtocolVersion).mockImplementation(
      async () => {
        currentV2 = {
          ...currentV2,
          status: "Approved",
          approval_attestation:
            "attestation:0192f3c4-5e6a-7b8c-9d0e-0000000000e1",
        };
        return currentV2;
      }
    );
    vi.mocked(kernelGovernanceApi.registerStudyArtifact).mockImplementation(
      async (payload) => {
        const created = {
          artifact_ref: "artifact:0192f3c4-5e6a-7b8c-9d0e-0000000000c0",
          artifact_id: "0192f3c4-5e6a-7b8c-9d0e-0000000000c0",
          artifact_version_ref: `artifact_version:${ARTIFACT_VER_ID}`,
          artifact_version_id: ARTIFACT_VER_ID,
          study_ref: STUDY,
          kind: payload.kind,
          title: payload.title,
          version_number: 1,
          content_hash: payload.contentHash,
          size_bytes: 1,
          media_type: "application/pdf",
          storage_ref: payload.storageRef,
          created_at: "2026-10-15T10:00:00Z",
        };
        artifactsState.push(created);
        return created;
      }
    );
    vi.mocked(
      kernelGovernanceApi.declareProtocolEffectivity
    ).mockImplementation(async (payload) => {
      const declared = {
        effectivity_ref: `effectivity:${EFFECTIVITY_ID}`,
        effectivity_id: EFFECTIVITY_ID,
        study_ref: STUDY,
        protocol_version_ref: `protocol_version:${V2_ID}`,
        protocol_version_number: 2,
        scope_level: payload.scopeLevel,
        country: null,
        study_site_ref: null,
        status: "Pending",
        status_since: "2026-10-15T10:01:00Z",
        evidence_artifact_version_refs: [],
      };
      effectivitiesState.push(declared);
      return declared;
    });
    vi.mocked(kernelGovernanceApi.changeEffectivityStatus).mockImplementation(
      async (payload) => {
        effectivitiesState[0] = {
          ...effectivitiesState[0],
          status: payload.status,
          evidence_artifact_version_refs: payload.evidenceRefs,
        };
        return effectivitiesState[0];
      }
    );

    const canvasStore = useStudyCanvasStore();
    const loadScheduleSpy = vi
      .spyOn(canvasStore, "loadScheduleOptions")
      .mockResolvedValue(null);

    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    // 1. Submit Draft v2 for review -> InReview
    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Freeze amendment v2 for QA review");
    await wrapper.find("#btn-submit-protocol-review").trigger("click");
    await flushPromises();

    expect(
      kernelGovernanceApi.submitProtocolVersionForReview
    ).toHaveBeenCalledWith(
      expect.objectContaining({
        versionRef: V2,
        reason: "Freeze amendment v2 for QA review",
      })
    );
    expect(wrapper.find("#btn-approve-protocol-version").exists()).toBe(true);
    expect(wrapper.find("#btn-return-protocol-draft").exists()).toBe(true);

    // 2. Return to Draft and re-submit for review to verify both transitions
    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Revise visit window wording");
    await wrapper.find("#btn-return-protocol-draft").trigger("click");
    await flushPromises();
    expect(
      kernelGovernanceApi.returnProtocolVersionToDraft
    ).toHaveBeenCalledWith(
      expect.objectContaining({
        versionRef: V2,
        reason: "Revise visit window wording",
      })
    );

    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Re-freeze amendment v2 for approval");
    await wrapper.find("#btn-submit-protocol-review").trigger("click");
    await flushPromises();

    // 3. Sign & approve protocol version via 21 CFR Part 11 step-up modal
    await wrapper.find("#btn-approve-protocol-version").trigger("click");
    await flushPromises();

    expect(wrapper.find("#signature-capture-modal").exists()).toBe(true);
    await wrapper.find("#sig-password").setValue("ClinicalPass!123");
    await wrapper
      .find("#sig-reason")
      .setValue("Approve frozen protocol version for clinical conduct");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        username: "designer.lead",
        action: `/api/v1/kernel/protocol-versions/protocol_version:${V2_ID}/approve`,
        semanticAction: "kernel.protocol_version.approve",
      })
    );
    expect(kernelGovernanceApi.approveProtocolVersion).toHaveBeenCalledWith(
      expect.objectContaining({
        versionRef: V2,
        sigToken: "step-up-sig-token-60s",
        expectedContentHash: FROZEN_HASH,
      })
    );

    // 4. Register EthicsApproval evidence artifact
    await wrapper.find("#protocol-artifact-kind").setValue("EthicsApproval");
    await wrapper
      .find("#protocol-artifact-title")
      .setValue("Central IRB Approval v2.0");
    await wrapper.find("#protocol-artifact-hash").setValue(IRB_HASH);
    await wrapper
      .find("#protocol-artifact-storage")
      .setValue("s3://cadence-etmf/irb-v2.pdf");
    await wrapper
      .find("#protocol-artifact-reason")
      .setValue("Record central IRB approval letter");
    await wrapper.find("#protocol-artifact-form").trigger("submit");
    await flushPromises();

    expect(kernelGovernanceApi.registerStudyArtifact).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: STUDY,
        kind: "EthicsApproval",
        title: "Central IRB Approval v2.0",
        contentHash: IRB_HASH,
      })
    );

    // 5. Declare study-wide effectivity and transition to Effective with IRB evidence
    await wrapper
      .find("#effectivity-declare-reason")
      .setValue("Declare study-wide effectivity for approved v2");
    await wrapper.find("#protocol-declare-effectivity-form").trigger("submit");
    await flushPromises();

    expect(kernelGovernanceApi.declareProtocolEffectivity).toHaveBeenCalledWith(
      expect.objectContaining({
        versionRef: V2,
        scopeLevel: "Study",
        reason: "Declare study-wide effectivity for approved v2",
      })
    );

    const evidenceCheckbox = wrapper.find(
      `#protocol-effectivity-card input[type="checkbox"][value="artifact_version:${ARTIFACT_VER_ID}"]`
    );
    expect(evidenceCheckbox.exists()).toBe(true);
    await evidenceCheckbox.setValue(true);
    await wrapper
      .find(`#effectivity-reason-${EFFECTIVITY_ID}`)
      .setValue("Go live study-wide with IRB approval");
    await wrapper
      .find(`[id="btn-transition-effectivity-${EFFECTIVITY_ID}"]`)
      .trigger("click");
    await flushPromises();

    expect(kernelGovernanceApi.changeEffectivityStatus).toHaveBeenCalledWith(
      expect.objectContaining({
        effectivityRef: `effectivity:${EFFECTIVITY_ID}`,
        status: "Effective",
        evidenceRefs: [`artifact_version:${ARTIFACT_VER_ID}`],
        reason: "Go live study-wide with IRB approval",
      })
    );
    expect(loadScheduleSpy).toHaveBeenCalledWith("CAD-101", { force: true });
  });
});
