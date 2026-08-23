import { defineStore } from "pinia";
import { fileshareService } from "../api/fileshare";
import { useAuthStore } from "./auth";

/**
 * Standard DIA TMF Reference Model Zones & Sections template
 */
export const TMF_TAXONOMY_SECTIONS = [
  {
    code: "01",
    name: "Trial Management",
    sections: [
      { code: "01.01", name: "Trial Oversight & Protocol", folders: ["01.01.01 Protocol", "01.01.02 Amendments"] },
      { code: "01.02", name: "Trial Committee & Governance", folders: ["01.02.01 Charter", "01.02.02 Minutes"] },
    ],
  },
  {
    code: "02",
    name: "Central Trial Documents",
    sections: [
      { code: "02.01", name: "Investigator Brochure", folders: ["02.01.01 IB Edition", "02.01.02 Updates"] },
      { code: "02.02", name: "Core Informed Consent", folders: ["02.02.01 Master ICF", "02.02.02 Translated ICF"] },
    ],
  },
  {
    code: "03",
    name: "Regulatory",
    sections: [
      { code: "03.01", name: "Competent Authority", folders: ["03.01.01 IND/CTA Submission", "03.01.02 Approvals"] },
      { code: "03.02", name: "IRB / IEC Governance", folders: ["03.02.01 Submissions", "03.02.02 Approvals"] },
    ],
  },
  {
    code: "04",
    name: "Site Management",
    sections: [
      { code: "04.01", name: "Feasibility & Selection", folders: ["04.01.01 Questionnaire", "04.01.02 Report"] },
      { code: "04.02", name: "Site Initiation & Training", folders: ["04.02.01 SIV Report", "04.02.02 Delegation Log (DOA)"] },
    ],
  },
  {
    code: "05",
    name: "Clinical Operations & Data",
    sections: [
      { code: "05.01", name: "Subject Eligibility", folders: ["05.01.01 Screening Log", "05.01.02 Enrollment Log"] },
      { code: "05.02", name: "eCRF & Source Records", folders: ["05.02.01 Signed eCRF", "05.02.02 Lab Diagnostics"] },
    ],
  },
];

/**
 * Standard studies and site hierarchy catalog
 */
export const DEFAULT_STUDIES = [
  {
    id: "STUDY-USDM-001",
    title: "Phase II Trial of Cadence-001 in Essential Hypertension",
    code: "CAD-HTN-201",
    sites: [
      { id: "SITE-101", name: "Site 101 - St. Jude Medical Center", pi: "Dr. Sarah Jenkins" },
      { id: "SITE-102", name: "Site 102 - Mayo Clinical Research", pi: "Dr. Robert Vance" },
      { id: "SITE-103", name: "Site 103 - Johns Hopkins Oncology & Cardio", pi: "Dr. Emily Chen" },
    ],
  },
  {
    id: "STUDY-USDM-002",
    title: "Phase III Evaluation of Cadence-002 in Type 2 Diabetes Mellitus",
    code: "CAD-T2D-301",
    sites: [
      { id: "SITE-201", name: "Site 201 - Cleveland Clinic Diabetes Center", pi: "Dr. Michael Chang" },
      { id: "SITE-202", name: "Site 202 - Stanford University Medical Center", pi: "Dr. Alice Morgan" },
    ],
  },
  {
    id: "STUDY-USDM-003",
    title: "Observational Registry on Cardiovascular Outcomes in CAD-003",
    code: "CAD-CVR-401",
    sites: [
      { id: "SITE-301", name: "Site 301 - Brigham and Women's Hospital", pi: "Dr. David Brooks" },
    ],
  },
];

/**
 * Seed mock files for rich initial demo state across studies
 */
