# tickets API

Generated from `docs/openapi/tickets_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

The service exposes 11 operations.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/tickets` | ListTickets |  |
| `POST` | `/api/v1/tickets` | CreateTicket |  |
| `GET` | `/api/v1/tickets/status` | TicketsStatus | Returns Tickets operational status. |
| `GET` | `/api/v1/tickets/{ticket_id}` | GetTicket |  |
| `PATCH` | `/api/v1/tickets/{ticket_id}` | UpdateTicket |  |
| `GET` | `/api/v1/tickets/{ticket_id}/audit-trail` | GetTicketAuditTrail |  |
| `GET` | `/api/v1/tickets/{ticket_id}/comments` | ListTicketComments |  |
| `POST` | `/api/v1/tickets/{ticket_id}/comments` | CreateTicketComment |  |
| `POST` | `/api/v1/tickets/{ticket_id}/sign` | SignTicket |  |
| `POST` | `/api/v1/tickets/{ticket_id}/transitions` | TransitionTicket |  |
| `GET` | `/healthz` | TicketsHealthCheck | Support tickets health probe. |
