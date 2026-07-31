import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";
import { useNotificationsStore } from "../src/stores/notifications";
import { notificationsService } from "../src/api/notifications";
import NotificationsView from "../src/views/NotificationsView.vue";
import App from "../src/App.vue";

// Mock the notifications service
vi.mock("../src/api/notifications", () => {
  return {
    notificationsService: {
      getNotifications: vi.fn(),
      getNotification: vi.fn(),
      acknowledgeNotification: vi.fn(),
      resolveNotification: vi.fn(),
    },
  };
});

describe("Notifications System End-to-End Visual Integration", () => {
  let pinia;
  let authStore;
  let notificationsStore;

  const mockNotifications = [
    {
      id: "notif-001",
      recipient_user_id: "fderuiter",
      recipient_role: null,
      category: "ALERTS",
      priority: "CRITICAL",
      channels: "IN_APP",
      message_content: "High temperature warning in Lab A.",
      related_entity_id: "LAB-A",
      related_entity_type: "LAB_FRIDGE",
      status: "OPEN",
      delivery_state: "DELIVERED",
      retries: 0,
      created_at: "2026-08-15T12:00:00Z", // deid-ignore
      created_by: "system",
      version_index: 1,
      reason_for_change: "System generated",
    },
    {
      id: "notif-002",
      recipient_user_id: null,
      recipient_role: "monitor",
      category: "ACTION_ITEMS",
      priority: "HIGH",
      channels: "IN_APP",
      message_content: "Review form submission for Subject 001.",
      related_entity_id: "FSUB-001",
      related_entity_type: "FORM",
      status: "ACKNOWLEDGED",
      delivery_state: "DELIVERED",
      retries: 0,
      created_at: "2026-08-16T14:30:00Z", // deid-ignore
      created_by: "system",
      version_index: 2,
      reason_for_change: "User acknowledged",
    },
  ];

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();
    notificationsStore = useNotificationsStore();

    // Reset mocks
    vi.clearAllMocks();

    // Setup standard success mock defaults with a deep copy to prevent cross-test state leakage
    notificationsService.getNotifications.mockImplementation(() =>
      Promise.resolve(JSON.parse(JSON.stringify(mockNotifications)))
    );
  });

  describe("Navigation & Route Gating", () => {
    it("allows authenticated user access to /notifications and shows active menu button", async () => {
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Auditor"];

      await router.push("/notifications");
      expect(router.currentRoute.value.path).toBe("/notifications");

      const wrapper = mount(App, {
        global: {
          plugins: [pinia, router],
        },
      });

      const navBtn = wrapper.find("#tab-btn-notifications");
      expect(navBtn.exists()).toBe(true);
      expect(navBtn.classes()).toContain("active");
    });
  });

  describe("Dashboard List Filtering & Rendering", () => {
    it("renders retrieved notification list items and details correctly", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      // Wait for async fetch to resolve
      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.text()).toContain("ALERTS");
      expect(wrapper.text()).toContain("CRITICAL");
      expect(wrapper.text()).toContain("OPEN");
      expect(wrapper.text()).toContain("High temperature warning in Lab A.");
      expect(wrapper.text()).toContain("fderuiter");
      expect(wrapper.text()).toContain("LAB_FRIDGE (LAB-A)");

      expect(wrapper.text()).toContain("ACTION_ITEMS");
      expect(wrapper.text()).toContain("HIGH");
      expect(wrapper.text()).toContain("ACKNOWLEDGED");
      expect(wrapper.text()).toContain(
        "Review form submission for Subject 001."
      );
      expect(wrapper.text()).toContain("Role: monitor");
    });

    it("triggers a service re-fetch with selected category/priority/status filters when filters change", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Ensure initial call had no specific filters
      expect(notificationsService.getNotifications).toHaveBeenLastCalledWith({
        category: undefined,
        priority: undefined,
        status: undefined,
      });

      // Change category filter
      await wrapper.find("#filter-category").setValue("SYSTEM");
      expect(notificationsService.getNotifications).toHaveBeenLastCalledWith({
        category: "SYSTEM",
        priority: undefined,
        status: undefined,
      });

      // Change priority filter
      await wrapper.find("#filter-priority").setValue("MEDIUM");
      expect(notificationsService.getNotifications).toHaveBeenLastCalledWith({
        category: "SYSTEM",
        priority: "MEDIUM",
        status: undefined,
      });

      // Change status filter
      await wrapper.find("#filter-status").setValue("RESOLVED");
      expect(notificationsService.getNotifications).toHaveBeenLastCalledWith({
        category: "SYSTEM",
        priority: "MEDIUM",
        status: "RESOLVED",
      });
    });

    it("shows an elegant empty list message when zero items are returned", async () => {
      notificationsService.getNotifications.mockResolvedValue([]);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#notifications-empty").exists()).toBe(true);
      expect(wrapper.text()).toContain(
        "No notifications found matching the active filters."
      );
    });
  });

  describe("Lifecycle State Actions & Justification Modal", () => {
    it("gates Acknowledge control only to OPEN status and Resolve control to OPEN/ACKNOWLEDGED status", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      const cards = wrapper.findAll(".notification-card");
      expect(cards.length).toBe(2);

      // Card 1 (OPEN) -> has both Acknowledge and Resolve
      const card1 = cards[0];
      expect(card1.find(".btn-acknowledge").exists()).toBe(true);
      expect(card1.find(".btn-resolve").exists()).toBe(true);

      // Card 2 (ACKNOWLEDGED) -> has Resolve but NOT Acknowledge
      const card2 = cards[1];
      expect(card2.find(".btn-acknowledge").exists()).toBe(false);
      expect(card2.find(".btn-resolve").exists()).toBe(true);
    });

    it("blocks modal confirmation action until a non-empty trimmed reason is supplied", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Trigger acknowledge click on OPEN card
      await wrapper.find(".btn-acknowledge").trigger("click");
      expect(wrapper.find("#justification-modal").exists()).toBe(true);

      // Set select to 'Other' to test text area requirement
      await wrapper.find("#modal-reason-select").setValue("Other");
      await wrapper.find("#modal-custom-reason").setValue("  "); // spaces/empty

      const confirmBtn = wrapper.find("#btn-submit-modal");
      expect(confirmBtn.element.disabled).toBe(true);

      // Supply a non-empty trimmed reason
      await wrapper.find("#modal-custom-reason").setValue("My valid reason");
      expect(confirmBtn.element.disabled).toBe(false);
    });

    it("submits the correct change reason to the service layer and updates the item in place on success", async () => {
      const updatedNotif = {
        ...mockNotifications[0],
        status: "ACKNOWLEDGED",
        version_index: 2,
        reason_for_change: "Acknowledged via test",
      };
      notificationsService.acknowledgeNotification.mockResolvedValue(
        updatedNotif
      );

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Click acknowledge
      await wrapper.find(".btn-acknowledge").trigger("click");
      await wrapper
        .find("#modal-reason-select")
        .setValue("Clinical event acknowledged");

      // Confirm trigger
      await wrapper.find("#btn-submit-modal").trigger("click");
      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Verify endpoint and changeReason payload was correctly forwarded
      expect(notificationsService.acknowledgeNotification).toHaveBeenCalledWith(
        "notif-001",
        { changeReason: "Clinical event acknowledged" }
      );

      // Ensure modal is closed
      expect(wrapper.find("#justification-modal").exists()).toBe(false);

      // Assert state was updated in place
      expect(notificationsStore.notifications[0].status).toBe("ACKNOWLEDGED");
      expect(notificationsStore.notifications[0].version_index).toBe(2);
    });

    it("displays transition validation or server errors inline when actions fail, keeping modal open", async () => {
      notificationsService.acknowledgeNotification.mockRejectedValue(
        new Error(
          "Validation Failure: Transition not permitted under CFR guidelines"
        )
      );

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Click acknowledge
      await wrapper.find(".btn-acknowledge").trigger("click");
      await wrapper.find("#btn-submit-modal").trigger("click");

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Assert modal stays open and error is shown
      expect(wrapper.find("#justification-modal").exists()).toBe(true);
      expect(wrapper.find("#modal-error").exists()).toBe(true);
      expect(wrapper.find("#modal-error").text()).toContain(
        "Transition not permitted"
      );
    });
  });

  describe("API Error States Visualization", () => {
    it("renders a distinct 403 Access Denied banner when response status is 403", async () => {
      const error403 = new Error("Forbidden context boundary.");
      error403.status = 403;
      notificationsService.getNotifications.mockRejectedValue(error403);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#notifications-error-403").exists()).toBe(true);
      expect(wrapper.find("#notifications-error-generic").exists()).toBe(false);
      expect(wrapper.text()).toContain("Access Denied");
    });

    it("renders a generic error banner when response fails with other error codes", async () => {
      const error500 = new Error(
        "Severe internal database connection failure."
      );
      error500.status = 500;
      notificationsService.getNotifications.mockRejectedValue(error500);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#notifications-error-generic").exists()).toBe(true);
      expect(wrapper.find("#notifications-error-403").exists()).toBe(false);
      expect(wrapper.text()).toContain(
        "Severe internal database connection failure."
      );
    });
  });

  describe("Differentiated Rendering & Filter/Action Gaps", () => {
    it("renders global broadcast message when recipient fields are null", async () => {
      notificationsService.getNotifications.mockResolvedValue([
        {
          id: "notif-global",
          recipient_user_id: null,
          recipient_role: null,
          category: "SYSTEM",
          priority: "LOW",
          channels: "IN_APP",
          message_content: "Global Maintenance Tonight",
          status: "OPEN",
          delivery_state: "DELIVERED",
          created_at: "2026-08-17T09:00:00Z",
          created_by: "system",
          version_index: 1,
        },
      ]);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.find(".global-indicator").exists()).toBe(true);
      expect(wrapper.find(".global-indicator").text()).toContain(
        "Global / Broadcast"
      );
    });

    it("resets all filters when clicking Reset Filters", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Modify filters
      notificationsStore.filters.category = "ALERTS";
      notificationsStore.filters.priority = "HIGH";
      notificationsStore.filters.status = "OPEN";

      await wrapper.find("#btn-reset-filters").trigger("click");
      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(notificationsStore.filters.category).toBe("");
      expect(notificationsStore.filters.priority).toBe("");
      expect(notificationsStore.filters.status).toBe("");
      expect(notificationsService.getNotifications).toHaveBeenLastCalledWith({
        category: undefined,
        priority: undefined,
        status: undefined,
      });
    });

    it("hides action controls completely when a notification is RESOLVED", async () => {
      notificationsService.getNotifications.mockResolvedValue([
        {
          id: "notif-resolved",
          recipient_user_id: "fderuiter",
          category: "ALERTS",
          priority: "LOW",
          message_content: "Resolved system message",
          status: "RESOLVED",
          delivery_state: "DELIVERED",
          created_at: "2026-08-17T09:00:00Z",
          created_by: "system",
          version_index: 2,
        },
      ]);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(wrapper.find(".btn-acknowledge").exists()).toBe(false);
      expect(wrapper.find(".btn-resolve").exists()).toBe(false);
    });

    it("completes the Resolve transition successfully and updates in-place", async () => {
      const updated = {
        ...mockNotifications[0],
        status: "RESOLVED",
        version_index: 2,
        reason_for_change: "Issue resolved",
      };
      notificationsService.resolveNotification.mockResolvedValue(updated);

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Trigger resolve modal on card 1
      await wrapper.find(".btn-resolve").trigger("click");
      expect(wrapper.find("#justification-modal").exists()).toBe(true);

      await wrapper
        .find("#modal-reason-select")
        .setValue("Corrective action documented");
      await wrapper.find("#btn-submit-modal").trigger("click");

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      expect(notificationsService.resolveNotification).toHaveBeenCalledWith(
        "notif-001",
        { changeReason: "Corrective action documented" }
      );
      expect(notificationsStore.notifications[0].status).toBe("RESOLVED");
    });

    it("resets modal fields on reopening", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      // Open, change fields
      await wrapper.find(".btn-acknowledge").trigger("click");
      await wrapper.find("#modal-reason-select").setValue("Other");
      await wrapper.find("#modal-custom-reason").setValue("Modified reason");

      // Close
      await wrapper.find("#btn-cancel-modal").trigger("click");
      expect(wrapper.find("#justification-modal").exists()).toBe(false);

      // Reopen
      await wrapper.find(".btn-acknowledge").trigger("click");
      expect(wrapper.find("#modal-reason-select").element.value).toBe(
        "Action completed successfully"
      );
      expect(wrapper.find("#modal-custom-reason").element.value).toBe("");
    });

    it("closes modal on cancel without calling api endpoints", async () => {
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();

      await wrapper.find(".btn-acknowledge").trigger("click");
      await wrapper.find("#btn-cancel-modal").trigger("click");

      expect(wrapper.find("#justification-modal").exists()).toBe(false);
      expect(
        notificationsService.acknowledgeNotification
      ).not.toHaveBeenCalled();
    });
  });

  describe("State Surfacing & Loading Visualization", () => {
    it("surfaces loading element when store is loading", async () => {
      notificationsStore.loading = true;
      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });

      expect(wrapper.find("#notifications-loading").exists()).toBe(true);
    });

    it("surfaces distinct error banners based on status codes (403, 404, 422)", async () => {
      // 403 check
      const error403 = new Error("Forbidden Access");
      error403.status = 403;
      notificationsService.getNotifications.mockRejectedValueOnce(error403);

      let wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });
      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();
      expect(wrapper.find("#notifications-error-403").exists()).toBe(true);

      // 422 check (generic error banner)
      const error422 = new Error("Unprocessable Entity State Violation");
      error422.status = 422;
      notificationsService.getNotifications.mockRejectedValueOnce(error422);

      wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });
      await new Promise((resolve) => setTimeout(resolve, 50));
      await wrapper.vm.$nextTick();
      expect(wrapper.find("#notifications-error-generic").exists()).toBe(true);
      expect(wrapper.text()).toContain("Unprocessable Entity State Violation");
    });

    it("renders generic error banner when network error occurs without a status code", async () => {
      notificationsStore.errorStatus = null;
      notificationsStore.error = "TypeError: Failed to fetch";
      notificationsStore.loading = false;

      const wrapper = mount(NotificationsView, {
        global: {
          plugins: [pinia],
        },
      });
      expect(wrapper.find("#notifications-error-generic").exists()).toBe(true);
      expect(wrapper.text()).toContain("TypeError: Failed to fetch");
    });
  });

  describe("Pinia Store Unit Tests", () => {
    it("handles loading and error state transitions during fetchNotifications", async () => {
      notificationsService.getNotifications.mockRejectedValue(
        new Error("Database offline")
      );

      await notificationsStore.fetchNotifications();
      expect(notificationsStore.loading).toBe(false);
      expect(notificationsStore.error).toBe("Database offline");
      expect(notificationsStore.notifications).toEqual([]);
    });

    it("re-throws server-side errors on failed acknowledge and resolve actions", async () => {
      notificationsService.acknowledgeNotification.mockRejectedValue(
        new Error("Acknowledge forbidden")
      );
      notificationsService.resolveNotification.mockRejectedValue(
        new Error("Resolve unprocessable")
      );

      await expect(
        notificationsStore.acknowledge("notif-123", "Reason")
      ).rejects.toThrow("Acknowledge forbidden");
      await expect(
        notificationsStore.resolve("notif-123", "Reason")
      ).rejects.toThrow("Resolve unprocessable");
    });
  });

  describe("API Client Contract Tests", () => {
    beforeEach(() => {
      vi.stubGlobal("fetch", vi.fn());
    });

    it("constructs correct query strings and attaches JWT/Reason headers correctly", async () => {
      // Mock Pinia Auth token
      const authStore = useAuthStore();
      authStore.accessToken = "dummy-jwt-signature-token";

      globalThis.fetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => [{ id: "notif-01" }],
      });

      // Import actual notificationsService
      const { notificationsService: realService } = await vi.importActual(
        "../src/api/notifications"
      );

      await realService.getNotifications({
        category: "ALERTS",
        priority: "CRITICAL",
        status: "OPEN",
      });

      expect(globalThis.fetch).toHaveBeenCalledTimes(1);
      const [url, options] = globalThis.fetch.mock.calls[0];

      expect(url).toContain(
        "/api/v1/notifications?category=ALERTS&priority=CRITICAL&status=OPEN"
      );
      expect(options.headers["Authorization"]).toBe(
        "Bearer dummy-jwt-signature-token"
      );
    });

    it("attaches X-Change-Reason to mutations correctly", async () => {
      const authStore = useAuthStore();
      authStore.accessToken = "dummy-jwt-signature-token";

      globalThis.fetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ id: "notif-01", status: "ACKNOWLEDGED" }),
      });

      const { notificationsService: realService } = await vi.importActual(
        "../src/api/notifications"
      );

      await realService.acknowledgeNotification("notif-01", {
        changeReason: "Attestation reason",
      });

      const [, options] = globalThis.fetch.mock.calls[0];
      expect(options.headers["X-Change-Reason"]).toBe("Attestation reason");
    });
  });
});
