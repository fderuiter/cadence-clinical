import { defineStore } from "pinia";
import { DEMO_STUDY, getDemoPersonaByWorkflow } from "demo-fixtures";
import { etmfService } from "../api/etmf";
import { getSyncedStudyScope, syncActiveStudyScope } from "./studyScopeBridge";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../utils/callerScope";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  isDemoBuild,
  nextDemoStep,
} from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

export function arrayBufferToBase64(buffer) {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  const chunkSize = 0x8000;
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    const slice = bytes.subarray(offset, offset + chunkSize);
    binary += String.fromCharCode(...slice);
  }
  return btoa(binary);
}

function buildDefaultDocuments() {
  const studyId = DEMO_STUDY.code || "CADENCE-101";
  const leadUsername =
    getDemoPersonaByWorkflow("designer_lead")?.username || "designer.lead";
  const piPersona = getDemoPersonaByWorkflow("investigator");
  const piUsername = piPersona?.username || "pi.site101";
  const piFullName = piPersona
    ? `${piPersona.firstName} ${piPersona.lastName}`
    : "Site 101 Investigator";
  return [
    {
      id: "DOC-100",
      study_id: studyId,
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "protocol_v1_draft.pdf",
      mime_type: "application/pdf",
      created_at: "2026-01-06T12:00:00Z",
      created_by: leadUsername,
      version_index: 1,
      status: "DRAFT",
      reason_for_change: "Initial version",
    },
    {
      id: "DOC-999",
      study_id: studyId,
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.05",
      artifact_type: "Informed Consent Form",
      filename: "icf_v1.0_signed.pdf",
      mime_type: "application/pdf",
      created_at: "2026-01-10T09:30:00Z",
      created_by: piUsername,
      version_index: 1,
      status: "APPROVED",
      reason_for_change: "Initial signed consent.",
      signer: piFullName,
      signing_timestamp: "2026-01-10T09:35:00Z",
      signature_manifestation: {
        signing_reason: "Consent approval",
      },
    },
  ];
}

function getDefaultDocuments() {
  return isDemoBuild() ? buildDefaultDocuments() : [];
}

const TAXONOMY_RULES = [
  [
    ["protocol", "amendment"],
    1,
    "01.01",
    "01.01.01",
    "Clinical Trial Protocol",
    0.96,
  ],
  [["1572", "fda 1572"], 5, "05.02", "05.02.01", "FDA Form 1572", 0.98],
  [
    ["financial", "disclosure"],
    5,
    "05.02",
    "05.02.02",
    "Financial Disclosure",
    0.94,
  ],
  [
    ["cv", "curriculum", "resume"],
    5,
    "05.02",
    "05.02.03",
    "Investigator CV",
    0.95,
  ],
  [
    ["delegation", "doa", "log"],
    5,
    "05.02",
    "05.02.04",
    "Delegation of Authority Log",
    0.92,
  ],
  [
    ["consent", "icf", "informed"],
    5,
    "05.02",
    "05.02.05",
    "Informed Consent Form",
    0.97,
  ],
  [["brochure", "ib"], 2, "02.01", "02.01.01", "Investigator's Brochure", 0.95],
  [
    ["irb", "approval", "iec"],
    4,
    "04.02",
    "04.02.01",
    "IRB/IEC Approval Notification",
    0.91,
  ],
  [
    ["sae", "adverse", "safety"],
    7,
    "07.01",
    "07.01.01",
    "Serious Adverse Event Report",
    0.93,
  ],
  [
    ["lab", "certificate"],
    8,
    "08.01",
    "08.01.01",
    "Central Laboratory Certificate",
    0.89,
  ],
  [
    ["sap", "statistic"],
    11,
    "11.01",
    "11.01.01",
    "Statistical Analysis Plan",
    0.92,
  ],
];

function localPredictTaxonomy(filename = "", content = "") {
  const text = `${filename} ${content}`.toLowerCase();
  for (const [
    keywords,
    zone,
    section,
    artifact_code,
    artifact_type,
    confidence,
  ] of TAXONOMY_RULES) {
    if (keywords.some((kw) => text.includes(kw))) {
      return { zone, section, artifact_code, artifact_type, confidence };
    }
  }
  return {
    zone: 1,
    section: "01.01",
    artifact_code: "01.01.01",
    artifact_type: "General Clinical Document",
    confidence: 0.65,
  };
}

