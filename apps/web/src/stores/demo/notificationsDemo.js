import {
  DEFAULT_DEMO_PARTICIPANT,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import { isDemoBuild } from "../../utils/demoMode.js";
import { useAuthStore } from "../auth.js";

export function buildDefaultTemplates() {
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

export function getDefaultTemplates() {
  return isDemoBuild() ? buildDefaultTemplates() : [];
}

export function getNotificationsStorageKey(authStore) {
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

export function getInitialDemoNotifications(authStore) {
  if (!isDemoBuild()) {
    return [];
  }
  let savedNotifications = null;
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const key = getNotificationsStorageKey(authStore);
      const stored = window.localStorage.getItem(key);
      if (stored) {
        savedNotifications = JSON.parse(stored);
      }
    } catch (e) {
      console.error("Failed to parse demo_notifications from localStorage", e);
    }
  }
  return savedNotifications || getDefaultTemplates();
}

function isServiceMethodMocked(fn) {
  return Boolean(fn && (fn._isMockFunction || typeof fn.mock === "object"));
}

export async function fetchDemoNotifications({
  authStore,
  notificationsService,
  filters,
}) {
  if (
    !isDemoBuild() ||
    !authStore?.isDemoMode ||
    authStore.token ||
    isServiceMethodMocked(notificationsService?.getNotifications)
  ) {
    return { handled: false, notifications: [] };
  }

  const storageKey = getNotificationsStorageKey(authStore);
  await new Promise((resolve) => setTimeout(resolve, 100));
  let list;
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

  const filtered = list.filter((n) => {
    if (filters?.category && n.category !== filters.category) return false;
    if (filters?.priority && n.priority !== filters.priority) return false;
    if (filters?.status && n.status !== filters.status) return false;
    return true;
  });

  return { handled: true, notifications: filtered };
}

export async function acknowledgeDemoNotification({
  authStore,
  notificationsService,
  currentNotifications,
  id,
  changeReason,
}) {
  if (
    !isDemoBuild() ||
    !authStore?.isDemoMode ||
    authStore.token ||
    isServiceMethodMocked(notificationsService?.acknowledgeNotification)
  ) {
    return { handled: false, updated: null };
  }

  const storageKey = getNotificationsStorageKey(authStore);
  await new Promise((resolve) => setTimeout(resolve, 100));
  let list = [];
  if (typeof window !== "undefined" && window.localStorage) {
    const stored = window.localStorage.getItem(storageKey);
    if (stored) {
      list = JSON.parse(stored);
    }
  }
  if (list.length === 0) {
    list = JSON.parse(JSON.stringify(currentNotifications || []));
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
    window.localStorage.setItem(storageKey, JSON.stringify(list));
  }

  return { handled: true, updated };
}

export async function resolveDemoNotification({
  authStore,
  notificationsService,
  currentNotifications,
  id,
  changeReason,
}) {
  if (
    !isDemoBuild() ||
    !authStore?.isDemoMode ||
    authStore.token ||
    isServiceMethodMocked(notificationsService?.resolveNotification)
  ) {
    return { handled: false, updated: null };
  }

  const storageKey = getNotificationsStorageKey(authStore);
  await new Promise((resolve) => setTimeout(resolve, 100));
  let list = [];
  if (typeof window !== "undefined" && window.localStorage) {
    const stored = window.localStorage.getItem(storageKey);
    if (stored) {
      list = JSON.parse(stored);
    }
  }
  if (list.length === 0) {
    list = JSON.parse(JSON.stringify(currentNotifications || []));
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
    window.localStorage.setItem(storageKey, JSON.stringify(list));
  }

  return { handled: true, updated };
}

export function resetDemoNotificationsStorage() {
  if (!isDemoBuild()) {
    return [];
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
  return getDefaultTemplates();
}
