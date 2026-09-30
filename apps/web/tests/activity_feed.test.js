import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ActivityFeed from "../src/components/fileshare/ActivityFeed.vue";
import DocumentsHubView from "../src/views/DocumentsHubView.vue";
import { useFileshareStore } from "../src/stores/fileshare";
import { useAuthStore } from "../src/stores/auth";
import { router } from "../src/router";
import { fileshareService } from "../src/api/fileshare";

// Mock fileshare API client
vi.mock("../src/api/fileshare", () => ({
  fileshareService: {
    listFiles: vi.fn(() => Promise.resolve([])),
    getActivityFeed: vi.fn((fileId, params = {}) =>
      Promise.resolve({
        data: {
          entries: [
            {
              event_type: "FILE_UPLOADED",
              actor_user_id: "crc_user_01",
              actor_display_name: "CRC User 01",
              timestamp: new Date(Date.now() - 3600000).toISOString(),
              detail: { filename: "protocol_v1.pdf", size_bytes: 20480 },
            },
            {
              event_type: "SCAN_COMPLETE",
              actor_user_id: "system_user",
              actor_display_name: "System Automated Process",
              timestamp: new Date(Date.now() - 3500000).toISOString(),
              detail: { scan_engine: "clamav", status: "CLEAN" },
            },
            {
              event_type: "GRANT_CREATED",
              actor_user_id: "crc_user_01",
              actor_display_name: "CRC User 01",
              timestamp: new Date(Date.now() - 3000000).toISOString(),
              detail: {
                granted_to_user_id: "cra_monitor_02",
                permission_level: "VIEW",
                scope: "INDIVIDUAL",
                expires_at: new Date(Date.now() + 86400000).toISOString(),
              },
            },
            {
              event_type: "DOCUMENT_APPROVED",
              actor_user_id: "pi_user_01",
              actor_display_name: "PI Investigator",
              timestamp: new Date(Date.now() - 1000000).toISOString(),
              detail: {
                outcome: "APPROVED",
                signature_manifestation: {
                  printed_name: "Dr. Investigator",
                  meaning: "Approved for clinical use",
                  signature:
                    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                },
              },
            },
          ],
          total: 4,
          page: params.page || 1,
          page_size: params.page_size || 25,
        },
      })
    ),
  },
}));