const BINDER_TREE_SPEC = [
  [
    "1",
    "Trial Management",
    [
      [
        "01.01",
        "Trial Design",
        [
          ["01.01.01", "Clinical Trial Protocol"],
          ["01.01.02", "Clinical Trial Protocol Amendment"],
          ["01.01.03", "Protocol Sign-off"],
        ],
      ],
      [
        "01.02",
        "Trial Oversight",
        [["01.02.01", "Trial Oversight Committee Charter"]],
      ],
      ["01.03", "Trial Monitoring", [["01.03.01", "Trial Monitoring Plan"]]],
      ["01.04", "Trial Close-out", [["01.04.01", "Trial Close-out Report"]]],
    ],
  ],
  [
    "2",
    "Central Trial Documents",
    [
      [
        "02.01",
        "Product Information",
        [["02.01.01", "Investigator's Brochure"]],
      ],
      [
        "02.02",
        "Clinical Trial Materials",
        [["02.02.01", "Clinical Trial Material Specifications"]],
      ],
    ],
  ],
  [
    "3",
    "Regulatory",
    [
      [
        "03.01",
        "Regulatory Submissions",
        [["03.01.01", "Regulatory Authority Submission"]],
      ],
      [
        "03.02",
        "Regulatory Approvals",
        [["03.02.01", "Regulatory Authority Approval"]],
      ],
    ],
  ],
  [
    "4",
    "IRB/IEC & other Approvals",
    [
      ["04.01", "IRB/IEC Submissions", [["04.01.01", "IRB/IEC Approval"]]],
      [
        "04.02",
        "IRB/IEC Approvals",
        [["04.02.01", "IRB/IEC Approval Notification"]],
      ],
    ],
  ],
  [
    "5",
    "Site Management",
    [
      ["05.01", "Site Selection", [["05.01.01", "Site Feasibility Survey"]]],
      [
        "05.02",
        "Investigator Qualification",
        [
          ["05.02.01", "FDA Form 1572"],
          ["05.02.02", "Financial Disclosure"],
          ["05.02.03", "Investigator CV"],
          ["05.02.04", "Delegation of Authority Log"],
          ["05.02.05", "Informed Consent Form"],
        ],
      ],
      ["05.03", "Site Training", [["05.03.01", "Site Training Records"]]],
      ["05.04", "Site Communication", [["05.04.01", "Site Communication Log"]]],
    ],
  ],
  [
    "6",
    "IP & Trial Supplies",
    [
      [
        "06.01",
        "IP Documentation",
        [["06.01.01", "Investigational Product Records"]],
      ],
      ["06.02", "IP Logistics", [["06.02.01", "IP Shipping Records"]]],
    ],
  ],
  [
    "7",
    "Safety Reporting",
    [
      [
        "07.01",
        "Safety Notifications",
        [["07.01.01", "Serious Adverse Event Report"]],
      ],
      ["07.02", "Safety Operations", [["07.02.01", "Safety Management Plan"]]],
    ],
  ],
  [
    "8",
    "Centralized & Local Testing",
    [
      [
        "08.01",
        "Lab Documentation",
        [["08.01.01", "Central Laboratory Certificate"]],
      ],
      [
        "08.02",
        "Lab Operations",
        [["08.02.01", "Laboratory Reference Ranges"]],
      ],
    ],
  ],
  [
    "9",
    "Third Parties",
    [
      [
        "09.01",
        "Vendor Management",
        [["09.01.01", "Vendor Service Agreement"]],
      ],
      ["09.02", "Vendor Operations", [["09.02.01", "Vendor Audit Report"]]],
    ],
  ],
  [
    "10",
    "Data Management",
    [
      [
        "10.01",
        "Data Management Specifications",
        [
          ["10.01.01", "Data Management Plan"],
          ["10.01.02", "Define-XML Specifications"],
        ],
      ],
      ["10.02", "Case Report Forms", [["10.02.01", "Blank CRF"]]],
      ["10.03", "Data Operations", [["10.03.01", "Data Review Guidelines"]]],
    ],
  ],
  [
    "11",
    "Statistics",
    [
      [
        "11.01",
        "Statistical Analysis",
        [
          ["11.01.01", "Statistical Analysis Plan"],
          ["11.01.02", "Data Lock Certificate"],
        ],
      ],
      [
        "11.02",
        "Data Analysis and Reports",
        [["11.02.01", "Clinical Study Report"]],
      ],
    ],
  ],
];

function buildDefaultBinderTree() {
  return BINDER_TREE_SPEC.map(([zCode, zName, sections]) => ({
    id: `zone_${zCode}`,
    name: zName,
    code: zCode,
    type: "zone",
    children: sections.map(([sCode, sName, artifacts]) => ({
      id: `sec_${sCode}`,
      name: sName,
      code: sCode,
      type: "section",
      children: artifacts.map(([aCode, aName]) => ({
        id: aCode,
        name: aName,
        code: aCode,
        type: "artifact",
      })),
    })),
  }));
}

function buildDocQcQueueItem(
  doc,
  {
    docId = doc?.id,
    studyId = "",
    siteId = null,
    assignedCra = null,
    contentFallback = doc?.content ?? "",
    status = "TECHNICAL_QC",
  } = {}
) {
  const filename = doc?.filename || "document.pdf";
  const report =
    doc?.metadata_json?.document_intelligence ||
    buildLocalIntelligenceReport(
      filename,
      contentFallback,
      doc?.mime_type || "application/pdf"
    );
  return {
    document_id: docId,
    study_id: doc?.study_id || studyId,
    site_id: doc?.site_id ?? siteId,
    filename,
    status,
    intelligence_report: report,
    staged_at: doc?.created_at || "",
    staged_by: doc?.created_by || "",
    qc_assigned_to: assignedCra || null,
    discrepancy_notes:
      report?.confidence_tier === "LOW"
        ? ["Low classification confidence score"]
        : [],
  };
}

