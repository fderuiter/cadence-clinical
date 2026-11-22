import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DocumentsHubView from "../src/views/DocumentsHubView.vue";
import ShareDrawer from "../src/components/fileshare/ShareDrawer.vue";
import MediaPreviewModal from "../src/components/fileshare/MediaPreviewModal.vue";
import DocumentsFolderTree from "../src/components/fileshare/DocumentsFolderTree.vue";
import { useFileshareStore, SEED_FILES } from "../src/stores/fileshare";
import { useAuthStore } from "../src/stores/auth";
import { router } from "../src/router";
import { fileshareService } from "../src/api/fileshare";

// Mock fileshare API client
vi.mock("../src/api/fileshare", () => ({
  fileshareService: {
    listFiles: vi.fn(),
    getFileRecord: vi.fn((fileId) => Promise.resolve({ id: fileId })),
    getDownloadUrl: vi.fn((fileId) =>
      Promise.resolve({
        file_id: fileId,
        download_url: "http://example.com/file",
      })
    ),
    createShareGrant: vi.fn((fileId, payload) =>
      Promise.resolve({
        id: "grant-mock-123",
        file_record_id: fileId,
        granted_to_user_id: payload.granted_to_user_id,
        granted_by_user_id: "demo-user",
        scope: payload.scope || "individual",
        permission_level: payload.permission_level || "view",
        expires_at: payload.expires_at || null,
        revoked_at: null,
        is_active: true,
        created_at: new Date().toISOString(),
        created_by: "demo-user",
        reason_for_change: payload.reason_for_change,
      })
    ),
    deleteShareGrant: vi.fn(() => Promise.resolve({ success: true })),
    listShareGrants: vi.fn(() => Promise.resolve([])),
    createGuestLink: vi.fn((fileId, payload) =>
      Promise.resolve({
        id: "guest-mock-123",
        file_record_id: fileId,
        guest_url: "https://cadence.example.com/share/gxt_mock_123",
        expires_at: new Date().toISOString(),
        created_by: "demo-user",
        access_count: 0,
        is_valid: true,
        created_at: new Date().toISOString(),
        reason_for_change: payload.reason_for_change,
      })
    ),
    revokeGuestLink: vi.fn(() => Promise.resolve({ success: true })),
    listGuestLinks: vi.fn(() => Promise.resolve([])),
    getGuestShareUrl: vi.fn((token) => `http://localhost:8000/share/${token}`),
    getStreamUrl: vi.fn((fileId) =>
      Promise.resolve({
        file_id: fileId,
        stream_url: "https://cadence.example.com/hls/master.m3u8",
        expires_in: 300,
        transcode_status: "READY",
        message: "Video stream is ready",
      })
    ),
    recordPlaybackAudit: vi.fn((payload) =>
      Promise.resolve({
        id: "audit-mock-123",
        file_record_id: payload.file_record_id,
        event: payload.event,
        source: payload.source || "internal",
        session_id: payload.session_id,
        user_id: "demo-user",
        timestamp: new Date().toISOString(),
        created_at: new Date().toISOString(),
        reason_for_change: payload.reason_for_change,
      })
    ),
  },
}));

