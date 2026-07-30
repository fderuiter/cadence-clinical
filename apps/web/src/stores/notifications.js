import { defineStore } from "pinia";
import { notificationsService } from "../api/notifications";

export const useNotificationsStore = defineStore("notifications", {
  state: () => ({
    notifications: [],
    filters: {
      category: "",
      priority: "",
      status: "",
    },
    loading: false,
    error: null,
    errorStatus: null,
  }),
  actions: {
    setFilter(key, value) {
      this.filters[key] = value;
    },
    async fetchNotifications() {
      this.loading = true;
      this.error = null;
      this.errorStatus = null;
      try {
        const data = await notificationsService.getNotifications({
          category: this.filters.category || undefined,
          priority: this.filters.priority || undefined,
          status: this.filters.status || undefined,
        });
        this.notifications = data || [];
      } catch (err) {
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
  },
});
