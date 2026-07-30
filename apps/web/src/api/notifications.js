import { apiClient } from "./apiClient";

/**
 * Service module for the Notifications microservice.
 * Interfaces with notification queries and actions.
 */
export const notificationsService = {
  /**
   * Retrieves notifications matching filters.
   */
  getNotifications(options = {}) {
    const { category, priority, status, ...rest } = options;
    const params = new URLSearchParams();
    if (category) params.append("category", category);
    if (priority) params.append("priority", priority);
    if (status) params.append("status", status);

    const queryString = params.toString();
    const path = queryString
      ? `/api/v1/notifications?${queryString}`
      : "/api/v1/notifications";
    return apiClient.get(path, rest);
  },

  /**
   * Retrieves detail for a single notification.
   */
  getNotification(id, options = {}) {
    return apiClient.get(`/api/v1/notifications/${id}`, options);
  },

  /**
   * Acknowledges a notification (transition from OPEN to ACKNOWLEDGED).
   */
  acknowledgeNotification(id, options = {}) {
    return apiClient.post(
      `/api/v1/notifications/${id}/acknowledge`,
      {},
      options
    );
  },

  /**
   * Resolves a notification (transition from OPEN or ACKNOWLEDGED to RESOLVED).
   */
  resolveNotification(id, options = {}) {
    return apiClient.post(`/api/v1/notifications/${id}/resolve`, {}, options);
  },
};
