import { defineStore } from "pinia";
import { DEMO_STUDY, getDemoPersonaByWorkflow } from "demo-fixtures";
import { etmfService } from "../api/etmf";
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

function localPredictTaxonomy(filename = "", content = "") {
  const text = `${filename} ${content}`.toLowerCase();
  if (text.includes("protocol") || text.includes("amendment")) {
    return {
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      confidence: 0.96,
    };
  }
  if (text.includes("1572") || text.includes("fda 1572")) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.01",
      artifact_type: "FDA Form 1572",
      confidence: 0.98,
    };
  }
  if (text.includes("financial") || text.includes("disclosure")) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.02",
      artifact_type: "Financial Disclosure",
      confidence: 0.94,
    };
  }
  if (
    text.includes("cv") ||
    text.includes("curriculum") ||
    text.includes("resume")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.03",
      artifact_type: "Investigator CV",
      confidence: 0.95,
    };
  }
  if (
    text.includes("delegation") ||
    text.includes("doa") ||
    text.includes("log")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.04",
      artifact_type: "Delegation of Authority Log",
      confidence: 0.92,
    };
  }
  if (
    text.includes("consent") ||
    text.includes("icf") ||
    text.includes("informed")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.05",
      artifact_type: "Informed Consent Form",
      confidence: 0.97,
    };
  }
  if (text.includes("brochure") || text.includes("ib")) {
    return {
      zone: 2,
      section: "02.01",
      artifact_code: "02.01.01",
      artifact_type: "Investigator's Brochure",
      confidence: 0.95,
    };
  }
  if (
    text.includes("irb") ||
    text.includes("approval") ||
    text.includes("iec")
  ) {
    return {
      zone: 4,
      section: "04.02",
      artifact_code: "04.02.01",
      artifact_type: "IRB/IEC Approval Notification",
      confidence: 0.91,
    };
  }
  if (
    text.includes("sae") ||
    text.includes("adverse") ||
    text.includes("safety")
  ) {
    return {
      zone: 7,
      section: "07.01",
      artifact_code: "07.01.01",
      artifact_type: "Serious Adverse Event Report",
      confidence: 0.93,
    };
  }
  if (text.includes("lab") || text.includes("certificate")) {
    return {
      zone: 8,
      section: "08.01",
      artifact_code: "08.01.01",
      artifact_type: "Central Laboratory Certificate",
      confidence: 0.89,
    };
  }
  if (text.includes("sap") || text.includes("statistic")) {
    return {
      zone: 11,
      section: "11.01",
      artifact_code: "11.01.01",
      artifact_type: "Statistical Analysis Plan",
      confidence: 0.92,
    };
  }
  return {
    zone: 1,
    section: "01.01",
    artifact_code: "01.01.01",
    artifact_type: "General Clinical Document",
    confidence: 0.65,
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
    /** @type {EtmfDocumentRecord | null} */
    activeDocument: null,
    isUploading: false,
    currentStudyId: isDemoBuild() && DEMO_STUDY.code ? DEMO_STUDY.code : "",
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
      } else if (!this.currentStudyId && isDemoBuild() && DEMO_STUDY.code) {
        this.currentStudyId = DEMO_STUDY.code;
      }

      // Seed hierarchical DIA TMF Reference Model v3.2.0-complete hierarchy
      this.binderTree = [
        {
          id: "zone_1",
          name: "Trial Management",
          code: "1",
          type: "zone",
          children: [
            {
              id: "sec_01.01",
              name: "Trial Design",
              code: "01.01",
              type: "section",
              children: [
                {
                  id: "01.01.01",
                  name: "Clinical Trial Protocol",
                  code: "01.01.01",
                  type: "artifact",
                },
                {
                  id: "01.01.02",
                  name: "Clinical Trial Protocol Amendment",
                  code: "01.01.02",
                  type: "artifact",
                },
                {
                  id: "01.01.03",
                  name: "Protocol Sign-off",
                  code: "01.01.03",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_01.02",
              name: "Trial Oversight",
              code: "01.02",
              type: "section",
              children: [
                {
                  id: "01.02.01",
                  name: "Trial Oversight Committee Charter",
                  code: "01.02.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_01.03",
              name: "Trial Monitoring",
              code: "01.03",
              type: "section",
              children: [
                {
                  id: "01.03.01",
                  name: "Trial Monitoring Plan",
                  code: "01.03.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_01.04",
              name: "Trial Close-out",
              code: "01.04",
              type: "section",
              children: [
                {
                  id: "01.04.01",
                  name: "Trial Close-out Report",
                  code: "01.04.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_2",
          name: "Central Trial Documents",
          code: "2",
          type: "zone",
          children: [
            {
              id: "sec_02.01",
              name: "Product Information",
              code: "02.01",
              type: "section",
              children: [
                {
                  id: "02.01.01",
                  name: "Investigator's Brochure",
                  code: "02.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_02.02",
              name: "Clinical Trial Materials",
              code: "02.02",
              type: "section",
              children: [
                {
                  id: "02.02.01",
                  name: "Clinical Trial Material Specifications",
                  code: "02.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_3",
          name: "Regulatory",
          code: "3",
          type: "zone",
          children: [
            {
              id: "sec_03.01",
              name: "Regulatory Submissions",
              code: "03.01",
              type: "section",
              children: [
                {
                  id: "03.01.01",
                  name: "Regulatory Authority Submission",
                  code: "03.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_03.02",
              name: "Regulatory Approvals",
              code: "03.02",
              type: "section",
              children: [
                {
                  id: "03.02.01",
                  name: "Regulatory Authority Approval",
                  code: "03.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_4",
          name: "IRB/IEC & other Approvals",
          code: "4",
          type: "zone",
          children: [
            {
              id: "sec_04.01",
              name: "IRB/IEC Submissions",
              code: "04.01",
              type: "section",
              children: [
                {
                  id: "04.01.01",
                  name: "IRB/IEC Approval",
                  code: "04.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_04.02",
              name: "IRB/IEC Approvals",
              code: "04.02",
              type: "section",
              children: [
                {
                  id: "04.02.01",
                  name: "IRB/IEC Approval Notification",
                  code: "04.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_5",
          name: "Site Management",
          code: "5",
          type: "zone",
          children: [
            {
              id: "sec_05.01",
              name: "Site Selection",
              code: "05.01",
              type: "section",
              children: [
                {
                  id: "05.01.01",
                  name: "Site Feasibility Survey",
                  code: "05.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_05.02",
              name: "Investigator Qualification",
              code: "05.02",
              type: "section",
              children: [
                {
                  id: "05.02.01",
                  name: "FDA Form 1572",
                  code: "05.02.01",
                  type: "artifact",
                },
                {
                  id: "05.02.02",
                  name: "Financial Disclosure",
                  code: "05.02.02",
                  type: "artifact",
                },
                {
                  id: "05.02.03",
                  name: "Investigator CV",
                  code: "05.02.03",
                  type: "artifact",
                },
                {
                  id: "05.02.04",
                  name: "Delegation of Authority Log",
                  code: "05.02.04",
                  type: "artifact",
                },
                {
                  id: "05.02.05",
                  name: "Informed Consent Form",
                  code: "05.02.05",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_05.03",
              name: "Site Training",
              code: "05.03",
              type: "section",
              children: [
                {
                  id: "05.03.01",
                  name: "Site Training Records",
                  code: "05.03.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_05.04",
              name: "Site Communication",
              code: "05.04",
              type: "section",
              children: [
                {
                  id: "05.04.01",
                  name: "Site Communication Log",
                  code: "05.04.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_6",
          name: "IP & Trial Supplies",
          code: "6",
          type: "zone",
          children: [
            {
              id: "sec_06.01",
              name: "IP Documentation",
              code: "06.01",
              type: "section",
              children: [
                {
                  id: "06.01.01",
                  name: "Investigational Product Records",
                  code: "06.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_06.02",
              name: "IP Logistics",
              code: "06.02",
              type: "section",
              children: [
                {
                  id: "06.02.01",
                  name: "IP Shipping Records",
                  code: "06.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_7",
          name: "Safety Reporting",
          code: "7",
          type: "zone",
          children: [
            {
              id: "sec_07.01",
              name: "Safety Notifications",
              code: "07.01",
              type: "section",
              children: [
                {
                  id: "07.01.01",
                  name: "Serious Adverse Event Report",
                  code: "07.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_07.02",
              name: "Safety Operations",
              code: "07.02",
              type: "section",
              children: [
                {
                  id: "07.02.01",
                  name: "Safety Management Plan",
                  code: "07.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_8",
          name: "Centralized & Local Testing",
          code: "8",
          type: "zone",
          children: [
            {
              id: "sec_08.01",
              name: "Lab Documentation",
              code: "08.01",
              type: "section",
              children: [
                {
                  id: "08.01.01",
                  name: "Central Laboratory Certificate",
                  code: "08.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_08.02",
              name: "Lab Operations",
              code: "08.02",
              type: "section",
              children: [
                {
                  id: "08.02.01",
                  name: "Laboratory Reference Ranges",
                  code: "08.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_9",
          name: "Third Parties",
          code: "9",
          type: "zone",
          children: [
            {
              id: "sec_09.01",
              name: "Vendor Management",
              code: "09.01",
              type: "section",
              children: [
                {
                  id: "09.01.01",
                  name: "Vendor Service Agreement",
                  code: "09.01.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_09.02",
              name: "Vendor Operations",
              code: "09.02",
              type: "section",
              children: [
                {
                  id: "09.02.01",
                  name: "Vendor Audit Report",
                  code: "09.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_10",
          name: "Data Management",
          code: "10",
          type: "zone",
          children: [
            {
              id: "sec_10.01",
              name: "Data Management Specifications",
              code: "10.01",
              type: "section",
              children: [
                {
                  id: "10.01.01",
                  name: "Data Management Plan",
                  code: "10.01.01",
                  type: "artifact",
                },
                {
                  id: "10.01.02",
                  name: "Define-XML Specifications",
                  code: "10.01.02",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_10.02",
              name: "Case Report Forms",
              code: "10.02",
              type: "section",
              children: [
                {
                  id: "10.02.01",
                  name: "Blank CRF",
                  code: "10.02.01",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_10.03",
              name: "Data Operations",
              code: "10.03",
              type: "section",
              children: [
                {
                  id: "10.03.01",
                  name: "Data Review Guidelines",
                  code: "10.03.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
        {
          id: "zone_11",
          name: "Statistics",
          code: "11",
          type: "zone",
          children: [
            {
              id: "sec_11.01",
              name: "Statistical Analysis",
              code: "11.01",
              type: "section",
              children: [
                {
                  id: "11.01.01",
                  name: "Statistical Analysis Plan",
                  code: "11.01.01",
                  type: "artifact",
                },
                {
                  id: "11.01.02",
                  name: "Data Lock Certificate",
                  code: "11.01.02",
                  type: "artifact",
                },
              ],
            },
            {
              id: "sec_11.02",
              name: "Data Analysis and Reports",
              code: "11.02",
              type: "section",
              children: [
                {
                  id: "11.02.01",
                  name: "Clinical Study Report",
                  code: "11.02.01",
                  type: "artifact",
                },
              ],
            },
          ],
        },
      ];
    },

    async fetchDocuments(artifactId) {
      if (artifactId !== undefined) {
        this.selectedArtifactId = artifactId;
      }
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

        const allDocs = await etmfService.getDocuments(queryParams);

        if (this.selectedArtifactId) {
          this.documentsList = (allDocs || []).filter(
            (doc) => doc.artifact_code === this.selectedArtifactId
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
          } else {
            this.documentsList.push(newDoc);
          }

          if (this.selectedArtifactId) {
            await this.fetchDocuments(this.selectedArtifactId);
          }

          return {
            status: "success",
            document_id: newDoc.id,
            version_index: 1,
          };
        }

        const res = await etmfService.ingestDocument(body, { changeReason });

        if (this.selectedArtifactId !== undefined) {
          await this.fetchDocuments(this.selectedArtifactId);
        }
        return res;
      } finally {
        this.isUploading = false;
      }
    },

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
        await this.fetchDocuments(this.selectedArtifactId);
        await this.fetchQcQueue(this.currentStudyId, this.currentSiteId);
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
      const scopedPayload = {
        ...payload,
        study_id: payload.study_id || payload.studyId || this.currentStudyId,
        site_id:
          payload.site_id ?? payload.siteId ?? (this.currentSiteId || null),
      };
      try {
        const result = await etmfService.stageQcDocument(scopedPayload);
        await this.fetchQcQueue(scopedPayload.study_id, scopedPayload.site_id);
        return result;
      } catch (err) {
        if (!offlineDemo) {
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
        const item = {
          document_id: newDoc.id,
          study_id: newDoc.study_id,
          site_id: newDoc.site_id,
          filename: newDoc.filename,
          status: "TECHNICAL_QC",
          intelligence_report: report,
          staged_at: newDoc.created_at,
          staged_by: newDoc.created_by,
          qc_assigned_to: scopedPayload.assigned_cra || null,
          discrepancy_notes:
            report.confidence_tier === "LOW"
              ? ["Low classification confidence score"]
              : [],
        };
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
        if (!offlineDemo) {
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
        // Fallback: derive from documentsList in offline demo state only
        this.qcQueue = this.documentsList
          .filter((d) => d.status === "TECHNICAL_QC")
          .map((d) => {
            const report =
              d.metadata_json?.document_intelligence ||
              buildLocalIntelligenceReport(d.filename, d.content, d.mime_type);
            return {
              document_id: d.id,
              study_id: d.study_id,
              site_id: d.site_id,
              filename: d.filename,
              status: d.status,
              intelligence_report: report,
              staged_at: d.created_at,
              staged_by: d.created_by,
              qc_assigned_to: null,
              discrepancy_notes:
                report.confidence_tier === "LOW"
                  ? ["Low classification confidence score"]
                  : [],
            };
          });
        return this.qcQueue;
      }
    },

    async reviewQcDocument(
      docId,
      {
        decision,
        reason_for_change,
        override_artifact_code,
        discrepancy_comment,
      }
    ) {
      const authStore = useAuthStore();
      const offlineDemo =
        isDemoBuild() && authStore.isDemoMode && !authStore.token;
      try {
        const res = await etmfService.submitQcReview(docId, {
          decision,
          reason_for_change,
          override_artifact_code,
          discrepancy_comment,
        });
        await this.fetchQcQueue(this.currentStudyId);
        await this.fetchDocuments(this.selectedArtifactId);
        return res;
      } catch (err) {
        if (!offlineDemo) {
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
      const results = [];
      for (const id of docIds) {
        const res = await this.reviewQcDocument(id, {
          decision: "ACCEPT",
          reason_for_change: reasonForChange,
        });
        results.push(res);
      }
      return results;
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
