# interop API

Generated from `docs/openapi/interop_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/interop/dct/dtp/shipments` | ListDtpShipments | List Direct-to-Patient drug shipments |
| `POST` | `/api/v1/interop/dct/dtp/shipments` | CreateDtpShipment | Create Direct-to-Patient (DTP) courier dispatch order |
| `GET` | `/api/v1/interop/dct/dtp/shipments/{trackingNumber}` | GetDtpShipment | Retrieve single DTP shipment details by tracking number |
| `POST` | `/api/v1/interop/dct/dtp/shipments/{trackingNumber}/delivery` | ConfirmDtpDelivery | Confirm final delivery with recipient signature |
| `POST` | `/api/v1/interop/dct/dtp/shipments/{trackingNumber}/handoff` | RecordDtpHandoff | Record Chain of Custody (CoC) handoff event |
| `POST` | `/api/v1/interop/dct/dtp/shipments/{trackingNumber}/temperature` | IngestDtpTemperature | Ingest cold-chain temperature telemetry points & calculate MKT |
| `GET` | `/api/v1/interop/status` | InteropStatus | Returns Interoperability operational status. |
| `GET` | `/healthz` | InteropHealthCheck | Interoperability FHIR & CDISC connector health probe. |