describe("feat(fileshare): 10 — Documents Hub UI (#4317)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setActivePinia(createPinia());
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "super_admin";
    authStore.rawRoles = [
      "Sponsor Admin",
      "Sponsor Designer",
      "CRA",
      "CRC",
      "Auditor",
    ];
    // Outside a demo build the store starts empty (#4523): load the sample records as if the server returned them.
    const cloneSeed = () => SEED_FILES.map((f) => ({ ...f }));
    fileshareService.listFiles.mockImplementation(() =>
      Promise.resolve(cloneSeed())
    );
    const fileshareStore = useFileshareStore();
    fileshareStore.files = cloneSeed();
    fileshareStore.activeGrants = {
      [SEED_FILES[0].id]: [
        {
          id: "grant-001",
          file_record_id: SEED_FILES[0].id,
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
      ],
    };
  });

  describe("DocumentsHubView.vue Component", () => {
    it("mounts and renders page header, search input, folder tree and documents table", async () => {
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      expect(wrapper.find(".hub-page-title").text()).toBe("Documents Hub");
      expect(wrapper.find(".hub-search-input").exists()).toBe(true);
      expect(wrapper.findComponent(DocumentsFolderTree).exists()).toBe(true);
      expect(wrapper.find(".documents-table").exists()).toBe(true);

      const rows = wrapper.findAll(".document-row");
      expect(rows.length).toBeGreaterThan(0);
      expect(rows.length).toBe(SEED_FILES.length);
    });

    it("filters document grid when typing in the search bar", async () => {
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      const searchInput = wrapper.find(".hub-search-input");
      await searchInput.setValue("Protocol");

      const rows = wrapper.findAll(".document-row");
      expect(rows.length).toBe(1);
      expect(rows[0].text()).toContain(
        "Clinical_Trial_Protocol_v2.0_Final_Signed.pdf"
      );
    });

    it("sorts columns ascending and descending when header is clicked", async () => {
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      const nameTh = wrapper
        .findAll(".sortable-th")
        .find((th) => th.text().includes("Name"));
      expect(nameTh).toBeDefined();

      // Click to sort by name ASC
      await nameTh.trigger("click");
      let firstRowName = wrapper
        .findAll(".document-row")[0]
        .find(".filename-text")
        .text();
      expect(firstRowName).toBe(
        "Clinical_Trial_Protocol_v2.0_Final_Signed.pdf"
      );

      // Click again to sort by name DESC
      await nameTh.trigger("click");
      firstRowName = wrapper
        .findAll(".document-row")[0]
        .find(".filename-text")
        .text();
      expect(firstRowName).toBe("Subject_101_Baseline_Echocardiogram.png");
    });

    it("handles multi-select and displays bulk actions ribbon", async () => {
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      const checkboxes = wrapper.findAll(
        '.document-row .checkbox-col input[type="checkbox"]'
      );
      expect(checkboxes.length).toBeGreaterThan(1);

      // Select first row
      await checkboxes[0].setChecked(true);
      expect(wrapper.find(".bulk-actions-toolbar").exists()).toBe(true);
      expect(wrapper.find(".selection-count-badge").text()).toContain(
        "1 Selected"
      );

      // Select second row
      await checkboxes[1].setChecked(true);
      expect(wrapper.find(".selection-count-badge").text()).toContain(
        "2 Selected"
      );

      // Click Bulk Share button
      const bulkShareBtn = wrapper.find(".btn-bulk-share");
      expect(bulkShareBtn.exists()).toBe(true);
      await bulkShareBtn.trigger("click");

      const shareDrawer = wrapper.findComponent(ShareDrawer);
      expect(shareDrawer.exists()).toBe(true);
      expect(shareDrawer.props("isOpen")).toBe(true);
      expect(shareDrawer.props("files").length).toBe(2);
    });

    it("opens MediaPreviewModal when clicking preview button on a row", async () => {
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      expect(wrapper.findComponent(MediaPreviewModal).exists()).toBe(false);

      const previewBtn = wrapper.find(".btn-preview-action");
      await previewBtn.trigger("click");
      await flushPromises();

      const mediaModal = wrapper.findComponent(MediaPreviewModal);
      expect(mediaModal.exists()).toBe(true);
      expect(mediaModal.props("fileRecord")).toBeDefined();
      expect(mediaModal.props("fileRecord").filename).toBeDefined();
    });

    it("displays 🔒 badge and disables delete button when file is on retention hold", async () => {
      const store = useFileshareStore();
      store.files = [
        {
          id: "doc-held-001",
          study_id: "STUDY-USDM-001",
          site_id: "SITE-101",
          filename: "Investigator_Brochure_v5.0.pdf",
          mime_type: "application/pdf",
          size_bytes: 204800,
          uploaded_by: "Dr. Sarah Chen",
          uploaded_at: "2026-08-10T14:30:00Z",
          is_on_hold: true,
          hold_reason: "Legal subpoena investigation",
          status: "ON_HOLD",
          grant_count: 0,
        },
        {
          id: "doc-unheld-002",
          study_id: "STUDY-USDM-001",
          site_id: "SITE-101",
          filename: "Lab_Results_002.pdf",
          mime_type: "application/pdf",
          size_bytes: 102400,
          uploaded_by: "Lab Tech",
          uploaded_at: "2026-08-12T10:00:00Z",
          is_on_hold: false,
          hold_reason: null,
          status: "ACTIVE",
          grant_count: 1,
        },
      ];

      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });

      const rows = wrapper.findAll(".document-row");
      expect(rows.length).toBe(2);

      // Held file row
      const heldRow = rows.find((r) =>
        r.text().includes("Investigator_Brochure_v5.0.pdf")
      );
      expect(heldRow).toBeDefined();
      const heldBadge = heldRow.find(".status-pill.status-hold");
      expect(heldBadge.exists()).toBe(true);
      expect(heldBadge.text()).toContain("🔒");
      expect(heldBadge.attributes("title")).toBe(
        "Legal subpoena investigation"
      );

      const heldDeleteBtn = heldRow.find(".btn-delete-action");
      expect(heldDeleteBtn.attributes("disabled")).toBeDefined();
      expect(heldDeleteBtn.attributes("title")).toBe(
        "File is under retention hold."
      );

      // Unheld file row
      const unheldRow = rows.find((r) =>
        r.text().includes("Lab_Results_002.pdf")
      );
      expect(unheldRow).toBeDefined();
      const unheldDeleteBtn = unheldRow.find(".btn-delete-action");
      expect(unheldDeleteBtn.attributes("disabled")).toBeUndefined();
      expect(unheldDeleteBtn.attributes("title")).toBe("Delete document");
    });
  });

  describe("ShareDrawer.vue Component", () => {
    it("renders drawer tabs, forms, and permission selector", async () => {
      const testFile = SEED_FILES[0];
      const wrapper = mount(ShareDrawer, {
        props: {
          isOpen: true,
          files: [testFile],
        },
      });

      expect(wrapper.find(".drawer-title").text()).toBe("Share Document");
      expect(wrapper.find("#share-user-input").exists()).toBe(true);
      expect(wrapper.find("#share-perm-select").exists()).toBe(true);
      expect(wrapper.find("#share-reason-input").exists()).toBe(true);
    });

    it("submits internal share grant delegation with 21 CFR Part 11 reason", async () => {
      const testFile = SEED_FILES[0];
      const wrapper = mount(ShareDrawer, {
        props: {
          isOpen: true,
          files: [testFile],
        },
      });

      const userInput = wrapper.find("#share-user-input");
      const reasonInput = wrapper.find("#share-reason-input");
      const permSelect = wrapper.find("#share-perm-select");

      await userInput.setValue("auditor_user_02");
      await permSelect.setValue("download");
      await reasonInput.setValue("Regulatory trial master inspection access");

      const submitBtn = wrapper.find(".btn-submit-grant");
      expect(submitBtn.attributes("disabled")).toBeUndefined();

      await wrapper.find("form").trigger("submit");
      await flushPromises();

      expect(wrapper.emitted("grant-created")).toBeTruthy();
      const emittedPayload = wrapper.emitted("grant-created")[0][0];
      expect(emittedPayload.granted_to_user_id).toBe("auditor_user_02");
      expect(emittedPayload.permission_level).toBe("download");
      expect(emittedPayload.reason_for_change).toBe(
        "Regulatory trial master inspection access"
      );
    });

    it("generates external guest link with expiry and copy support", async () => {
      const testFile = SEED_FILES[0];
      const wrapper = mount(ShareDrawer, {
        props: {
          isOpen: true,
          files: [testFile],
        },
      });

      // Switch to Guest Link tab
      const guestTabBtn = wrapper
        .findAll(".tab-btn")
        .find((b) => b.text().includes("External Guest Link"));
      expect(guestTabBtn).toBeDefined();
      await guestTabBtn.trigger("click");

      const reasonInput = wrapper.find("#guest-reason-input");
      await reasonInput.setValue("External ethics committee review");

      await wrapper.find("form").trigger("submit");
      await flushPromises();

      expect(wrapper.emitted("guest-link-created")).toBeTruthy();
      expect(wrapper.find(".generated-link-card").exists()).toBe(true);
      expect(wrapper.find(".btn-copy-link").exists()).toBe(true);
    });

    it("displays active grants list and allows revoking grant with reason", async () => {
      const testFile = SEED_FILES[0];
      const wrapper = mount(ShareDrawer, {
        props: {
          isOpen: true,
          files: [testFile],
        },
      });

      // Switch to Active Grants tab
      const grantsTabBtn = wrapper
        .findAll(".tab-btn")
        .find((b) => b.text().includes("Active Grants"));
      await grantsTabBtn.trigger("click");

      const grantCards = wrapper.findAll(".grant-row-card");
      expect(grantCards.length).toBeGreaterThan(0);

      // Click revoke button
      const revokeBtn = grantCards[0].find(".btn-revoke-grant");
      await revokeBtn.trigger("click");

      expect(wrapper.find(".revoke-confirm-card").exists()).toBe(true);
      const confirmBtn = wrapper.find(".btn-confirm-revoke");
      await confirmBtn.trigger("click");
      await flushPromises();

      expect(wrapper.emitted("grant-revoked")).toBeTruthy();
    });
  });

  describe("MediaPreviewModal.vue Component", () => {
    it("renders PDF document page preview with diagonal watermark", async () => {
      const pdfFile = SEED_FILES[0];
      const wrapper = mount(MediaPreviewModal, {
        props: {
          fileRecord: pdfFile,
          permissions: { canDownload: true },
        },
      });

      expect(wrapper.find(".pdf-document-sheet").exists()).toBe(true);
      expect(wrapper.find(".watermark-overlay-container").exists()).toBe(true);
      expect(wrapper.find(".watermark-text").text()).toContain(
        "SECURE PREVIEW"
      );
      expect(wrapper.find(".btn-download").exists()).toBe(true);
    });

    it("renders video player when MIME type is video/mp4", async () => {
      const videoFile = SEED_FILES[2]; // Site_Initiation_Training_Video.mp4
      const wrapper = mount(MediaPreviewModal, {
        props: {
          fileRecord: videoFile,
          permissions: { canDownload: false },
        },
      });

      expect(wrapper.find(".video-container").exists()).toBe(true);
      expect(wrapper.find("video.video-element").exists()).toBe(true);
      // Download button hidden when not permitted
      expect(wrapper.find(".btn-download").exists()).toBe(false);
    });

    it("renders audio player when MIME type is audio/mpeg", async () => {
      const audioFile = SEED_FILES[6]; // Safety_Committee_Audio_Debrief.mp3
      const wrapper = mount(MediaPreviewModal, {
        props: {
          fileRecord: audioFile,
          permissions: { canDownload: true },
        },
      });

      expect(wrapper.find(".audio-container").exists()).toBe(true);
      expect(wrapper.find("audio.audio-element").exists()).toBe(true);
    });

    it("renders image preview when MIME type is image/png", async () => {
      const imageFile = SEED_FILES[4]; // Subject_101_Baseline_Echocardiogram.png
      const wrapper = mount(MediaPreviewModal, {
        props: {
          fileRecord: imageFile,
          permissions: { canDownload: true },
        },
      });

      expect(wrapper.find(".image-container").exists()).toBe(true);
      expect(wrapper.find("img.preview-image-element").exists()).toBe(true);
    });
  });

  describe("Router Registration", () => {
    it("has /documents route configured with requiresAuth: true", () => {
      const routes = router.getRoutes();
      const docRoute = routes.find(
        (r) => r.path === "/documents" || r.name === "documents"
      );
      expect(docRoute).toBeDefined();
      expect(docRoute.meta?.requiresAuth).toBe(true);
    });
  });
});
