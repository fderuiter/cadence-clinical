import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { ref } from "vue";
import { executionApi } from "../../src/api/executionApi";
import { etmfService } from "../../src/api/etmf";
import { useAuthStore } from "../../src/stores/auth";
import { useClinicalStore } from "../../src/stores/clinical";
import { useConsentGating } from "../../src/composables/useConsentGating";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import RtsmSupplySection from "../../src/components/clinical/RtsmSupplySection.vue";

vi.mock("../../src/api/executionApi", async () => {
  const actual = await vi.importActual("../../src/api/executionApi");
  return {
    ...actual,
    executionApi: {
      listSubjects: vi.fn(),
      addSubject: vi.fn(),
      changeSubjectState: vi.fn(),
      getSubjectDetail: vi.fn(),
      recordSubjectConsent: vi.fn(),
      randomizeSubject: vi.fn(),
      getSiteComplianceStatus: vi.fn(),
      activateSite: vi.fn(),
      listRandomizationSchemes: vi.fn(),
      draftRandomizationScheme: vi.fn(),
      approveRandomizationScheme: vi.fn(),
      seedSiteIpKitInventory: vi.fn(),
      getSiteIpKitInventory: vi.fn(),
      listCgtCertificates: vi.fn(),
      listCgtReconciliations: vi.fn(),
      getChainOfCustody: vi.fn(),
      recordCgtApheresis: vi.fn(),
    },
  };
});

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    verifySignature: vi.fn(),
  },
}));

const SUBJECT_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000010";
const SCHEME_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000099";

