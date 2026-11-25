/**
 * Unit & Component tests for ActivityFeed.vue document activity feed & audit trail inspection.
 *
 * Requirements Traceability: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002 | GxP 21 CFR Part 11
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ActivityFeed from "../../src/components/fileshare/ActivityFeed.vue";
import { useFileshareStore } from "../../src/stores/fileshare";

describe("ActivityFeed.vue - Document Activity Feed & Audit Trail", () => {
  let pinia: any;

  const mockFile = {
    id: "file-act-123",
    filename: "Investigator_Brochure_v2.pdf",
    study_id: "STUDY-2026",
    site_id: "SITE-101",
    version_index: 2,
  };

  const mockActivityResponse = {
    total: 3,
    page: 1,
    page_size: 25,
    entries: [
      {
        event_type: "DOCUMENT_APPROVED",
        actor_user_id: "dr_smith",
        actor_display_name: "Dr Smith",
        timestamp: "2026-08-22T15:00:00Z",
        detail: {
          hold_reason: null,
          permission_level: "approve",
          granted_to_user_id: "dr_smith",
          scope: "individual",
        },
      },
      {
        event_type: "GRANT_CREATED",
        actor_user_id: "lead_crc",
        actor_display_name: "Lead Crc",
        timestamp: "2026-08-22T14:30:00Z",
        detail: {
          permission_level: "view",
          granted_to_user_id: "cra_user",
          scope: "individual",
        },
      },
      {
        event_type: "FILE_UPLOADED",
        actor_user_id: "lead_crc",
        actor_display_name: "Lead Crc",
        timestamp: "2026-08-22T14:00:00Z",
        detail: {},
      },
    ],
  };

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.restoreAllMocks();
  });

  it("renders the activity drawer when isOpen is true and loads feed entries", async () => {
    const store = useFileshareStore();
    vi.spyOn(store, "fetchActivityFeed").mockResolvedValue(
      mockActivityResponse as any
    );

    const wrapper = mount(ActivityFeed, {
      props: {
        isOpen: true,
        file: mockFile,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find(".activity-feed-drawer").exists()).toBe(true);
    expect(wrapper.text()).toContain("Document Activity Feed");
    expect(wrapper.text()).toContain("Investigator_Brochure_v2.pdf");

    await flushPromises();

    expect(store.fetchActivityFeed).toHaveBeenCalledWith(
      "file-act-123",
      expect.any(Object)
    );

    // Verify timeline items rendered
    const timelineItems = wrapper.findAll(".timeline-item");
    expect(timelineItems.length).toBe(3);
    expect(wrapper.text()).toContain("Dr Smith");
    expect(wrapper.text()).toContain("Lead Crc");
  });

  it("expands structured technical audit details accordion on click", async () => {
    const store = useFileshareStore();
    vi.spyOn(store, "fetchActivityFeed").mockResolvedValue(
      mockActivityResponse as any
    );

    const wrapper = mount(ActivityFeed, {
      props: {
        isOpen: true,
        file: mockFile,
      },
      global: {
        plugins: [pinia],
      },
    });

    await flushPromises();

    const toggleBtn = wrapper.find(".detail-toggle-btn");
    expect(toggleBtn.exists()).toBe(true);
    expect(toggleBtn.text()).toContain("Show Details");

    // Click to expand
    await toggleBtn.trigger("click");
    expect(toggleBtn.text()).toContain("Hide Details");
    expect(wrapper.find(".detail-content").exists()).toBe(true);
    expect(wrapper.text()).toContain("Permission:");
    expect(wrapper.text()).toContain("Granted To:");
  });

  it("filters activity feed when event type selector is changed", async () => {
    const store = useFileshareStore();
    const fetchSpy = vi
      .spyOn(store, "fetchActivityFeed")
      .mockResolvedValue(mockActivityResponse as any);

    const wrapper = mount(ActivityFeed, {
      props: {
        isOpen: true,
        file: mockFile,
      },
      global: {
        plugins: [pinia],
      },
    });

    await flushPromises();

    const select = wrapper.find("#filter-event-type");
    await select.setValue("DOCUMENT_APPROVED");
    await select.trigger("change");
    await flushPromises();

    expect(fetchSpy).toHaveBeenCalledWith(
      "file-act-123",
      expect.objectContaining({ event_type: "DOCUMENT_APPROVED" })
    );
  });

  it("renders empty state when no activities match", async () => {
    const store = useFileshareStore();
    vi.spyOn(store, "fetchActivityFeed").mockResolvedValue({
      total: 0,
      page: 1,
      page_size: 25,
      entries: [],
    } as any);

    const wrapper = mount(ActivityFeed, {
      props: {
        isOpen: true,
        file: mockFile,
      },
      global: {
        plugins: [pinia],
      },
    });

    await flushPromises();

    expect(wrapper.find(".empty-state").exists()).toBe(true);
    expect(wrapper.text()).toContain("No Activity Events Found");
  });

  it("emits close event when close button is clicked", async () => {
    const store = useFileshareStore();
    vi.spyOn(store, "fetchActivityFeed").mockResolvedValue(
      mockActivityResponse as any
    );

    const wrapper = mount(ActivityFeed, {
      props: {
        isOpen: true,
        file: mockFile,
      },
      global: {
        plugins: [pinia],
      },
    });

    const closeBtn = wrapper.find(".close-drawer-btn");
    await closeBtn.trigger("click");

    expect(wrapper.emitted("close")).toBeTruthy();
  });
});
