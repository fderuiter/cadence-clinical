# org API

Generated from `docs/openapi/org_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/org/preferences/onboarding` | GetUserOnboardingPreference | Fetch user onboarding preferences by persona query parameter |
| `PUT` | `/api/v1/org/preferences/onboarding` | SaveUserOnboardingPreference | Save user onboarding preferences |
| `GET` | `/api/v1/org/preferences/onboarding/{persona}` | GetUserOnboardingPreferenceByRoute | Fetch user onboarding preferences by persona route parameter |
| `PUT` | `/api/v1/org/preferences/onboarding/{persona}` | SaveUserOnboardingPreferenceByRoute | Save user onboarding preferences by persona route parameter |
| `GET` | `/api/v1/org/status` | OrgStatus | Returns Organization directory operational status. |
| `GET` | `/healthz` | OrgHealthCheck | Organization directory health probe. |
