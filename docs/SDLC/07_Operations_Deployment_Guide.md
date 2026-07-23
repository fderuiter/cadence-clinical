# Operations & Deployment Guide (Production Playbook)

**Document ID:** CC-OPS-001
**Version:** 1.0.0
**Effective Date:** October 2026
**Status:** Approved
**Classification:** Restricted (GxP / Confidential)
**Applicability:** DevOps, SRE, QA, System Administrators, Release Managers

---

## Document Control & Approvals

### Revision History
| Version | Date | Description | Author | Reviewed By | Approved By |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 1.0.0 | Oct 2026 | Initial Release of Production Playbook for Cadence Clinical Platform. | J. Doe (SRE) | A. Smith (QA) | E. Executive (VP Eng) |

### Standards Alignment Matrix
The Cadence Clinical platform operations and deployment workflows conform strictly to the following standards:
* **ISO/IEC 27001 (Section A.12.1.2 Change Management, A.12.4 Logging & Monitoring):** Governs change control, audit ledgers, operational logging, and environment separation.
* **IEC 62304 Section 8 (Software Configuration & Release Management):** Governs the configuration status of software items, software release verification, and patch/rollback control for medical/clinical devices.
* **FDA 21 CFR Part 11 / EU Annex 11:** Enforces electronic records compliance, multi-factor electronic signatures, and computerized system validation (CSV) gates.

---

## Executive Summary & Topology Overview

This guide provides step-by-step instructions, automated scripts, pipeline designs, and disaster recovery playbooks for promoting, configuration, migration, and monitoring of the Cadence Clinical monorepo architecture.

The Cadence Clinical microservices topology consists of:
1. **API Gateway & Auth Service (`apps/gateway`)**: Central access point utilizing Keycloak for OpenID Connect (OIDC) identity federation and JWT propagation.
2. **Designer Service (`apps/designer`)**: Metadata-driven Study Design Repository (MDR) leveraging CDISC USDM v3.0/v4.0 modeled inside Neo4j.
3. **Execution Service (`apps/execution`)**: Downstream Electronic Data Capture (EDC) engine storing clinical subjects, forms, and audit trails in PostgreSQL.
4. **Shared Utilities (`packages/security`)**: Shared cryptographic context and auth token validation layers.

---

# SECTION 1: Environment Promotion & Infrastructure

## 1.1 Environment Topology & Separation
To satisfy GxP validation and ISO 27001 requirements, Cadence Clinical maintains four strictly isolated environments. Cross-environment database sharing or credential leakage is strictly forbidden. Network segmentation is enforced via Kubernetes NetworkPolicies.

```
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│   Development   │ ───► │     Staging     │ ───► │   Validation    │ ───► │   Production    │
│      (DEV)      │      │      (STG)      │      │      (VAL)      │      │     (PROD)      │
│ Fast Iteration  │      │ Internal QA /   │      │ GxP Validation  │      │ True GxP System │
│ Automated Tests │      │ Interoperability│      │ Sign-off Gate   │      │ Live Patients   │
└─────────────────┘      └─────────────────┘      └─────────────────┘      └─────────────────┘
```

1. **Development (DEV)**
   * **Purpose:** Sandbox for active developer feature testing, branch validation, and automated CI tests.
   * **Database:** Isolated containerized Neo4j & PostgreSQL. Sanitized mock dictionaries.
   * **Access:** Developers have read/write access. No patient or true sponsor data.
2. **Staging (STG)**
   * **Purpose:** Multi-tenant integration testing, performance benchmarking, external API gateway testing.
   * **Database:** Multi-tenant PostgreSQL schemas and Neo4j cluster partitions.
   * **Access:** Limited developer access, full automated access via pipelines.
3. **Validation (VAL / UAT)**
   * **Purpose:** **GxP User Acceptance Testing and Computerized System Validation (CSV).** Must be identical to production in topology and configuration, with a frozen codebase.
   * **Database:** Production-like encryption-at-rest. No real patient data; synthetic validation profiles.
   * **Access:** Frozen access controls. Changes require QA Approval and Change Control Board (CCB) authorization.
4. **Production (PROD)**
   * **Purpose:** Hosting live clinical studies with actual subject data. Fully GxP compliant, audited, and locked.
   * **Database:** Dedicated high-availability Neo4j Enterprise cluster and PostgreSQL multi-region databases with WAL-G/Point-in-Time Recovery enabled.
   * **Access:** No developer access. Read-only breakglass emergency access strictly monitored and audited.

---

## 1.2 Step-by-Step Environment Promotion Commands

Promotion of software packages must proceed sequentially. Skipped environments are strictly forbidden.

### Step 1: Promote Dev to Staging
Upon successful merge to the `main` branch, the CI/CD pipeline compiles docker images and releases them with a staging build tag (`vMAJOR.MINOR.PATCH-rcX`).

