import { defineStore } from "pinia";
import { etmfService } from "../api/etmf";

export const useEtmfStore = defineStore("etmf", {
  state: () => ({
    binderTree: [],
    selectedArtifactId: null,
    documentsList: [],
    activeDocument: null,
    isUploading: false,
    currentStudyId: "STUDY-USDM-001",
  }),

  getters: {
    documents: (state) => state.documentsList,
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
                { id: "01.01.01", name: "Clinical Trial Protocol", code: "01.01.01", type: "artifact" },
                { id: "01.01.02", name: "Clinical Trial Protocol Amendment", code: "01.01.02", type: "artifact" },
                { id: "01.01.03", name: "Protocol Sign-off", code: "01.01.03", type: "artifact" },
              ],
            },
            {
              id: "sec_01.02",
              name: "Trial Oversight",
              code: "01.02",
              type: "section",
              children: [
                { id: "01.02.01", name: "Trial Oversight Committee Charter", code: "01.02.01", type: "artifact" },
              ],
            },
            {
              id: "sec_01.03",
              name: "Trial Monitoring",
              code: "01.03",
              type: "section",
              children: [
                { id: "01.03.01", name: "Trial Monitoring Plan", code: "01.03.01", type: "artifact" },
              ],
            },
            {
              id: "sec_01.04",
              name: "Trial Close-out",
              code: "01.04",
              type: "section",
              children: [
                { id: "01.04.01", name: "Trial Close-out Report", code: "01.04.01", type: "artifact" },
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
                { id: "02.01.01", name: "Investigator's Brochure", code: "02.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_02.02",
              name: "Clinical Trial Materials",
              code: "02.02",
              type: "section",
              children: [
                { id: "02.02.01", name: "Clinical Trial Material Specifications", code: "02.02.01", type: "artifact" },
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
                { id: "03.01.01", name: "Regulatory Authority Submission", code: "03.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_03.02",
              name: "Regulatory Approvals",
              code: "03.02",
              type: "section",
              children: [
                { id: "03.02.01", name: "Regulatory Authority Approval", code: "03.02.01", type: "artifact" },
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
                { id: "04.01.01", name: "IRB/IEC Approval", code: "04.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_04.02",
              name: "IRB/IEC Approvals",
              code: "04.02",
              type: "section",
              children: [
                { id: "04.02.01", name: "IRB/IEC Approval Notification", code: "04.02.01", type: "artifact" },
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
                { id: "05.01.01", name: "Site Feasibility Survey", code: "05.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_05.02",
              name: "Investigator Qualification",
              code: "05.02",
              type: "section",
              children: [
                { id: "05.02.01", name: "FDA Form 1572", code: "05.02.01", type: "artifact" },
                { id: "05.02.02", name: "Financial Disclosure", code: "05.02.02", type: "artifact" },
                { id: "05.02.03", name: "Investigator CV", code: "05.02.03", type: "artifact" },
                { id: "05.02.04", name: "Delegation of Authority Log", code: "05.02.04", type: "artifact" },
                { id: "05.02.05", name: "Informed Consent Form", code: "05.02.05", type: "artifact" },
              ],
            },
            {
              id: "sec_05.03",
              name: "Site Training",
              code: "05.03",
              type: "section",
              children: [
                { id: "05.03.01", name: "Site Training Records", code: "05.03.01", type: "artifact" },
              ],
            },
            {
              id: "sec_05.04",
              name: "Site Communication",
              code: "05.04",
              type: "section",
              children: [
                { id: "05.04.01", name: "Site Communication Log", code: "05.04.01", type: "artifact" },
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
                { id: "06.01.01", name: "Investigational Product Records", code: "06.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_06.02",
              name: "IP Logistics",
              code: "06.02",
              type: "section",
              children: [
                { id: "06.02.01", name: "IP Shipping Records", code: "06.02.01", type: "artifact" },
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
                { id: "07.01.01", name: "Serious Adverse Event Report", code: "07.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_07.02",
              name: "Safety Operations",
              code: "07.02",
              type: "section",
              children: [
                { id: "07.02.01", name: "Safety Management Plan", code: "07.02.01", type: "artifact" },
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
                { id: "08.01.01", name: "Central Laboratory Certificate", code: "08.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_08.02",
              name: "Lab Operations",
              code: "08.02",
              type: "section",
              children: [
                { id: "08.02.01", name: "Laboratory Reference Ranges", code: "08.02.01", type: "artifact" },
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
                { id: "09.01.01", name: "Vendor Service Agreement", code: "09.01.01", type: "artifact" },
              ],
            },
            {
              id: "sec_09.02",
              name: "Vendor Operations",
              code: "09.02",
              type: "section",
              children: [
                { id: "09.02.01", name: "Vendor Audit Report", code: "09.02.01", type: "artifact" },
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
                { id: "10.01.01", name: "Data Management Plan", code: "10.01.01", type: "artifact" },
                { id: "10.01.02", name: "Define-XML Specifications", code: "10.01.02", type: "artifact" },
              ],
            },
            {
              id: "sec_10.02",
              name: "Case Report Forms",
              code: "10.02",
              type: "section",
              children: [
                { id: "10.02.01", name: "Blank CRF", code: "10.02.01", type: "artifact" },
              ],
            },
            {
              id: "sec_10.03",
              name: "Data Operations",
              code: "10.03",
              type: "section",
              children: [
                { id: "10.03.01", name: "Data Review Guidelines", code: "10.03.01", type: "artifact" },
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
                { id: "11.01.01", name: "Statistical Analysis Plan", code: "11.01.01", type: "artifact" },
                { id: "11.01.02", name: "Data Lock Certificate", code: "11.01.02", type: "artifact" },
              ],
            },
            {
              id: "sec_11.02",
              name: "Data Analysis and Reports",
              code: "11.02",
              type: "section",
              children: [
                { id: "11.02.01", name: "Clinical Study Report", code: "11.02.01", type: "artifact" },
              ],
            },
          ],
        },
      ];
    },

    async fetchDocuments(artifactId) {
      this.selectedArtifactId = artifactId;
      try {
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
        console.error("Failed to fetch documents for artifact:", artifactId, err);
        this.documentsList = [];
      }
    },

    async uploadDocument(formData) {
      this.isUploading = true;
      try {
        let body = {};
        let changeReason = "Initial upload";

        if (formData instanceof FormData) {
          changeReason = formData.get("reason_for_change") || "Initial upload";
          body = {
            study_id: formData.get("study_id") || this.currentStudyId,
            site_id: formData.get("site_id") || null,
            artifact_type: formData.get("artifact_type") || "Clinical Trial Protocol",
            filename: formData.get("filename") || "document.pdf",
            content: formData.get("content") || "Mock base64 or plaintext content",
            mime_type: formData.get("mime_type") || "application/pdf",
            artifact_code: formData.get("artifact_code") || this.selectedArtifactId || "01.01.01",
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
            artifact_code: formData.artifact_code || this.selectedArtifactId || "01.01.01",
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
  },
});
