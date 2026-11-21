import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ref } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import {
  findKernelEnrollmentRef,
  findStudyScheduleOptions,
} from "../../src/canvas/kernelStudy";
import { resolveSiteReadinessPath } from "../../src/canvas/palette";
import { useConsentGating } from "../../src/composables/useConsentGating";
import { useAuthStore } from "../../src/stores/auth";
import { useClinicalStore } from "../../src/stores/clinical";
import { useToastStore } from "../../src/stores/toasts.js";
import CtmsView from "../../src/views/CtmsView.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    addSubject: vi.fn(),
    changeSubjectState: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
    requestSignatureToken: vi.fn(),
    listRandomizationSchemes: vi.fn(),
    draftRandomizationScheme: vi.fn(),
    approveRandomizationScheme: vi.fn(),
    randomizeSubject: vi.fn(),
    recordSubjectConsent: vi.fn(),
    getSubjectDetail: vi.fn(),
    getSiteIpKitInventory: vi.fn(),
    seedIpKitInventory: vi.fn(),
  },
}));

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(async () => []),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(async () => []),
      listForEnrollment: vi.fn(async () => []),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(async () => []),
    getCommands: vi.fn(),
    getObservation: vi.fn(),
    getZoomPath: vi.fn(async () => []),
  },
}));

vi.mock("../../src/canvas/kernelStudy", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    findKernelEnrollmentRef: vi.fn(async () => ({
      kind: "enrollment",
      id: "0192f3c4-5e6a-7b8c-9d0e-00000000e001",
    })),
    findStudyScheduleOptions: vi.fn(async () => null),
  };
});

const SAMPLE_ISO = new Date(Date.UTC(2027, 5, 14)).toISOString();
const SAMPLE_DATE = SAMPLE_ISO.slice(0, 10);
const SUBJECT_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const SCHEME_UUID = "0192f3c4-5e6a-7b8c-9d0e-00000000c001";

function makeParticipant(overrides = {}) {
  return {
    id: SUBJECT_UUID,
    subject_id: "017-0001",
    study_id: "CAD-101",
    site_id: "017",
    status: "ENROLLED",
    active_protocol_version: "1.0",
    enrollment_index: 1,
    ...overrides,
  };
}

function makeScheme(overrides = {}) {
  return {
    id: SCHEME_UUID,
    study_id: "CAD-101",
    version: 1,
    method: "permuted_block",
    status: "DRAFT",
    arms: { ACTIVE: 1, PLACEBO: 1 },
    stratification_factors: { AGE_GROUP: ["ADULT", "SENIOR"] },
    block_sizes: [4],
    slots_per_stratum: 16,
    preferred_arm_probability: 0.8,
    requires_kit: true,
    list_size: 32,
    configuration_hash: "sha256:scheme-v1",
    drafted_by: "stat.author",
    drafted_at: SAMPLE_ISO,
    approved_by: null,
    approved_at: null,
    retired_at: null,
    ...overrides,
  };
}