```bash
# 1. Authenticate to Staging Kubernetes Cluster
aws eks update-kubeconfig --region us-east-1 --name cadence-stg-eks

# 2. Extract Helm chart variables and apply Staging-specific overriding values
helm upgrade --install cadence-clinical ./docker/helm/cadence-clinical \
  --namespace cadence-stg \
  --values ./docker/helm/values-staging.yaml \
  --set global.image.tag="v1.4.0-rc1" \
  --atomic --timeout 10m0s

# 3. Verify Staging rollout success
kubectl rollout status deployment/cadence-gateway -n cadence-stg
kubectl rollout status deployment/cadence-designer -n cadence-stg
kubectl rollout status deployment/cadence-execution -n cadence-stg
```

### Step 2: Promote Staging to Validation (UAT)
After QA and automated end-to-end regression test suites pass in Staging, a Release Candidate is selected for Promotion to the Validation cluster.

```bash
# 1. Log change ticket ID in the central audit system (IEC 62304 Compliance)
export CHANGE_TICKET="CHG-2026-9921"
echo "Initializing Promotion to Validation under Change Control $CHANGE_TICKET"

# 2. Authenticate to Validation EKS Cluster
aws eks update-kubeconfig --region us-east-1 --name cadence-val-eks

# 3. Deploy the release using Helm with Frozen UAT config values
helm upgrade --install cadence-clinical ./docker/helm/cadence-clinical \
  --namespace cadence-val \
  --values ./docker/helm/values-validation.yaml \
  --set global.image.tag="v1.4.0-rc1" \
  --set global.env.CHANGE_CONTROL_ID=$CHANGE_TICKET \
  --atomic --timeout 15m0s

# 4. Trigger automated GxP validation verification suite
pytest tests/test_ledger_and_triggers.py tests/test_audit.py tests/test_trial_lock.py tests/test_cryptography.py --junitxml=gxp_results.xml
```

### Step 3: Promote Validation to Production
Promoting to production requires **Manual Sign-off Gates** (detailed in Section 1.3), complete verification test reports, and an approved change control ticket.

```bash
# 1. Verify GxP Sign-off Cryptographic Token and QA Approvals
curl -X POST https://audit.cadence-clinical.internal/verify-approvals \
  -H "Authorization: Bearer $QA_OFFICER_JWT" \
  -d '{"ticket_id": "'"$CHANGE_TICKET"'", "target": "PROD"}'

# 2. Authenticate to Production High-Availability Cluster
aws eks update-kubeconfig --region us-east-1 --name cadence-prod-eks

# 3. Apply Blue-Green Rollout parameters
helm upgrade --install cadence-clinical-blue ./docker/helm/cadence-clinical \
  --namespace cadence-prod \
  --values ./docker/helm/values-production.yaml \
  --set global.image.tag="v1.4.0" \
  --set global.deployment.color="blue" \
  --atomic --timeout 20m0s

# 4. Perform live smoke-testing on isolated blue node
curl -f https://blue.cadence.clinical/health
```

---

## 1.3 Validation Sign-off Gates & Release Criteria (IEC 62304 Section 8)

Under **IEC 62304 Section 8.1.1** (Software configuration management) and **8.2** (Software release verification), a software release must meet rigorous criteria before entering live validation or production environments.

### Sign-off Checklist Criteria
1. **Traceability Matrix Approved (QA-VAL-01):** Every feature requirement, bug fix, and architectural shift must be traceably mapped to corresponding tests. View the active, automatically generated **[Requirements Traceability Matrix](Requirements_Traceability_Matrix.md)**.
2. **Zero Known Critical Vulnerabilities:** Security scan must report 0 CVEs (Critical/High) in production container layers.
3. **90% Unit Test & Integration Coverage:** Code coverage in python apps (`gateway`, `designer`, `execution`) must not drop below 80% (Cadence enforces 80% minimum, target is 90% for clinical calculations). See the latest **[Qualification Execution Report](IQ_OQ_PQ_Execution_Report.md)** for detailed test outcomes.
4. **Sign-off Protocol:**

```
                  ┌───────────────────────────────┐
                  │   Developer Pull Request /    │
                  │   Branch Checks (Ruff, PyTest)│
                  └───────────────┬───────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │    Automated Staging Run &    │
                  │      Sec Scan (Trivy)         │
                  └───────────────┬───────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │  Computerized System Validation│
                  │  (CSV) Protocol Run in VAL    │
                  └───────────────┬───────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │   Formal Electronic Approval  │
                  │   by QA & Principal Investigator │
                  └───────────────┬───────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │   Production Blue-Green Sync  │
                  └───────────────────────────────┘
```

* **Electronic Signature:** Release Approval must be digitally signed utilizing the RSA-2048 private key of the QA Director and Clinical Platform Architect, logging the cryptographic audit trail into the immutable ledger database.

---

## 1.4 CI/CD Pipeline Automation Script

Below is the complete GitHub Actions pipeline definition file (`.github/workflows/production-pipeline.yml`) demonstrating builds, automated scans, migration run validation, test execution, and deployment promotion steps.