describe("Clinical Canvas RTSM workflows", () => {
  let pinia;
  let router;
  let wrapper;
  let participants;

  beforeEach(() => {
    localStorage.clear();
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
    participants = [
      {
        id: SUBJECT_UUID,
        subject_id: "101-0001",
        study_id: "CAD-101",
        site_id: "SITE-101",
        status: "ENROLLED",
        active_protocol_version: "1.0",
        enrollment_index: 1,
      },
    ];
    for (const fn of Object.values(executionApi)) {
      if (typeof fn === "function" && "mockReset" in fn) {
        vi.mocked(fn).mockReset();
      }
    }
    vi.mocked(etmfService.verifySignature).mockReset();

    vi.mocked(executionApi.listSubjects).mockImplementation(
      async ({ status, siteId, limit = 50, offset = 0 }) => {
        const matching = participants.filter(
          (p) =>
            (!status || p.status === status) &&
            (!siteId || p.site_id === siteId)
        );
        return {
          items: matching.slice(offset, offset + limit),
          total: matching.length,
          limit,
          offset,
        };
      }
    );
    vi.mocked(executionApi.listCgtCertificates).mockResolvedValue([]);
    vi.mocked(executionApi.listCgtReconciliations).mockResolvedValue([]);
    vi.mocked(executionApi.getSiteIpKitInventory).mockResolvedValue([]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  function signIn({
    id = "usr-crc-1",
    username = "crc.site101",
    roles = ["crc"],
  } = {}) {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id,
      username,
      displayName: username,
      assignedStudies: ["CAD-101"],
      assignedSites: ["SITE-101"],
    };
    auth.rawRoles = roles;
  }

  async function openCanvas(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("drafts a scheme, enforces second-person statistician step-up approval, and seeds ACTIVE_AT_SITE kits with treatment_arm", async () => {
    const schemes = [];
    vi.mocked(executionApi.listRandomizationSchemes).mockImplementation(
      async () => [...schemes]
    );
    vi.mocked(executionApi.draftRandomizationScheme).mockImplementation(
      async (payload) => {
        const created = {
          id: SCHEME_UUID,
          study_id: payload.study_id,
          version: 1,
          method: payload.method,
          status: "DRAFT",
          arms: payload.arms,
          stratification_factors: payload.stratification_factors || {},
          block_sizes: payload.block_sizes || [4],
          slots_per_stratum: payload.slots_per_stratum || 8,
          preferred_arm_probability: payload.preferred_arm_probability || 0,
          requires_kit: payload.requires_kit,
          list_size: 0,
          configuration_hash: "hash-1",
          drafted_by: "usr-stat-author",
          drafted_at: new Date().toISOString(),
          approved_by: null,
          approved_at: null,
          retired_at: null,
        };
        schemes.splice(0, schemes.length, created);
        return created;
      }
    );

    // 1. Authoring statistician drafts scheme; self-approval is disabled.
    signIn({
      id: "usr-stat-author",
      username: "stat.author",
      roles: ["unblinded_statistician"],
    });
    wrapper = mount(RtsmSupplySection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    await wrapper.find("#btn-toggle-draft-scheme").trigger("click");
    await wrapper.find("#scheme-arms").setValue("PLACEBO:1, ACTIVE:1");
    await wrapper
      .find("#scheme-strat-factors")
      .setValue("age_group=<65,>=65; stage=II,III");
    await wrapper
      .find("#scheme-draft-reason")
      .setValue("Initial SAP randomization list specification");
    await wrapper.find("#rtsm-draft-scheme-form").trigger("submit");
    await flushPromises();

    expect(executionApi.draftRandomizationScheme).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CAD-101",
        method: "permuted_block",
        arms: { PLACEBO: 1, ACTIVE: 1 },
        stratification_factors: {
          age_group: ["<65", ">=65"],
          stage: ["II", "III"],
        },
        requires_kit: true,
      }),
      "Initial SAP randomization list specification"
    );
    expect(
      wrapper.find('[data-testid="btn-approve-scheme"]').element.disabled
    ).toBe(true);
    expect(
      wrapper.find('[data-testid="self-approval-blocked-note"]').exists()
    ).toBe(true);

    // 2. Second statistician approves with fresh single-use X-Sig-Token (execution.form.approve).
    signIn({
      id: "usr-stat-approver",
      username: "stat.approver",
      roles: ["unblinded_statistician"],
    });
    await flushPromises();

    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "sig-scheme-approve-001",
    });
    vi.mocked(executionApi.approveRandomizationScheme).mockImplementation(
      async (id) => {
        schemes[0] = {
          ...schemes[0],
          id,
          status: "APPROVED",
          approved_by: "usr-stat-approver",
        };
        return schemes[0];
      }
    );

    const approveBtn = wrapper.find('[data-testid="btn-approve-scheme"]');
    expect(approveBtn.element.disabled).toBe(false);
    await approveBtn.trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#sig-reason")
      .setValue(
        "Independent second-person verification and approval of randomization scheme"
      );
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        username: "stat.approver",
        action: `/api/v1/execution/randomization/schemes/${SCHEME_UUID}/approve`,
        semanticAction: "execution.form.approve",
      })
    );
    expect(executionApi.approveRandomizationScheme).toHaveBeenCalledWith(
      SCHEME_UUID,
      "Independent second-person verification and approval of randomization scheme",
      "sig-scheme-approve-001"
    );

    // 3. Sponsor Data Manager seeds IP kit inventory as ACTIVE_AT_SITE with treatment_arm.
    signIn({
      id: "usr-dm-1",
      username: "dm.lead",
      roles: ["sponsor_dm"],
    });
    await flushPromises();
    vi.mocked(executionApi.seedSiteIpKitInventory).mockResolvedValue({
      seeded: 1,
    });

    await wrapper.find("#seed-kit-number").setValue("KIT-1001");
    await wrapper.find("#seed-lot-number").setValue("LOT-ALPHA");
    await wrapper.find("#seed-treatment-arm").setValue("ACTIVE");
    const nextYear = `${new Date().getUTCFullYear() + 1}-12-31`;
    await wrapper.find("#seed-expiry").setValue(nextYear);
    await wrapper.find("#seed-reason").setValue("Initial depot release");
    await wrapper.find("#rtsm-seed-kits-form").trigger("submit");
    await flushPromises();

    expect(executionApi.seedSiteIpKitInventory).toHaveBeenCalledWith(
      {
        study_id: "CAD-101",
        site_id: "SITE-101",
        kits: [
          {
            kit_number: "KIT-1001",
            lot_number: "LOT-ALPHA",
            expiration_date: `${nextYear}T00:00:00Z`,
            status: "ACTIVE_AT_SITE",
            treatment_arm: "ACTIVE",
          },
        ],
      },
      "Initial depot release"
    );
  });

  it("records signed informed consent via CanvasInspector and useConsentGating against Execution", async () => {
    signIn({ roles: ["crc"] });
    vi.mocked(executionApi.recordSubjectConsent).mockResolvedValue({
      id: "consent-1",
      subject_id: "101-0001",
      study_id: "CAD-101",
      version_tag: "2.0",
      version_index: 2,
      icf_signed: true,
      icf_signed_date: new Date().toISOString(),
      requires_reconsent: false,
      is_paper_override: false,
      version: 1,
    });

    await openCanvas(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    await wrapper.find("#btn-toggle-consent-form").trigger("click");
    await wrapper.find("#consent-version-tag").setValue("2.0");
    await wrapper.find("#consent-version-index").setValue("2");
    await wrapper
      .find("#consent-reason")
      .setValue("Signed initial ICF prior to randomization");
    await wrapper.find("#inspector-consent-form").trigger("submit");
    await flushPromises();

    expect(executionApi.recordSubjectConsent).toHaveBeenCalledWith(
      SUBJECT_UUID,
      expect.objectContaining({
        protocol_version: {
          study_id: "CAD-101",
          version_tag: "2.0",
          version_index: 2,
        },
        icf_signed: true,
        requires_reconsent: false,
        is_paper_override: false,
      }),
      "Signed initial ICF prior to randomization"
    );

    // Also verify useConsentGating outside demo mode calls recordSubjectConsent and unlocks the gate.
    const clinical = useClinicalStore();
    clinical.activeStudyId = "CAD-101";
    clinical.subjects = [
      {
        id: "101-0001",
        status: "RECONSENT_REQUIRED",
        active_protocol_version: "1.0",
        isGated: true,
      },
    ];
    const gating = useConsentGating(ref("101-0001"));
    gating.openEconsentModal();
    await gating.handleCompleteReconsent("ECONSENT", clinical, {
      sigToken: "sig-consent-1",
      signingReason: "Participant signed protocol amendment 2.0",
    });

    expect(executionApi.recordSubjectConsent).toHaveBeenCalledWith(
      "101-0001",
      expect.objectContaining({
        protocol_version: expect.objectContaining({
          study_id: "CAD-101",
          version_tag: "2.0.0",
        }),
        icf_signed: true,
        requires_reconsent: false,
      }),
      "Participant signed protocol amendment 2.0"
    );
    expect(gating.isReconsentGated.value).toBe(false);
    expect(gating.showEconsentModal.value).toBe(false);
  });

  it("withholds RANDOMIZED from generic state select, dispatches step-up randomization with stratification factors, and keeps blinded coordinator blind", async () => {
    signIn({ roles: ["crc"] });
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "sig-rand-001",
    });
    vi.mocked(executionApi.randomizeSubject).mockImplementation(async () => {
      participants[0].status = "RANDOMIZED";
      return {
        randomization_id: "rand-001",
        subject_id: "101-0001",
        study_id: "CAD-101",
        scheme_version: 1,
        stratum_key: "age_group=>=65|stage=III",
        kit_number: "KIT-1001",
        status: "RANDOMIZED",
        randomized_at: new Date().toISOString(),
      };
    });
    // Blinded GetSubjectDetail response omits treatment_group and investigational_product_id.
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      id: SUBJECT_UUID,
      subject_id: "101-0001",
      study_id: "CAD-101",
      site_id: "SITE-101",
      status: "RANDOMIZED",
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group: null,
      randomization_seed: null,
      investigational_product_id: null,
    });

    await openCanvas(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    // Generic state dropdown does NOT include Randomized.
    const genericTargets = wrapper
      .findAll("#state-change-target option")
      .filter((o) => o.attributes("value"))
      .map((o) => o.text());
    expect(genericTargets).toEqual(["Re-consent required", "Withdrawn"]);

    // Coordinator enters stratification factors and signs step-up randomization.
    await wrapper.find("#btn-add-strat-factor").trigger("click");
    await wrapper.find("#strat-factor-key-0").setValue("age_group");
    await wrapper.find("#strat-factor-val-0").setValue(">=65");
    await wrapper.find("#btn-add-strat-factor").trigger("click");
    await wrapper.find("#strat-factor-key-1").setValue("stage");
    await wrapper.find("#strat-factor-val-1").setValue("III");
    await wrapper
      .find("#randomize-reason")
      .setValue("Eligibility confirmed; ready for treatment allocation");
    await wrapper.find("#btn-randomize-participant").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#sig-reason")
      .setValue("I verify participant eligibility and authorize randomization");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: `/api/v1/execution/subjects/${SUBJECT_UUID}/randomize`,
        semanticAction: "execution.subject.randomize",
      })
    );
    expect(executionApi.randomizeSubject).toHaveBeenCalledWith(
      SUBJECT_UUID,
      {
        stratification_factors: {
          age_group: ">=65",
          stage: "III",
        },
      },
      "Eligibility confirmed; ready for treatment allocation",
      "sig-rand-001"
    );

    expect(wrapper.find("#inspector-state").text()).toBe("Randomized");
    expect(wrapper.find("#inspector-assigned-kit").text()).toBe("KIT-1001");
    expect(wrapper.find("#inspector-treatment-group").exists()).toBe(false);
  });

  it("enforces >= 50-character clinical justification for emergency unblinding and reveals treatment_group and kit to Principal Investigator", async () => {
    participants[0].status = "RANDOMIZED";
    signIn({
      id: "usr-pi-1",
      username: "pi.site101",
      roles: ["Site Investigator", "Principal Investigator"],
    });

    // Before emergency unblinding, Principal Investigator without unblinded_access stays blinded.
    vi.mocked(executionApi.getSubjectDetail).mockImplementation(async () => ({
      id: SUBJECT_UUID,
      subject_id: "101-0001",
      study_id: "CAD-101",
      site_id: "SITE-101",
      status: participants[0].status,
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group: participants[0].status === "UNBLINDED" ? "ACTIVE" : null,
      randomization_seed: null,
      investigational_product_id:
        participants[0].status === "UNBLINDED" ? "KIT-1001" : null,
    }));
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "sig-unblind-pi-001",
    });
    vi.mocked(executionApi.changeSubjectState).mockImplementation(
      async (_id, nextState) => {
        participants[0].status = nextState;
        return { ...participants[0] };
      }
    );

    await openCanvas(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    expect(wrapper.find("#inspector-treatment-group").exists()).toBe(false);
    expect(wrapper.find("#emergency-unblind-section").exists()).toBe(true);

    await wrapper.find("#btn-toggle-emergency-unblind").trigger("click");
    await wrapper
      .find("#emergency-unblind-reason")
      .setValue("Too short clinical reason");
    expect(
      wrapper.find("#btn-confirm-emergency-unblind").element.disabled
    ).toBe(true);

    const justification =
      "Severe Grade 4 anaphylactoid reaction requiring immediate antidote selection in emergency care.";
    expect(justification.length).toBeGreaterThanOrEqual(50);
    await wrapper.find("#emergency-unblind-reason").setValue(justification);
    expect(
      wrapper.find("#btn-confirm-emergency-unblind").element.disabled
    ).toBe(false);

    await wrapper.find("#btn-confirm-emergency-unblind").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#sig-reason")
      .setValue("Emergency clinical safety unblinding authorized");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: `/api/v1/execution/subjects/${SUBJECT_UUID}/state`,
        semanticAction: "execution.subject.unblind",
      })
    );
    expect(executionApi.changeSubjectState).toHaveBeenCalledWith(
      SUBJECT_UUID,
      "UNBLINDED",
      justification,
      "sig-unblind-pi-001"
    );
    expect(wrapper.find("#inspector-state").text()).toBe("Unblinded");
    expect(wrapper.find("#inspector-assigned-kit").text()).toBe("KIT-1001");
    expect(wrapper.find("#inspector-treatment-group").text()).toBe("ACTIVE");
  });

  it("offers emergency unblinding to an authorized_er_physician while hiding ordinary participant management controls", async () => {
    participants[0].status = "ACTIVE";
    signIn({
      id: "usr-er-1",
      username: "er.physician",
      roles: ["authorized_er_physician"],
    });

    vi.mocked(executionApi.getSubjectDetail).mockImplementation(async () => ({
      id: SUBJECT_UUID,
      subject_id: "101-0001",
      study_id: "CAD-101",
      site_id: "SITE-101",
      status: participants[0].status,
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group:
        participants[0].status === "UNBLINDED" ? "PLACEBO" : null,
      randomization_seed: null,
      investigational_product_id:
        participants[0].status === "UNBLINDED" ? "KIT-2002" : null,
    }));
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "sig-unblind-er-002",
    });
    vi.mocked(executionApi.changeSubjectState).mockImplementation(
      async (_id, nextState) => {
        participants[0].status = nextState;
        return { ...participants[0] };
      }
    );

    await openCanvas(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    expect(wrapper.find("#inspector-consent").exists()).toBe(false);
    expect(wrapper.find("#state-change-target").exists()).toBe(false);
    expect(wrapper.find("#participant-randomize-section").exists()).toBe(false);
    expect(wrapper.find("#emergency-unblind-section").exists()).toBe(true);

    await wrapper.find("#btn-toggle-emergency-unblind").trigger("click");
    const justification =
      "Acute toxicology consult requiring immediate verification of investigational treatment arm.";
    await wrapper.find("#emergency-unblind-reason").setValue(justification);
    await wrapper.find("#btn-confirm-emergency-unblind").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#sig-reason")
      .setValue("Emergency clinical safety unblinding authorized");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: `/api/v1/execution/subjects/${SUBJECT_UUID}/state`,
        semanticAction: "execution.subject.unblind",
      })
    );
    expect(executionApi.changeSubjectState).toHaveBeenCalledWith(
      SUBJECT_UUID,
      "UNBLINDED",
      justification,
      "sig-unblind-er-002"
    );
    expect(wrapper.find("#inspector-state").text()).toBe("Unblinded");
    expect(wrapper.find("#inspector-assigned-kit").text()).toBe("KIT-2002");
    expect(wrapper.find("#inspector-treatment-group").text()).toBe("PLACEBO");
  });

  it("reveals arm details to an unblinded caller and purges cached treatment_group when switching to a blinded coordinator", async () => {
    participants[0].status = "RANDOMIZED";
    let currentUnblinded = true;
    signIn({
      id: "usr-stat-author",
      username: "stat.author",
      roles: ["unblinded_statistician"],
    });

    vi.mocked(executionApi.getSubjectDetail).mockImplementation(async () => ({
      id: SUBJECT_UUID,
      subject_id: "101-0001",
      study_id: "CAD-101",
      site_id: "SITE-101",
      status: "RANDOMIZED",
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group: currentUnblinded ? "ACTIVE" : null,
      randomization_seed: null,
      investigational_product_id: currentUnblinded ? "KIT-1001" : null,
    }));

    await openCanvas(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    expect(wrapper.find("#inspector-assigned-kit").text()).toBe("KIT-1001");
    expect(wrapper.find("#inspector-treatment-group").text()).toBe("ACTIVE");

    // Switch to a blinded CRC in the same SPA session: treatment_group must clear immediately.
    currentUnblinded = false;
    signIn({
      id: "usr-crc-1",
      username: "crc.site101",
      roles: ["crc"],
    });
    await flushPromises();

    expect(wrapper.find("#inspector-treatment-group").exists()).toBe(false);
  });
});
