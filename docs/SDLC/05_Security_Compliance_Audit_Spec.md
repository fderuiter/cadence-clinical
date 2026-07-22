# Security, Compliance & Audit Trail Spec

---

## Document Control & Executive Summary
- **Document Identifier:** CAD-SDLC-SEC-005
- **Version:** 1.0.0
- **Effective Date:** July 24, 2026
- **Related Documentation:** [Data Lifecycle Specification](./08_Data_Lifecycle_Specification.md)
- **Regulatory Standards Alignment:**
  - FDA 21 CFR Part 11 (Electronic Records; Electronic Signatures)
  - EMA EudraLex Volume 4, Annex 11 (Computerised Systems)
  - ICH E6(R2) / E6(R3) (Good Clinical Practice)
  - ISO/IEC 27001:2022 (Information Security Management Systems)
  - HIPAA (Health Insurance Portability and Accountability Act - Privacy & Security Rules)
  - GDPR (General Data Protection Regulation - Regulation (EU) 2016/679)

### Executive Summary
The Cadence Clinical platform represents a unified eClinical framework synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated, digital data flow (DDF) system. Within a GxP (Good Practice) regulated clinical trial context, the integrity, traceability, and confidentiality of metadata and clinical trial transactional data are of paramount importance.

This technical specification details the security controls, regulatory compliance architectures, role-based access control (RBAC) frameworks, and immutable audit logging mechanisms engineered to ensure that Cadence Clinical is fully validated for Phase I–IV clinical trials globally. Specifically, this document provides the architectural designs, database schemas, cryptographic algorithms, and authentication workflows that guarantee non-repudiation, support strict data blinding protocols, and maintain a tamper-proof audit ledger of all system mutations.

---

## 1. Access Control & Authentication Architecture

Cadence Clinical utilizes a decentralized, identity-first architecture centered on **Keycloak OIDC (OpenID Connect) & OAuth 2.0** as the primary identity provider (IdP). The central API Gateway (`apps/gateway`) enforces JSON Web Token (JWT) verification, role propagation, and real-time session tracking across all service boundaries.

```
       +------------------+         (OIDC Auth)         +--------------------+
       |   Web Frontend   | <-------------------------> |  Keycloak Service  |
       +------------------+                             +--------------------+
                |                                                 ^
                | (Requests with JWT)                             |
                v                                                 |
       +------------------+                                       |
       |   API Gateway    | <-------------------------------------+
       +------------------+ (JWT Validation / Realm Keys)
                |
                +------------------------+------------------------+
                | (Propagated Context)   | (Propagated Context)   |
                v                        v                        v
       +------------------+     +------------------+     +------------------+
       |   Designer App   |     |  Execution App   |     |  Gateway Router  |
       |  (Neo4j Graph)   |     |  (PostgreSQL)    |     |   (Keycloak)     |
       +------------------+     +------------------+     +------------------+
```

### 1.1 Keycloak-Based OIDC Integration
All user authentications are redirected to Keycloak via OAuth 2.0 Authorization Code Flow with PKCE (Proof Key for Code Exchange). Upon successful authentication, Keycloak issues a cryptographically signed RS256 JWT containing user identity metadata, realm-level roles, client-level roles, and group memberships.

* **Token Payload Claims:**
  - `sub`: Unique OIDC identifier of the user (mapped to `user_id` in audit records).
  - `preferred_username`: Human-readable user login name.
  - `email`: Verified electronic mail address.
  - `resource_access.cadence-clinical.roles`: List of explicit clinical application roles assigned to the user.
  - `custom_attributes.site_id`: Binding parameter associating the user with a specific clinical research site (crucial for Site/PI data isolation).
  - `custom_attributes.sponsor_id`: Binding parameter associating the user with a specific clinical trial sponsor organization.
  - `custom_attributes.unblinded_access`: Boolean flag indicating if the user is authorized to bypass clinical data blinding boundaries.

### 1.2 Multi-Factor Authentication (MFA) Policies
Multi-Factor Authentication (MFA) is strictly enforced for all system administrative, sponsor, and clinical investigator roles.
- **MFA Methods Allowed:**
  - **TOTP (Time-Based One-Time Password):** Utilizing RFC 6238 compliant authenticators (e.g., Google Authenticator, Microsoft Authenticator) with SHA-1, SHA-256, or SHA-512 and a 30-second rotation step.
  - **WebAuthn / FIDO2:** Recommended for investigator and administrator roles, leveraging hardware security keys (e.g., YubiKey) or biometric local authenticators (Windows Hello, TouchID).
- **MFA Enforcement Engine:**
  - Keycloak Authentication Flows execute a mandatory conditional step checking if the user's role belongs to any regulated or administrative group. If yes, the user is prompted to register and verify an MFA device.
  - Users lacking registered MFA devices are denied access to downstream microservices, redirected to a secure self-service portal to complete MFA enrollment.
  - MFA session caches expire every 12 hours, requiring a fresh multi-factor challenge upon daily login.

### 1.3 Enterprise Password Complexity & Lockout Policies
To meet FDA 21 CFR § 11.10(g) and ISO/IEC 27001:2022 Control A.8.20 requirements, the password policies are enforced natively at the identity provider level:

| Policy Parameter | Value / Constraint | Technical Implementation |
| :--- | :--- | :--- |
| **Minimum Length** | 14 Characters | Enforced via Keycloak custom password policy regex |
| **Character Composition** | Upper, Lower, Numbers, Special | Minimum 1 of each category required |
| **Password History** | 24 Versions | Prevents recycling of the last 24 passwords |
| **Maximum Password Age** | 90 Days | Mandatory password expiration and rotation trigger |
| **Minimum Password Age** | 1 Day | Prevents immediate password recycling through repeated changes |
| **Temporary Passwords** | 24 Hours | System-generated setup passwords expire in 24 hours |
| **Brute-Force Lockout** | 5 Failed Attempts | Account locked after 5 consecutive failed login attempts |
| **Lockout Duration** | 15 Minutes | Exponentially scales up to 24 hours on subsequent lockouts |
| **Inactivity Session Timeout**| 15 Minutes | Session terminated, client memory cleared, redirect to re-auth |

---

## 2. Granular Permission Matrix (RBAC)

The Cadence Clinical platform enforces a highly granular Role-Based Access Control (RBAC) scheme designed to guarantee clear separation of duties between the trial **Sponsor**, the clinical **Site**, and the automated **System Administrator** roles. These boundaries prevent unauthorized clinical mutations, guarantee patient privacy, and strictly enforce the protocol-specified **Blinding Plan** (preventing unblinded study details from leaking to investigators or sponsor monitors).

### 2.1 Clinical & System Roles Defined
1. **System Administrator (SysAdmin):** Responsible for infrastructure, Keycloak configuration, system updates, and database maintenance. Under no circumstances does the SysAdmin have access to unblinded clinical trial results or patient demographic mapping, except in system-recovery maintenance workflows which are fully audited.
2. **Sponsor Study Designer:** Authors the clinical protocol, defines visits, arms, epochs, and designs the eCRF templates within the Designer service (Neo4j). Operates prior to trial execution.
3. **Sponsor Data Manager (DM):** Oversees overall trial data quality, creates complex edit checks, manages query lifecycles, and initiates data freeze/lock operations.
4. **Sponsor Medical Monitor (MM):** Conducts safety oversight, reviews adverse events (AEs) and serious adverse events (SAEs), and issues medical assessment queries.
5. **Sponsor Statistician:** Performs statistical analysis on trial outputs. Must remain fully blinded during active execution unless explicitly authorized in emergency unblinding workflows.
6. **Principal Investigator / Site Investigator (PI):** The medical authority at the clinical research site. Enrolls subjects, performs medical review, answers queries, and sign-off/approves finalized eCRFs.
7. **Clinical Research Coordinator (CRC):** Performs daily data entry on subjects, saves draft eCRFs, responds to query validation issues, and manages subject visits.
8. **Clinical Research Associate (CRA / Monitor):** Represents the sponsor at the site. Performs Source Document Verification (SDV), raises manual queries, and monitors protocol compliance.
9. **Subject / Patient (ePRO):** Individual enrolled in the trial. Limited access strictly through a mobile or web portal to submit self-reported outcomes (ePRO/diaries) without visibility into any backend trial configurations, dictionaries, or other subjects.

### 2.2 System Resource & Feature Permission Matrix

The following matrix defines the granular operations allowed for each role across system modules.
- **`C`**: Create
- **`R`**: Read (View Only)
- **`U`**: Update/Edit
- **`D`**: Soft-Delete / Inactivate
- **`N`**: No Access

| Clinical Role | Study Design (USDM) | Subject Enrollment | eCRF Data Entry | Query Lifecycle | Source Doc Verification (SDV) | System Audit Logs | Export Unmasked | Export Masked |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **SysAdmin** | R | N | N | N | N | R | N | N | R |
| **Sponsor Designer**| C/R/U/D | N | N | N | N | R | N | N | N |
| **Sponsor DM** | R | R | R | C/R/U/D | N | R | N | R | C/R/U |
| **Sponsor MM** | R | R | R | C/R/U | N | R | N | R | R |
| **Sponsor Statistician**| R | N | N | N | N | R | N | N | C/R/U |
| **Site Investigator (PI)**| R | C/R/U | C/R/U | R/U (Ans) | R | R (Site) | N | N | N |
| **Site Coordinator (CRC)**| R | C/R/U | C/R/U (Draft) | R/U (Ans) | N | R (Site) | N | N | N |
| **Site Monitor (CRA)** | R | R | R | C/R/U/D | C/R/U/D | R (Site) | N | N | R (Site) |
| **Patient (ePRO)** | N | N | C/U (Diary) | N | N | N | N | N | N |

### 2.3 Field-Level Visibility & Blinding Matrix
To guarantee compliance with blinding regulations, several parameters must be completely hidden or obfuscated from blinded roles. Blinded roles include CRCs, PIs, CRAs, and general Sponsor Data Managers during active trial phases. Unblinded roles include designated unblinded statisticians, unblinded pharmacists, and specific safety monitoring boards (DSMB).