describe("feat(fileshare): 15 — Per-Document Activity Feed (#4322)", () => {
  const mockFile = {
    id: "file-doc-4322",
    study_id: "STUDY-USDM-001",
    site_id: "SITE-101",
    filename: "Protocol_Amendment_v2.1.pdf",
    version_index: 2,
    uploaded_by: "crc_user_01",
    uploaded_at: "2026-08-20T10:00:00Z",
    status: "READY",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    setActivePinia(createPinia());
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "super_admin";
  });

  describe("ActivityFeed.vue Component", () => {
    it("renders drawer header, document name, version tag and GxP inspection strip when open", async () => {
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      expect(wrapper.find(".drawer-title").text()).toBe(
        "Document Activity Feed"
      );
      expect(wrapper.find(".drawer-subtitle").text()).toContain(
        "Protocol_Amendment_v2.1.pdf"
      );
      expect(wrapper.find(".version-tag").text()).toBe("v2");
      expect(wrapper.find(".gxp-badge-strip").text()).toContain(
        "ICH E6(R2) & 21 CFR Part 11"
      );
    });

    it("fetches and displays timeline events with icons, actors, and relative timestamps", async () => {
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      const items = wrapper.findAll(".timeline-item");
      expect(items.length).toBe(4);

      // Verify event badges and titles
      expect(items[0].text()).toContain("File Uploaded");
      expect(items[0].text()).toContain("CRC User 01");
      expect(items[1].text()).toContain("Virus Scan Clean");
      expect(items[1].text()).toContain("System Automated Process");
      expect(items[2].text()).toContain("Share Grant Delegated");
      expect(items[3].text()).toContain("Document Approved");
      expect(items[3].text()).toContain("PI Investigator");
    });

    it("toggles expandable event details with rich formatted fields and e-signatures", async () => {
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      const items = wrapper.findAll(".timeline-item");
      const approvalCard = items[3];

      const toggleBtn = approvalCard.find(".detail-toggle-btn");
      expect(toggleBtn.exists()).toBe(true);
      expect(toggleBtn.text()).toContain("Show Details");

      // Expand details
      await toggleBtn.trigger("click");
      expect(approvalCard.find(".detail-content").exists()).toBe(true);
      expect(approvalCard.find(".signature-box").text()).toContain(
        "Dr. Investigator"
      );
      expect(approvalCard.find(".signature-box").text()).toContain(
        "Approved for clinical use"
      );

      // Collapse details
      await toggleBtn.trigger("click");
      expect(approvalCard.find(".detail-content").exists()).toBe(false);
    });

    it("filters activities by event type when selecting from dropdown", async () => {
      const { fileshareService } = await import("../src/api/fileshare");
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      const select = wrapper.find(".filter-select");
      await select.setValue("GRANT_CREATED");
      await flushPromises();

      expect(fileshareService.getActivityFeed).toHaveBeenCalledWith(
        mockFile.id,
        expect.objectContaining({
          event_type: "GRANT_CREATED",
          page: 1,
        })
      );
    });

    it("filters activities by date range and provides a reset button", async () => {
      const { fileshareService } = await import("../src/api/fileshare");
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      const fromInput = wrapper.find("#filter-from-date");
      const toInput = wrapper.find("#filter-to-date");

      await fromInput.setValue("2026-08-01");
      await toInput.setValue("2026-08-22");
      await flushPromises();

      expect(fileshareService.getActivityFeed).toHaveBeenCalledWith(
        mockFile.id,
        expect.objectContaining({
          from_date: expect.stringContaining("2026-08-01"),
          to_date: expect.stringContaining("2026-08-22"),
        })
      );

      // Reset button appears
      const resetBtn = wrapper.find(".reset-filters-btn");
      expect(resetBtn.exists()).toBe(true);

      await resetBtn.trigger("click");
      await flushPromises();

      expect(fromInput.element.value).toBe("");
      expect(toInput.element.value).toBe("");
    });

    it("emits close event when clicking close button or pressing Escape", async () => {
      const wrapper = mount(ActivityFeed, {
        props: {
          isOpen: true,
          file: mockFile,
        },
      });
      await flushPromises();

      const closeBtn = wrapper.find(".close-drawer-btn");
      await closeBtn.trigger("click");
      expect(wrapper.emitted("close")).toBeTruthy();

      // Escape key
      const event = new KeyboardEvent("keydown", { key: "Escape" });
      window.dispatchEvent(event);
      expect(wrapper.emitted("close").length).toBe(2);
    });
  });

  describe("DocumentsHubView Integration with ActivityFeed", () => {
    it("opens ActivityFeed drawer when clicking the activity button on a document row", async () => {
      // The hub lists what the server returns; there are no sample rows outside a demo build (#4523).
      fileshareService.listFiles.mockResolvedValueOnce([{ ...mockFile }]);
      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [router],
        },
      });
      await flushPromises();

      const activityFeedComponent = wrapper.findComponent(ActivityFeed);
      expect(activityFeedComponent.exists()).toBe(true);
      expect(activityFeedComponent.props("isOpen")).toBe(false);

      // Click Activity button on the first document row
      const activityBtn = wrapper.find(".btn-activity-action");
      expect(activityBtn.exists()).toBe(true);

      await activityBtn.trigger("click");
      await flushPromises();

      expect(activityFeedComponent.props("isOpen")).toBe(true);
      expect(activityFeedComponent.props("file")).toBeDefined();
    });
  });
});