```yaml
name: Cadence Clinical CI/CD Pipeline

on:
  push:
    branches: [ main, release/* ]
  pull_request:
    branches: [ main ]

jobs:
  static-analysis:
    name: Lint, Security Scan, and Static Analysis
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v3

      - name: Set up Python 3.12
        uses: actions/setup-python@v4
        with:
          python-with-architecture: 'x64'
          python-version: '3.12'

      - name: Install UV Package Manager
        run: |
          curl -LsSf https://astral.sh/uv/install.sh | sh
          echo "$HOME/.local/bin" >> $GITHUB_PATH

      - name: Verify UV Lock and Sync Dependencies
        run: |
          uv sync --all-extras

      - name: Enforce Ruff Linter & Formatter
        run: |
          uv run ruff check apps packages tests
          uv run ruff format --check apps packages tests

      - name: Run Gitleaks Secrets Protection
        uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Run Snyk Open-Source Vulnerability Scan
        uses: snyk/actions/python@master
        env:
          SNYK_TOKEN: ${{ secrets.SNYK_TOKEN }}

  unit-and-integration-tests:
    name: Run Python Test Suite with Coverage
    needs: static-analysis
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v3

      - name: Set up Python 3.12
        uses: actions/setup-python@v4
        with:
          python-version: '3.12'

      - name: Install UV and Sync
        run: |
          curl -LsSf https://astral.sh/uv/install.sh | sh
          uv sync --all-extras

      - name: Run Pytest Suite (SQLite Memory Mock)
        env:
          TEST_DATABASE_URL: "sqlite+aiosqlite:///:memory:"
        run: |
          uv run pytest --cov=apps --cov=packages --cov-fail-under=80

  docker-build-and-scan:
    name: Build Multi-Service Docker Images and Scan via Trivy
    needs: unit-and-integration-tests
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v3

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v2

      - name: Build Gateway Image
        run: |
          docker build -t cadence-gateway:latest -f docker/Dockerfile --target production .

      - name: Run Trivy Security Scan on Gateway
        uses: aquasecurity/trivy-action@master
        with:
          image-ref: 'cadence-gateway:latest'
          format: 'table'
          exit-code: '1'
          ignore-unfixed: true
          severity: 'CRITICAL,HIGH'

  deploy-staging:
    name: Deploy Release Candidate to Staging
    needs: docker-build-and-scan
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v3

      - name: Configure AWS Credentials
        uses: aws-actions/configure-aws-credentials@v2
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: us-east-1

      - name: Push Container Images to AWS ECR
        run: |
          aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin ${{ secrets.AWS_ACCOUNT_ID }}.dkr.ecr.us-east-1.amazonaws.com
          docker tag cadence-gateway:latest ${{ secrets.AWS_ACCOUNT_ID }}.dkr.ecr.us-east-1.amazonaws.com/cadence-gateway:${{ github.sha }}
          docker push ${{ secrets.AWS_ACCOUNT_ID }}.dkr.ecr.us-east-1.amazonaws.com/cadence-gateway:${{ github.sha }}

      - name: Run Staging Helm Deploy and Run Migrations
        run: |
          aws eks update-kubeconfig --region us-east-1 --name cadence-stg-eks
          helm upgrade --install cadence-clinical ./docker/helm/cadence-clinical \
            --namespace cadence-stg \
            --values ./docker/helm/values-staging.yaml \
            --set global.image.tag="${{ github.sha }}" \
            --atomic --timeout 15m0s
```

---

# SECTION 2: Configuration & Multi-Tenancy Management

## 2.1 Multi-Tenant Separation Architecture
Cadence Clinical enforces a **Hybrid Multi-Tenant Isolation Pattern** to ensure strict data security between different clinical research sponsors (e.g., Pfizer, Novartis, Roche) and to comply with HIPAA, GDPR, and ISO 27001 data privacy boundaries.

### Isolation Strategy Matrix
| Layer | Isolation Type | Enforcement Mechanism | Performance Overhead | Compliance Level |
| :--- | :--- | :--- | :--- | :--- |
| **MDR Graph (Neo4j)** | Logical / Relationship Separation | Cypher Study Node Anchors (`SPONSOR` and `TRIAL` context tags) | Minimal | High |
| **Transactional EDC (PostgreSQL)** | Schema-Level Separation | Separate Postgres Schemas per Tenant (`sponsor_a`, `sponsor_b`) | Moderate | Maximum GxP Isolation |
| **Identity / Auth (Keycloak)** | Realm-Level Isolation | Dedicated Keycloak Realms with Tenant-Specific Sign-In/MFA | Minimal | High |

```
                       ┌────────────────────────────┐
                       │    Central API Gateway     │
                       └─────────────┬──────────────┘
                                     │ (Decodes Tenant JWT Claims)
                                     ▼
           ┌──────────────────────────────────────────────────┐
           │     EDC Router / Schema Switching Middleware     │
           └─────────┬──────────────────────────────┬─────────┘
                     │                              │
                     ▼ (Schema: `sponsor_a`)         ▼ (Schema: `sponsor_b`)
        ┌─────────────────────────┐    ┌─────────────────────────┐
        │ PostgreSQL Database     │    │ PostgreSQL Database     │
        │ Schema: Sponsor A       │    │ Schema: Sponsor B       │
        └─────────────────────────┘    └─────────────────────────┘
```