export const SEED_FILES = [
  {
    id: "file-cad-001",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Clinical_Trial_Protocol_v2.0_Final_Signed.pdf",
    mime_type: "application/pdf",
    size_bytes: 4285120,
    object_key: "tenant_default/STUDY-USDM-001/file-cad-001/Clinical_Trial_Protocol_v2.0_Final_Signed.pdf",
    checksum_sha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    version_index: 2,
    uploaded_by: "sponsor_designer_01",
    uploaded_at: "2026-08-10T14:32:00Z",
    is_on_hold: false,
    status: "FINAL_APPROVED",
    zone_code: "01",
    section_code: "01.01",
    folder_code: "01.01.01",
    grant_count: 3,
    created_at: "2026-08-10T14:32:00Z",
    created_by: "sponsor_designer_01",
    reason_for_change: "Approved protocol amendment v2.0 incorporation",
  },
  {
    id: "file-cad-002",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Informed_Consent_Form_Template_v2.0.pdf",
    mime_type: "application/pdf",
    size_bytes: 1852400,
    object_key: "tenant_default/STUDY-USDM-001/file-cad-002/Informed_Consent_Form_Template_v2.0.pdf",
    checksum_sha256: "f4a8b79284e9c71a3d6e5c8291b0f3e7a6d5c4b3a29182736450192837465abc",
    version_index: 2,
    uploaded_by: "sponsor_designer_01",
    uploaded_at: "2026-08-11T09:15:00Z",
    is_on_hold: false,
    status: "ACTIVE",
    zone_code: "02",
    section_code: "02.02",
    folder_code: "02.02.01",
    grant_count: 2,
    created_at: "2026-08-11T09:15:00Z",
    created_by: "sponsor_designer_01",
    reason_for_change: "Annual IRB re-consent approval update",
  },
  {
    id: "file-cad-003",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Site_Initiation_Training_Video.mp4",
    mime_type: "video/mp4",
    size_bytes: 52428800,
    object_key: "tenant_default/STUDY-USDM-001/file-cad-003/Site_Initiation_Training_Video.mp4",
    checksum_sha256: "a1b2c3d4e5f60718293a4b5c6d7e8f90123456789abcdef0123456789abcdef0",
    version_index: 1,
    uploaded_by: "cra_fderuiter",
    uploaded_at: "2026-08-12T16:45:00Z",
    is_on_hold: false,
    status: "ACTIVE",
    zone_code: "04",
    section_code: "04.02",
    folder_code: "04.02.01",
    grant_count: 4,
    created_at: "2026-08-12T16:45:00Z",
    created_by: "cra_fderuiter",
    reason_for_change: "Investigator site initiation walkthrough training asset",
  },
  {
    id: "file-cad-004",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-102",
    filename: "Delegation_of_Authority_Log_Signed.pdf",
    mime_type: "application/pdf",
    size_bytes: 845200,
    object_key: "tenant_default/STUDY-USDM-001/file-cad-004/Delegation_of_Authority_Log_Signed.pdf",
    checksum_sha256: "9876543210abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    version_index: 1,
    uploaded_by: "crc_user_102",
    uploaded_at: "2026-08-14T11:20:00Z",
    is_on_hold: false,
    status: "VERIFIED",
    zone_code: "04",
    section_code: "04.02",
    folder_code: "04.02.02",
    grant_count: 1,
    created_at: "2026-08-14T11:20:00Z",
    created_by: "crc_user_102",
    reason_for_change: "Site 102 staff onboarding and role delegation sign-off",
  },
  {
    id: "file-cad-005",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Subject_101_Baseline_Echocardiogram.png",
    mime_type: "image/png",
    size_bytes: 3145728,
    object_key: "tenant_default/STUDY-USDM-001/file-cad-005/Subject_101_Baseline_Echocardiogram.png",
    checksum_sha256: "11223344556677889900aabbccddeeff11223344556677889900aabbccddeeff",
    version_index: 1,
    uploaded_by: "crc_user_101",
    uploaded_at: "2026-08-15T08:30:00Z",
    is_on_hold: false,
    status: "ACTIVE",
    zone_code: "05",
    section_code: "05.02",
    folder_code: "05.02.02",
    grant_count: 0,
    created_at: "2026-08-15T08:30:00Z",
    created_by: "crc_user_101",
    reason_for_change: "Source diagnostic telemetry image attachment",
  },
  {
    id: "file-cad-006",
    study_id: "STUDY-USDM-002",
    site_id: "SITE-201",
    filename: "Investigator_Brochure_Edition_7.pdf",
    mime_type: "application/pdf",
    size_bytes: 6540100,
    object_key: "tenant_default/STUDY-USDM-002/file-cad-006/Investigator_Brochure_Edition_7.pdf",
    checksum_sha256: "2233445566778899aabbccddeeff00112233445566778899aabbccddeeff0011",
    version_index: 7,
    uploaded_by: "sponsor_admin",
    uploaded_at: "2026-08-01T10:00:00Z",
    is_on_hold: false,
    status: "FINAL_APPROVED",
    zone_code: "02",
    section_code: "02.01",
    folder_code: "02.01.01",
    grant_count: 5,
    created_at: "2026-08-01T10:00:00Z",
    created_by: "sponsor_admin",
    reason_for_change: "Edition 7 annual safety and toxicology update",
  },
  {
    id: "file-cad-007",
    study_id: "STUDY-USDM-002",
    site_id: "SITE-202",
    filename: "Safety_Committee_Audio_Debrief.mp3",
    mime_type: "audio/mpeg",
    size_bytes: 12582912,
    object_key: "tenant_default/STUDY-USDM-002/file-cad-007/Safety_Committee_Audio_Debrief.mp3",
    checksum_sha256: "33445566778899aabbccddeeff00112233445566778899aabbccddeeff001122",
    version_index: 1,
    uploaded_by: "data_manager_01",
    uploaded_at: "2026-08-16T15:10:00Z",
    is_on_hold: false,
    status: "CONFIDENTIAL",
    zone_code: "01",
    section_code: "01.02",
    folder_code: "01.02.02",
    grant_count: 2,
    created_at: "2026-08-16T15:10:00Z",
    created_by: "data_manager_01",
    reason_for_change: "DSMB interim unblinded safety meeting audio recording",
  },
];