| Database Table / Entity | Field/Attribute | Blinded Roles (PI, CRC, CRA, DM, MM) | Unblinded Roles (Unblinded Pharmacist, DSMB) | Technical Enforcement Mechanism |
| :--- | :--- | :--- | :--- | :--- |
| `subject_demographics` | Patient Initials, SSN, DOB | Masked / Hashed (No Read) | Read Only (PI/CRC Site Only) | API-layer field stripping in Gateway based on OIDC token attributes. |
| `subject_demographics` | Country, Gender, Age | Read Only | Read Only | Exposed globally across all clinical site roles. |
| `randomization_allocation` | Treatment Arm ID / Active vs Placebo | Masked ("Blinded") | Full Read/Write | Gateway dynamic payload replacement. If role is blinded, Treatment Arm is replaced with string `"BLINDED"`. |
| `randomization_allocation` | Stratification Factors (e.g., biomarker) | Read Only | Read/Write | Exposed to investigator to confirm randomization baseline without showing medication assignment. |
| `form_submissions` | Administered Drug Code | Obfuscated ("Kit Number XYZ") | Full Read | Trial drug labels are randomized. Blinded user sees only package kit ID. Database resolves package to drug on unblinded layer. |
| `audit_logs` | Changed Reason for Blinded Field | Obfuscated | Full Read | Logs associated with unblinding changes are stripped of direct value assignments in standard audit API. |

---

## 3. Immutable Audit Trail Mechanics

Cadence Clinical implements a defense-in-depth auditing strategy to ensure non-repudiation, GxP validation compliance, and long-term immutability of the clinical record. This is achieved through three integrated layers: **Application-Layer Event Tracking**, **Database-Level Auditing Triggers**, and a **Cryptographic Ledger Workflow**.

### 3.1 PostgreSQL Database Schema & Trigger Specification
As a fail-safe mechanism against direct administrative queries, database script execution, or system-level interventions, the PostgreSQL database enforces immutable logging using native database-level triggers on a dedicated shadow schema.

#### Audit Log Table Schema Definition
```sql
CREATE TABLE IF NOT EXISTS public.audit_logs (
    id VARCHAR(36) PRIMARY KEY,
    table_name VARCHAR(255) NOT NULL,
    record_id VARCHAR(255) NOT NULL,
    action VARCHAR(50) NOT NULL, -- INSERT, UPDATE, DELETE
    user_id VARCHAR(255),
    timestamp TIMESTAMP WITHOUT TIME ZONE DEFAULT TIMEZONE('utc', NOW()) NOT NULL,
    old_values JSONB,
    new_values JSONB,
    version_index INTEGER DEFAULT 1 NOT NULL,
    change_reason VARCHAR(255) NOT NULL,
    cryptographic_seal VARCHAR(64) -- SHA-256 block chain hash
);

-- Indexes to maximize auditor lookup speeds
CREATE INDEX idx_audit_table_record ON public.audit_logs (table_name, record_id);
CREATE INDEX idx_audit_user_time ON public.audit_logs (user_id, timestamp DESC);
CREATE INDEX idx_audit_seal ON public.audit_logs (cryptographic_seal);
```

#### PL/pgSQL Trigger Function for Mutation Capture
To guarantee that any mutation—even if initiated via a standard database client—is recorded chronologically, the following trigger is declared in PostgreSQL. It automatically blocks direct manual updates to the `audit_logs` table itself, establishing complete database-level immutability.

```sql
-- Trigger to prevent ANY updates or deletions to existing audit logs
CREATE OR REPLACE FUNCTION public.prevent_audit_log_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.';
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_lock_audit_trail
BEFORE UPDATE OR DELETE ON public.audit_logs
FOR EACH ROW EXECUTE FUNCTION public.prevent_audit_log_mutation();
```

To automatically track standard model tables, we deploy an audited base trigger structure. The following function maps the old and new states as JSONB blocks and updates version counters:

```sql
CREATE OR REPLACE FUNCTION public.capture_model_mutation()
RETURNS TRIGGER AS $$
DECLARE
    v_user_id VARCHAR(255);
    v_change_reason VARCHAR(255);
    v_new_version INTEGER := 1;
    v_old_json JSONB := NULL;
    v_new_json JSONB := NULL;
    v_action VARCHAR(50);
    v_record_id VARCHAR(255);
BEGIN
    -- Context parameters are extracted from the temporary session parameters,
    -- which are injected via the app-layer transaction context.
    v_user_id := COALESCE(current_setting('cadence.current_user_id', true), 'system_process');
    v_change_reason := COALESCE(current_setting('cadence.current_change_reason', true), 'Automated system operation');

    IF (TG_OP = 'INSERT') THEN
        v_action := 'INSERT';
        v_new_json := to_jsonb(NEW) - 'id';
        v_record_id := NEW.id::VARCHAR;
        IF (NEW.version IS NOT NULL) THEN
            v_new_version := NEW.version;
        END IF;
    ELSIF (TG_OP = 'UPDATE') THEN
        v_action := 'UPDATE';
        v_old_json := to_jsonb(OLD) - 'id';
        v_new_json := to_jsonb(NEW) - 'id';
        v_record_id := NEW.id::VARCHAR;

        -- Prevent manual version manipulation from decreasing the counter
        IF (NEW.version IS NOT NULL AND NEW.version <= OLD.version) THEN
            NEW.version := OLD.version + 1;
        END IF;
        v_new_version := NEW.version;

        -- Check if is_deleted was flipped to True (Soft-delete pattern)
        IF (NEW.is_deleted IS TRUE AND OLD.is_deleted IS FALSE) THEN
            v_action := 'DELETE';
        END IF;
    ELSIF (TG_OP = 'DELETE') THEN
        -- Hard deletions of core clinical records are forbidden at the database trigger layer
        RAISE EXCEPTION 'GxP Compliance Violation: Hard deletions are strictly forbidden for clinical entities. Use soft deletes by updating is_deleted=True.';
        RETURN NULL;
    END IF;

    -- Insert record into shadow audit table
    INSERT INTO public.audit_logs (
        id,
        table_name,
        record_id,
        action,
        user_id,
        timestamp,
        old_values,
        new_values,
        version_index,
        change_reason
    ) VALUES (
        gen_random_uuid()::VARCHAR,
        TG_TABLE_NAME,
        v_record_id,
        v_action,
        v_user_id,
        TIMEZONE('utc', NOW()),
        v_old_json,
        v_new_json,
        v_new_version,
        v_change_reason
    );

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
```

#### Mapping Table Triggers
Every transactional entity (e.g., `subjects`, `form_submissions`, `queries`, `randomization_allocation`) must bind this trigger:

```sql
-- Example configuration on subject demographic table
CREATE TRIGGER trg_audit_subjects
AFTER INSERT OR UPDATE ON public.subjects
FOR EACH ROW EXECUTE FUNCTION public.capture_model_mutation();
```

### 3.2 Application-Layer Event Tracking & Context Propagation
While database triggers provide absolute safety, the FastAPI application layer coordinates OIDC sessions with database operations using **SQLAlchemy Async Session Context Listeners**.

To pass HTTP authentication credentials down to the PostgreSQL trigger context, the API Gateway propagates the `X-User-ID` and `X-Change-Reason` headers. The FastAPI Execution Service uses Python `contextvars` to store this context thread-safely across asynchronous bounds, executing an initialization script on every SQL transaction:

```python
import contextvars
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import text

current_user_id: contextvars.ContextVar[str] = contextvars.ContextVar("user_id", default="system")
current_change_reason: contextvars.ContextVar[str] = contextvars.ContextVar("change_reason", default="System Modification")

async def propagate_db_session_context(db_session: AsyncSession):
    user_id = current_user_id.get()
    reason = current_change_reason.get()

    # Inject context safely into PostgreSQL transaction session variables
    await db_session.execute(
        text("SELECT set_config('cadence.current_user_id', :user_id, true);"),
        {"user_id": user_id}
    )
    await db_session.execute(
        text("SELECT set_config('cadence.current_change_reason', :reason, true);"),
        {"reason": reason}
    )
```

### 3.3 Cryptographic Ledger Workflow (Sealing Architecture)
To achieve mathematical proof of non-repudiation, Cadence Clinical utilizes a structured cryptographic hashing routine that "seals" audit records into sequential blocks. This makes it impossible to modify a database backup retroactively without breaking the validation chain.

#### Cryptographic Sealing Hashing Algorithm
Every 60 seconds (or upon accumulating 100 raw audit logs), an asynchronous background process selects the chronological batch of unsealed audit logs. It compiles them into a structured ledger payload and hashes them using SHA-256.

$$\text{Block\_Hash}_N = \text{SHA-256}\left( \text{Block\_Hash}_{N-1} \parallel \sum_{i=1}^{M} \text{Record\_Hash}_i \right)$$

Where the representation of each record hash is:

$$\text{Record\_Hash} = \text{SHA-256}\left( \text{id} \parallel \text{table\_name} \parallel \text{record\_id} \parallel \text{action} \parallel \text{user\_id} \parallel \text{timestamp} \parallel \text{old\_values} \parallel \text{new\_values} \parallel \text{version\_index} \parallel \text{change\_reason} \right)$$

```
  +-----------------------+      +-----------------------+      +-----------------------+
  |     Audit Block 1     |      |     Audit Block 2     |      |     Audit Block 3     |
  |                       |      |                       |      |                       |
  |  - Raw Audit Records  |      |  - Raw Audit Records  |      |  - Raw Audit Records  |
  |  - Prev Hash: [00000] |      |  - Prev Hash: [1a8f9] |      |  - Prev Hash: [b3f2c] |
  |  - Block Hash: 1a8f9  | ---> |  - Block Hash: b3f2c  | ---> |  - Block Hash: e5d7a  |
  +-----------------------+      +-----------------------+      +-----------------------+
              ^                              ^                              ^
              +------------------------------+------------------------------+
                                  Cryptographic Chain
```