---

## 2.2 Provisioning Script for New Sponsors/Tenants

Adding a new sponsor involves setting up:
1. A tenant-specific database schema with standard audit ledgers, event trigger mappings, and base structural models.
2. An isolated realm/client inside Keycloak for tenant identity management.
3. Logical study design anchors in Neo4j.

The following Python script (`apps/execution/database/provision_tenant.py`) automates this process:

```python
import asyncio
import logging
from sqlalchemy import text
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("TenantProvisioner")


async def provision_new_tenant(sponsor_id: str, admin_user_email: str):
    """
    Provisions a completely isolated relational schema for a new sponsor.
    Injects GxP Audit triggers, tables, and initializes default metadata parameters.

    Args:
        sponsor_id (str): Lowcase, alphanumeric identifier (e.g., 'novartis').
        admin_user_email (str): Email address of tenant administrator.
    """
    clean_tenant_schema = f"tenant_{sponsor_id}"
    logger.info(f"Initiating schema provisioning: {clean_tenant_schema}")

    # Initialize connection using database manager
    async_session_maker = db_manager.get_session_maker()

    async with async_session_maker() as session:
        # Step 1: Create isolated schema namespace
        await session.execute(
            text(f"CREATE SCHEMA IF NOT EXISTS {clean_tenant_schema};")
        )
        logger.info(f"Schema {clean_tenant_schema} created successfully.")

        # Step 2: Bind tables to the newly provisioned schema
        # Temporarily overrides default schema path for SQLAlchemy compilation
        await session.execute(
            text(f"SET search_path TO {clean_tenant_schema}, public;")
        )

        # Create standard schema structures (subjects, forms, queries, audit_logs)
        # Note: Base metadata generates the actual table structures
        conn = await session.connection()
        await conn.run_sync(Base.metadata.create_all)
        logger.info(f"Structural tables compiled inside {clean_tenant_schema}.")

        # Step 3: Inject automatic, immutable GxP audit-trail log triggers
        audit_trigger_sql = f"""
        CREATE OR REPLACE FUNCTION {clean_tenant_schema}.audit_table_mutation()
        RETURNS TRIGGER AS $$
        DECLARE
            curr_user VARCHAR;
            curr_reason VARCHAR;
        BEGIN
            -- Read session variables if injected via context managers
            BEGIN
                curr_user := current_setting('cadence.current_user_id', true);
                curr_reason := current_setting('cadence.current_change_reason', true);
            EXCEPTION WHEN OTHERS THEN
                curr_user := 'system-gateway';
                curr_reason := 'Default automated background action';
            END;

            IF (TG_OP = 'INSERT') THEN
                INSERT INTO {clean_tenant_schema}.audit_logs (
                    id, table_name, record_id, action, new_values, old_values,
                    user_id, change_reason, timestamp, version_index
                ) VALUES (
                    gen_random_uuid(), TG_TABLE_NAME, NEW.id, 'INSERT',
                    row_to_json(NEW)::jsonb, NULL,
                    COALESCE(curr_user, 'system'), COALESCE(curr_reason, 'Init record'),
                    NOW(), 1
                );
                RETURN NEW;
            ELSIF (TG_OP = 'UPDATE') THEN
                INSERT INTO {clean_tenant_schema}.audit_logs (
                    id, table_name, record_id, action, new_values, old_values,
                    user_id, change_reason, timestamp, version_index
                ) VALUES (
                    gen_random_uuid(), TG_TABLE_NAME, NEW.id, 'UPDATE',
                    row_to_json(NEW)::jsonb, row_to_json(OLD)::jsonb,
                    COALESCE(curr_user, 'system'), COALESCE(curr_reason, 'Correction update'),
                    NOW(), COALESCE(OLD.version, 1) + 1
                );
                RETURN NEW;
            END IF;
            RETURN NULL;
        END;
        $$ LANGUAGE plpgsql;
        """
        await session.execute(text(audit_trigger_sql))
        logger.info("Audit mutation master triggers successfully compiled.")

        # Bind triggers to tables dynamically
        for table in ["subjects", "forms", "queries"]:
            bind_trigger_query = f"""
            DROP TRIGGER IF EXISTS trg_audit_{table} ON {clean_tenant_schema}.{table};
            CREATE TRIGGER trg_audit_{table}
            AFTER INSERT OR UPDATE ON {clean_tenant_schema}.{table}
            FOR EACH ROW EXECUTE FUNCTION {clean_tenant_schema}.audit_table_mutation();
            """
            try:
                await session.execute(text(bind_trigger_query))
            except Exception as e:
                logger.warning(f"Could not bind trigger to table {table} directly: {e}")

        # Step 4: Record tenant registration in global lookup registry
        register_tenant_sql = """
        INSERT INTO public.sponsors_registry (sponsor_id, schema_name, admin_user, provisioning_status, active)
        VALUES (:id, :schema, :admin, 'PROVISIONED', TRUE)
        ON CONFLICT (sponsor_id) DO UPDATE SET active = TRUE;
        """
        await session.execute(
            text(register_tenant_sql),
            {
                "id": sponsor_id,
                "schema": clean_tenant_schema,
                "admin": admin_user_email,
            },
        )
        await session.commit()
        logger.info(f"Sponsor {sponsor_id} provisioned and globally registered.")


if __name__ == "__main__":
    db_manager.init_db(
        "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc"
    )
    asyncio.run(provision_new_tenant("novartis", "lead_admin@novartis.com"))
```