export const useFileshareStore = defineStore("fileshare", {
  state: () => ({
    files: [...SEED_FILES],
    isLoading: false,
    error: null,
    activeFilters: {
      studyId: null,
      siteId: null,
      zoneCode: null,
      sectionCode: null,
      folderCode: null,
      searchQuery: "",
    },
    selectedFileIds: [],
    activeFileForModal: null,
    activeGrants: {
      "file-cad-001": [
        {
          id: "grant-001",
          file_record_id: "file-cad-001",
          granted_to_user_id: "cra_fderuiter",
          granted_by_user_id: "sponsor_designer_01",
          scope: "individual",
          permission_level: "download",
          expires_at: "2026-12-31T23:59:59Z",
          revoked_at: null,
          is_active: true,
          created_at: "2026-08-10T14:35:00Z",
          created_by: "sponsor_designer_01",
          reason_for_change: "Lead CRA monitoring assignment delegation",
        },
        {
          id: "grant-002",
          file_record_id: "file-cad-001",
          granted_to_user_id: "auditor_user_01",
          granted_by_user_id: "sponsor_admin",
          scope: "role",
          permission_level: "view",
          expires_at: "2026-09-30T23:59:59Z",
          revoked_at: null,
          is_active: true,
          created_at: "2026-08-10T15:00:00Z",
          created_by: "sponsor_admin",
          reason_for_change: "Regulatory compliance audit inspection window",
        },
      ],
      "file-cad-003": [
        {
          id: "grant-003",
          file_record_id: "file-cad-003",
          granted_to_user_id: "crc_user_101",
          granted_by_user_id: "cra_fderuiter",
          scope: "individual",
          permission_level: "view",
          expires_at: null,
          revoked_at: null,
          is_active: true,
          created_at: "2026-08-12T17:00:00Z",
          created_by: "cra_fderuiter",
          reason_for_change: "Site CRC protocol training access",
        },
      ],
    },
    activeGuestLinks: {
      "file-cad-001": [
        {
          id: "guest-link-001",
          file_record_id: "file-cad-001",
          guest_url: "https://cadence.example.com/share/gxt_sec_8923a1f8c",
          expires_at: "2026-09-10T14:32:00Z",
          created_by: "sponsor_designer_01",
          access_count: 2,
          is_valid: true,
          created_at: "2026-08-10T14:40:00Z",
          reason_for_change: "External central IRB reviewer access link",
        },
      ],
    },
  }),

  getters: {
    /**
     * Tree hierarchy filtered by the current user's role/persona
     */
    filteredStudiesTree: (state) => {
      const authStore = useAuthStore();
      const roles = authStore.normalizedRoles || [];
      const isSuperAdmin = roles.includes("sponsor_admin") || authStore.currentPersona === "super_admin";
      const isCrc = roles.includes("crc") || roles.includes("site_investigator") || authStore.currentPersona === "site_crc";
      const isCra = roles.includes("cra") || roles.includes("monitor") || authStore.currentPersona === "cra_monitor";

      return DEFAULT_STUDIES.filter((study) => {
        if (isSuperAdmin || (!isCrc && !isCra)) return true;
        if (isCrc) {
          // CRC sees Site-101 on Study-USDM-001
          return study.id === "STUDY-USDM-001";
        }
        if (isCra) {
          // CRA sees Study 001 and Study 002
          return study.id === "STUDY-USDM-001" || study.id === "STUDY-USDM-002";
        }
        return true;
      }).map((study) => {
        let sites = study.sites;
        if (isCrc) {
          sites = study.sites.filter((s) => s.id === "SITE-101");
        }
        return {
          ...study,
          sites: sites.map((site) => ({
            ...site,
            zones: TMF_TAXONOMY_SECTIONS.map((zone) => ({
              id: `${study.id}-${site.id}-zone-${zone.code}`,
              code: zone.code,
              name: zone.name,
              studyId: study.id,
              siteId: site.id,
              sections: zone.sections.map((section) => ({
                id: `${study.id}-${site.id}-sec-${section.code}`,
                code: section.code,
                name: section.name,
                studyId: study.id,
                siteId: site.id,
                zoneCode: zone.code,
                folders: section.folders.map((fName, idx) => ({
                  id: `${study.id}-${site.id}-folder-${section.code}.${idx + 1}`,
                  code: `${section.code}.${idx + 1}`,
                  name: fName,
                  studyId: study.id,
                  siteId: site.id,
                  zoneCode: zone.code,
                  sectionCode: section.code,
                })),
              })),
            })),
          })),
        };
      });
    },

    /**
     * Filtered documents matching all active tree node filters and search queries
     */
    filteredDocuments: (state) => {
      const q = state.activeFilters.searchQuery.trim().toLowerCase();
      const studyId = state.activeFilters.studyId;
      const siteId = state.activeFilters.siteId;
      const zoneCode = state.activeFilters.zoneCode;
      const sectionCode = state.activeFilters.sectionCode;
      const folderCode = state.activeFilters.folderCode;

      return state.files.filter((file) => {
        if (studyId && file.study_id !== studyId) return false;
        if (siteId && file.site_id && file.site_id !== siteId) return false;
        if (zoneCode && file.zone_code && file.zone_code !== zoneCode) return false;
        if (sectionCode && file.section_code && file.section_code !== sectionCode) return false;
        if (folderCode && file.folder_code && file.folder_code !== folderCode) return false;

        if (q) {
          const matchName = file.filename.toLowerCase().includes(q);
          const matchStudy = file.study_id.toLowerCase().includes(q);
          const matchUploader = (file.uploaded_by || "").toLowerCase().includes(q);
          const matchSite = (file.site_id || "").toLowerCase().includes(q);
          if (!matchName && !matchStudy && !matchUploader && !matchSite) return false;
        }

        return true;
      });
    },

    /**
     * Get active grants for a specific file record
     */
    getGrantsForFile: (state) => (fileId) => {
      return state.activeGrants[fileId] || [];
    },

    /**
     * Get active guest links for a specific file record
     */
    getGuestLinksForFile: (state) => (fileId) => {
      return state.activeGuestLinks[fileId] || [];
    },
  },

  actions: {
    /**
     * Fetch file records from the microservice backend or retain seed fallback
     */
    async fetchFiles(params = {}) {
      this.isLoading = true;
      this.error = null;
      try {
        const response = await fileshareService.listFiles({
          study_id: params.study_id || this.activeFilters.studyId || undefined,
          site_id: params.site_id || this.activeFilters.siteId || undefined,
          q: params.q || this.activeFilters.searchQuery || undefined,
          page: params.page || 1,
          page_size: params.page_size || 50,
        });

        if (Array.isArray(response) && response.length > 0) {
          // Merge API responses with any local UI metadata
          this.files = response.map((remote) => {
            const existing = this.files.find((f) => f.id === remote.id);
            return {
              ...remote,
              status: remote.is_on_hold ? "ON_HOLD" : "ACTIVE",
              zone_code: existing?.zone_code || "01",
              section_code: existing?.section_code || "01.01",
              folder_code: existing?.folder_code || "01.01.01",
              grant_count: this.activeGrants[remote.id]?.length || existing?.grant_count || 0,
            };
          });
        }
      } catch (err) {
        // Fallback gracefully to offline seed state for tests/mock environments
        console.warn("Fileshare API query failed, preserving local cached seed state:", err);
      } finally {
        this.isLoading = false;
      }
    },

    /**
     * Fetch grants for a specific file record
     */
    async fetchGrants(fileId) {
      try {
        const response = await fileshareService.listShareGrants(fileId);
        if (Array.isArray(response)) {
          this.activeGrants[fileId] = response;
        }
      } catch (err) {
        console.warn(`Failed to fetch remote grants for ${fileId}, using local cache:`, err);
      }
    },

    /**
     * Delegate internal share grant
     */
    async addGrant(fileId, payload) {
      this.error = null;
      let newGrant = null;
      try {
        newGrant = await fileshareService.createShareGrant(fileId, payload);
      } catch (err) {
        console.warn("Backend share grant API failed, creating mock local grant:", err);
        const authStore = useAuthStore();
        newGrant = {
          id: `grant-${Date.now()}`,
          file_record_id: fileId,
          granted_to_user_id: payload.granted_to_user_id || "all",
          granted_by_user_id: authStore.identity?.username || "demo-user",
          scope: payload.scope || "individual",
          permission_level: payload.permission_level || "view",
          expires_at: payload.expires_at || null,
          revoked_at: null,
          is_active: true,
          created_at: new Date().toISOString(),
          created_by: authStore.identity?.username || "demo-user",
          reason_for_change: payload.reason_for_change,
        };
      }

      if (newGrant) {
        if (!this.activeGrants[fileId]) {
          this.activeGrants[fileId] = [];
        }
        this.activeGrants[fileId].push(newGrant);

        // Update grant count on the file record
        const file = this.files.find((f) => f.id === fileId);
        if (file) {
          file.grant_count = this.activeGrants[fileId].length;
        }
      }
      return newGrant;
    },

    /**
     * Revoke an active share grant
     */
    async revokeGrant(fileId, grantId, reason) {
      try {
        await fileshareService.deleteShareGrant(fileId, grantId, { reason_for_change: reason });
      } catch (err) {
        console.warn("Backend revoke grant API failed, updating local state:", err);
      }

      if (this.activeGrants[fileId]) {
        this.activeGrants[fileId] = this.activeGrants[fileId].filter((g) => g.id !== grantId);
        const file = this.files.find((f) => f.id === fileId);
        if (file) {
          file.grant_count = this.activeGrants[fileId].length;
        }
      }
    },

    /**
     * Create external guest link
     */
    async generateGuestLink(fileId, payload) {
      let newLink = null;
      try {
        newLink = await fileshareService.createGuestLink(fileId, payload);
      } catch (err) {
        console.warn("Backend guest link API failed, creating mock link:", err);
        const authStore = useAuthStore();
        const rawToken = `gxt_${Math.random().toString(36).substring(2, 12)}`;
        newLink = {
          id: `guest-${Date.now()}`,
          file_record_id: fileId,
          guest_url: fileshareService.getGuestShareUrl(rawToken),
          expires_at: new Date(Date.now() + (payload.expires_in_hours || 24) * 3600 * 1000).toISOString(),
          created_by: authStore.identity?.username || "demo-user",
          access_count: 0,
          is_valid: true,
          created_at: new Date().toISOString(),
          reason_for_change: payload.reason_for_change,
        };
      }

      if (newLink) {
        if (!this.activeGuestLinks[fileId]) {
          this.activeGuestLinks[fileId] = [];
        }
        this.activeGuestLinks[fileId].push(newLink);
      }
      return newLink;
    },

    /**
     * Revoke external guest link
     */
    async revokeGuestLink(fileId, guestLinkId, reason) {
      try {
        await fileshareService.revokeGuestLink(fileId, guestLinkId, { reason_for_change: reason });
      } catch (err) {
        console.warn("Backend revoke guest link API failed, updating local state:", err);
      }

      if (this.activeGuestLinks[fileId]) {
        this.activeGuestLinks[fileId] = this.activeGuestLinks[fileId].filter((l) => l.id !== guestLinkId);
      }
    },

    /**
     * Set retention/legal hold on a file record
     */
    async setHold(fileId, reason, holdReason = null) {
      try {
        await fileshareService.setHold(fileId, {
          reason_for_change: reason,
          hold_reason: holdReason || reason,
        });
      } catch (err) {
        console.warn("Backend set hold API failed, updating local state:", err);
      }
      const file = this.files.find((f) => f.id === fileId);
      if (file) {
        file.is_on_hold = true;
        file.hold_reason = holdReason || reason;
        file.status = "ON_HOLD";
      }
    },

    /**
     * Clear retention/legal hold from a file record
     */
    async clearHold(fileId, reason) {
      try {
        await fileshareService.clearHold(fileId, {
          reason_for_change: reason,
        });
      } catch (err) {
        console.warn("Backend clear hold API failed, updating local state:", err);
      }
      const file = this.files.find((f) => f.id === fileId);
      if (file) {
        file.is_on_hold = false;
        file.hold_reason = null;
        file.status = "ACTIVE";
      }
    },

    /**
     * Delete a file record (gated on hold)
     */
    async deleteFile(fileId, reason) {
      const file = this.files.find((f) => f.id === fileId);
      if (file && file.is_on_hold) {
        throw new Error("File is under retention hold.");
      }
      try {
        await fileshareService.deleteFile(fileId, { reason_for_change: reason });
      } catch (err) {
        console.warn("Backend delete file API failed, updating local state:", err);
      }
      this.files = this.files.filter((f) => f.id !== fileId);
      this.selectedFileIds = this.selectedFileIds.filter((id) => id !== fileId);
    },


    /**
     * Selection & Filters Management
     */
    toggleFileSelection(fileId) {
      const idx = this.selectedFileIds.indexOf(fileId);
      if (idx > -1) {
        this.selectedFileIds.splice(idx, 1);
      } else {
        this.selectedFileIds.push(fileId);
      }
    },

    selectAllFiltered(selectAll = true) {
      if (selectAll) {
        this.selectedFileIds = this.filteredDocuments.map((f) => f.id);
      } else {
        this.selectedFileIds = [];
      }
    },

    clearSelection() {
      this.selectedFileIds = [];
    },

    setFilter(key, value) {
      this.activeFilters[key] = value;
    },

    setSearchQuery(query) {
      this.activeFilters.searchQuery = query;
    },

    resetFilters() {
      this.activeFilters = {
        studyId: null,
        siteId: null,
        zoneCode: null,
        sectionCode: null,
        folderCode: null,
        searchQuery: "",
      };
      this.selectedFileIds = [];
    },

    /**
     * Fetch chronological, filterable activity feed for a file record
     */
    async fetchActivityFeed(fileId, params = {}) {
      try {
        const res = await fileshareService.getActivityFeed(fileId, params);
        return res.data || res;
      } catch (err) {
        console.warn("Backend fetch activity feed failed, returning mock feed:", err);
        const mockFile = this.files.find((f) => f.id === fileId);
        const entries = [
          {
            event_type: "FILE_UPLOADED",
            actor_user_id: mockFile?.uploaded_by || "sponsor_designer_01",
            actor_display_name: mockFile?.uploaded_by || "Sponsor Designer",
            timestamp: mockFile?.uploaded_at || "2026-08-10T14:32:00Z",
            detail: { filename: mockFile?.filename || "document.pdf", size_bytes: mockFile?.size_bytes || 1024 },
          },
          {
            event_type: "SCAN_COMPLETE",
            actor_user_id: "system_user",
            actor_display_name: "System Automated Process",
            timestamp: mockFile?.uploaded_at || "2026-08-10T14:32:05Z",
            detail: { scan_engine: "clamav", status: "CLEAN" },
          },
        ];
        if (mockFile?.approval_status === "APPROVED") {
          entries.unshift({
            event_type: "DOCUMENT_APPROVED",
            actor_user_id: mockFile?.approved_by || "pi_user_01",
            actor_display_name: mockFile?.approved_by || "Principal Investigator",
            timestamp: mockFile?.approved_at || "2026-08-11T09:15:00Z",
            detail: { outcome: "APPROVED", signature_manifestation: mockFile?.signature_manifestation },
          });
        }
        if (mockFile?.is_on_hold) {
          entries.unshift({
            event_type: "HOLD_SET",
            actor_user_id: "super_admin_01",
            actor_display_name: "Super Admin",
            timestamp: "2026-08-12T10:00:00Z",
            detail: { hold_reason: mockFile?.hold_reason || "Audit freeze" },
          });
        }
        let filtered = entries;
        if (params.event_type) {
          filtered = filtered.filter((e) => e.event_type.toUpperCase() === params.event_type.toUpperCase());
        }
        return {
          entries: filtered,
          total: filtered.length,
          page: params.page || 1,
          page_size: params.page_size || 50,
        };
      }
    },
  },
});