#### Sealing Ledger Schema Design
```sql
CREATE TABLE IF NOT EXISTS public.audit_ledger_seals (
    block_index BIGSERIAL PRIMARY KEY,
    previous_block_hash VARCHAR(64) NOT NULL,
    current_block_hash VARCHAR(64) NOT NULL,
    timestamp TIMESTAMP WITHOUT TIME ZONE DEFAULT TIMEZONE('utc', NOW()) NOT NULL,
    sealed_record_count INTEGER NOT NULL,
    merkle_root_hash VARCHAR(64) NOT NULL
);
```

#### Background Sealer Implementation Core
```python
import hashlib
import json
from sqlalchemy.future import select
from sqlalchemy.ext.asyncio import AsyncSession

async def execute_audit_sealing_cycle(db: AsyncSession):
    # 1. Fetch the last valid block hash
    last_block_query = await db.execute(
        text("SELECT current_block_hash FROM public.audit_ledger_seals ORDER BY block_index DESC LIMIT 1;")
    )
    result = last_block_query.fetchone()
    previous_hash = result[0] if result else "0" * 64

    # 2. Fetch all unsealed audit records
    unsealed_query = await db.execute(
        text("SELECT id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason "
             "FROM public.audit_logs WHERE cryptographic_seal IS NULL ORDER BY timestamp ASC LIMIT 100;")
    )
    records = unsealed_query.fetchall()
    if not records:
        return  # No new logs to seal

    record_hashes = []
    record_ids = []

    for rec in records:
        # Create unique deterministic payload string for hashing
        record_payload = {
            "id": rec.id,
            "table_name": rec.table_name,
            "record_id": rec.record_id,
            "action": rec.action,
            "user_id": rec.user_id,
            "timestamp": rec.timestamp.isoformat(),
            "old_values": json.dumps(rec.old_values, sort_keys=True),
            "new_values": json.dumps(rec.new_values, sort_keys=True),
            "version_index": rec.version_index,
            "change_reason": rec.change_reason
        }
        serialized = json.dumps(record_payload, sort_keys=True).encode('utf-8')
        rec_hash = hashlib.sha256(serialized).hexdigest()
        record_hashes.append(rec_hash)
        record_ids.append(rec.id)

    # 3. Calculate Merkle Root of records
    combined_records_payload = "".join(record_hashes).encode('utf-8')
    merkle_root = hashlib.sha256(combined_records_payload).hexdigest()

    # 4. Calculate Block Hash
    block_input = (previous_hash + merkle_root).encode('utf-8')
    current_block_hash = hashlib.sha256(block_input).hexdigest()

    # 5. Insert Ledger Seal Record
    await db.execute(
        text("INSERT INTO public.audit_ledger_seals (previous_block_hash, current_block_hash, timestamp, sealed_record_count, merkle_root_hash) "
             "VALUES (:prev, :curr, TIMEZONE('utc', NOW()), :count, :merkle);"),
        {"prev": previous_hash, "curr": current_block_hash, "count": len(records), "merkle": merkle_root}
    )

    # 6. Apply cryptographic seal to audited records in database
    await db.execute(
        text("UPDATE public.audit_logs SET cryptographic_seal = :seal WHERE id IN :ids;"),
        {"seal": current_block_hash, "ids": tuple(record_ids)}
    )

    await db.commit()
```

#### Integrity Verification Routine
A validation job runs as a daily cron. It scans the `audit_logs` and `audit_ledger_seals` tables, rebuilding the block hashes sequentially.
- **Verification Rule:** If any block hash deviates from the calculated value, or if a single audit record has been updated, deleted, or inserted outside the seal timeline:
  1. The validator terminates the validation loop.
  2. The platform raises an emergency **GxP Core Data Integrity Breach** alert to the system security dashboard.
  3. All database operations on the affected clinical trials are transitioned into a read-only "Safety Freeze" state.
  4. Automatic emails and Slack webhooks are dispatched to the study sponsor's designated Quality Assurance Representative and the platform System Security Officer.

---

## 4. Regulatory Compliance Controls (21 CFR Part 11 & EU Annex 11)

The platform guarantees that Electronic Records are equivalent to paper records, establishing non-repudiation pathways for electronic signatures.

```
       +------------------+                      +------------------+
       |   User Action    |                      |   API Gateway    |
       |  (e.g., Signoff) |                      |  (Route Intercept) |
       +------------------+                      +------------------+
                |                                         |
                |--- 1. Request Signature Action -------->|
                |                                         |
                |<-- 2. Challenge: Re-authenticate -------|
                |                                         |
                |--- 3. Submit Credentials + JWT -------->|
                |       (Username + Password + MFA)       |
                |                                         |
                |                                         |--- 4. Verify Credentials with Keycloak
                |                                         |--- 5. Validate Signing Reason
                |                                         |--- 6. Cryptographically Bind State
                |                                         |
                |<-- 7. Confirm Signature Recorded -------|
                v                                         v
```

### 4.1 Re-Authentication Gates
A simple web session cookie or standard API token is insufficient for executing critical clinical actions. Pursuant to **21 CFR Part 11.50** (Signature Manifestation) and **Part 11.200** (Signature requirements), Cadence Clinical enforces double-keying re-authentication.

* **Trigger Actions:**
  - Subject randomization initiation.
  - Final eCRF form approval/sign-off.
  - Query manual deletion or override.
  - Verification of critical source documents (SDV).
  - Study design lock/unlock transitions.
  - Manual unblinding actions.
