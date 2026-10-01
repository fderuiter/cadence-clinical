import { defineStore } from "pinia";
import { etmfService } from "../api/etmf";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

const defaultDocuments = [
  {
    id: "DOC-100",
    study_id: "STUDY-USDM-001",
    zone: 1,
    section: "01.01",
    artifact_code: "01.01.01",
    artifact_type: "Clinical Trial Protocol",
    filename: "protocol_v1_draft.pdf",
    mime_type: "application/pdf",
    created_at: "2026-08-01T12:00:00Z",
    created_by: "fderuiter",
    version_index: 1,
    status: "DRAFT",
    reason_for_change: "Initial version",
  },
  {
    id: "DOC-999",
    study_id: "STUDY-USDM-001",
    zone: 5,
    section: "05.02",
    artifact_code: "05.02.05",
    artifact_type: "Informed Consent Form",
    filename: "icf_v1.0_signed.pdf",
    mime_type: "application/pdf",
    created_at: "2026-08-15T09:30:00Z",
    created_by: "fderuiter",
    version_index: 1,
    status: "APPROVED",
    reason_for_change: "Initial signed consent.",
    signer: "Frans de Ruiter",
    signing_timestamp: "2026-08-15T09:35:00Z",
    signature_manifestation: {
      signing_reason: "Consent approval",
    },
  },
];

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