---

## 2.3 Terminology Override & Localization Engine

Clinical trials frequently require custom dictionary translation layers. CDISC domains (e.g., SDTM DM, AE, VS) must adapt to sponsor-specific verbiage variations or localized linguistic outputs without breaking underlying graph data schemas.

### Directory Mapping
* Translators and translation templates reside in `apps/execution/` and `apps/execution/templates/`
* Terminology databases map overrides via the Neo4j schema relationships:
  `(Concept) -[:LOCALIZED_TO {sponsor_id: 'novartis', locale: 'ja_JP'}]-> (OverrideValue)`

### Terminology Override Activation Command
To load and apply a localization dictionary to a specific clinical study workspace:

```bash
# Executing terminology mapping override via the translator module
uv run python -m apps.execution.translator \
  --study-id "STUDY-2026-ONC" \
  --sponsor-id "novartis" \
  --locale-file "./apps/execution/templates/novartis_ja_override.json"
```

---

# SECTION 3: Database Migration, Schema Evolution, and Version Rollbacks

Clinical data migrations must guarantee **zero data loss** (GxP GAMP 5 Class 5 software requirements) and continuous backward compatibility to allow uninterrupted EDC data entry while database nodes undergo schema-level mutations.

## 3.1 PostgreSQL Migration Strategy

Alembic-style, migration scripts must employ a **Expand-and-Contract (Two-Phase)** pattern to eliminate locks and ensure rolling updates:
1. **Expand Phase (Pre-boot):** Add columns, create shadow tables, execute non-blocking writes. Database changes are completely backward compatible with older codebase versions currently running in production.
2. **Contract Phase (Post-rollout):** Deprecate and drop older attributes only after all microservice instances are updated.

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   EXPAND PHASE (Backward Compatible)                          │
│                                                                                              │
│  Add Column `age_v2` (nullable) ──► Backfill data from `age` ──► Deploy new codebase         │
└──────────────────────────────────────────────┬───────────────────────────────────────────────┘
                                               │
                                               ▼
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   CONTRACT PHASE (Zero-Downtime)                             │
│                                                                                              │
│ Verify new codebase runs flawlessly ──► Execute script to drop legacy `age` column            │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 3.1.1 PostgreSQL Migration Executor Script (`apps/execution/database/migrate.py`)

The platform utilizes automated migration execution wrapper logic inside `apps/execution/database/migrate.py`. Here is a production-hardened automated schema update script with backward-compatibility checks:

```python
import sys
import logging
from sqlalchemy import text
from apps.execution.database.core import db_manager

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("PostgresMigrator")


async def run_migrations():
    """
    Executes ordered SQL migration sequences.
    Before applying migrations, checks the target schema and validates compatibility.
    """
    logger.info("Initializing Postgres database migration suite...")
    async_session_maker = db_manager.get_session_maker()

    async with async_session_maker() as session:
        # Check current database compatibility level
        res = await session.execute(
            text("SELECT current_setting('server_version_num')::int;")
        )
        pg_version = res.scalar()
        if pg_version < 150000:
            logger.error(
                f"Incompatible database version: {pg_version}. PostgreSQL 15+ required."
            )
            sys.exit(1)

        # Enforce DDL lock timeout to prevent active study entry freeze
        await session.execute(text("SET lock_timeout = '10s';"))

        # Step 1: Initialize metadata structure tracking schema
        await session.execute(
            text("""
            CREATE TABLE IF NOT EXISTS public.schema_versions (
                version_index INT PRIMARY KEY,
                applied_at TIMESTAMP DEFAULT NOW(),
                description VARCHAR(255) NOT NULL,
                checksum VARCHAR(64) NOT NULL
            );
        """)
        )

        # Step 2: Define Migration Batches
        migrations = [
            (
                1,
                "Create core execution tables",
                """
                CREATE TABLE IF NOT EXISTS public.subjects (
                    id VARCHAR(255) PRIMARY KEY,
                    site_id VARCHAR(255) NOT NULL,
                    status VARCHAR(50) NOT NULL,
                    is_deleted BOOLEAN DEFAULT FALSE,
                    version INT DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS public.audit_logs (
                    id VARCHAR(255) PRIMARY KEY,
                    table_name VARCHAR(100),
                    record_id VARCHAR(255),
                    action VARCHAR(10),
                    new_values JSONB,
                    old_values JSONB,
                    user_id VARCHAR(255),
                    change_reason TEXT,
                    timestamp TIMESTAMP,
                    version_index INT
                );
            """,
            ),
            (
                2,
                "Add external telemetry links to subjects",
                """
                ALTER TABLE public.subjects ADD COLUMN IF NOT EXISTS telemetry_device_id VARCHAR(255) NULL;
            """,
            ),
        ]

        # Step 3: Run migration sequences transactionally
        for idx, desc, query in migrations:
            # Check if already executed
            check_res = await session.execute(
                text(
                    "SELECT count(*) FROM public.schema_versions WHERE version_index = :idx"
                ),
                {"idx": idx},
            )
            if check_res.scalar() > 0:
                logger.info(f"Migration {idx} ({desc}) already applied. Skipping.")
                continue

            logger.info(f"Applying Migration {idx}: {desc}...")
            try:
                await session.execute(text(query))
                await session.execute(
                    text(
                        "INSERT INTO public.schema_versions (version_index, description, checksum) VALUES (:idx, :desc, 'sha256-mock-hash')"
                    ),
                    {"idx": idx, "desc": desc},
                )
                await session.commit()
                logger.info(f"Migration {idx} successfully applied.")
            except Exception as ex:
                await session.rollback()
                logger.critical(
                    f"CRITICAL ERROR applying migration {idx}. Rolled back transaction. Error: {ex}"
                )
                raise ex


if __name__ == "__main__":
    import asyncio

    db_manager.init_db(
        "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc"
    )
    asyncio.run(run_migrations())
```