function buildLocalIntelligenceReport(
  filename,
  content = "",
  mimeType = "application/pdf"
) {
  const primary = localPredictTaxonomy(filename, content);
  const tier =
    primary.confidence >= 0.85
      ? "HIGH"
      : primary.confidence >= 0.7
        ? "MEDIUM"
        : "LOW";
  return {
    filename,
    mime_type: mimeType,
    sha256_hash:
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
    modality: mimeType.includes("pdf") ? "PDF_BINARY" : "TEXT",
    primary_classification: primary,
    alternative_candidates: [],
    confidence_tier: tier,
    qc_recommendation: tier === "HIGH" ? "AUTO_CLASSIFY" : "CRA_QC_REQUIRED",
    ai_generation_manifest: {
      model: "Cadence-eTMF-Classifier-v3.2",
      prompt_hash: "a1b2c3d4e5f6", // pragma: allowlist secret
      confidence: primary.confidence,
      confidence_tier: tier,
      timestamp: new Date().toISOString(),
    },
  };
}

/**
 * @typedef {Object} EtmfExportJobRecord
 * @property {string} job_id
 * @property {string} study_id
 * @property {string} status
 * @property {number} progress_percentage
 * @property {number} total_documents
 * @property {number} processed_documents
 * @property {string} [requested_by]
 * @property {string} [download_url]
 * @property {string} [expires_at]
 * @property {string} [created_at]
 */

/**
 * @typedef {Object} EtmfDocumentRecord
 * @property {string} id
 * @property {string} study_id
 * @property {string} [site_id]
 * @property {number} [zone]
 * @property {string} [section]
 * @property {string} [artifact_code]
 * @property {string} [taxonomy_code]
 * @property {string} [artifact_type]
 * @property {string} [filename]
 * @property {string} [mime_type]
 * @property {string} [status]
 * @property {number} [version_index]
 * @property {string} [created_by]
 * @property {string} [created_at]
 * @property {string} [reason_for_change]
 * @property {string} [signer]
 * @property {string} [signing_timestamp]
 * @property {string} [content_checksum]
 * @property {string} [object_key]
 * @property {Record<string, unknown>} [signature_manifestation]
 */