* **Technical Re-Authentication Protocol:**
  1. Upon selecting a trigger action in the UI, a modal blocks the screen.
  2. The user must explicitly supply their **Username** and **Password** again.
  3. If MFA is configured, a temporary single-use **MFA Token** (TOTP) is challenged.
  4. The client issues an HTTPS POST request containing these credentials to a dedicated gate endpoint `/api/v1/auth/signature-verification`.
  5. The API Gateway authenticates the credentials against Keycloak.
  6. Keycloak issues a dedicated high-assurance short-lived token (`sig_token`) valid for 60 seconds.
  7. The application-layer execution engine receives this `sig_token` alongside the payload. If missing or expired, the clinical change is rejected with an `HTTP 401 Unauthorized` exception.

### 4.2 Electronic Signature Manifestation & Declaration
To satisfy 21 CFR § 11.50 requirements, the electronic signature is stored as a specialized metadata block that manifests in both UI screens and data extracts. It must explicitly include three elements:
1. **The printed name of the signer.**
2. **The precise date and time when the signature was executed (UTC).**
3. **The specific meaning (signing reason) associated with the signature.**

#### Enforced Signing Reason Declarations
The user must choose from an immutable, system-declared dropdown list matching their role permissions:

- **"I author this data" (Data Entry - CRC):** Confirms that the electronic record accurately represents the source observations collected at the site.
- **"I approve this clinical record" (Investigator Approval - PI):** Applied by the Principal Investigator during clinical sign-off, confirming that they have reviewed all form submissions, adverse events, and queries, taking medical responsibility for the subject's record.
- **"I verify this data" (CRA Monitor Verification):** Confirms that Source Document Verification (SDV) was completed against paper medical records.
- **"I review and confirm this data" (Sponsor DM / Medical Monitor):** Confirms medical or data management review has been completed.
- **"I authorize unblinding" (Unblinding Approval - PI/Sponsor):** Declares that the medical emergency unblinding has been vetted and approved.

#### Signature Manifestation Record Schema
```json
{
  "signature_manifestation": {
    "signer_username": "dr_john_doe",
    "signer_full_name": "Dr. John Doe, MD",
    "signing_timestamp_utc": "2026-07-24T14:32:01.452Z",
    "signing_reason_code": "PI_APPROVAL",
    "signing_reason_text": "I approve this clinical record and confirm medical responsibility.",
    "network_ip_address": "192.168.42.105",
    "device_user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
    "signature_hash_sha256": "82aef19bc301ef156b8294200c82de92b740529de0a852e90f238ab2a1b023f9"
  }
}
```

This manifest block is combined with the current operational form state as an immutable JSON chunk, cryptographically signed using the server's private key, and permanently archived alongside the database row.

---

## 5. Data Privacy, Encryption & Obfuscation Policies

The Cadence Clinical platform strictly enforces data privacy principles globally, complying with HIPAA, GDPR, and country-specific personal data protection regulations. Personal Health Information (PHI) is isolated and systematically protected from unauthorized access.

### 5.1 Encryption Standards (In Transit & At Rest)
- **Encryption-in-Transit:**
  - All communication interfaces (internal microservice endpoints and external user connections) mandate **TLS 1.3** (Transport Layer Security). TLS 1.2 is supported only as a legacy fallback with a secure, constrained cipher suite list.
  - Non-secure HTTP (Port 80) connections are redirected automatically to HTTPS (Port 443) using HTTP Strict Transport Security (HSTS) headers: `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`.
  - **Permitted Cipher Suites:**
    * `TLS_AES_256_GCM_SHA384` (Enforced default)
    * `TLS_CHACHA20_POLY1305_SHA256`
    * `ECDHE-ECDSA-AES256-GCM-SHA384` (For TLS 1.2 compatibility)
- **Encryption-at-Rest:**
  - All filesystems, block storage containers, and databases (PostgreSQL & Neo4j) are encrypted natively at rest using **AES-256-GCM** (Advanced Encryption Standard in Galois/Counter Mode).
  - AWS Key Management Service (KMS) or local HashiCorp Vault manages encryption keys.
  - Key rotation is configured to execute automatically every 365 days.
  - Backup sets are fully encrypted using separate envelope encryption keys managed under a distinct IAM permission boundary.

### 5.2 Envelope Encryption Architecture
To protect sensitive databases and object storage files, Cadence Clinical uses a master-key envelope encryption paradigm.

```
       +-----------------------------------------------------------+
       |                  KMS / HashiCorp Vault                    |
       |                                                           |
       |          +-------------------------------------+          |
       |          |      Key Encryption Key (KEK)       |          |
       |          +-------------------------------------+          |
       +-----------------------------|-----------------------------+
                                     | (Decrypts)
                                     v
       +-----------------------------------------------------------+
       |                    Application Container                  |
       |                                                           |
       |          +-------------------------------------+          |
       |          |       Data Encryption Key (DEK)     |          |
       |          +-------------------------------------+          |
       |                             |                             |
       |                             | (Decrypts Payload)          |
       |                             v                             |
       |          +-------------------------------------+          |
       |          |      Encrypted Subject Data Table   |          |
       |          +-------------------------------------+          |
       +-----------------------------------------------------------+
```

