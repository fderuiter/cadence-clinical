# Clinical DDF Bridge

Middleware bridge for automated clinical study provisioning between OpenStudyBuilder and OpenClinica.

## Directory Structure
- `middleware/`: FastAPI ETL service (USDM extraction, schema translation, OpenClinica provisioning).
- `keycloak/`: SSO configuration and OIDC realm definitions.
- `docker/`: Unified local development stack (`docker-compose.yml`).