describe("Clinical Canvas RTSM, consent, blinding, and emergency unblinding", () => {
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
        {
          path: "/ctms",
          name: "ctms",
          component: CtmsView,
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div />" },
        },
      ],
    });
    participants = [makeParticipant()];
    for (const fn of Object.values(executionApi)) vi.mocked(fn).mockReset();
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
    vi.mocked(executionApi.listRandomizationSchemes).mockResolvedValue([]);
    vi.mocked(executionApi.getSiteIpKitInventory).mockResolvedValue([]);
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      ...makeParticipant({ status: "RANDOMIZED" }),
      treatment_group: null,
      investigational_product_id: null,
    });
    vi.mocked(findKernelEnrollmentRef).mockResolvedValue({
      kind: "enrollment",
      id: "0192f3c4-5e6a-7b8c-9d0e-00000000e001",
    });
    vi.mocked(findStudyScheduleOptions).mockResolvedValue(null);
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([]);
    vi.mocked(ecrfQueriesApi.listForEnrollment).mockResolvedValue([]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    document.body.innerHTML = "";
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  function signIn({
    roles,
    username = "crc.site101",
    userId = "u-1",
    siteId = "017",
    unblindedAccess = false,
  }) {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = roles;
    auth.user = {
      id: userId,
      username,
      siteId,
      unblindedAccess,
    };
  }

  async function open(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      {
        global: {
          plugins: [pinia, router],
          stubs: { Teleport: true },
        },
        attachTo: document.body,
      }
    );
    await flushPromises();
    return wrapper;
  }

  it("drafts a scheme as one unblinded statistician, blocks self-approval, and lets a second unblinded statistician sign-approve it", async () => {
    const schemes = [];
    vi.mocked(executionApi.listRandomizationSchemes).mockImplementation(
      async () => [...schemes]
    );
    vi.mocked(executionApi.draftRandomizationScheme).mockImplementation(
      async (payload) => {
        const created = makeScheme({
          method: payload.method,
          arms: payload.arms,
          stratification_factors: payload.stratification_factors,
          drafted_by: "stat.author",
        });
        schemes.push(created);
        return created;
      }
    );
    vi.mocked(executionApi.requestSignatureToken).mockResolvedValue({
      sig_token: "sig-approve-1",
      expires_in: 60,
    });
    vi.mocked(executionApi.approveRandomizationScheme).mockImplementation(
      async () => {
        schemes[0] = {
          ...schemes[0],
          status: "APPROVED",
          approved_by: "stat.approver",
          approved_at: SAMPLE_ISO,
        };
        return schemes[0];
      }
    );

    signIn({
      roles: ["unblinded_statistician"],
      username: "stat.author",
      userId: "b1a2c3d4-e5f6-4a7b-8c9d-e0f1a2b3c4d5",
      unblindedAccess: true,
    });

    await open("/studies/CAD-101?stage=start&section=randomization-schemes");

    await wrapper.find("#btn-draft-scheme").trigger("click");
    await wrapper
      .find("#scheme-draft-reason")
      .setValue("Protocol v1.0 1:1 permuted block schedule");
    await wrapper.find("#draft-scheme-form").trigger("submit");
    await flushPromises();

    expect(executionApi.draftRandomizationScheme).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CAD-101",
        method: "permuted_block",
        arms: { ACTIVE: 1, PLACEBO: 1 },
        stratification_factors: { AGE_GROUP: ["ADULT", "SENIOR"] },
        requires_kit: true,
      }),
      "Protocol v1.0 1:1 permuted block schedule"
    );

    // Self-approval is blocked in the UI for the drafting statistician.
    expect(wrapper.find("#scheme-sod-note-1").exists()).toBe(true);
    expect(wrapper.find("#btn-approve-scheme-1").exists()).toBe(false);

    // Second unblinded statistician signs and approves the draft scheme.
    wrapper.unmount();
    signIn({
      roles: ["unblinded_statistician"],
      username: "stat.approver",
      userId: "c2b3d4e5-f6a7-4b8c-9d0e-f1a2b3c4d5e6",
      unblindedAccess: true,
    });
    await open("/studies/CAD-101?stage=start&section=randomization-schemes");

    await wrapper.find("#btn-approve-scheme-1").trigger("click");
    await wrapper.find("#approve-scheme-password").setValue("ApproverPass!1");
    await wrapper
      .find("#approve-scheme-reason")
      .setValue("Verified independently against SAP");
    await wrapper.find("#btn-confirm-approve-scheme").trigger("click");
    await flushPromises();

    expect(executionApi.requestSignatureToken).toHaveBeenCalledWith({
      password: "ApproverPass!1", // pragma: allowlist secret
      totp: undefined,
      username: "stat.approver",
      action: `/api/v1/execution/randomization/schemes/${SCHEME_UUID}/approve`,
      semanticAction: "execution.form.approve",
    });
    expect(executionApi.approveRandomizationScheme).toHaveBeenCalledWith(
      SCHEME_UUID,
      {
        reason: "Verified independently against SAP",
        sigToken: "sig-approve-1",
      }
    );
    expect(wrapper.find("#scheme-row-v1").text()).toContain("APPROVED");
  });

  it("seeds kits under Conduct -> Supply without exposing treatment arms and opens non-demo kit reconciliation in Supply and CtmsView", async () => {
    const siteKits = [];
    vi.mocked(executionApi.getSiteIpKitInventory).mockImplementation(
      async () => [...siteKits]
    );
    vi.mocked(executionApi.seedIpKitInventory).mockImplementation(
      async (req) => {
        for (const k of req.kits) {
          siteKits.push({
            id: "kit-id-1",
            study_id: req.study_id,
            site_id: req.site_id,
            kit_number: k.kit_number,
            lot_number: k.lot_number,
            expiration_date: k.expiration_date,
            status: "AVAILABLE",
            residual_volume_ml: 100,
            residual_volume_percentage: 100,
            reconciliation_count: 0,
          });
        }
        return siteKits;
      }
    );

    signIn({
      roles: ["sponsor_dm"],
      username: "dm.user",
      siteId: "017",
    });
    await open("/studies/CAD-101?stage=conduct&section=supply&site=017");

    await wrapper.find("#btn-open-seed-kits").trigger("click");
    await wrapper.find("#seed-kit-number-0").setValue("KIT-0101");
    await wrapper.find("#seed-kit-arm-0").setValue("ACTIVE");
    await wrapper.find("#seed-kit-lot-0").setValue("LOT-A1");
    await wrapper.find("#seed-kit-expiry-0").setValue(SAMPLE_DATE);
    await wrapper
      .find("#seed-kits-reason")
      .setValue("Initial depot shipment to Site 017");
    await wrapper.find("#seed-kits-form").trigger("submit");
    await flushPromises();

    expect(executionApi.seedIpKitInventory).toHaveBeenCalledWith(
      {
        study_id: "CAD-101",
        site_id: "017",
        kits: [
          {
            kit_number: "KIT-0101",
            treatment_arm: "ACTIVE",
            lot_number: "LOT-A1",
            expiration_date: `${SAMPLE_DATE}T00:00:00Z`,
          },
        ],
      },
      "Initial depot shipment to Site 017"
    );
    expect(wrapper.find("#supply-inventory-table").text()).toContain(
      "KIT-0101"
    );
    // Treatment arm is never rendered in the site inventory table.
    expect(wrapper.find("#supply-inventory-table").text()).not.toContain(
      "ACTIVE"
    );

    // Pharmacist opens returned kit reconciliation in Supply and in non-demo CtmsView.
    global.fetch = vi.fn(async () => ({
      ok: true,
      json: async () => siteKits,
    }));
    wrapper.unmount();
    signIn({
      roles: ["pharmacist"],
      username: "pharmacist.site101",
      siteId: "017",
    });
    await open("/studies/CAD-101?stage=conduct&section=supply&site=017");
    await wrapper.find("#btn-open-supply-reconciliation").trigger("click");
    await flushPromises();
    expect(wrapper.text()).toContain(
      "Bulk IP Kit Reconciliation & Batch Disposition Drawer"
    );

    wrapper.unmount();
    await open("/ctms?studyId=CAD-101&siteId=017");
    expect(wrapper.find("#ctms-kit-reconciliation-panel").exists()).toBe(true);
    await wrapper.find("#ctms-kit-reconciliation-scope").trigger("submit");
    await flushPromises();
    expect(wrapper.text()).toContain(
      "Bulk IP Kit Reconciliation & Batch Disposition Drawer"
    );
  });

  it("records informed consent with an explicit signed date in CanvasInspector and useConsentGating outside demo mode", async () => {
    vi.mocked(executionApi.recordSubjectConsent).mockResolvedValue({
      consent_id: "c-1",
      subject_uuid: SUBJECT_UUID,
      study_id: "CAD-101",
      version_tag: "1.0",
      version_index: 1,
      icf_signed: true,
      icf_signed_date: `${SAMPLE_DATE}T00:00:00Z`,
      requires_reconsent: false,
      is_paper_override: false,
      recorded_at: SAMPLE_ISO,
    });

    signIn({ roles: ["crc"], username: "crc.site101" });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    await wrapper.find("#btn-open-consent-form").trigger("click");
    // Submit stays disabled until an explicit signed date is entered.
    await wrapper
      .find("#inspector-consent-reason")
      .setValue("Signed paper ICF verified");
    expect(wrapper.find("#btn-submit-consent").element.disabled).toBe(true);

    await wrapper.find("#inspector-consent-date").setValue(SAMPLE_DATE);
    expect(wrapper.find("#btn-submit-consent").element.disabled).toBe(false);
    await wrapper.find("#inspector-consent-form").trigger("submit");
    await flushPromises();

    expect(executionApi.recordSubjectConsent).toHaveBeenCalledWith(
      SUBJECT_UUID,
      {
        protocol_version: {
          study_id: "CAD-101",
          version_tag: "1.0",
          version_index: 1,
        },
        icf_signed: true,
        icf_signed_date: `${SAMPLE_DATE}T00:00:00Z`,
        requires_reconsent: false,
        is_paper_override: false,
      },
      "Signed paper ICF verified"
    );
    expect(wrapper.find("#inspector-consent-recorded").text()).toContain(
      `Signed v1.0 recorded (${SAMPLE_DATE})`
    );

    // useConsentGating outside demo mode calls recordSubjectConsent when studyId + signedDate are given.
    const clinicalStore = useClinicalStore();
    clinicalStore.activeStudyId = "CAD-101";
    const clearSpy = vi
      .spyOn(clinicalStore, "clearReconsentGate")
      .mockResolvedValue();
    const gating = useConsentGating(ref("017-0001"));
    gating.showEconsentModal.value = true;
    await gating.handleCompleteReconsent("ECONSENT", {
      studyId: "CAD-101",
      versionTag: "2.0",
      versionIndex: 2,
      signedDate: SAMPLE_DATE,
      reason: "Re-consented to v2.0",
    });
    expect(executionApi.recordSubjectConsent).toHaveBeenCalledTimes(2);
    expect(clearSpy).toHaveBeenCalled();
    expect(gating.showEconsentModal.value).toBe(false);
  });

  it("withholds RANDOMIZED from ParticipantStateChange and randomizes an ENROLLED participant via RandomizeParticipantDrawer", async () => {
    vi.mocked(executionApi.listRandomizationSchemes).mockResolvedValue([
      makeScheme({ status: "APPROVED", approved_by: "stat.approver" }),
    ]);
    vi.mocked(executionApi.requestSignatureToken).mockResolvedValue({
      sig_token: "sig-rand-1",
      expires_in: 60,
    });
    vi.mocked(executionApi.randomizeSubject).mockImplementation(async () => {
      participants[0].status = "RANDOMIZED";
      return {
        randomization_id: "rand-1",
        subject_uuid: SUBJECT_UUID,
        subject_id: "017-0001",
        study_id: "CAD-101",
        site_id: "017",
        scheme_id: SCHEME_UUID,
        scheme_version: 1,
        stratum_key: "AGE_GROUP=SENIOR",
        kit_number: "KIT-0101",
        status: "RANDOMIZED",
        randomized_at: SAMPLE_ISO,
      };
    });

    signIn({ roles: ["crc"], username: "crc.site101" });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    const stateOptions = wrapper
      .findAll("#state-change-target option")
      .filter((o) => o.attributes("value"))
      .map((o) => o.attributes("value"));
    expect(stateOptions).toEqual(["RECONSENT_REQUIRED", "WITHDRAWN"]);
    expect(stateOptions).not.toContain("RANDOMIZED");

    await wrapper.find("#btn-open-randomize-drawer").trigger("click");
    await flushPromises();

    await wrapper.find("#randomize-factor-AGE_GROUP").setValue("SENIOR");
    await wrapper
      .find("#randomize-reason")
      .setValue("Eligibility and consent verified");
    await wrapper.find("#randomize-password").setValue("CrcPass!1");
    await wrapper.find("#randomize-participant-form").trigger("submit");
    await flushPromises();

    expect(executionApi.requestSignatureToken).toHaveBeenCalledWith({
      password: "CrcPass!1", // pragma: allowlist secret
      totp: undefined,
      username: "crc.site101",
      action: `/api/v1/execution/subjects/${SUBJECT_UUID}/randomize`,
      semanticAction: "execution.subject.randomize",
    });
    expect(executionApi.randomizeSubject).toHaveBeenCalledWith(SUBJECT_UUID, {
      stratificationFactors: { AGE_GROUP: "SENIOR" },
      reason: "Eligibility and consent verified",
      sigToken: "sig-rand-1",
    });

    expect(wrapper.find("#randomization-receipt-arm").text()).toBe(
      "Randomized, arm masked"
    );
    expect(wrapper.find("#randomization-receipt-kit").text()).toBe("KIT-0101");
    expect(wrapper.find("#inspector-state").text()).toBe("Randomized");
    expect(wrapper.find("#inspector-allocation").text()).toBe(
      "Randomized, arm masked"
    );
    expect(wrapper.find("#inspector-kit").text()).toBe("KIT-0101");
  });

  it("renders 'Randomized, arm masked' when blinded and treatment_group + investigational_product_id when unblinded in CanvasInspector and ParticipantRecord", async () => {
    participants = [makeParticipant({ status: "RANDOMIZED" })];
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValueOnce({
      ...participants[0],
      treatment_group: null,
      investigational_product_id: null,
    });

    signIn({ roles: ["crc"], username: "crc.site101", unblindedAccess: false });
    await open("/studies/CAD-101?section=participants");
    await wrapper.find("#participant-017-0001").trigger("click");
    await flushPromises();
    await wrapper.find("#inspector-open-record").trigger("click");
    await flushPromises();

    expect(executionApi.getSubjectDetail).toHaveBeenCalledWith("017-0001");
    expect(wrapper.find("#inspector-allocation").text()).toBe(
      "Randomized, arm masked"
    );
    expect(wrapper.find("#record-allocation-arm").text()).toBe(
      "Randomized, arm masked"
    );

    // Unblinded caller sees treatment_group and investigational_product_id.
    wrapper.unmount();
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      ...participants[0],
      status: "UNBLINDED",
      treatment_group: "ACTIVE",
      investigational_product_id: "KIT-0101",
    });
    signIn({
      roles: ["Principal Investigator"],
      username: "pi.site101",
      unblindedAccess: true,
    });
    await open("/studies/CAD-101?section=participants");
    await wrapper.find("#participant-017-0001").trigger("click");
    await flushPromises();
    await wrapper.find("#inspector-open-record").trigger("click");
    await flushPromises();

    expect(wrapper.find("#inspector-allocation").text()).toBe("ACTIVE");
    expect(wrapper.find("#inspector-kit").text()).toBe("KIT-0101");
    expect(wrapper.find("#record-allocation-arm").text()).toBe("ACTIVE");
    expect(wrapper.find("#record-allocation-kit").text()).toBe("KIT-0101");
  });

  it("gates emergency unblinding to Principal Investigator / Authorized ER Physician and enforces a >= 50-character clinical reason", async () => {
    participants = [makeParticipant({ status: "ACTIVE" })];

    // CRC does not get the emergency unblinding trigger.
    signIn({ roles: ["crc"], username: "crc.site101" });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );
    expect(wrapper.find("#btn-emergency-unblind").exists()).toBe(false);
    expect(wrapper.find("#state-change-unblinding-note").exists()).toBe(true);

    // Principal Investigator gets the emergency unblinding modal and >= 50-char validation.
    wrapper.unmount();
    vi.mocked(executionApi.changeSubjectState).mockImplementation(
      async (id, nextState) => {
        participants[0].status = nextState;
        return { ...participants[0] };
      }
    );
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      ...participants[0],
      status: "UNBLINDED",
      treatment_group: "ACTIVE",
      investigational_product_id: "KIT-0101",
    });

    signIn({ roles: ["Principal Investigator"], username: "pi.site101" });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    expect(wrapper.find("#btn-emergency-unblind").exists()).toBe(true);
    await wrapper.find("#btn-emergency-unblind").trigger("click");
    await flushPromises();

    // Short clinical reason (< 50 chars) is rejected.
    await wrapper
      .find("#emergency-unblind-reason")
      .setValue("Short reason under fifty chars");
    expect(
      wrapper.find("#btn-confirm-emergency-unblind").element.disabled
    ).toBe(true);
    expect(wrapper.find("#emergency-unblind-validation").exists()).toBe(true);
    expect(executionApi.changeSubjectState).not.toHaveBeenCalled();

    // >= 50-character clinical emergency reason succeeds and unblinds participant.
    const validReason =
      "Suspected anaphylactic SAE in emergency department requiring immediate antidote selection.";
    await wrapper.find("#emergency-unblind-reason").setValue(validReason);
    expect(
      wrapper.find("#btn-confirm-emergency-unblind").element.disabled
    ).toBe(false);
    await wrapper.find("#btn-confirm-emergency-unblind").trigger("click");
    await flushPromises();

    expect(executionApi.changeSubjectState).toHaveBeenCalledWith(
      SUBJECT_UUID,
      "UNBLINDED",
      validReason
    );
    expect(wrapper.find("#inspector-state").text()).toBe("Unblinded");
    expect(wrapper.find("#inspector-allocation").text()).toBe("ACTIVE");
    expect(
      useToastStore().toasts.some(
        (t) => t.title === "017-0001 is now unblinded"
      )
    ).toBe(true);
  });

  it("passes axe-core WCAG 2.2 AA accessibility audit on Randomization schemes drafting and approval", async () => {
    vi.mocked(executionApi.listRandomizationSchemes).mockResolvedValue([
      makeScheme({ status: "DRAFT", drafted_by: "stat.author" }),
    ]);

    signIn({
      roles: ["unblinded_statistician"],
      username: "stat.approver",
      unblindedAccess: true,
    });
    await open("/studies/CAD-101?stage=start&section=randomization-schemes");
    await wrapper.find("#btn-draft-scheme").trigger("click");
    await wrapper.find("#btn-approve-scheme-1").trigger("click");
    await flushPromises();
    await expect(
      wrapper.find("#randomization-schemes-section").element
    ).toBeAccessible();
  });

  it("passes axe-core WCAG 2.2 AA accessibility audit on Investigational supply and kit seeding", async () => {
    signIn({ roles: ["sponsor_dm"], username: "dm.user", siteId: "017" });
    await open("/studies/CAD-101?stage=conduct&section=supply&site=017");
    await wrapper.find("#btn-open-seed-kits").trigger("click");
    await flushPromises();
    await expect(wrapper.find("#supply-section").element).toBeAccessible();
  });

  it("passes axe-core WCAG 2.2 AA accessibility audit on inspector consent capture and RandomizeParticipantDrawer", async () => {
    vi.mocked(executionApi.listRandomizationSchemes).mockResolvedValue([
      makeScheme({ status: "APPROVED", approved_by: "stat.approver" }),
    ]);
    signIn({
      roles: ["Principal Investigator"],
      username: "pi.site101",
    });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );
    await wrapper.find("#btn-open-consent-form").trigger("click");
    await wrapper.find("#btn-open-randomize-drawer").trigger("click");
    await flushPromises();
    await expect(wrapper.find("#canvas-inspector").element).toBeAccessible();
  });

  it("passes axe-core WCAG 2.2 AA accessibility audit on EmergencyUnblindModal", async () => {
    participants = [makeParticipant({ status: "ACTIVE" })];
    signIn({
      roles: ["Principal Investigator"],
      username: "pi.site101",
    });
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&site=017&node=participant:${SUBJECT_UUID}`
    );
    await wrapper.find("#btn-emergency-unblind").trigger("click");
    await flushPromises();
    await expect(wrapper.find("#canvas-inspector").element).toBeAccessible();
  });

  it("passes axe-core WCAG 2.2 AA accessibility audit on CTMS returned kit reconciliation panel", async () => {
    signIn({ roles: ["pharmacist"], username: "pharmacist.site101" });
    await open("/ctms?studyId=CAD-101&siteId=017");
    await expect(
      wrapper.find("#ctms-kit-reconciliation-panel").element
    ).toBeAccessible();
  });

  it("integrates specialist RTSM roles with trial onboarding guards, site readiness routing, and the collapsible inspector rail", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.isDemoMode = false;
    auth.accessToken = "oidc-access-token";
    auth.user = { id: "u-self", username: "new.founder", tenantId: null };
    auth.rawRoles = [];
    expect(auth.needsTrialOnboarding).toBe(true);

    for (const specialistRole of [
      "unblinded_statistician",
      "pharmacist",
      "Principal Investigator",
      "Authorized ER Physician",
    ]) {
      auth.rawRoles = [specialistRole];
      expect(auth.needsTrialOnboarding).toBe(false);
    }
    expect(resolveSiteReadinessPath(["pharmacist"], "CAD-101")).toBe("/ctms");

    participants = [makeParticipant({ status: "RANDOMIZED" })];
    signIn({ roles: ["crc"], username: "crc.site101", unblindedAccess: false });
    await open("/studies/CAD-101?section=participants");

    // Inspector starts collapsed when no object is selected; selecting a participant auto-expands it with RTSM allocation.
    expect(wrapper.find("#canvas-inspector").attributes("data-collapsed")).toBe(
      "true"
    );
    expect(
      wrapper.find("#btn-toggle-inspector").attributes("aria-expanded")
    ).toBe("false");

    await wrapper.find("#participant-017-0001").trigger("click");
    await flushPromises();

    expect(wrapper.find("#canvas-inspector").attributes("data-collapsed")).toBe(
      "false"
    );
    expect(
      wrapper.find("#btn-toggle-inspector").attributes("aria-expanded")
    ).toBe("true");
    expect(wrapper.find("#inspector-allocation").text()).toBe(
      "Randomized, arm masked"
    );
  });
});