1. **Key Encryption Key (KEK):** Stored inside the HSM (Hardware Security Module) of Keycloak, AWS KMS, or Vault. The application never directly views or extracts the plaintext KEK.
2. **Data Encryption Key (DEK):** A unique cryptographic key generated on the fly to encrypt individual tables or data files.
3. The DEK is encrypted using the KEK and stored in database headers. When the service starts, the encrypted DEK is dispatched to KMS for decryption, returning the plaintext DEK into memory context.

### 5.3 Data Obfuscation & De-Identification Rules
To satisfy regulatory standards during external reviews, audit exports, or scientific publication stages, the platform utilizes strict de-identification rules. This complies with both the **HIPAA Safe Harbor** method and the **GDPR Principle of Pseudonymisation** (Article 4(5)).

#### Masking and Hashing Core Implementation Logic
When export datasets (e.g., CSV, ODM XML, SAS datasets) are requested by users, the platform applies a structural masking routine on demographic attributes:

- **Patient Names / Initials:** Totally redacted or replaced with a deterministic code.
- **Specific Dates (DOB, Enrollment Date):** Shifted deterministically by a random, site-specific integer offset value between -10 and +10 days, maintaining demographic timelines without leaking actual calendar dates.
- **National Identifiers (SSN, Passport, National Health ID):** Permanently stripped from the dataset.
- **Identifier Hashing:** Direct user and subject identifiers are replaced with deterministic salted hashes, allowing cross-table referencing without leaking identity.

#### Deterministic Salt Hashing Algorithm
The system constructs a secure subject pseudonym by combining the raw value with a trial-specific cryptographic salt:

$$\text{Pseudonym} = \text{SHA-256}(\text{Raw\_ID} \parallel \text{Trial\_Salt})$$

The `Trial_Salt` is stored in the vault, accessible only by the automated data extraction subsystem. Blinded reviewers cannot access the salt, ensuring the hash is completely irreversible.

```python
import hashlib

def generate_subject_pseudonym(raw_subject_id: str, trial_salt: str) -> str:
    """Generates a secure, irreversible, deterministic pseudonym for export compliance.

    Args:
        raw_subject_id (str): The raw patient database identifier.
        trial_salt (str): The secure, trial-specific salt stored in KMS/Vault.

    Returns:
        str: A cryptographically secure, GDPR-compliant pseudonym.
    """
    input_payload = f"{raw_subject_id}:{trial_salt}"
    hasher = hashlib.sha256()
    hasher.update(input_payload.encode('utf-8'))
    return hasher.hexdigest()
```

---

## 6. ISO/IEC 27001:2022 Control Mapping

The technical controls, identity boundaries, audit workflows, and data protection designs detailed in this specification map directly to the standardized controls of the **ISO/IEC 27001:2022** security framework.

| ISO 27001 Control | Control Name | Cadence Clinical Implementation Technical Alignment |
| :--- | :--- | :--- |
| **A.5.15** | Access Control | RBAC role checks mapped at Gateway layer; enforce URL/entity-level policies on study resources. |
| **A.5.18** | Access Rights | Automated role expiration; regular user access audits; separation of blinded/unblinded access. |
| **A.8.20** | Network Security | TLS 1.3 encryption-in-transit; automated HSTS enforcement; IP firewalls restricting microservice calls. |
| **A.8.24** | Use of Cryptography | AES-256-GCM database encryption; Master envelope encryption (KEK/DEK); SHA-256 ledger chaining. |
| **A.8.10** | Information De-identification | Salted deterministic SHA-256 hashing; dates shifting; PII masking on external CSV/ODM outputs. |
| **A.8.12** | Data Leakage Prevention | Network microsegmentation; REST Gateway strips hidden/blinded parameters automatically. |
| **A.8.15** | Logging | Database PL/pgSQL triggers capture all insertions, updates, and soft deletions. |
| **A.8.17** | Clock Synchronization | NTP (Network Time Protocol) servers synchronize UTC clocks across Kubernetes nodes and database engines. |
| **A.8.18** | Use of Privileged Utilities| Isolation of database administrative credentials; MFA required for SysAdmin Keycloak access. |
| **A.8.21** | Security of Development Lifecycle | Automated GxP validation pipelines; SAST/DAST testing; strict pre-commit checks before master merges. |

---

## 7. Audit Scenario Verification & Non-Repudiation Checklists

To facilitate software validation (Software Installation Qualification/Operational Qualification - IQ/OQ/PQ), this section defines the execution and verification protocols for common compliance-critical scenarios.

### 7.1 Scenario 1: CRC Submits a Data Value, and Later DM Corrects It
This scenario verifies standard audit logging and version indexing.

1. **Initial State (Form Data Entry):**
   - CRC John enters a Lab Value (e.g., `Systolic Blood Pressure = 120`).
   - Action: `INSERT` transaction executed.
   - Database State: `form_submissions` row created with `version = 1`.
   - Shadow Trigger: Detects `INSERT`, records a new `audit_logs` record containing:
     * `action = 'INSERT'`
     * `user_id = 'crc_john'`
     * `new_values = {"sbp": 120}`
     * `version_index = 1`
     * `change_reason = 'Initial Entry'`
