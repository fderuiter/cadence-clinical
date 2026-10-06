# notifications API

Generated from `docs/openapi/notifications_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/notifications/render` | RenderNotificationTemplate | Renders a Liquid notification template asynchronously. |
| `GET` | `/api/v1/notifications/status` | NotificationsStatus | Returns Notifications operational status. |
| `POST` | `/api/v1/notifications/subscriptions` | RegisterPushSubscription | Registers or updates a participant device Web Push subscription. |
| `DELETE` | `/api/v1/notifications/subscriptions` | UnsubscribePushSubscription | Deactivates or removes a device Web Push subscription. |
| `POST` | `/api/v1/notifications/trigger-ecoa-job` | TriggerEcoaPushJob | Manually triggers the background eCOA visit window push scheduler job. |
| `GET` | `/api/v1/notifications/vapid-public-key` | GetVapidPublicKey | Gets the VAPID public key for Web Push subscription negotiation. |
| `GET` | `/healthz` | NotificationsHealthCheck | Notifications dispatch health probe. |