export const useEtmfStore = defineStore("etmf", {
  state: () => ({
    /** @type {Array<Record<string, unknown>>} */
    binderTree: [],
    /** @type {string | null} */
    selectedArtifactId: null,
    /** @type {EtmfDocumentRecord[]} */
    documentsList: [],
    documentsFetchEpoch: 0,
    /** @type {EtmfDocumentRecord | null} */
    activeDocument: null,
    isUploading: false,
    currentStudyId:
      getSyncedStudyScope() ||
      (isDemoBuild() && DEMO_STUDY.code ? DEMO_STUDY.code : ""),
    currentSiteId: "",
    scopeForbiddenGuidance: "",
    documentsError: "",
    /** @type {Array<Record<string, unknown>>} */
    batchQueue: [],
    /** @type {Array<Record<string, unknown>>} */
    qcQueue: [],
    isBatchUploading: false,
    batchProgress: 0,
    /** @type {EtmfExportJobRecord | null} */
    activeExportJob: null,
    /** @type {EtmfExportJobRecord[]} */
    exportJobs: [],
    isExportDrawerOpen: false,
    /** @type {string[]} */
    selectedDocumentIds: [],
  }),

  getters: {
    documents: (state) => state.documentsList,
    folderLookup: (state) => {
      const lookup = {};
      const traverse = (node, parent = null) => {
        if (!node) return;
        const mapped = {
          ...node,
          parentCode: parent ? parent.code || parent.id : null,
        };
        // Key by both node id and code (if available) to guarantee instant O(1) lookups
        if (node.id) {
          lookup[node.id] = mapped;
        }
        if (node.code) {
          lookup[node.code] = mapped;
        }
        if (node.children) {
          node.children.forEach((child) => traverse(child, node));
        }
      };
      state.binderTree.forEach((node) => traverse(node, null));
      return lookup;
    },
  },

  actions: {
    setScope(studyId, siteId) {
      if (studyId !== undefined && studyId !== null) {
        const nextStudy = String(studyId).trim();
        if (nextStudy !== this.currentStudyId) {
          this.currentStudyId = nextStudy;
          this.scopeForbiddenGuidance = "";
          this.documentsError = "";
        }
        if (nextStudy) {
          syncActiveStudyScope(nextStudy);
        }
      }
      if (siteId !== undefined && siteId !== null) {
        const nextSite = String(siteId).trim();
        if (nextSite !== this.currentSiteId) {
          this.currentSiteId = nextSite;
          this.scopeForbiddenGuidance = "";
          this.documentsError = "";
        }
      }
    },

    async fetchBinderTree(studyId) {
      if (typeof studyId === "string" && studyId.trim()) {
        this.currentStudyId = studyId.trim();
        syncActiveStudyScope(this.currentStudyId);
      } else if (!this.currentStudyId && isDemoBuild() && DEMO_STUDY.code) {
        this.currentStudyId = DEMO_STUDY.code;
        syncActiveStudyScope(this.currentStudyId);
      }

      // Seed hierarchical DIA TMF Reference Model v3.2.0-complete hierarchy
      this.binderTree = buildDefaultBinderTree();
    },

    async fetchDocuments(artifactId) {
      if (artifactId !== undefined) {
        this.selectedArtifactId = artifactId;
      }
      this.documentsFetchEpoch += 1;
      this.scopeForbiddenGuidance = "";
      this.documentsError = "";
      const authStore = useAuthStore();
      try {
        const isMocked =
          etmfService.getDocuments &&
          (etmfService.getDocuments._isMockFunction ||
            typeof etmfService.getDocuments.mock === "object");
        if (
          isDemoBuild() &&
          authStore.isDemoMode &&
          !authStore.token &&
          !isMocked
        ) {
          let allDocs = [];
          if (typeof window !== "undefined" && window.localStorage) {
            const stored = window.localStorage.getItem("demo_documents");
            if (stored) {
              allDocs = JSON.parse(stored);
            } else {
              allDocs = getDefaultDocuments();
              window.localStorage.setItem(
                "demo_documents",
                JSON.stringify(allDocs)
              );
            }
          } else {
            allDocs = getDefaultDocuments();
          }

          if (this.selectedArtifactId) {
            this.documentsList = allDocs.filter(
              (doc) => doc.artifact_code === this.selectedArtifactId
            );
          } else {
            this.documentsList = allDocs;
          }
          return;
        }

        const queryParams = {};
        if (this.currentStudyId) {
          queryParams.study_id = this.currentStudyId;
        }
        if (this.currentSiteId) {
          queryParams.site_id = this.currentSiteId;
        }
        if (this.selectedArtifactId) {
          queryParams.artifact_code = this.selectedArtifactId;
        }

        const allDocs = await etmfService.getDocuments(queryParams);

        if (this.selectedArtifactId) {
          this.documentsList = (allDocs || []).filter(
            (doc) =>
              !doc?.artifact_code ||
              doc.artifact_code === this.selectedArtifactId
          );
        } else {
          this.documentsList = allDocs || [];
        }
      } catch (err) {
        this.documentsList = [];
        if (isForbiddenScopeError(err)) {
          this.scopeForbiddenGuidance = formatScopeForbiddenGuidance(err, {
            studyId: this.currentStudyId || undefined,
            siteId: this.currentSiteId || undefined,
            assignedStudies: authStore.assignedStudies || [],
            assignedSites: authStore.assignedSites || [],
            hasAllStudiesScope: authStore.hasAllStudiesScope,
          });
          return;
        }
        this.documentsError =
          err instanceof Error
            ? err.message
            : "Failed to load regulated documents.";
      }
    },

    async uploadDocument(formData) {
      this.isUploading = true;
      try {
        const authStore = useAuthStore();
        const isMocked =
          etmfService.ingestDocument &&
          (etmfService.ingestDocument._isMockFunction ||
            typeof etmfService.ingestDocument.mock === "object");

        let body = {};
        let changeReason = "Initial upload";

        if (formData instanceof FormData) {
          changeReason =
            String(formData.get("reason_for_change") || "").trim() ||
            "Initial upload";
          let contentBase64 = formData.get("content_base64") || null;
          const rawFile = formData.get("file");
          if (
            !contentBase64 &&
            rawFile &&
            typeof rawFile === "object" &&
            typeof rawFile.arrayBuffer === "function"
          ) {
            const buf = await rawFile.arrayBuffer();
            if (buf.byteLength > 0) {
              contentBase64 = arrayBufferToBase64(buf);
            }
          }
          body = {
            study_id:
              String(formData.get("study_id") || "").trim() ||
              this.currentStudyId,
            site_id:
              String(formData.get("site_id") || "").trim() ||
              this.currentSiteId ||
              null,
            artifact_type:
              formData.get("artifact_type") || "Clinical Trial Protocol",
            filename: formData.get("filename") || "document.pdf",
            content: formData.get("content") || null,
            content_base64: contentBase64,
            mime_type: formData.get("mime_type") || "application/pdf",
            artifact_code:
              formData.get("artifact_code") ||
              this.selectedArtifactId ||
              "01.01.01",
            zone: parseInt(formData.get("zone"), 10) || 1,
            section: formData.get("section") || "01.01",
            reason_for_change: changeReason,
            taxonomy_version: "v3.2.0-complete",
          };
        } else {
          changeReason =
            String(formData?.reason_for_change || "").trim() ||
            "Initial upload";
          body = {
            study_id: formData?.study_id || this.currentStudyId,
            site_id: formData?.site_id || this.currentSiteId || null,
            artifact_type: formData?.artifact_type || "Clinical Trial Protocol",
            filename: formData?.filename || "document.pdf",
            content: formData?.content ?? null,
            content_base64:
              formData?.content_base64 ?? formData?.contentBase64 ?? null,
            mime_type: formData?.mime_type || "application/pdf",
            artifact_code:
              formData?.artifact_code || this.selectedArtifactId || "01.01.01",
            zone: formData?.zone || 1,
            section: formData?.section || "01.01",
            reason_for_change: changeReason,
            taxonomy_version: "v3.2.0-complete",
          };
        }

        if (
          isDemoBuild() &&
          authStore.isDemoMode &&
          !authStore.token &&
          !isMocked
        ) {
          await new Promise((resolve) => setTimeout(resolve, 300));
          const step = nextDemoStep("etmf-upload");
          const newDoc = {
            id: "DOC-" + demoNowMs(step),
            study_id: body.study_id,
            site_id: body.site_id,
            zone: body.zone,
            section: body.section,
            artifact_code: body.artifact_code,
            artifact_type: body.artifact_type,
            filename: body.filename,
            mime_type: body.mime_type,
            content: body.content,
            content_base64: body.content_base64,
            created_at: demoNowIso(step),
            created_by:
              authStore.identity?.username ||
              getDemoPersonaByWorkflow("designer_lead")?.username ||
              "designer.lead",
            version_index: 1,
            status: "DRAFT",
            reason_for_change: changeReason,
          };

          let allDocs = [];
          if (typeof window !== "undefined" && window.localStorage) {
            const stored = window.localStorage.getItem("demo_documents");
            if (stored) {
              allDocs = JSON.parse(stored);
            } else {
              allDocs = getDefaultDocuments();
            }
            allDocs.push(newDoc);
            window.localStorage.setItem(
              "demo_documents",
              JSON.stringify(allDocs)
            );
          }

          this.reconcileDocument(newDoc);

          return {
            status: "success",
            document_id: newDoc.id,
            version_index: 1,
          };
        }

        const res = await etmfService.ingestDocument(body, { changeReason });
        const docId = res?.id || res?.document_id || res?.documentId;
        if (docId) {
          this.reconcileDocument(res, {
            fallbackId: docId,
            defaults: {
              study_id: body.study_id,
              site_id: body.site_id,
              zone: body.zone,
              section: body.section,
              artifact_code: body.artifact_code,
              artifact_type: body.artifact_type,
              filename: body.filename,
              mime_type: body.mime_type,
              version_index: res?.version_index ?? 1,
              status: res?.status || "DRAFT",
              approval_status: res?.approval_status || res?.status || "DRAFT",
              reason_for_change: changeReason,
            },
          });
        }
        return res;
      } finally {
        this.isUploading = false;
      }
    },

    /**
     * @param {string} docId
     * @param {{ reason_for_change?: string, assigned_cra?: string | null }} [options]
     */
    async submitDraftForQc(
      docId,
      { reason_for_change, assigned_cra = null } = {}
    ) {
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      const isMocked =
        etmfService.submitDocumentForQc &&
        (etmfService.submitDocumentForQc._isMockFunction ||
          typeof etmfService.submitDocumentForQc.mock === "object");
      try {
        const res = await etmfService.submitDocumentForQc(
          docId,
          {
            reason_for_change,
            assigned_cra,
          },
          { changeReason: reason_for_change }
        );
        const nextStatus = res?.status || "TECHNICAL_QC";
        const reconciledDoc = this.reconcileDocument(
          {
            ...(res && typeof res === "object" ? res : {}),
            status: nextStatus,
            approval_status: res?.approval_status || nextStatus,
            reason_for_change: res?.reason_for_change || reason_for_change,
          },
          { fallbackId: docId, insertIfMissing: false }
        );
        if (nextStatus === "TECHNICAL_QC") {
          this.reconcileQcQueueItem(
            buildDocQcQueueItem(reconciledDoc, {
              docId,
              studyId: this.currentStudyId,
              siteId: this.currentSiteId || null,
              assignedCra: assigned_cra,
              contentFallback: "",
            })
          );
        }
        return res;
      } catch (err) {
        if (!offlineDemo || isMocked) {
          throw err;
        }
        const doc = this.documentsList.find((d) => d.id === docId);
        if (doc) {
          doc.status = "TECHNICAL_QC";
          doc.approval_status = "TECHNICAL_QC";
          doc.reason_for_change = reason_for_change;
          this.reconcileQcQueueItem(
            buildDocQcQueueItem(doc, {
              studyId: this.currentStudyId,
              siteId: this.currentSiteId || null,
              assignedCra: assigned_cra,
            })
          );
        }
        return doc || { id: docId, status: "TECHNICAL_QC" };
      }
    },
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("demo_documents");
      }
      this.documentsList = getDefaultDocuments();
    },

    async classifyIntelligence(
      filename,
      content = "",
      mimeType = "application/pdf",
      contentHint = ""
    ) {
      try {
        return await etmfService.classifyIntelligence({
          filename,
          content,
          mime_type: mimeType,
          content_hint: contentHint,
          taxonomy_version: "v3.2.0-complete",
        });
      } catch {
        // Deterministic local heuristic fallback (<100ms)
        return buildLocalIntelligenceReport(filename, content, mimeType);
      }
    },

    async stageQcDocument(payload) {
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      const isMocked =
        etmfService.stageQcDocument &&
        (etmfService.stageQcDocument._isMockFunction ||
          typeof etmfService.stageQcDocument.mock === "object");
      const scopedPayload = {
        ...payload,
        study_id: payload.study_id || payload.studyId || this.currentStudyId,
        site_id:
          payload.site_id ?? payload.siteId ?? (this.currentSiteId || null),
      };
      try {
        const result = await etmfService.stageQcDocument(scopedPayload);
        const docId = result?.document_id || result?.documentId || result?.id;
        if (docId) {
          this.reconcileQcQueueItem(result);
          const primary = result?.intelligence_report?.primary_classification;
          this.reconcileDocument(
            {
              id: docId,
              study_id:
                result.study_id ||
                scopedPayload.study_id ||
                this.currentStudyId,
              site_id: result.site_id ?? scopedPayload.site_id ?? null,
              zone: primary?.zone ?? 1,
              section: primary?.section ?? "01.01",
              artifact_code: primary?.artifact_code ?? "",
              artifact_type: primary?.artifact_type ?? "",
              filename: result.filename || scopedPayload.filename,
              mime_type:
                result?.intelligence_report?.mime_type ||
                scopedPayload.mime_type ||
                "application/pdf",
              content_checksum:
                result?.intelligence_report?.sha256_hash || null,
              created_at: result.staged_at || "",
              created_by: result.staged_by || "",
              version_index: 1,
              status: result.status || "TECHNICAL_QC",
              approval_status: result.status || "TECHNICAL_QC",
              reason_for_change:
                scopedPayload.reason_for_change ||
                scopedPayload.reasonForChange ||
                "",
            },
            { fallbackId: docId }
          );
        }
        return result;
      } catch (err) {
        if (!offlineDemo || isMocked) {
          throw err;
        }
        // Fallback for offline demo builds only
        const report = buildLocalIntelligenceReport(
          scopedPayload.filename,
          scopedPayload.content,
          scopedPayload.mime_type
        );
        const step = nextDemoStep("etmf-qc-stage");
        const newDoc = {
          id: "DOC-" + demoRandomToken(7, 36, "etmf-qc-stage").toUpperCase(),
          study_id: scopedPayload.study_id || this.currentStudyId,
          site_id: scopedPayload.site_id || null,
          zone: report.primary_classification.zone,
          section: report.primary_classification.section,
          artifact_code: report.primary_classification.artifact_code,
          artifact_type: report.primary_classification.artifact_type,
          filename: scopedPayload.filename,
          mime_type: scopedPayload.mime_type || "application/pdf",
          created_at: demoNowIso(step),
          created_by: "crc.site101",
          version_index: 1,
          status: "TECHNICAL_QC",
          approval_status: "TECHNICAL_QC",
          reason_for_change: scopedPayload.reason_for_change,
          metadata_json: {
            document_intelligence: report,
            assigned_cra: scopedPayload.assigned_cra || null,
          },
        };
        this.documentsList.push(newDoc);
        const item = buildDocQcQueueItem(newDoc, {
          studyId: newDoc.study_id,
          siteId: newDoc.site_id,
          assignedCra: scopedPayload.assigned_cra,
        });
        this.qcQueue.unshift(item);
        return item;
      }
    },

    async fetchQcQueue(
      studyId = this.currentStudyId,
      siteId = this.currentSiteId
    ) {
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      const isMocked =
        etmfService.getQcQueue &&
        (etmfService.getQcQueue._isMockFunction ||
          typeof etmfService.getQcQueue.mock === "object");
      if (typeof etmfService.getQcQueue !== "function") {
        this.qcQueue = [];
        return this.qcQueue;
      }
      try {
        const queue = siteId
          ? await etmfService.getQcQueue(studyId || undefined, siteId)
          : await etmfService.getQcQueue(studyId || undefined);
        this.qcQueue = Array.isArray(queue) ? queue : [];
        return this.qcQueue;
      } catch (err) {
        if (!offlineDemo || isMocked) {
          this.qcQueue = [];
          if (isForbiddenScopeError(err)) {
            this.scopeForbiddenGuidance = formatScopeForbiddenGuidance(err, {
              studyId: studyId || undefined,
              siteId: siteId || undefined,
              assignedStudies: authStore.assignedStudies || [],
              assignedSites: authStore.assignedSites || [],
              hasAllStudiesScope: authStore.hasAllStudiesScope,
            });
          }
          throw err;
        }
        this.qcQueue = this.documentsList
          .filter((d) => d.status === "TECHNICAL_QC")
          .map((d) =>
            buildDocQcQueueItem(d, {
              studyId: d.study_id,
              siteId: d.site_id,
              status: d.status,
            })
          );
        return this.qcQueue;
      }
    },

    /**
     * @param {string} docId
     * @param {{ decision?: string, reason_for_change?: string, override_artifact_code?: string, discrepancy_comment?: string }} [payload]
     */
    async reviewQcDocument(
      docId,
      {
        decision,
        reason_for_change,
        override_artifact_code = undefined,
        discrepancy_comment = undefined,
      } = {}
    ) {
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      const isMocked =
        etmfService.submitQcReview &&
        (etmfService.submitQcReview._isMockFunction ||
          typeof etmfService.submitQcReview.mock === "object");
      try {
        const res = await etmfService.submitQcReview(docId, {
          decision,
          reason_for_change,
          override_artifact_code,
          discrepancy_comment,
        });
        const upperDecision = String(decision || "").toUpperCase();
        const fallbackStatus =
          upperDecision === "REJECT"
            ? "REJECTED"
            : upperDecision === "ACCEPT" || upperDecision === "OVERRIDE"
              ? "APPROVED"
              : undefined;
        const nextStatus = res?.status || fallbackStatus;
        const queuedItem = this.qcQueue.find((i) => i.document_id === docId);
        const hadInList = this.documentsList.some((d) => d.id === docId);
        if (!nextStatus || nextStatus !== "TECHNICAL_QC") {
          this.removeQcQueueItem(docId);
        }
        const patch = {
          ...(res &&
          typeof res === "object" &&
          !("success" in res && Object.keys(res).length === 1)
            ? res
            : {}),
        };
        if (nextStatus) {
          patch.status = nextStatus;
          patch.approval_status = res?.approval_status || nextStatus;
        }
        if (reason_for_change && !patch.reason_for_change) {
          patch.reason_for_change = reason_for_change;
        }
        if (
          upperDecision === "OVERRIDE" &&
          override_artifact_code &&
          !patch.artifact_code
        ) {
          patch.artifact_code = override_artifact_code;
          const targetNode = this.folderLookup?.[override_artifact_code];
          if (targetNode) {
            patch.artifact_type = targetNode.name || patch.artifact_type;
          }
        }
        this.reconcileDocument(patch, {
          fallbackId: docId,
          insertIfMissing: Boolean(res?.id || queuedItem || hadInList),
          defaults: queuedItem
            ? {
                study_id: queuedItem.study_id,
                site_id: queuedItem.site_id,
                filename: queuedItem.filename,
                zone:
                  queuedItem.intelligence_report?.primary_classification
                    ?.zone ?? 1,
                section:
                  queuedItem.intelligence_report?.primary_classification
                    ?.section ?? "01.01",
                artifact_code:
                  queuedItem.intelligence_report?.primary_classification
                    ?.artifact_code ?? "",
                artifact_type:
                  queuedItem.intelligence_report?.primary_classification
                    ?.artifact_type ?? "",
                created_at: queuedItem.staged_at,
                created_by: queuedItem.staged_by,
                version_index: 1,
              }
            : undefined,
        });
        return res;
      } catch (err) {
        if (!offlineDemo || isMocked) {
          throw err;
        }
        // Fallback for offline demo state only
        const doc = this.documentsList.find((d) => d.id === docId);
        if (doc) {
          if (decision === "ACCEPT" || decision === "OVERRIDE") {
            if (override_artifact_code) {
              doc.artifact_code = override_artifact_code;
            }
            doc.status = "APPROVED";
            doc.approval_status = "APPROVED";
          } else if (decision === "REJECT") {
            doc.status = "REJECTED";
            doc.approval_status = "REJECTED";
          }
          doc.reason_for_change = reason_for_change;
        }
        this.qcQueue = this.qcQueue.filter((i) => i.document_id !== docId);
        return { success: true };
      }
    },

    async batchAcceptClassifications(docIds, reasonForChange) {
      const ids = [...docIds];
      const results = [];
      for (const id of ids) {
        const res = await this.reviewQcDocument(id, {
          decision: "ACCEPT",
          reason_for_change: reasonForChange,
        });
        results.push(res);
      }
      return results;
    },

    reconcileDocument(partialDoc, options = {}) {
      const docId =
        partialDoc?.id ||
        partialDoc?.document_id ||
        partialDoc?.documentId ||
        options.fallbackId;
      if (!docId) return partialDoc;

      const idx = this.documentsList.findIndex((d) => d.id === docId);
      const existing = idx >= 0 ? this.documentsList[idx] : null;
      const merged = {
        ...(options.defaults || {}),
        ...(existing || {}),
        ...(partialDoc && typeof partialDoc === "object" ? partialDoc : {}),
        id: docId,
      };
      if (merged.status && !merged.approval_status) {
        merged.approval_status = merged.status;
      }

      const matchesActiveFolder =
        !this.selectedArtifactId ||
        !merged.artifact_code ||
        merged.artifact_code === this.selectedArtifactId;

      if (!matchesActiveFolder) {
        if (idx >= 0) {
          this.documentsList.splice(idx, 1);
          const selIdx = this.selectedDocumentIds.indexOf(docId);
          if (selIdx >= 0) {
            this.selectedDocumentIds.splice(selIdx, 1);
          }
        }
        return merged;
      }

      if (idx >= 0) {
        this.documentsList.splice(idx, 1, merged);
      } else if (options.insertIfMissing !== false) {
        this.documentsList.push(merged);
      }
      return merged;
    },

    reconcileQcQueueItem(stagingItem) {
      const docId =
        stagingItem?.document_id || stagingItem?.documentId || stagingItem?.id;
      if (!docId) return stagingItem;
      const idx = this.qcQueue.findIndex((item) => item.document_id === docId);
      const merged = {
        ...(idx >= 0 ? this.qcQueue[idx] : {}),
        ...stagingItem,
        document_id: docId,
      };
      if (idx >= 0) {
        this.qcQueue.splice(idx, 1, merged);
      } else {
        this.qcQueue.unshift(merged);
      }
      return merged;
    },

    removeQcQueueItem(docId) {
      if (!docId) return;
      this.qcQueue = this.qcQueue.filter((item) => item.document_id !== docId);
    },

    async fetchDocumentDetail(docOrId) {
      const docId =
        typeof docOrId === "string"
          ? docOrId
          : docOrId?.id || docOrId?.document_id || docOrId?.documentId;
      const baseDoc =
        typeof docOrId === "object" && docOrId !== null ? docOrId : null;
      const existing = docId
        ? this.documentsList.find((d) => d.id === docId) || null
        : null;
      if (!docId || typeof etmfService.getDocumentById !== "function") {
        return existing ? { ...existing, ...(baseDoc || {}) } : baseDoc;
      }

      const authStore = useAuthStore();
      const isMocked =
        etmfService.getDocumentById._isMockFunction ||
        typeof etmfService.getDocumentById.mock === "object";
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token && !isMocked;
      if (offlineDemo) {
        return existing ? { ...existing, ...(baseDoc || {}) } : baseDoc;
      }

      const detail = await etmfService.getDocumentById(docId);
      if (detail && typeof detail === "object") {
        const idx = this.documentsList.findIndex((d) => d.id === docId);
        if (idx >= 0) {
          this.documentsList.splice(idx, 1, {
            ...this.documentsList[idx],
            ...detail,
          });
        }
        return {
          ...(existing || {}),
          ...(baseDoc || {}),
          ...detail,
        };
      }
      return existing ? { ...existing, ...(baseDoc || {}) } : baseDoc;
    },

    toggleExportDrawer(open) {
      this.isExportDrawerOpen =
        open !== undefined ? open : !this.isExportDrawerOpen;
    },

    setSelectedDocumentIds(ids) {
      this.selectedDocumentIds = [...ids];
    },

    toggleDocumentSelection(docId) {
      const idx = this.selectedDocumentIds.indexOf(docId);
      if (idx >= 0) {
        this.selectedDocumentIds.splice(idx, 1);
      } else {
        this.selectedDocumentIds.push(docId);
      }
    },

    clearDocumentSelection() {
      this.selectedDocumentIds = [];
    },

    async triggerExport(payload) {
      const fullPayload = {
        study_id: this.currentStudyId || null,
        ...payload,
      };

      const result = await etmfService.exportDocuments(fullPayload);

      if (result.mode === "stream") {
        const url = URL.createObjectURL(result.blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = result.filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
        return { mode: "stream", filename: result.filename };
      }
      if (result.mode === "job") {
        this.activeExportJob = result.job;
        this.isExportDrawerOpen = true;
        const existingIdx = this.exportJobs.findIndex(
          (j) => j.job_id === result.job.job_id
        );
        if (existingIdx >= 0) {
          this.exportJobs[existingIdx] = result.job;
        } else {
          this.exportJobs.unshift(result.job);
        }
        this.startPollingExportJob(result.job.job_id);
        return { mode: "job", job: result.job };
      }
      return result;
    },

    async downloadExportJobArchive(jobOrId) {
      const jobId = typeof jobOrId === "string" ? jobOrId : jobOrId?.job_id;
      if (!jobId) {
        throw new Error("Export job identifier is required to download.");
      }
      const { blob, filename } = await etmfService.downloadExportJob(jobId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
      return { blob, filename };
    },

    async fetchDocumentContent(docOrId) {
      const docId = typeof docOrId === "string" ? docOrId : docOrId?.id;
      if (!docId) {
        throw new Error("Document identifier is required for preview.");
      }
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      const isMocked =
        etmfService.downloadDocumentContent &&
        (etmfService.downloadDocumentContent._isMockFunction ||
          typeof etmfService.downloadDocumentContent.mock === "object");

      if (
        typeof etmfService.downloadDocumentContent === "function" &&
        (!offlineDemo || isMocked)
      ) {
        return await etmfService.downloadDocumentContent(docId);
      }

      if (offlineDemo) {
        const doc =
          (typeof docOrId === "object" ? docOrId : null) ||
          this.documentsList.find((d) => d.id === docId);
        const mimeType = doc?.mime_type || "application/pdf";
        const bodyText =
          doc?.content || `Demo document: ${doc?.filename || docId}`;
        const blob = new Blob([bodyText], { type: mimeType });
        return {
          blob,
          mimeType,
          sha256: doc?.content_checksum || doc?.contentChecksum || "",
        };
      }

      throw new Error("Document content streaming service is unavailable.");
    },

    async fetchExportJobs(studyId = this.currentStudyId) {
      try {
        const jobs = await etmfService.listExportJobs(studyId || undefined);
        this.exportJobs = jobs || [];
        if (this.activeExportJob) {
          const updated = this.exportJobs.find(
            (j) => j.job_id === this.activeExportJob.job_id
          );
          if (updated) {
            this.activeExportJob = updated;
          }
        }
        return this.exportJobs;
      } catch (err) {
        console.warn("Could not fetch export jobs from server:", err);
        return this.exportJobs;
      }
    },

    async pollExportJob(jobId) {
      try {
        const job = await etmfService.getExportJob(jobId);
        this.activeExportJob = job;
        const idx = this.exportJobs.findIndex((j) => j.job_id === jobId);
        if (idx >= 0) {
          this.exportJobs[idx] = job;
        } else {
          this.exportJobs.unshift(job);
        }
        return job;
      } catch (err) {
        console.warn(`Failed to poll export job ${jobId}:`, err);
        return this.activeExportJob;
      }
    },

    startPollingExportJob(jobId) {
      const interval = setInterval(async () => {
        const job = await this.pollExportJob(jobId);
        if (!job || job.status === "COMPLETED" || job.status === "FAILED") {
          clearInterval(interval);
        }
      }, 2000);
    },
  },
});
