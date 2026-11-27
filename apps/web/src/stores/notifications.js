import { defineStore } from "pinia";
import {
  DEFAULT_DEMO_PARTICIPANT,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

let cachedNotificationsService = null;
async function getNotificationsService() {
  if (cachedNotificationsService) return cachedNotificationsService;
  const mod = await import("../api/notifications");
  cachedNotificationsService = mod.notificationsService;
  return cachedNotificationsService;
}

function buildDefaultTemplates() {
  if (!isDemoBuild()) {
    return [];
  }
  const leadUsername =
    getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead";
  const subjectCode = DEFAULT_DEMO_PARTICIPANT?.subjectCode || "SUBJ-101-001";
  return [
    {
      id: "notif-001",
      recipient_user_id: leadUsername,
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
      created_at: "2026-01-15T12:00:00Z",
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
      message_content: `Review form submission for ${subjectCode}.`,
      related_entity_id: "FSUB-001",
      related_entity_type: "FORM",
      status: "OPEN",
      delivery_state: "DELIVERED",
      retries: 0,
      created_at: "2026-01-16T14:30:00Z",
      created_by: "system",
      version_index: 1,
      reason_for_change: "System generated",
    },
    {
      id: "notif-003",
      recipient_user_id: leadUsername,
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
      created_at: "2026-01-17T09:00:00Z",
      created_by: "system",
      version_index: 1,
      reason_for_change: "System generated",
    },
  ];
}

function getDefaultTemplates() {
  return isDemoBuild() ? buildDefaultTemplates() : [];
}

function getStorageKey(authStore) {
  if (!isDemoBuild()) {
    return "";
  }
  let store = authStore;
  if (!store) {
    try {
      store = useAuthStore();
    } catch {
      // Pinia active store fallback
    }
  }
  const persona = store?.currentPersona || "super_admin";
  return `demo_notifications_${persona}`;
}

export const useNotificationsStore = defineStore("notifications", {
  state: () => {
    let savedNotifications = null;
    // Locally simulated notifications exist only in a demo build (#4523).
    const demoBuild = isDemoBuild();
    if (demoBuild && typeof window !== "undefined" && window.localStorage) {
      try {
        let authStore = null;
        try {
          authStore = useAuthStore();
        } catch {
          // Store not active yet
        }
        const key = getStorageKey(authStore);
        const stored = window.localStorage.getItem(key);
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
      notifications: demoBuild
        ? savedNotifications || getDefaultTemplates()
        : [],
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
        const notificationsService = await getNotificationsService();
        if (isDemoBuild()) {
          const authStore = useAuthStore();
          const storageKey = getStorageKey(authStore);
          const isMocked =
            notificationsService.getNotifications &&
            (notificationsService.getNotifications._isMockFunction ||
              typeof notificationsService.getNotifications.mock === "object");
          if (authStore.isDemoMode && !authStore.token && !isMocked) {
            await new Promise((resolve) => setTimeout(resolve, 100));
            let list = [];
            if (typeof window !== "undefined" && window.localStorage) {
              const stored = window.localStorage.getItem(storageKey);
              if (stored) {
                list = JSON.parse(stored);
              } else {
                list = getDefaultTemplates();
                window.localStorage.setItem(storageKey, JSON.stringify(list));
              }
            } else {
              list = getDefaultTemplates();
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
    async _transitionNotificationStatus(
      id,
      changeReason,
      { status, serviceMethod, fallbackError }
    ) {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const notificationsService = await getNotificationsService();
        const method = notificationsService[serviceMethod];
        if (isDemoBuild()) {
          const authStore = useAuthStore();
          const storageKey = getStorageKey(authStore);
          const isMocked =
            method &&
            (method._isMockFunction || typeof method.mock === "object");
          if (authStore.isDemoMode && !authStore.token && !isMocked) {
            await new Promise((resolve) => setTimeout(resolve, 100));
            let list = [];
            if (typeof window !== "undefined" && window.localStorage) {
              const stored = window.localStorage.getItem(storageKey);
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
              status,
              version_index: (list[index].version_index || 1) + 1,
              reason_for_change: changeReason,
            };
            list[index] = updated;

            if (typeof window !== "undefined" && window.localStorage) {
              window.localStorage.setItem(storageKey, JSON.stringify(list));
            }

            const activeIndex = this.notifications.findIndex(
              (n) => n.id === id
            );
            if (activeIndex !== -1) {
              this.notifications[activeIndex] = updated;
            }
            return updated;
          }
        }

        const updated = await notificationsService[serviceMethod](id, {
          changeReason,
        });
        const index = this.notifications.findIndex((n) => n.id === id);
        if (index !== -1) {
          this.notifications[index] = updated;
        }
        return updated;
      } catch (err) {
        this.error = err.message || fallbackError;
        this.errorStatus = err.status || null;
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async acknowledge(id, changeReason) {
      return this._transitionNotificationStatus(id, changeReason, {
        status: "ACKNOWLEDGED",
        serviceMethod: "acknowledgeNotification",
        fallbackError: "Failed to acknowledge notification",
      });
    },
    async resolve(id, changeReason) {
      return this._transitionNotificationStatus(id, changeReason, {
        status: "RESOLVED",
        serviceMethod: "resolveNotification",
        fallbackError: "Failed to resolve notification",
      });
    },
    resetDemoStorage() {
      if (!isDemoBuild()) {
        return;
      }
      if (typeof window !== "undefined" && window.localStorage) {
        const keysToRemove = [];
        for (let i = 0; i < window.localStorage.length; i += 1) {
          const key = window.localStorage.key(i);
          if (
            key === "demo_notifications" ||
            (key && key.startsWith("demo_notifications_"))
          ) {
            keysToRemove.push(key);
          }
        }
        for (const key of keysToRemove) {
          window.localStorage.removeItem(key);
        }
      }
      this.notifications = getDefaultTemplates();
    },
  },
});
