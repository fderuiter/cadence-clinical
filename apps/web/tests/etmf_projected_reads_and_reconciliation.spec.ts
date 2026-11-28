import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia, type Pinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import AuditView from "../src/views/AuditView.vue";
import DocumentGrid from "../src/components/etmf/DocumentGrid.vue";
import PdfPreviewModal from "../src/components/etmf/PdfPreviewModal.vue";
import BatchUploadDrawer from "../src/components/etmf/BatchUploadDrawer.vue";
import CraQcQueue from "../src/components/etmf/CraQcQueue.vue";
import { executeCommitTaxonomy } from "../src/components/etmf/document-grid/documentGridActions.js";
import { etmfService } from "../src/api/etmf";
import { auditorService } from "../src/api/auditor";
import { useEtmfStore } from "../src/stores/etmf";
import { useAuthStore } from "../src/stores/auth";
import { signInAs } from "./helpers/signIn.js";

describe("eTMF projected reads, single-document detail hydration, and local store reconciliation", () => {
  let pinia: Pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Sponsor Admin", "CRA", "CRC"], {
      username: "cra.lead",
      studyIds: ["CADENCE-101"],
      siteIds: ["SITE-101"],
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("forwards artifact_code to GET /documents when a folder is selected and omits it when null", async () => {
    const store = useEtmfStore();
    store.setScope("CADENCE-101", "SITE-101");

    const getDocsSpy = vi
      .spyOn(etmfService, "getDocuments")
      .mockResolvedValue([]);

    await store.fetchDocuments("05.02.01");
    expect(getDocsSpy).toHaveBeenLastCalledWith({
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      artifact_code: "05.02.01",
    });

    await store.fetchDocuments(null);
    expect(getDocsSpy).toHaveBeenLastCalledWith({
      study_id: "CADENCE-101",
      site_id: "SITE-101",
    });
    expect(getDocsSpy.mock.calls[1][0]).not.toHaveProperty("artifact_code");
  });

  it("hydrates metadata, signature_manifestation, and redaction_manifest_json from GET /documents/{id} on inspect and preview while streaming /content", async () => {
    const summaryRow = {
      id: "DOC-SUM-201",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.01",
      artifact_type: "FDA Form 1572",
      filename: "Site101_1572_Signed.pdf",
      mime_type: "application/pdf",
      content_checksum: "a".repeat(64),
      created_at: "2026-10-12T09:00:00Z",
      created_by: "crc.site101",
      version_index: 2,
      status: "APPROVED",
      reason_for_change: "Signed 1572 uploaded",
      signer: "Dr. Elena Rostova",
      signing_timestamp: "2026-10-12T09:15:00Z",
      is_redacted: true,
    };

    const hydratedDetail = {
      ...summaryRow,
      metadata_json: {
        document_intelligence: {
          modality: "PDF_BINARY",
          confidence_tier: "HIGH",
          primary_classification: {
            zone: 5,
            section: "05.02",
            artifact_code: "05.02.01",
            artifact_type: "FDA Form 1572",
            confidence: 0.99,
          },
          signature_analysis: {
            status: "FULLY_SIGNED",
          },
          extracted_metadata: {
            investigator_name: "Dr. Elena Rostova",
            protocol_number: "CADENCE-101-AM2",
          },
        },
      },
      signature_manifestation: {
        signer_id: "Dr. Elena Rostova",
        timestamp: "2026-10-12T09:15:00Z",
        signing_reason: "Principal Investigator Regulatory Approval",
        sha256_hash: "a".repeat(64),
        signature: "sig-ed25519-201",
      },
      redaction_manifest_json: {
        operator_name: "Cadence DeID Pipeline",
        reason: "HIPAA Safe Harbor Redaction",
        redacted_items_count: { SSN: 2, MRN: 1 },
        signature: "hmac-redaction-seal-201",
      },
    };

    vi.spyOn(etmfService, "getDocuments").mockResolvedValue([summaryRow]);
    const getDetailSpy = vi
      .spyOn(etmfService, "getDocumentById")
      .mockResolvedValue(hydratedDetail as never);
    const streamSpy = vi
      .spyOn(etmfService, "downloadDocumentContent")
      .mockResolvedValue({
        blob: new Blob(["%PDF-1.7 binary bytes"], { type: "application/pdf" }),
        mimeType: "application/pdf",
        sha256: summaryRow.content_checksum,
      });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/etmf", name: "etmf", component: DocumentManagerView }],
    });
    await router.push("/etmf");
    await router.isReady();

    const wrapper = mount(DocumentManagerView, {
      props: { studyId: "CADENCE-101", siteId: "SITE-101" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // 1. Inspect drawer fetches GET /documents/{id} and renders hydrated classification & extracted metadata
    const grid = wrapper.findComponent(DocumentGrid);
    grid.vm.$emit("inspect", summaryRow);
    await flushPromises();

    expect(getDetailSpy).toHaveBeenCalledWith("DOC-SUM-201");
    expect(wrapper.text()).toContain("99% (HIGH)");
    expect(wrapper.text()).toContain("Dr. Elena Rostova");
    expect(wrapper.text()).toContain("CADENCE-101-AM2");

    // 2. Preview modal hydrates signature_manifestation + redaction_manifest_json and streams /content
    grid.vm.$emit("preview", summaryRow);
    await flushPromises();

    const modal = wrapper.findComponent(PdfPreviewModal);
    expect(modal.exists()).toBe(true);
    expect(streamSpy).toHaveBeenCalledWith("DOC-SUM-201");
    expect(modal.text()).toContain(
      "Principal Investigator Regulatory Approval"
    );
    expect(modal.text()).toContain("Cadence DeID Pipeline");
    expect(modal.text()).toContain("HIPAA Safe Harbor Redaction");
    expect(modal.text()).toContain("hmac-redaction-seal-201");

    wrapper.unmount();

    // 3. AuditView preview also hydrates GET /documents/{id} before rendering .secure-preview-panel
    vi.spyOn(auditorService, "getAuditLogs").mockResolvedValue({
      items: [],
      total_count: 0,
      limit: 20,
      offset: 0,
      has_more: false,
    } as never);
    vi.spyOn(auditorService, "getExecutionIntegrity").mockResolvedValue({
      verified: true,
      message: "Verified",
    } as never);
    vi.spyOn(auditorService, "getWatermarkedDownloadUrl").mockReturnValue(
      "http://localhost:8000/api/v1/etmf/documents/DOC-SUM-201/watermark"
    );
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("CONFIDENTIAL WATERMARKED COPY", { status: 200 })
    );
    vi.spyOn(etmfService, "getCompleteness").mockResolvedValue({
      study_id: "CADENCE-101",
      milestone: "INITIATION",
      is_complete: true,
      present_count: 1,
      expected_count: 1,
    } as never);

    getDetailSpy.mockClear();
    const auditRouter = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/audit", name: "audit", component: AuditView }],
    });
    await auditRouter.push("/audit");
    await auditRouter.isReady();

    const auditWrapper = mount(AuditView, {
      global: { plugins: [pinia, auditRouter] },
    });
    await flushPromises();

    await auditWrapper.find(".btn-preview-doc").trigger("click");
    await flushPromises();

    expect(getDetailSpy).toHaveBeenCalledWith("DOC-SUM-201");
    const auditPanel = auditWrapper.find(".secure-preview-panel");
    expect(auditPanel.exists()).toBe(true);
    expect(auditPanel.text()).toContain(
      "Principal Investigator Regulatory Approval"
    );
    expect(auditPanel.text()).toContain("Dr. Elena Rostova");
    expect(auditPanel.text()).toContain("sig-ed25519-201");
    auditWrapper.unmount();
  });

  it("stages 50 files in BatchUploadDrawer with 50 POST /intelligence/stage-qc calls and 0 GET reloads", async () => {
    const store = useEtmfStore();
    store.setScope("CADENCE-101", "SITE-101");

    const getDocsSpy = vi.spyOn(etmfService, "getDocuments");
    const getQcSpy = vi.spyOn(etmfService, "getQcQueue");
    const stageSpy = vi
      .spyOn(etmfService, "stageQcDocument")
      .mockImplementation(async (payload) => {
        const idx = stageSpy.mock.calls.length;
        return {
          document_id: `DOC-BATCH-${idx}`,
          study_id: payload.study_id || "CADENCE-101",
          site_id: payload.site_id ?? "SITE-101",
          filename: payload.filename,
          status: "TECHNICAL_QC",
          staged_at: "2026-10-15T10:00:00Z",
          staged_by: "cra.lead",
          qc_assigned_to: null,
          discrepancy_notes: [],
          intelligence_report: {
            document_id: `DOC-BATCH-${idx}`,
            filename: payload.filename,
            mime_type: "application/pdf",
            sha256_hash: "abc123",
            modality: "PDF_BINARY",
            confidence_tier: "HIGH",
            qc_recommendation: "AUTO_CLASSIFY",
            primary_classification: {
              zone: 5,
              section: "05.02",
              artifact_code: "05.02.01",
              artifact_type: "FDA Form 1572",
              confidence: 0.96,
            },
            alternative_candidates: [],
          },
        } as never;
      });

    const wrapper = mount(BatchUploadDrawer, {
      props: { isOpen: true, studyId: "CADENCE-101" },
      global: { plugins: [pinia] },
    });

    const fiftyFiles = Array.from(
      { length: 50 },
      (_, i) =>
        new File([`%PDF-1.7 content ${i + 1}`], `FDA_1572_${i + 1}.pdf`, {
          type: "application/pdf",
        })
    );
    const input = wrapper.find('input[type="file"]');
    Object.defineProperty(input.element, "files", {
      value: fiftyFiles,
      configurable: true,
    });
    await input.trigger("change");

    await wrapper.find("button.btn-submit-batch").trigger("click");
    for (let tick = 0; tick < 60 && stageSpy.mock.calls.length < 50; tick++) {
      await new Promise((resolve) => setTimeout(resolve, 20));
      await flushPromises();
    }
    await flushPromises();

    expect(stageSpy).toHaveBeenCalledTimes(50);
    expect(getDocsSpy).not.toHaveBeenCalled();
    expect(getQcSpy).not.toHaveBeenCalled();
    expect(store.qcQueue).toHaveLength(50);
    expect(store.documentsList).toHaveLength(50);
    wrapper.unmount();
  });

  it("accepts 50 QC items in CraQcQueue with 50 POST /intelligence/qc-review/{id} calls and 0 GET reloads after mount", async () => {
    const store = useEtmfStore();
    store.setScope("CADENCE-101", "SITE-101");

    const fiftyStaged = Array.from({ length: 50 }, (_, i) => ({
      document_id: `DOC-QC-${i + 1}`,
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      filename: `Investigator_CV_${i + 1}.pdf`,
      status: "TECHNICAL_QC",
      staged_at: "2026-10-15T11:00:00Z",
      staged_by: "crc.site101",
      intelligence_report: {
        confidence_tier: "HIGH",
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.03",
          artifact_type: "Investigator CV",
          confidence: 0.95,
        },
      },
    }));

    store.documentsList = fiftyStaged.map((item) => ({
      id: item.document_id,
      study_id: item.study_id,
      site_id: item.site_id,
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.03",
      artifact_type: "Investigator CV",
      filename: item.filename,
      status: "TECHNICAL_QC",
    }));

    const getQcSpy = vi
      .spyOn(etmfService, "getQcQueue")
      .mockResolvedValue(fiftyStaged as never);
    const getDocsSpy = vi.spyOn(etmfService, "getDocuments");
    const reviewSpy = vi
      .spyOn(etmfService, "submitQcReview")
      .mockImplementation(
        async (docId) =>
          ({
            id: docId,
            status: "APPROVED",
            artifact_code: "05.02.03",
            artifact_type: "Investigator CV",
          }) as never
      );

    const wrapper = mount(CraQcQueue, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // Clear initial mount GET spy so we measure strictly the 50-item batch action
    getQcSpy.mockClear();
    getDocsSpy.mockClear();

    const rowCheckboxes = wrapper.findAll('tbody input[type="checkbox"]');
    expect(rowCheckboxes).toHaveLength(50);
    for (const cb of rowCheckboxes) {
      await cb.setValue(true);
    }

    await wrapper.find("button.btn-batch-accept").trigger("click");
    await wrapper.find("button.btn-confirm-batch-accept").trigger("click");
    await flushPromises();

    expect(reviewSpy).toHaveBeenCalledTimes(50);
    expect(getQcSpy).not.toHaveBeenCalled();
    expect(getDocsSpy).not.toHaveBeenCalled();
    expect(store.qcQueue).toHaveLength(0);
    expect(store.documentsList.every((doc) => doc.status === "APPROVED")).toBe(
      true
    );
    wrapper.unmount();
  });

  it("reconciles uploads, draft submissions, taxonomy edits, and QC reviews locally without GET reloads and never synthesizes approvals on error", async () => {
    const store = useEtmfStore();
    store.setScope("CADENCE-101", "SITE-101");
    await store.fetchBinderTree("CADENCE-101");
    store.selectedArtifactId = "01.01.01";

    const getDocsSpy = vi.spyOn(etmfService, "getDocuments");
    const getQcSpy = vi.spyOn(etmfService, "getQcQueue");

    // 1. Upload reconciles into matching folder without calling getDocuments
    vi.spyOn(etmfService, "ingestDocument").mockResolvedValue({
      id: "DOC-MUT-1",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "Protocol_v1.pdf",
      status: "DRAFT",
      version_index: 1,
    } as never);

    await store.uploadDocument({
      filename: "Protocol_v1.pdf",
      artifact_code: "01.01.01",
      zone: 1,
      section: "01.01",
      reason_for_change: "Initial protocol upload for verification",
    });
    expect(store.documentsList).toHaveLength(1);
    expect(store.documentsList[0].status).toBe("DRAFT");
    expect(getDocsSpy).not.toHaveBeenCalled();

    // 2. Submit draft for QC updates status to TECHNICAL_QC and stages into qcQueue locally
    vi.spyOn(etmfService, "submitDocumentForQc").mockResolvedValue({
      id: "DOC-MUT-1",
      status: "TECHNICAL_QC",
      reason_for_change: "Submitting draft for CRA technical QC",
    } as never);

    await store.submitDraftForQc("DOC-MUT-1", {
      reason_for_change: "Submitting draft for CRA technical QC",
      assigned_cra: "cra.lead",
    });
    expect(store.documentsList[0].status).toBe("TECHNICAL_QC");
    expect(store.qcQueue).toHaveLength(1);
    expect(store.qcQueue[0].document_id).toBe("DOC-MUT-1");
    expect(getDocsSpy).not.toHaveBeenCalled();
    expect(getQcSpy).not.toHaveBeenCalled();

    // 3. Failed QC review never synthesizes APPROVED and keeps item in TECHNICAL_QC
    const submitQcSpy = vi
      .spyOn(etmfService, "submitQcReview")
      .mockRejectedValueOnce(new Error("Backend 500 failure"));

    await expect(
      store.reviewQcDocument("DOC-MUT-1", {
        decision: "ACCEPT",
        reason_for_change: "Attempted approval during outage",
      })
    ).rejects.toThrow("Backend 500 failure");

    expect(store.documentsList[0].status).toBe("TECHNICAL_QC");
    expect(store.qcQueue).toHaveLength(1);

    // 4. Successful QC OVERRIDE updates status + artifact_code, removes from active 01.01.01 folder and qcQueue
    submitQcSpy.mockResolvedValueOnce({
      id: "DOC-MUT-1",
      status: "APPROVED",
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.01",
      artifact_type: "FDA Form 1572",
    } as never);

    await store.reviewQcDocument("DOC-MUT-1", {
      decision: "OVERRIDE",
      override_artifact_code: "05.02.01",
      reason_for_change: "Reclassified to Zone 5 FDA Form 1572",
    });
    expect(store.qcQueue).toHaveLength(0);
    expect(store.documentsList).toHaveLength(0);

    // 5. Taxonomy edit in documentGridActions reconciles updated row locally and removes if moved out of active folder
    store.selectedArtifactId = "05.02.01";
    store.documentsList = [
      {
        id: "DOC-MUT-2",
        study_id: "CADENCE-101",
        zone: 5,
        section: "05.02",
        artifact_code: "05.02.01",
        artifact_type: "FDA Form 1572",
        filename: "Investigator_CV_Misfiled.pdf",
        status: "DRAFT",
      },
    ];
    vi.spyOn(etmfService, "tagDocument").mockResolvedValue({
      id: "DOC-MUT-2",
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.03",
      artifact_type: "Investigator CV",
      reason_for_change: "Reclassified misfiled CV to 05.02.03",
    } as never);

    let exitedEditing = false;
    await executeCommitTaxonomy({
      doc: store.documentsList[0],
      targetCode: "05.02.03",
      etmfStore: store,
      getZoneAndSectionFromCode: (code: string) => {
        const parts = code.split(".");
        return {
          zone: parseInt(parts[0], 10),
          section: `${parts[0]}.${parts[1]}`,
        };
      },
      cancelEditing: () => {},
      exitEditing: () => {
        exitedEditing = true;
      },
    });

    expect(exitedEditing).toBe(true);
    expect(store.documentsList).toHaveLength(0);
    expect(getDocsSpy).not.toHaveBeenCalled();
    expect(getQcSpy).not.toHaveBeenCalled();
  });
});
