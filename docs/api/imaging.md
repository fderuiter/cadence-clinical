# imaging API

Generated from `docs/openapi/imaging_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path                                                                                               | Operation                           | Summary                                     |
| ------ | -------------------------------------------------------------------------------------------------- | ----------------------------------- | ------------------------------------------- |
| `POST` | `/api/v1/imaging/adjudications`                                                                    | SubmitOrEvaluateAdjudicationSession |                                             |
| `GET`  | `/api/v1/imaging/adjudications/{sessionId}`                                                        | GetImagingAdjudicationSession       |                                             |
| `POST` | `/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate`                                       | ChairAdjudicateImagingSession       |                                             |
| `GET`  | `/api/v1/imaging/certificates/{certificateId}`                                                     | GetDeidCertificate                  |                                             |
| `GET`  | `/api/v1/imaging/status`                                                                           | ImagingStatus                       | Returns Medical Imaging operational status. |
| `POST` | `/api/v1/imaging/studies`                                                                          | StowRsStoreInstances                |                                             |
| `GET`  | `/api/v1/imaging/studies/{studyUid}`                                                               | WadoRsGetStudy                      |                                             |
| `GET`  | `/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}` | WadoRsGetInstanceFrames             |                                             |
| `GET`  | `/healthz`                                                                                         | ImagingHealthCheck                  | Medical Imaging DICOM service health probe. |
