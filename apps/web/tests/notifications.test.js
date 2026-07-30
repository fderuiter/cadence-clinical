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
});