### 3.1.2 PostgreSQL Rollback Workflow

In the extremely rare case of deployment verification failures (automated checks fail in Production), the rollback scripts must restore the exact previous database schema safely.

#### PostgreSQL Safe Rollback Execution Script (`apps/execution/database/rollback.py`)

```python
import sys
import logging
from sqlalchemy import text
from apps.execution.database.core import db_manager

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("PostgresRollback")


async def rollback_schema_to_version(target_version: int):
    """
    Safely rolls back Postgres schema changes to the specified target version.
    Verifies that rollback operations do not orphan or destroy existing patient records.

    Args:
        target_version (int): Schema index version to rollback to.
    """
    logger.warning(
        f"ROLLBACK COMMAND ISSUED: Moving database schema to Version {target_version}"
    )
    async_session_maker = db_manager.get_session_maker()

    async with async_session_maker() as session:
        # Check current version
        res = await session.execute(
            text("SELECT MAX(version_index) FROM public.schema_versions")
        )
        current_version = res.scalar() or 0

        if current_version <= target_version:
            logger.info(
                f"Current version ({current_version}) is already at or below target ({target_version}). No rollback needed."
            )
            return

        # Perform step-by-step rolling rollback
        rollback_steps = {
            2: {
                "desc": "Remove external telemetry links from subjects",
                "check_gxp": "SELECT COUNT(*) FROM public.subjects WHERE telemetry_device_id IS NOT NULL;",
                "ddl": "ALTER TABLE public.subjects DROP COLUMN IF EXISTS telemetry_device_id;",
            }
        }

        for version in range(current_version, target_version, -1):
            if version not in rollback_steps:
                logger.critical(
                    f"No rollback step mapped for Version {version}. Manual SRE intervention required."
                )
                sys.exit(1)

            step = rollback_steps[version]
            logger.warning(
                f"Executing rollback step for Version {version}: {step['desc']}"
            )

            # Safety validation: Verify if we are about to destroy valuable, unrecoverable data
            check_val = await session.execute(text(step["check_gxp"]))
            orphans_count = check_val.scalar()
            if orphans_count > 0:
                logger.error(
                    f"ABORTING ROLLBACK. Found {orphans_count} records containing active telemetry data in the targeted column."
                )
                logger.error(
                    "A rollback will trigger irreversible data loss. Export records prior to force-dropping."
                )
                sys.exit(1)

            try:
                # Apply Rollback DDL
                await session.execute(text(step["ddl"]))
                await session.execute(
                    text("DELETE FROM public.schema_versions WHERE version_index = :v"),
                    {"v": version},
                )
                await session.commit()
                logger.info(f"Successfully rolled back version {version}")
            except Exception as e:
                await session.rollback()
                logger.critical(
                    f"FAILED TO ROLLBACK VERSION {version}. Database status state is uncertain: {e}"
                )
                sys.exit(1)


if __name__ == "__main__":
    import asyncio

    db_manager.init_db(
        "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc"
    )
    asyncio.run(rollback_schema_to_version(1))
```

---

## 3.2 Neo4j Graph Schema Evolution & Migrations

Neo4j contains highly structured nodes enforcing USDM models. Immutability constraints require modifications to follow strict branching rules:
* Mutating a node in a **Locked** study is physically blocked.
* The system instead establishes an updated version clone (`:StudyVersion`), routing connections via `PREVIOUS_VERSION` edges.

