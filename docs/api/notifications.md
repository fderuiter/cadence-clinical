# notifications API

Generated from `docs/openapi/notifications_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

The service exposes 3 operations.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/notifications/render` | RenderNotificationTemplate | Renders a Liquid notification template asynchronously. |
| `GET` | `/api/v1/notifications/status` | NotificationsStatus | Returns Notifications operational status. |
| `GET` | `/healthz` | NotificationsHealthCheck | Notifications dispatch health probe. |
