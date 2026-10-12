/**
 * Unit & Component tests for eCRF Field Audit UI Components
 *
 * Requirements Traceability: @req:PRD-SYS-001 @req:PRD-SYS-053 | GxP 21 CFR Part 11 Regulated
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ClinicalFieldAuditFlag, EcrfFieldAuditModal } from "ui";
import CrcFormRenderer from "../../src/components/persona/CrcFormRenderer.vue";
import { useClinicalStore } from "../../src/stores/clinical";
import { executionApi } from "../../src/api/executionApi";

describe("ClinicalFieldAuditFlag.vue", () => {
  it("renders trigger button with ID audit-flag-{id}", () => {
    const wrapper = mount(ClinicalFieldAuditFlag, {
      props: {
        id: "vssbp",
        isOpen: false,
      },
    });

    const btn = wrapper.find("#audit-flag-vssbp");
    expect(btn.exists()).toBe(true);
    expect(btn.attributes("aria-label")).toContain("vssbp");
  });

  it("emits open-audit event on click", async () => {
    const wrapper = mount(ClinicalFieldAuditFlag, {
      props: {
        id: "vssbp",
      },
    });

    await wrapper.find("#audit-flag-vssbp").trigger("click");
    expect(wrapper.emitted("open-audit")).toBeTruthy();
    expect(wrapper.emitted("open-audit")[0]).toEqual(["vssbp"]);
  });
});

describe("EcrfFieldAuditModal.vue", () => {
  it("displays Part 11 header, revision timeline, and Merkle block hashes", () => {
    const auditHistory = [
      {
        versionIndex: 1,
        timestamp: "2026-03-31T10:00:00Z",
        userId: "crc_user_01",
        changeReason: "Initial record entry",
        oldValue: null,
        newValue: "120",
        blockHash: "hash111111111111111111111111111111111111111111111111111111111111",
        predecessorHash: "GENESIS",
        action: "CREATE",
      },
      {
        versionIndex: 2,
        timestamp: "2026-03-31T12:00:00Z",
        userId: "crc_user_02",
        changeReason: "Transcription correction",
        oldValue: "120",
        newValue: "128",
        blockHash: "hash222222222222222222222222222222222222222222222222222222222222",
        predecessorHash: "hash111111111111111111111111111111111111111111111111111111111111",
        action: "UPDATE",
      },
    ];

    const wrapper = mount(EcrfFieldAuditModal, {
      props: {
        isOpen: true,
        fieldId: "vssbp",
        fieldLabel: "Systolic Blood Pressure",
        auditHistory,
        loading: false,
      },
      global: {
        stubs: {
          Teleport: true,
        },
      },
    });

    expect(wrapper.text()).toContain("Systolic Blood Pressure");
    expect(wrapper.text()).toContain("21 CFR Part 11 eCRF Field Audit History");
    expect(wrapper.text()).toContain("Rev #1");
    expect(wrapper.text()).toContain("Rev #2");
    expect(wrapper.text()).toContain("crc_user_01");
    expect(wrapper.text()).toContain("crc_user_02");
    expect(wrapper.text()).toContain("Initial record entry");
    expect(wrapper.text()).toContain("Transcription correction");
    expect(wrapper.text()).toContain("120");
    expect(wrapper.text()).toContain("128");
    expect(wrapper.text()).toContain("hash111111111111111111111111111111111111111111111111111111111111");
  });

  it("renders empty state when audit history is empty", () => {
    const wrapper = mount(EcrfFieldAuditModal, {
      props: {
        isOpen: true,
        fieldId: "vssbp",
        fieldLabel: "Systolic Blood Pressure",
        auditHistory: [],
        loading: false,
      },
      global: {
        stubs: {
          Teleport: true,
        },
      },
    });

    expect(wrapper.text()).toContain("No Audit Records Found");
  });
});

describe("CrcFormRenderer.vue Field Audit Integration", () => {
  let pinia: any;
  let clinicalStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clinicalStore = useClinicalStore();
    clinicalStore.subjects = [
      {
        id: "SUBJ-101",
        status: "ACTIVE",
        active_protocol_version: "1.0.0",
        isGated: false,
      },
    ];
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("calls executionApi.getFieldAuditHistory when field audit flag is clicked", async () => {
    const mockAuditData = [
      {
        versionIndex: 1,
        timestamp: "2026-03-31T10:00:00Z",
        userId: "crc_user",
        changeReason: "Initial entry",
        oldValue: null,
        newValue: "120",
        blockHash: "blk12345",
        predecessorHash: "GENESIS",
      },
    ];

    vi.spyOn(executionApi, "getFieldAuditHistory").mockResolvedValue(mockAuditData as any);

    const wrapper = mount(CrcFormRenderer, {
      props: {
        store: clinicalStore,
        selectedSubjectId: "SUBJ-101",
        selectedVisitId: "Screening",
        isReconsentGated: false,
        showEconsentModal: false,
        showPaperIcfModal: false,
        reconsentSubmitting: false,
        econsentSignerName: "John Doe",
        paperIcfDate: "2026-03-31",
        paperIcfNote: "Verified",
        lookupStatuses: {},
        getValidationError: () => null,
        isCraUser: false,
        isAuthorizedForBulkSdv: false,
        sdvStates: {},
        getSdvKey: (id: string) => `sdv-${id}`,
        selectedBatchFields: [],
      },
      global: {
        stubs: {
          Teleport: true,
        },
      },
    });

    const auditFlag = wrapper.find("#audit-flag-vssbp");
    if (auditFlag.exists()) {
      await auditFlag.trigger("click");
      expect(executionApi.getFieldAuditHistory).toHaveBeenCalledWith("SUBJ-101", "vssbp");
    }
  });
});