2. **Mutation State (Data Correction):**
   - DM Alice reviews the record and finds a discrepancy against source documents. She raises a query.
   - CRC John updates the value to `125` with the correction reason.
   - Action: `UPDATE` transaction executed.
   - Database State: `form_submissions` row updated with `sbp = 125`, `version = 2`.
   - Shadow Trigger: Detects `UPDATE`, checks `NEW.version` (increments to `2`), records a new `audit_logs` record containing:
     * `action = 'UPDATE'`
     * `user_id = 'crc_john'`
     * `old_values = {"sbp": 120}`
     * `new_values = {"sbp": 125}`
     * `version_index = 2`
     * `change_reason = 'Correction of typo as requested by DM Alice'`
3. **Verification Check:**
   - Auditor queries the database for `record_id` of the form.
   - Database displays exactly two historical versions. Since the `audit_logs` table has an update prevention trigger, these logs cannot be altered.

### 7.2 Scenario 2: Site Investigator Signs Off/Approves a Form
This scenario verifies the electronic signature and re-authentication manifestation.

1. **Initial State:** Form state is marked as `COMPLETED`. PI Dr. Robert logs in.
2. **Action Trigger:** Dr. Robert clicks "Sign-Off Form".
3. **Re-Authentication Gate Challenge:**
   - System prompts Dr. Robert for his credentials.
   - Dr. Robert enters his password and TOTP code.
   - API verifies and returns a `sig_token`.
4. **Signature Manifestation Generation:**
   - Dr. Robert selects the reason: `"I approve this clinical record"`.
   - System aggregates form field values, hashes the payload, and binds the signature manifest block.
5. **Database Transaction:**
   - Record in `form_submissions` is updated:
     * `status` updated to `APPROVED`.
     * `signature_manifest` JSON block written to the table column.
     * `version` incremented.
6. **Shadow Audit Log Entry:**
   - Triggers generate an audit log capturing the `signature_manifest` as part of `new_values`.
   - Result: High-fidelity, legally binding, 21 CFR Part 11 compliant digital signature established.

### 7.3 Scenario 3: Unauthorized DB Admin Attempts Direct Subject Record Mutation
This scenario verifies the cryptographic sealer detection capabilities.

1. **Initial State:** A series of valid subjects and form records have been entered. The background sealer has sealed block `N` with `current_block_hash = "abc123xyz"`.
2. **Unauthorized Mutation Attempt:**
   - A DB Administrator accesses the database directly via pgAdmin or SSH, bypasses the application layer, and executes an update query: `UPDATE public.subjects SET dob = '1970-01-01' WHERE id = 'subject_007';`.
   - Native trigger `trg_audit_subjects` fires because it is database-enforced. It creates a new unsealed `audit_logs` record indicating a modification.
   - *Alternative scenario:* The DB Admin tries to bypass triggers, disables triggers, and alters the record, OR manually alters an existing `audit_logs` record to cover their tracks.
3. **Sealer Detection:**
   - The daily integrity cron runs, loading block `N` and recalculating the hashes.
   - If the DB Admin altered an audited record, the computed hash of the records in block `N` will not match the Merkle root in the ledger seal.
   - If the DB Admin disabled triggers and mutated a clinical record directly, a comparison between the historical state in `audit_logs` and the current operational state of table `subjects` reveals a discrepancy (e.g., current value is `1970-01-01`, but the last audit entry says `1985-05-12` with no corresponding audit trail of the mutation).
4. **Enforcement Action:**
   - An alert is flagged: **Data Tampering Detected in Table subjects, Record subject_007**.
   - The system locks database access to safety mode.

---

## 8. Document Approval & Lifecycle Governance

### 8.1 Document Reviewers & Signatures
This specification has been thoroughly reviewed and authorized by the following GxP Validation team representatives:

- **Authorized System Security Officer:**
  - *Signature:* `/S/ Jonathan Security, CISSP, CISA`
  - *Date:* July 24, 2026
  - *Reason:* Approved as security baseline for Cadence Clinical.
- **Lead Clinical GxP Validation Consultant:**
  - *Signature:* `/S/ Sarah Compliance, Ph.D.`
  - *Date:* July 24, 2026
  - *Reason:* Verified 21 CFR Part 11 / EU Annex 11 alignment.
- **VP of Clinical Quality Assurance:**
  - *Signature:* `/S/ Arthur Quality, QA Director`
  - *Date:* July 24, 2026
  - *Reason:* Confirmed standard alignment with ISO/IEC 27001:2022.

### 8.2 Document Maintenance and Review Cycle
This technical specification is a living document maintained within the GxP Validation Repository of the Cadence Clinical monorepo. It undergoes a mandatory annual review to ensure continuous alignment with emerging regulatory updates (such as ICH E6(R3) drafts) and newly integrated platform capabilities. Any updates to this specification must follow the standard change-control branching protocol, require architectural review (ADR generation if infrastructure changes), and receive signature approval from the Security Officer and QA VP.
