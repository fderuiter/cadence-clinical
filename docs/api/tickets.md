# tickets API

Generated from `docs/openapi/tickets_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/tickets` | ListTickets |  |
| `POST` | `/api/v1/tickets` | CreateTicket |  |
| `POST` | `/api/v1/tickets/batch` | BatchUpdateTickets |  |
| `POST` | `/api/v1/tickets/batch-update` | BatchUpdateTicketsAlias |  |
| `GET` | `/api/v1/tickets/status` | TicketsStatus | Returns Tickets operational status. |
| `GET` | `/api/v1/tickets/{id}` | GetTicketById |  |
| `PUT` | `/api/v1/tickets/{id}` | UpdateTicket |  |
| `GET` | `/healthz` | TicketsHealthCheck | Support tickets health probe. |
