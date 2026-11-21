# tickets API

Generated from `docs/openapi/tickets_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/tickets` | ListTickets |  |
| `POST` | `/api/v1/tickets` | CreateTicket |  |
| `GET` | `/api/v1/tickets/status` | TicketsStatus | Returns Tickets operational status. |
| `GET` | `/api/v1/tickets/{ticket_id}` | GetTicket |  |
| `GET` | `/healthz` | TicketsHealthCheck | Support tickets health probe. |