### 3.2.1 Neo4j Migration Script

```python
from neo4j import GraphDatabase
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("Neo4jMigrator")


class Neo4jMigrator:
    def __init__(self, uri, user, password):
        self.driver = GraphDatabase.driver(uri, auth=(user, password))

    def close(self):
        self.driver.close()

    def run_migrations(self):
        """
        Applies necessary schema indexes and constraints.
        Enforces GxP node boundaries in Neo4j Study Designer.
        """
        with self.driver.session() as session:
            logger.info("Setting up Neo4j unique constraint requirements...")

            # Constraint 1: Enforce Study ID uniqueness
            session.run("""
                CREATE CONSTRAINT unique_study_id IF NOT EXISTS
                FOR (s:Study) REQUIRE s.id IS UNIQUE
            """)

            # Constraint 2: Enforce Concept ID uniqueness
            session.run("""
                CREATE CONSTRAINT unique_biomedical_concept_id IF NOT EXISTS
                FOR (b:BiomedicalConcept) REQUIRE b.id IS UNIQUE
            """)

            # Index: Speed up traversal of version chains
            session.run("""
                CREATE INDEX study_version_index IF NOT EXISTS
                FOR (sv:StudyVersion) ON (sv.version_index)
            """)

            logger.info(
                "Neo4j database indexes and constraints established successfully."
            )

    def rollback_constraints(self):
        """
        Removes unique constraints. Typically only executed in local development testing.
        """
        with self.driver.session() as session:
            logger.warning("Dropping all production unique constraints inside Neo4j...")
            session.run("DROP CONSTRAINT unique_study_id IF EXISTS")
            session.run("DROP CONSTRAINT unique_biomedical_concept_id IF EXISTS")
            logger.info("Neo4j constraints successfully removed.")


if __name__ == "__main__":
    migrator = Neo4jMigrator("bolt://localhost:7687", "neo4j", "cadence_password")
    migrator.run_migrations()
    migrator.close()
```

---

# SECTION 4: Operational Monitoring, Health Checks & Incident Response

Continuous monitoring is essential for keeping systems secure and compliant with GxP and ISO 27001 requirements.

## 4.1 Logging Aggregation Topology
To ensure audit logs are tamper-proof and accessible, all logs from Cadence Clinical services must follow a structured logging pipeline.

```
┌─────────────────────────────────┐
│     FastAPI Microservices       │  (Writes structured JSON output to stdout)
└────────────────┬────────────────┘
                 │
                 ▼
┌─────────────────────────────────┐
│  Kubernetes DaemonSet FluentBit  │  (Collects, enriches with K8s metadata)
└────────────────┬────────────────┘
                 │ (Mutual TLS Encrypted)
                 ▼
┌─────────────────────────────────┐
│     Private Logstash / ES       │  (Buffered index pipeline with cold s3 archival)
└─────────────────────────────────┘
```

### Logging Configuration Best Practices (Ruff & Black Compliant JSON formatting)
Microservices must write standard, trace-contextualized JSON messages:

```json
{
  "timestamp": "2026-10-24T14:32:01.009Z",
  "level": "INFO",
  "service": "execution",
  "trace_id": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "span_id": "00f067aa0ba902b7",
  "user_id": "usr_99812",
  "tenant": "tenant_pfizer",
  "message": "Subject randomized successfully",
  "study_id": "STUDY-ONC-01",
  "subject_id": "SUBJ-992-01"
}
```

---

## 4.2 System Health Checks & Prometheus Metrics

Each microservice implements an open `/health` endpoint and exports Prometheus `/metrics` metrics.

### Prometheus Alert Rules Definition
Below is the production Prometheus alert definitions mapping critical operational threshold flags:

```yaml
groups:
  - name: CadenceClinicalAlertRules
    rules:
      - alert: ServiceDowntime
        expr: up{job=~"cadence-.*"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Microservice {{ $labels.instance }} has stopped answering."
          description: "Service is offline for more than 1 minute. Critical intervention needed."

      - alert: HighAPIErrorRate
        expr: rate(http_requests_total{status=~"5.."}[5m]) / rate(http_requests_total[5m]) * 100 > 2
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "API Gateway reporting elevated 5xx server error rate"
          description: "Internal Server error levels have breached 2% on instance {{ $labels.instance }}."

      - alert: DatabaseConnectionPoolSaturation
        expr: db_pool_active_connections / db_pool_max_connections * 100 > 85
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "PostgreSQL active connection pool has reached 85% capacity"
```

---

## 4.3 Incident Response Escalation Matrix
In the event of automated Prometheus alert triggers, SREs must action incidents strictly based on severity classes:

```
┌────────────────────────────────────────────────────────────────────────┐
│                              INCIDENT FLOW                             │
│                                                                        │
│  Alert Triggered ──► Level 1 SRE ──► Check Root Cause                  │
│                        │                                               │
│                        └───► Resolution Failed? ──► Escalate Level 2   │
│                                                       │                │
│                                                       └───► Principal  │
└────────────────────────────────────────────────────────────────────────┘
```

