import { apiClient } from "./apiClient";

export const notificationsService = {
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
  getNotification(id, options = {}) {
    return apiClient.get(`/api/v1/notifications/${id}`, options);
  },
  acknowledgeNotification(id, options = {}) {
    return apiClient.post(
      `/api/v1/notifications/${id}/acknowledge`,
      {},
      options
    );
  },
  resolveNotification(id, options = {}) {
    return apiClient.post(`/api/v1/notifications/${id}/resolve`, {}, options);
  },
};
