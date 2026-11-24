import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory } from "vue-router";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";
import DocumentGrid from "../src/components/etmf/DocumentGrid.vue";
import ExportJobDrawer from "../src/components/etmf/ExportJobDrawer.vue";
import PdfPreviewModal from "../src/components/etmf/PdfPreviewModal.vue";
import TmfBinderTree from "../src/components/etmf/TmfBinderTree.vue";
import CraQcQueue from "../src/components/etmf/CraQcQueue.vue";
import { etmfService } from "../src/api/etmf";
import { kernelApi } from "../src/api/kernelApi";
import { ApiError } from "../src/api/client";
import { useAuthStore } from "../src/stores/auth";
import { useEtmfStore } from "../src/stores/etmf";
import { signInAs } from "./helpers/signIn.js";

describe("eTMF binary storage, scope propagation, role gates, and mapped contracts", () => {
  let pinia: ReturnType<typeof createPinia>;
  let unhandledRejections: unknown[];
  const onUnhandledRejection = (reason: unknown) => {
    unhandledRejections.push(reason);
  };

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    unhandledRejections = [];
    process.on("unhandledRejection", onUnhandledRejection);

    vi.spyOn(kernelApi, "listStudies").mockResolvedValue({
      studies: [
        {
          id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e",
          code: "CADENCE-101",
          title: "CADENCE-101 Oncology Phase II",
          phase: "Phase II",
          status: "ACTIVE",
        },
        {
          id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6f",
          code: "CADENCE-202",
          title: "CADENCE-202 Cardiology",
          phase: "Phase III",
          status: "ACTIVE",
        },
      ],
    } as never);
  });

  afterEach(() => {
    process.off("unhandledRejection", onUnhandledRejection);
    vi.restoreAllMocks();
    expect(unhandledRejections).toEqual([]);
  });

  it("propagates study_id=CADENCE-101 and site_id=SITE-101 across standalone /etmf and Clinical Canvas reads, uploads, QC queue, and exports", async () => {
    const auth = useAuthStore();
    signInAs(auth, ["CRA"], {
      username: "cra.lead",
      studyIds: ["CADENCE-101", "CADENCE-202"],
      siteIds: ["SITE-101"],
    });

    const getDocsSpy = vi
      .spyOn(etmfService, "getDocuments")
      .mockResolvedValue([]);
    const getQcSpy = vi.spyOn(etmfService, "getQcQueue").mockResolvedValue([]);
    const ingestSpy = vi
      .spyOn(etmfService, "ingestDocument")
      .mockResolvedValue({
        id: "DOC-CAD-1",
        study_id: "CADENCE-101",
        site_id: "SITE-101",
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.01",
        artifact_type: "Clinical Trial Protocol",
        filename: "protocol_2mb.pdf",
        mime_type: "application/pdf",
        status: "DRAFT",
        version_index: 1,
        content_checksum: "a".repeat(64),
        object_key: "etmf/CADENCE-101/DOC-CAD-1/v1/protocol_2mb.pdf",
      } as never);
    const exportSpy = vi
      .spyOn(etmfService, "exportDocuments")
      .mockResolvedValue({
        isSync: false,
        job: {
          job_id: "EXP-CAD-1",
          study_id: "CADENCE-101",
          status: "COMPLETED",
          progress_percentage: 100,
          total_documents: 20,
          processed_documents: 20,
          download_url: "/api/v1/etmf/export-jobs/EXP-CAD-1/download",
        },
      } as never);
    vi.spyOn(etmfService, "listExportJobs").mockResolvedValue([]);
    vi.spyOn(etmfService, "getExportJob").mockResolvedValue({
      job_id: "EXP-CAD-1",
      study_id: "CADENCE-101",
      status: "COMPLETED",
      progress_percentage: 100,
      total_documents: 20,
      processed_documents: 20,
      download_url: "/api/v1/etmf/export-jobs/EXP-CAD-1/download",
    } as never);

    // 1. Standalone /etmf scope bar
    const standalone = mount(DocumentManagerView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(standalone.find("#etmf-scope-bar").exists()).toBe(true);
    await standalone.find("#etmf-study-select").setValue("CADENCE-101");
    await standalone.find("#etmf-site-select").setValue("SITE-101");
    await standalone.find("#etmf-scope-bar").trigger("submit");
    await flushPromises();

    expect(getDocsSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CADENCE-101",
        site_id: "SITE-101",
      })
    );
    await standalone.find("#btn-etmf-tab-qc-queue").trigger("click");
    await flushPromises();
    expect(getQcSpy).toHaveBeenCalledWith("CADENCE-101", "SITE-101");
    await standalone.find("#btn-etmf-tab-binder").trigger("click");
    await flushPromises();

    // Multi-megabyte binary PDF upload preserves every byte via contentBase64
    const etmfStore = useEtmfStore();
    await etmfStore.fetchDocuments("01.01.01");
    await flushPromises();

    const grid = standalone.findComponent(DocumentGrid);
    await grid.find(".upload-trigger-btn").trigger("click");

    const largeBytes = new Uint8Array(1_500_000);
    largeBytes.set([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37], 0);
    largeBytes[750_000] = 0xff;
    const largePdf = new File([largeBytes], "protocol_2mb.pdf", {
      type: "application/pdf",
    });

    const fileInput = grid.find('input[type="file"]');
    Object.defineProperty(fileInput.element, "files", {
      value: [largePdf],
      configurable: true,
    });
    await fileInput.trigger("change");
    await grid
      .find("textarea")
      .setValue("Initial approved CADENCE-101 protocol binary upload");
    await grid.find("form.upload-meta-form").trigger("submit");
    await new Promise((resolve) => setTimeout(resolve, 30));
    await flushPromises();

    expect(ingestSpy).toHaveBeenCalledTimes(1);
    const sentMeta = ingestSpy.mock.calls[0][0] as Record<string, unknown>;
    expect(sentMeta.study_id ?? sentMeta.studyId).toBe("CADENCE-101");
    expect(sentMeta.site_id ?? sentMeta.siteId).toBe("SITE-101");
    const b64 = (sentMeta.content_base64 ?? sentMeta.contentBase64) as string;
    expect(typeof b64).toBe("string");
    const decodedLen = Buffer.from(b64, "base64").byteLength;
    expect(decodedLen).toBe(1_500_000);

    // Export sends study_id=CADENCE-101
    await standalone
      .findComponent(TmfBinderTree)
      .find(".btn-export-full-binder")
      .trigger("click");
    await flushPromises();
    expect(exportSpy).toHaveBeenCalledWith(
      expect.objectContaining({ study_id: "CADENCE-101" })
    );

    standalone.unmount();

    // 2. Clinical Canvas /studies/CADENCE-101?stage=start&section=documents&site=SITE-101
    getDocsSpy.mockClear();
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
      ],
    });
    await router.push(
      "/studies/CADENCE-101?stage=start&section=documents&site=SITE-101"
    );
    await router.isReady();

    const canvas = mount(StudyCanvasView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(canvas.find("#etmf-scope-bar").exists()).toBe(false);
    expect(getDocsSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CADENCE-101",
        site_id: "SITE-101",
      })
    );
    canvas.unmount();
  });

  it("renders actionable FORBIDDEN_SCOPE assignment guidance instead of 'No Regulated Documents Found' on HTTP 403", async () => {
    signInAs(useAuthStore(), ["CRA"], {
      username: "cra.unassigned",
      studyIds: ["CADENCE-101"],
    });

    vi.spyOn(etmfService, "getDocuments").mockRejectedValue(
      new ApiError(
        "Caller is not authorized for study 'UNASSIGNED-999'.",
        403,
        "Forbidden",
        {
          code: "FORBIDDEN_SCOPE",
          detail: "Caller is not authorized for study 'UNASSIGNED-999'.",
        }
      )
    );
    vi.spyOn(etmfService, "getQcQueue").mockRejectedValue(
      new ApiError(
        "Caller is not authorized for study 'UNASSIGNED-999'.",
        403,
        "Forbidden",
        {
          code: "FORBIDDEN_SCOPE",
          detail: "Caller is not authorized for study 'UNASSIGNED-999'.",
        }
      )
    );

    const wrapper = mount(DocumentManagerView, {
      props: { studyId: "UNASSIGNED-999" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find("#etmf-scope-forbidden-banner").exists()).toBe(true);
    expect(wrapper.find("#grid-scope-forbidden-guidance").exists()).toBe(true);
    expect(wrapper.text()).toContain("UNASSIGNED-999");
    expect(wrapper.text()).not.toContain("No Regulated Documents Found");
    wrapper.unmount();
  });

  it("downloads active and historical export ZIPs in ExportJobDrawer.vue via authenticated Blob fetch and surfaces 410 Gone cleanly", async () => {
    signInAs(useAuthStore(), ["CRA"], {
      username: "cra.export",
      studyIds: ["CADENCE-101"],
    });
    const store = useEtmfStore();
    store.isExportDrawerOpen = true;
    const exportDownloadPath = (id: string) =>
      ["/api/v1/etmf/exports", id, "download"].join("/");
    store.activeExportJob = {
      job_id: "JOB-ACTIVE-1",
      study_id: "CADENCE-101",
      status: "COMPLETED",
      progress_percentage: 100,
      processed_documents: 18,
      total_documents: 18,
      download_url: exportDownloadPath("JOB-ACTIVE-1"),
    };
    store.exportJobs = [
      store.activeExportJob,
      {
        job_id: "JOB-HIST-2",
        study_id: "CADENCE-101",
        status: "COMPLETED",
        progress_percentage: 100,
        processed_documents: 5,
        total_documents: 5,
        download_url: exportDownloadPath("JOB-HIST-2"),
      },
    ];

    const createUrlSpy = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue("blob:etmf-export-zip");
    const revokeUrlSpy = vi
      .spyOn(URL, "revokeObjectURL")
      .mockImplementation(() => {});
    const downloadSpy = vi
      .spyOn(etmfService, "downloadExportJob")
      .mockResolvedValueOnce({
        blob: new Blob([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], {
          type: "application/zip",
        }),
        filename: "etmf_export_JOB-ACTIVE-1.zip",
      })
      .mockRejectedValueOnce(
        new ApiError("Export archive has expired.", 410, "Gone", {
          error: "Export archive has expired.",
        })
      );

    const wrapper = mount(ExportJobDrawer, {
      global: { plugins: [pinia] },
    });

    // No raw unauthenticated <a download> links remain
    expect(wrapper.findAll("a[download]").length).toBe(0);

    await wrapper.find("button.btn-download").trigger("click");
    await flushPromises();
    expect(downloadSpy).toHaveBeenCalledWith("JOB-ACTIVE-1");
    expect(createUrlSpy).toHaveBeenCalledTimes(1);
    expect(revokeUrlSpy).toHaveBeenCalledTimes(1);

    const historyButtons = wrapper.findAll("button.history-download-link");
    expect(historyButtons.length).toBe(2);
    await historyButtons[1].trigger("click");
    await flushPromises();

    expect(downloadSpy).toHaveBeenCalledWith("JOB-HIST-2");
    expect(wrapper.find("#export-download-error").text()).toContain("expired");
    wrapper.unmount();
  });

  it("enforces authority.ts role gates across StudyCanvasView, DocumentManagerView, DocumentGrid, TmfBinderTree, and CraQcQueue with zero unhandled rejections", async () => {
    const auth = useAuthStore();

    // 1. Pharmacist lacks etmf_document:read -> StudyCanvasView & DocumentManagerView show UX-9 guidance without calling eTMF API
    signInAs(auth, ["Pharmacist"], {
      username: "pharm.user",
      studyIds: ["CADENCE-101"],
    });
    const getDocsSpy = vi
      .spyOn(etmfService, "getDocuments")
      .mockResolvedValue([]);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
      ],
    });
    await router.push("/studies/CADENCE-101?stage=start&section=documents");
    await router.isReady();

    const pharmCanvas = mount(StudyCanvasView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    const readRoleGuidance = pharmCanvas
      .find("#etmf-read-role-guidance")
      .text();
    expect(readRoleGuidance).toContain(
      "Requires Clinical Research Coordinator, Investigator, CRA, Monitor, Sponsor Data Manager, Auditor, External Monitor, or System Administrator role."
    );
    expect(readRoleGuidance).not.toContain("etmf_document:read");
    expect(getDocsSpy).not.toHaveBeenCalled();
    pharmCanvas.unmount();

    // 2. Investigator has read, but lacks create, tag, export, and transition_clinical_qc
    signInAs(auth, ["Investigator"], {
      username: "pi.site101",
      studyIds: ["CADENCE-101"],
      siteIds: ["SITE-101"],
    });
    const store = useEtmfStore();
    await store.fetchBinderTree("CADENCE-101");
    store.documentsList = [
      {
        id: "DOC-DRAFT-1",
        study_id: "CADENCE-101",
        site_id: "SITE-101",
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.01",
        artifact_type: "Clinical Trial Protocol",
        filename: "draft_protocol.pdf",
        mime_type: "application/pdf",
        status: "DRAFT",
        version_index: 1,
        created_by: "crc.coord",
        created_at: "2026-11-22T10:00:00Z",
      },
    ];
    const tagSpy = vi.spyOn(etmfService, "tagDocument").mockResolvedValue({
      ...store.documentsList[0],
      artifact_code: "01.01.02",
      artifact_type: "Clinical Trial Protocol Amendment",
    } as never);

    const piView = mount(DocumentManagerView, {
      props: { studyId: "CADENCE-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // Upload disabled with UX-9 reason & inline taxonomy edit blocked for Investigator
    const piGrid = piView.findComponent(DocumentGrid);
    expect(
      piGrid.find("button.upload-trigger-btn").attributes("disabled")
    ).toBeDefined();
    expect(
      piGrid.find("button.batch-upload-trigger-btn").attributes("disabled")
    ).toBeDefined();
    const gridRoleNote = piGrid.find("#document-grid-role-note").text();
    expect(gridRoleNote).toContain(
      "Requires Clinical Research Coordinator, CRA, Monitor, Sponsor Data Manager, or System Administrator role."
    );
    expect(gridRoleNote).not.toContain("etmf_document:create");
    expect(gridRoleNote).not.toContain("etmf_document:tag");
    await piGrid.find('td[data-row="0"][data-col="1"]').trigger("dblclick");
    expect(piGrid.find("select.inline-select").exists()).toBe(false);
    expect(tagSpy).not.toHaveBeenCalled();
    piView.unmount();

    // 3. CRC has read + create + tag, and lacks export + transition_clinical_qc
    signInAs(auth, ["CRC"], {
      username: "crc.coord",
      studyIds: ["CADENCE-101"],
      siteIds: ["SITE-101"],
    });
    const draftDoc = {
      id: "DOC-DRAFT-1",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "draft_protocol.pdf",
      mime_type: "application/pdf",
      status: "DRAFT",
      version_index: 1,
      created_by: "crc.coord",
      created_at: "2026-11-22T10:00:00Z",
    };
    getDocsSpy.mockResolvedValue([draftDoc]);
    store.documentsList = [draftDoc];

    vi.spyOn(etmfService, "getQcQueue").mockResolvedValue([]);
    const submitQcSpy = vi
      .spyOn(etmfService, "submitDocumentForQc")
      .mockResolvedValue({
        ...draftDoc,
        status: "TECHNICAL_QC",
      } as never);

    const crcView = mount(DocumentManagerView, {
      props: { studyId: "CADENCE-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // TmfBinderTree export disabled with UX-9 reason & no mockUnreadBadges
    const tree = crcView.findComponent(TmfBinderTree);
    expect(
      tree.find(".btn-export-full-binder").attributes("disabled")
    ).toBeDefined();
    const exportRoleNote = tree.find("#etmf-export-role-note").text();
    expect(exportRoleNote).toContain(
      "Requires Sponsor Data Manager, CRA, Monitor, or Auditor role."
    );
    expect(exportRoleNote).not.toContain("etmf_document:export");
    expect(tree.findAll(".unread-badge").length).toBe(0);

    // Inline taxonomy edit persists through PATCH /api/v1/etmf/documents/{document_id}/taxonomy
    const grid = crcView.findComponent(DocumentGrid);
    const taxonomyCell = grid.find('td[data-row="0"][data-col="1"]');
    await taxonomyCell.trigger("dblclick");
    expect(grid.find("select.inline-select").exists()).toBe(true);
    await grid.find("select.inline-select").setValue("01.01.02");
    await flushPromises();
    expect(tagSpy).toHaveBeenCalledWith(
      "DOC-DRAFT-1",
      expect.objectContaining({ artifact_code: "01.01.02" }),
      expect.objectContaining({ changeReason: expect.any(String) })
    );

    // Inspect DRAFT document: no fabricated "95% High Confidence" or "✓ Verified", and CRC CAN submit DRAFT -> TECHNICAL_QC
    await grid.vm.$emit("inspect", draftDoc);
    await flushPromises();
    expect(crcView.text()).not.toContain("95% High Confidence");
    expect(crcView.text()).not.toContain("✓ Verified");
    expect(crcView.find("#btn-submit-technical-qc").exists()).toBe(true);
    expect(
      crcView.find("#btn-submit-technical-qc").attributes("disabled")
    ).toBeUndefined();

    await crcView.find("#btn-submit-technical-qc").trigger("click");
    await flushPromises();
    expect(submitQcSpy).toHaveBeenCalledWith(
      "DOC-DRAFT-1",
      expect.objectContaining({
        reason_for_change: expect.any(String),
      }),
      expect.objectContaining({
        changeReason: expect.any(String),
      })
    );

    // CraQcQueue batch accept & adjudication disabled for CRC
    const qcWrapper = mount(CraQcQueue, {
      global: { plugins: [pinia] },
    });
    await flushPromises();
    const craQcRoleNote = qcWrapper.find("#cra-qc-role-note").text();
    expect(craQcRoleNote).toContain(
      "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role."
    );
    expect(craQcRoleNote).not.toContain("etmf_document:transition_clinical_qc");
    expect(
      qcWrapper.find("button.btn-batch-accept").attributes("disabled")
    ).toBeDefined();

    qcWrapper.unmount();
    crcView.unmount();
  });

  it("streams real stored bytes in PdfPreviewModal.vue with dynamic watermark and verified SHA-256, and prunes the 8 unmapped routes", async () => {
    signInAs(useAuthStore(), ["Auditor"], {
      username: "auditor.jane",
      studyIds: ["CADENCE-101"],
    });

    // Verify all 8 unmapped legacy helpers are pruned from etmfService
    const prunedKeys = [
      "getArchivalStatus",
      "autoFile",
      "getInspectionReadiness",
      "getEmsExportUrl",
      "verifyAuditChain",
      "verifyDocumentSignature",
    ];
    for (const key of prunedKeys) {
      expect(key in etmfService).toBe(false);
    }

    vi.spyOn(URL, "createObjectURL").mockReturnValue(
      "blob:stored-regulated-pdf"
    );
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const streamSpy = vi
      .spyOn(etmfService, "downloadDocumentContent")
      .mockResolvedValue({
        blob: new Blob(["%PDF-1.7 real stored binary bytes"], {
          type: "application/pdf",
        }),
        mimeType: "application/pdf",
        sha256: "a".repeat(64),
      });

    const modal = mount(PdfPreviewModal, {
      props: {
        document: {
          id: "DOC-BIN-77",
          study_id: "CADENCE-101",
          artifact_code: "01.01.01",
          artifact_type: "Clinical Trial Protocol",
          filename: "protocol_final.pdf",
          mime_type: "application/pdf",
          status: "APPROVED",
          version_index: 2,
          created_by: "sponsor.dm",
          created_at: "2026-11-22T09:00:00Z",
          is_redacted: true,
        },
      },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(streamSpy).toHaveBeenCalledWith("DOC-BIN-77");
    expect(modal.find("#pdf-preview-iframe").attributes("src")).toBe(
      "blob:stored-regulated-pdf"
    );
    expect(modal.find("#pdf-preview-sha256").text()).toBe("a".repeat(64));
    expect(modal.text()).toContain("USER: auditor.jane");
    expect(modal.text()).toContain("FINAL APPROVED");
    expect(modal.text()).not.toContain("MOCK-SHA256-VALIDATED-INTEGRITY-OK");
    expect(modal.text()).not.toContain("John Doe");
    expect(modal.text()).not.toContain("TIN-REDACTED-0000");

    modal.unmount();
  });
});
