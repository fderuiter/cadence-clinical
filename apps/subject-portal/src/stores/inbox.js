// Inbox notifications and reminders.
import { defineStore } from "pinia";

export const useInboxStore = defineStore("inbox", {
  state: () => ({
    notifications: [],
    notificationsLoading: false,
    notificationsError: null,
    unreadCount: 0,
  }),
});
