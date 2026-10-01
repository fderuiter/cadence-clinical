import { encodeChangeReason } from "./changeReason";
import { openApiClient, unwrapResponse } from "./client";

export interface NotificationFilterOptions {
  category?: "ALERTS" | "SYSTEM" | "ACTION_ITEMS" | null;
  priority?: "HIGH" | "MEDIUM" | "LOW" | "CRITICAL" | null;
  status?: "OPEN" | "ACKNOWLEDGED" | "RESOLVED" | null;
}

/**
 * Service module for the Notifications microservice.
 * Interfaces with notification queries and actions.
 */
export const notificationsService = {
  /**
   * Retrieves notifications matching filters.
   */
  getNotifications(
    options: NotificationFilterOptions = {},
    restOptions: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/notifications", {
        params: {
          query: {
            category: options.category ?? undefined,
            priority: options.priority ?? undefined,
            status: options.status ?? undefined,
          },
        },
        ...restOptions,
      })
    );
  },

  /**
   * Retrieves detail for a single notification.
   */
  getNotification(id: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/notifications/{id}", {
        params: { path: { id } },
        ...options,
      })
    );
  },

  /**
   * Acknowledges a notification (transition from OPEN to ACKNOWLEDGED).
   */
  acknowledgeNotification(
    id: string,
    options: { changeReason?: string; [key: string]: unknown } = {}
  ) {
    const { changeReason, headers, ...rest } = options;
    return unwrapResponse(
      openApiClient.POST("/api/v1/notifications/{id}/acknowledge", {
        params: { path: { id } },
        headers: {
          ...(changeReason
            ? { "X-Change-Reason": encodeChangeReason(changeReason) }
            : {}),
          ...((headers as Record<string, string>) || {}),
        },
        changeReason,
        ...rest,
      })
    );
  },

  /**
   * Resolves a notification (transition from OPEN or ACKNOWLEDGED to RESOLVED).
   */
  resolveNotification(
    id: string,
    options: { changeReason?: string; [key: string]: unknown } = {}
  ) {
    const { changeReason, headers, ...rest } = options;
    return unwrapResponse(
      openApiClient.POST("/api/v1/notifications/{id}/resolve", {
        params: { path: { id } },
        headers: {
          ...(changeReason
            ? { "X-Change-Reason": encodeChangeReason(changeReason) }
            : {}),
          ...((headers as Record<string, string>) || {}),
        },
        changeReason,
        ...rest,
      })
    );
  },

  /**
   * Bulk acknowledges notifications (transition from OPEN to ACKNOWLEDGED).
   */
  acknowledgeBulkNotifications(
    ids: string[],
    options: { changeReason?: string; [key: string]: unknown } = {}
  ) {
    const { changeReason, headers, ...rest } = options;
    return unwrapResponse(
      openApiClient.POST("/api/v1/notifications/bulk-acknowledge", {
        body: { ids },
        headers: {
          ...(changeReason
            ? { "X-Change-Reason": encodeChangeReason(changeReason) }
            : {}),
          ...((headers as Record<string, string>) || {}),
        },
        changeReason,
        ...rest,
      })
    );
  },

  /**
   * Bulk resolves notifications (transition from OPEN or ACKNOWLEDGED to RESOLVED).
   */
  resolveBulkNotifications(
    ids: string[],
    options: { changeReason?: string; [key: string]: unknown } = {}
  ) {
    const { changeReason, headers, ...rest } = options;
    return unwrapResponse(
      openApiClient.POST("/api/v1/notifications/bulk-resolve", {
        body: { ids },
        headers: {
          ...(changeReason
            ? { "X-Change-Reason": encodeChangeReason(changeReason) }
            : {}),
          ...((headers as Record<string, string>) || {}),
        },
        changeReason,
        ...rest,
      })
    );
  },
};
