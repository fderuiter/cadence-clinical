import { defineStore } from "pinia";
import { etmfService } from "../api/etmf";
import { getSyncedStudyScope, syncActiveStudyScope } from "./studyScopeBridge";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../utils/callerScope";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";
import {
  buildLocalIntelligenceReport,
  fallbackDeriveDemoQcQueue,
  fallbackReviewDemoQcDocument,
  fallbackStageDemoQcDocument,
  fallbackSubmitDemoDraftForQc,
  fetchDemoDocumentContent,
  fetchDemoEtmfDocuments,
  resetDemoEtmfStorage,
  resolveDemoEtmfStudyId,
  uploadDemoEtmfDocument,
} from "./demo/etmfDemo.js";

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
    currentStudyId:
      getSyncedStudyScope() || (isDemoBuild() ? resolveDemoEtmfStudyId() : ""),
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
      } else if (!this.currentStudyId && isDemoBuild()) {
        const demoStudyId = resolveDemoEtmfStudyId();
        if (demoStudyId) {
          this.currentStudyId = demoStudyId;
          syncActiveStudyScope(this.currentStudyId);
        }
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
        if (isDemoBuild()) {
          const demoResult = fetchDemoEtmfDocuments({
            authStore,
            etmfService,
            selectedArtifactId: this.selectedArtifactId,
          });
          if (demoResult?.handled) {
            this.documentsList = demoResult.documentsList;
            return;
          }
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

        if (isDemoBuild()) {
          const demoUpload = await uploadDemoEtmfDocument({
            authStore: useAuthStore(),
            etmfService,
            body,
            changeReason,
            documentsList: this.documentsList,
          });
          if (demoUpload?.handled) {
            if (this.selectedArtifactId) {
              await this.fetchDocuments(this.selectedArtifactId);
            }
            return demoUpload.response;
          }
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
        if (isDemoBuild()) {
          const demoFallback = fallbackSubmitDemoDraftForQc({
            authStore: useAuthStore(),
            etmfService,
            documentsList: this.documentsList,
            docId,
            reason_for_change,
          });
          if (demoFallback?.handled) {
            return demoFallback.result;
          }
        }
        throw err;
      }
    },
    resetDemoStorage() {
      if (!isDemoBuild()) {
        this.documentsList = [];
        return;
      }
      this.documentsList = resetDemoEtmfStorage();
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
      } catch (err) {
        if (isDemoBuild()) {
          return buildLocalIntelligenceReport(filename, content, mimeType);
        }
        throw err;
      }
    },

    async stageQcDocument(payload) {
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
        if (isDemoBuild()) {
          const demoStage = fallbackStageDemoQcDocument({
            authStore: useAuthStore(),
            scopedPayload,
            currentStudyId: this.currentStudyId,
            documentsList: this.documentsList,
            qcQueue: this.qcQueue,
          });
          if (demoStage?.handled) {
            return demoStage.item;
          }
        }
        throw err;
      }
    },

    async fetchQcQueue(
      studyId = this.currentStudyId,
      siteId = this.currentSiteId
    ) {
      const authStore = useAuthStore();
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
        if (isDemoBuild()) {
          const demoQueue = fallbackDeriveDemoQcQueue({
            authStore,
            documentsList: this.documentsList,
          });
          if (demoQueue?.handled) {
            this.qcQueue = demoQueue.qcQueue;
            return this.qcQueue;
          }
        }
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
      } catch (err) {
        if (isDemoBuild()) {
          const demoReview = fallbackReviewDemoQcDocument({
            authStore: useAuthStore(),
            documentsList: this.documentsList,
            qcQueue: this.qcQueue,
            docId,
            decision,
            reason_for_change,
            override_artifact_code,
          });
          if (demoReview?.handled) {
            this.qcQueue = demoReview.qcQueue;
            return demoReview.result;
          }
        }
        throw err;
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
      if (isDemoBuild()) {
        const demoContent = fetchDemoDocumentContent({
          authStore: useAuthStore(),
          etmfService,
          docOrId,
          docId,
          documentsList: this.documentsList,
        });
        if (demoContent?.handled) {
          return demoContent.result;
        }
      }

      if (typeof etmfService.downloadDocumentContent === "function") {
        return await etmfService.downloadDocumentContent(docId);
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
