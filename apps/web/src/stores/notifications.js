import { defineStore } from "pinia";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";
import {
  acknowledgeDemoNotification,
  fetchDemoNotifications,
  getInitialDemoNotifications,
  resetDemoNotificationsStorage,
  resolveDemoNotification,
} from "./demo/notificationsDemo.js";

let cachedNotificationsService = null;
async function getNotificationsService() {
  if (cachedNotificationsService) return cachedNotificationsService;
  const mod = await import("../api/notifications");
  cachedNotificationsService = mod.notificationsService;
  return cachedNotificationsService;
}

export const useNotificationsStore = defineStore("notifications", {
  state: () => {
    // Locally simulated notifications exist only in a demo build (#4523).
    const demoBuild = isDemoBuild();
    let authStore = null;
    if (demoBuild) {
      try {
        authStore = useAuthStore();
      } catch {
        // Store not active yet
      }
    }
    return {
      notifications: demoBuild ? getInitialDemoNotifications(authStore) : [],
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
          const demoResult = await fetchDemoNotifications({
            authStore: useAuthStore(),
            notificationsService,
            filters: this.filters,
          });
          if (demoResult?.handled) {
            this.notifications = demoResult.notifications;
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
    async acknowledge(id, changeReason) {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const notificationsService = await getNotificationsService();
        if (isDemoBuild()) {
          const demoResult = await acknowledgeDemoNotification({
            authStore: useAuthStore(),
            notificationsService,
            currentNotifications: this.notifications,
            id,
            changeReason,
          });
          if (demoResult?.handled) {
            const activeIndex = this.notifications.findIndex(
              (n) => n.id === id
            );
            if (activeIndex !== -1) {
              this.notifications[activeIndex] = demoResult.updated;
            }
            return demoResult.updated;
          }
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
        const notificationsService = await getNotificationsService();
        if (isDemoBuild()) {
          const demoResult = await resolveDemoNotification({
            authStore: useAuthStore(),
            notificationsService,
            currentNotifications: this.notifications,
            id,
            changeReason,
          });
          if (demoResult?.handled) {
            const activeIndex = this.notifications.findIndex(
              (n) => n.id === id
            );
            if (activeIndex !== -1) {
              this.notifications[activeIndex] = demoResult.updated;
            }
            return demoResult.updated;
          }
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
      if (!isDemoBuild()) {
        return;
      }
      this.notifications = resetDemoNotificationsStorage();
    },
  },
});