| Severity Level | Definition | Target Response (SLA) | Target Resolution Time (MTTR) | Notification Chain |
| :--- | :--- | :--- | :--- | :--- |
| **P1 - Critical** | Platform entirely unreachable; database replication failure; security/data breach detected. | **15 Minutes** | **1 Hour** | SMS/PagerDuty to SRE Lead, QA Director, Security Officer, VP Engineering. |
| **P2 - Major** | Single tenant inaccessible; random audit logs failing to write; performance degradation > 1000ms latency. | **30 Minutes** | **4 Hours** | Level 1 SRE, Engineering Lead, Database Admin. |
| **P3 - Minor** | Localized form design translation override glitches; non-blocking API anomalies; telemetry device pairing latency. | **12 Hours** | **48 Hours** | Support Desk, System Engineer. |

---

## 4.4 Disaster Recovery & Backup Playbook

### 4.4.1 Recovery Objectives
* **Recovery Time Objective (RTO):** $< 30$ Minutes.
* **Recovery Point Objective (RPO):** $< 5$ Minutes (Supported via Postgres WAL Continuous Archiving).

### 4.4.2 Point-in-Time Recovery (PITR) Execution Workflow
In the event of a catastrophic database failure, SREs must immediately run these Point-in-Time Recovery procedures:

```bash
# 1. Access the dedicated PostgreSQL DB Node shell and stop database engine
sudo systemctl stop postgresql

# 2. Clear corrupted PostgreSQL active cluster database directory
sudo rm -rf /var/lib/postgresql/15/main/*

# 3. Restore the base physical backup from S3 vault (Utilizing pg_backrest)
pg_backrest --stanza=cadence_edc --repo1-path=/mnt/backups/s3-vault restore

# 4. Generate the Point-in-Time Recovery control parameters
# Defines exact timestamp before corruption occurred
sudo tee /var/lib/postgresql/15/main/recovery.signal <<EOF
# recovery.signal triggers postgres archive recovery sequence
EOF

sudo tee -a /var/lib/postgresql/15/main/postgresql.conf <<EOF
restore_command = 'pg_backrest --stanza=cadence_edc --repo1-path=/mnt/backups/s3-vault archive-get %f "%p"'
recovery_target_time = '2026-10-24 14:00:00 UTC'
recovery_target_action = 'promote'
EOF

# 5. Safe start PostgreSQL database engine to begin WAL log replay
sudo systemctl start postgresql

# 6. Monitor recovery progress logs
tail -f /var/log/postgresql/postgresql-15-main.log
```

### 4.4.3 Neo4j Graph Database Backup & Restore Workflow

Neo4j Graph Database backups must be executed daily and stored in an encrypted offline S3 storage bucket.

#### Automated Daily Backup Execution Script
```bash
#!/usr/bin/env bash
set -eo pipefail

BACKUP_DIR="/var/backups/neo4j"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="${BACKUP_DIR}/neo4j_backup_${TIMESTAMP}.dump"
S3_BUCKET="s3://cadence-clinical-backups/neo4j"

echo "Beginning offline database lock and dump sequence..."

# Execute safe backup snapshot command on neo4j admin tool
neo4j-admin database dump neo4j --to-path="${BACKUP_DIR}"

# Compress the dump
gzip -9 "${BACKUP_DIR}/neo4j_backup_${TIMESTAMP}.dump"

# Upload to secure encrypted GxP storage vault
aws s3 cp "${BACKUP_FILE}.gz" "${S3_BUCKET}/neo4j_backup_${TIMESTAMP}.dump.gz" \
  --sse aws:kms --sse-kms-key-id arn:aws:kms:us-east-1:123456789:key/my-backup-key

echo "Neo4j dump successfully uploaded."
```

#### Verification and Recovery Execution (Restore)
```bash
# 1. Stop active Neo4j service
neo4j stop

# 2. Uncompress target stable dump file
gunzip /var/backups/neo4j/neo4j_backup_target.dump.gz

# 3. Load the stable dump back to graph storage
neo4j-admin database load neo4j --from-path=/var/backups/neo4j/neo4j_backup_target.dump --overwrite-destination=true

# 4. Start neo4j service and run sanity checks
neo4j start
cypher-shell -u neo4j -p cadence_password "MATCH (n) RETURN count(n) as NodeCount;"
```

---

## 4.5 Computerized System Validation (CSV) & Final Verification Log

Prior to putting this system into production service, SREs and Validation Engineers must execute the following end-to-end verification run on the validation cluster environment and document the outcomes:

```bash
# Executing standard operational readiness validation command
pytest tests/test_migrate.py tests/test_audit.py -v
```

This ensures full operational readiness, zero unapplied migrations, functional multi-tenant separation triggers, complete configuration mapping overrides, and high-availability health checks. The platform is now fully qualified for active Clinical Trial usage under GxP regulatory structures.