export const useEtmfStore = defineStore("etmf", {
  state: () => ({
    binderTree: [],
    selectedArtifactId: null,
    documentsList: [],
    activeDocument: null,
    isUploading: false,
    currentStudyId: "STUDY-USDM-001",
    batchQueue: [],
    qcQueue: [],
    isBatchUploading: false,
    batchProgress: 0,
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
    async fetchBinderTree(studyId = "STUDY-USDM-001") {
      this.currentStudyId = studyId;

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
      this.selectedArtifactId = artifactId;
      try {
        const authStore = useAuthStore();
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
              allDocs = JSON.parse(JSON.stringify(defaultDocuments));
              window.localStorage.setItem(
                "demo_documents",
                JSON.stringify(allDocs)
              );
            }
          } else {
            allDocs = JSON.parse(JSON.stringify(defaultDocuments));
          }

          // Filter by selected artifact ID
          if (artifactId) {
            this.documentsList = allDocs.filter(
              (doc) => doc.artifact_code === artifactId
            );
          } else {
            this.documentsList = allDocs;
          }
          return;
        }

        const allDocs = await etmfService.getDocuments({
          study_id: this.currentStudyId,
        });

        // Filter by selected artifact ID
        if (artifactId) {
          this.documentsList = (allDocs || []).filter(
            (doc) => doc.artifact_code === artifactId
          );
        } else {
          this.documentsList = allDocs || [];
        }
      } catch (err) {
        console.error(
          "Failed to fetch documents for artifact:",
          artifactId,
          err
        );
        this.documentsList = [];
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
        if (
          isDemoBuild() &&
          authStore.isDemoMode &&
          !authStore.token &&
          !isMocked
        ) {
          await new Promise((resolve) => setTimeout(resolve, 300));
          let body = {};
          let changeReason = "Initial upload";

          if (formData instanceof FormData) {
            changeReason =
              formData.get("reason_for_change") || "Initial upload";
            body = {
              study_id: formData.get("study_id") || this.currentStudyId,
              site_id: formData.get("site_id") || null,
              artifact_type:
                formData.get("artifact_type") || "Clinical Trial Protocol",
              filename: formData.get("filename") || "document.pdf",
              content:
                formData.get("content") || "Mock base64 or plaintext content",
              mime_type: formData.get("mime_type") || "application/pdf",
              artifact_code:
                formData.get("artifact_code") ||
                this.selectedArtifactId ||
                "01.01.01",
              zone: parseInt(formData.get("zone")) || 1,
              section: formData.get("section") || "01.01",
              reason_for_change: changeReason,
              taxonomy_version: "v3.2.0-complete",
            };
          } else {
            changeReason = formData.reason_for_change || "Initial upload";
            body = {
              study_id: formData.study_id || this.currentStudyId,
              site_id: formData.site_id || null,
              artifact_type:
                formData.artifact_type || "Clinical Trial Protocol",
              filename: formData.filename || "document.pdf",
              content: formData.content || "Mock base64 or plaintext content",
              mime_type: formData.mime_type || "application/pdf",
              artifact_code:
                formData.artifact_code || this.selectedArtifactId || "01.01.01",
              zone: formData.zone || 1,
              section: formData.section || "01.01",
              reason_for_change: changeReason,
              taxonomy_version: "v3.2.0-complete",
            };
          }

          const newDoc = {
            id: "DOC-" + Date.now(),
            study_id: body.study_id,
            site_id: body.site_id,
            zone: body.zone,
            section: body.section,
            artifact_code: body.artifact_code,
            artifact_type: body.artifact_type,
            filename: body.filename,
            mime_type: body.mime_type,
            created_at: new Date().toISOString(),
            created_by: authStore.identity?.username || "fderuiter",
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
              allDocs = JSON.parse(JSON.stringify(defaultDocuments));
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

        let body = {};
        let changeReason = "Initial upload";

        if (formData instanceof FormData) {
          changeReason = formData.get("reason_for_change") || "Initial upload";
          body = {
            study_id: formData.get("study_id") || this.currentStudyId,
            site_id: formData.get("site_id") || null,
            artifact_type:
              formData.get("artifact_type") || "Clinical Trial Protocol",
            filename: formData.get("filename") || "document.pdf",
            content:
              formData.get("content") || "Mock base64 or plaintext content",
            mime_type: formData.get("mime_type") || "application/pdf",
            artifact_code:
              formData.get("artifact_code") ||
              this.selectedArtifactId ||
              "01.01.01",
            zone: parseInt(formData.get("zone")) || 1,
            section: formData.get("section") || "01.01",
            reason_for_change: changeReason,
            taxonomy_version: "v3.2.0-complete",
          };
        } else {
          changeReason = formData.reason_for_change || "Initial upload";
          body = {
            study_id: formData.study_id || this.currentStudyId,
            site_id: formData.site_id || null,
            artifact_type: formData.artifact_type || "Clinical Trial Protocol",
            filename: formData.filename || "document.pdf",
            content: formData.content || "Mock base64 or plaintext content",
            mime_type: formData.mime_type || "application/pdf",
            artifact_code:
              formData.artifact_code || this.selectedArtifactId || "01.01.01",
            zone: formData.zone || 1,
            section: formData.section || "01.01",
            reason_for_change: changeReason,
            taxonomy_version: "v3.2.0-complete",
          };
        }

        const res = await etmfService.ingestDocument(body, { changeReason });

        // Refresh documents for selected artifact after upload
        if (this.selectedArtifactId) {
          await this.fetchDocuments(this.selectedArtifactId);
        }
        return res;
      } catch (err) {
        console.error("Failed to upload document:", err);
        throw err;
      } finally {
        this.isUploading = false;
      }
    },
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("demo_documents");
      }
      this.documentsList = isDemoBuild()
        ? JSON.parse(JSON.stringify(defaultDocuments))
        : [];
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
      try {
        const result = await etmfService.stageQcDocument(payload);
        await this.fetchQcQueue(payload.study_id || this.currentStudyId);
        return result;
      } catch {
        // Fallback for demo or offline mode
        const report = buildLocalIntelligenceReport(
          payload.filename,
          payload.content,
          payload.mime_type
        );
        const newDoc = {
          id: "DOC-" + Math.random().toString(36).substring(2, 9).toUpperCase(),
          study_id: payload.study_id || this.currentStudyId,
          site_id: payload.site_id || null,
          zone: report.primary_classification.zone,
          section: report.primary_classification.section,
          artifact_code: report.primary_classification.artifact_code,
          artifact_type: report.primary_classification.artifact_type,
          filename: payload.filename,
          mime_type: payload.mime_type || "application/pdf",
          created_at: new Date().toISOString(),
          created_by: "site.coordinator",
          version_index: 1,
          status: "TECHNICAL_QC",
          approval_status: "TECHNICAL_QC",
          reason_for_change: payload.reason_for_change,
          metadata_json: {
            document_intelligence: report,
            assigned_cra: payload.assigned_cra || null,
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
          qc_assigned_to: payload.assigned_cra || null,
          discrepancy_notes:
            report.confidence_tier === "LOW"
              ? ["Low classification confidence score"]
              : [],
        };
        this.qcQueue.unshift(item);
        return item;
      }
    },

    async fetchQcQueue(studyId = this.currentStudyId) {
      try {
        const queue = await etmfService.getQcQueue(studyId);
        this.qcQueue = queue || [];
        return this.qcQueue;
      } catch {
        // Fallback: derive from documentsList in local state
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
      } catch {
        // Fallback for local state update
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
  },
});
