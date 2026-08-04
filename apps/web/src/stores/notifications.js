import { defineStore } from "pinia";
import { notificationsService } from "../api/notifications";
import { useAuthStore } from "./auth.js";

const defaultTemplates = [
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
    created_at: "2026-08-15T12:00:00Z",
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
    status: "OPEN",
    delivery_state: "DELIVERED",
    retries: 0,
    created_at: "2026-08-16T14:30:00Z",
    created_by: "system",
    version_index: 1,
    reason_for_change: "System generated",
  },
  {
    id: "notif-003",
    recipient_user_id: "fderuiter",
    recipient_role: null,
    category: "SYSTEM",
    priority: "LOW",
    channels: "IN_APP",
    message_content: "Global Maintenance Scheduled for Tonight at 22:00 UTC.",
    related_entity_id: "SYS-MAINT",
    related_entity_type: "SYSTEM",
    status: "OPEN",
    delivery_state: "DELIVERED",
    retries: 0,
    created_at: "2026-08-17T09:00:00Z",
    created_by: "system",
    version_index: 1,
    reason_for_change: "System generated",
  },
];

export const useNotificationsStore = defineStore("notifications", {
  state: () => {
    let savedNotifications = null;
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        const stored = window.localStorage.getItem("demo_notifications");
        if (stored) {
          savedNotifications = JSON.parse(stored);
        }
      } catch (e) {
        console.error(
          "Failed to parse demo_notifications from localStorage",
          e
        );
      }
    }
    return {
      notifications: savedNotifications || defaultTemplates,
      filters: {
        category: "",
        priority: "",
        status: "",
      },
      loading: false,
      error: null,
      errorStatus: null,
    };
  },
  actions: {
    setFilter(key, value) {
      this.filters[key] = value;
    },
    async fetchNotifications() {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const authStore = useAuthStore();
        const isMocked =
          notificationsService.getNotifications &&
          (notificationsService.getNotifications._isMockFunction ||
            typeof notificationsService.getNotifications.mock === "object");
        if (authStore.isDemoMode && !isMocked) {
          await new Promise((resolve) => setTimeout(resolve, 100));
          let list = [];
          if (typeof window !== "undefined" && window.localStorage) {
            const stored = window.localStorage.getItem("demo_notifications");
            if (stored) {
              list = JSON.parse(stored);
            } else {
              list = JSON.parse(JSON.stringify(defaultTemplates));
              window.localStorage.setItem(
                "demo_notifications",
                JSON.stringify(list)
              );
            }
          } else {
            list = JSON.parse(JSON.stringify(defaultTemplates));
          }

          this.notifications = list.filter((n) => {
            if (this.filters.category && n.category !== this.filters.category)
              return false;
            if (this.filters.priority && n.priority !== this.filters.priority)
              return false;
            if (this.filters.status && n.status !== this.filters.status)
              return false;
            return true;
          });
          return this.notifications;
        }

        const data = await notificationsService.getNotifications({
          category: this.filters.category || undefined,
          priority: this.filters.priority || undefined,
          status: this.filters.status || undefined,
        });
        this.notifications = data || [];
      } catch (err) {
        this.notifications = [];
        this.error = err.message || "Failed to fetch notifications";
        this.errorStatus = err.status || null;
      } finally {
        this.loading = false;
      }
    },
    async acknowledge(id, changeReason) {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const authStore = useAuthStore();
        const isMocked =
          notificationsService.acknowledgeNotification &&
          (notificationsService.acknowledgeNotification._isMockFunction ||
            typeof notificationsService.acknowledgeNotification.mock ===
              "object");
        if (authStore.isDemoMode && !isMocked) {
          await new Promise((resolve) => setTimeout(resolve, 100));
          let list = [];
          if (typeof window !== "undefined" && window.localStorage) {
            const stored = window.localStorage.getItem("demo_notifications");
            if (stored) {
              list = JSON.parse(stored);
            }
          }
          if (list.length === 0) {
            list = JSON.parse(JSON.stringify(this.notifications));
          }

          const index = list.findIndex((n) => n.id === id);
          if (index === -1) {
            throw new Error("Notification not found");
          }

          const updated = {
            ...list[index],
            status: "ACKNOWLEDGED",
            version_index: (list[index].version_index || 1) + 1,
            reason_for_change: changeReason,
          };
          list[index] = updated;

          if (typeof window !== "undefined" && window.localStorage) {
            window.localStorage.setItem(
              "demo_notifications",
              JSON.stringify(list)
            );
          }

          const activeIndex = this.notifications.findIndex((n) => n.id === id);
          if (activeIndex !== -1) {
            this.notifications[activeIndex] = updated;
          }
          return updated;
        }

        const updated = await notificationsService.acknowledgeNotification(id, {
          changeReason,
        });
        const index = this.notifications.findIndex((n) => n.id === id);
        if (index !== -1) {
          this.notifications[index] = updated;
        }
        return updated;
      } catch (err) {
        this.error = err.message || "Failed to acknowledge notification";
        this.errorStatus = err.status || null;
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async resolve(id, changeReason) {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const authStore = useAuthStore();
        const isMocked =
          notificationsService.resolveNotification &&
          (notificationsService.resolveNotification._isMockFunction ||
            typeof notificationsService.resolveNotification.mock === "object");
        if (authStore.isDemoMode && !isMocked) {
          await new Promise((resolve) => setTimeout(resolve, 100));
          let list = [];
          if (typeof window !== "undefined" && window.localStorage) {
            const stored = window.localStorage.getItem("demo_notifications");
            if (stored) {
              list = JSON.parse(stored);
            }
          }
          if (list.length === 0) {
            list = JSON.parse(JSON.stringify(this.notifications));
          }

          const index = list.findIndex((n) => n.id === id);
          if (index === -1) {
            throw new Error("Notification not found");
          }

          const updated = {
            ...list[index],
            status: "RESOLVED",
            version_index: (list[index].version_index || 1) + 1,
            reason_for_change: changeReason,
          };
          list[index] = updated;

          if (typeof window !== "undefined" && window.localStorage) {
            window.localStorage.setItem(
              "demo_notifications",
              JSON.stringify(list)
            );
          }

          const activeIndex = this.notifications.findIndex((n) => n.id === id);
          if (activeIndex !== -1) {
            this.notifications[activeIndex] = updated;
          }
          return updated;
        }

        const updated = await notificationsService.resolveNotification(id, {
          changeReason,
        });
        const index = this.notifications.findIndex((n) => n.id === id);
        if (index !== -1) {
          this.notifications[index] = updated;
        }
        return updated;
      } catch (err) {
        this.error = err.message || "Failed to resolve notification";
        this.errorStatus = err.status || null;
        throw err;
      } finally {
        this.loading = false;
      }
    },
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("demo_notifications");
      }
      this.notifications = JSON.parse(JSON.stringify(defaultTemplates));
    },
  },
});
