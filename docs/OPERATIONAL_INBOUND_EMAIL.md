# eTMF Inbound-Email Webhook - Operational & Technical Guide

**Document ID:** CC-OPS-TMF-002
**Version:** 1.0.0
**Status:** Approved
**Classification:** Restricted (GxP / Confidential)
**Applicability:** DevOps, SRE, TMF Administrators, Security Officers, Clinical Operators

---

## 1. Executive Summary

This guide describes the operational, technical, and regulatory mechanisms governing the secure inbound-email webhook within the electronic Trial Master File (eTMF) service of the Cadence Clinical platform. Users and clinical partners frequently need to forward correspondence, regulatory documents, and site communication records directly into the TMF repository via SMTP. This webhook allows SMTP parsed multipart payloads from external providers (e.g., Mailgun, SendGrid) to route message bodies and file attachments directly into the correct study and binder scopes while fully preserving GxP and FDA 21 CFR Part 11 validation and audit logs.

---

## 2. Configuration & Environment Variables

The inbound-email webhook is controlled by several environment variables in the eTMF container. These variables must be securely configured and managed by DevOps/SRE.

### 2.1 Configuration Attributes

The following variables manage the webhook's security and runtime limits:

| Environment Variable | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `INBOUND_EMAIL_HMAC_SECRET` | String | *dev-default* | Cryptographic shared secret used to sign and verify provider-HMAC signatures. |
| `INBOUND_EMAIL_MAX_SIZE_BYTES` | Integer | `10485760` (10MB) | Maximum permitted size for incoming requests (enforced via request Content-Length and attachment size). |

These variables are defined in the eTMF container service block in `docker/docker-compose.yml`.

---

## 3. Recipient Address Convention & Routing Rules

To determine the destination study and folder of an incoming email, the webhook uses a strict, canonical structured recipient email address.

### 3.1 Recipient Address Format

Addresses must follow this exact, canonical convention:

```
study-<STUDY_ID>[+<binder-hint>]@<domain>
```

- **`STUDY_ID`:** Verbatim identifier of the clinical study (e.g., `study_abc`, `study_123`). Empty or whitespace-only tokens are rejected.
- **`binder-hint`:** Optional taxonomy code or artifact name specifying the destination folder. If provided, the hint is parsed and resolved via the TMF Reference Model taxonomy.
  - *Example with hint (code):* `study-study_abc+05.02.01@cadenceclinical.com`
  - *Example with hint (name):* `study-study_abc+FDA Form 1572@cadenceclinical.com`
  - *Example with hint (alias):* `study-study_abc+FORM_1572@cadenceclinical.com`

### 3.2 Default Target Mapping

If no `binder-hint` is supplied in the address (e.g., `study-study_abc@cadenceclinical.com`), the webhook uses the following default mapping:
- **Default Artifact:** `"Site Communication Log"` (TMF Code: `"05.04.01"`)
- **Default Zone:** `5` (Investigator Site Management)
- **Default Section:** `"04"` (Site Communications)

### 3.3 Rejection Rules & Non-Revealing Responses

Any incoming request is immediately rejected if:
1. The recipient address is malformed or does not contain `study-`.
2. The `<STUDY_ID>` is missing or contains only whitespace.
3. The `<binder-hint>` is provided but is ambiguous or cannot be resolved in the active TMF Reference Model taxonomy.

To prevent malicious scanning and information leakage, all validation and unresolvable route failures return a generic `HTTP 422 Unprocessable Entity` or `HTTP 401 Unauthorized` with a stable, non-revealing error message (e.g., `{"detail": "Invalid routing metadata"}`). No sensitive details about study existence or catalog taxonomy mapping are leaked.

---

## 4. Webhook Security: Signature, Freshness, & Replays

Since this endpoint is exposed for external SMTP webhook integration, it is exempted from standard API gateway Bearer-JWT identity checks. Consequently, it relies on a multi-layer verification protocol to maintain security.

### 4.1 HMAC-SHA256 Signature Verification

Every incoming payload from the email provider must include `timestamp`, `token`, and `signature` fields. The eTMF service validates the signature by:
1. Recomputing the HMAC-SHA256 signature over the canonical concatenated string: `timestamp + token`.
2. Comparing the expected signature with the request-provided signature using a constant-time comparison helper `hmac.compare_digest` to prevent timing-based side-channel attacks.

### 4.2 Timestamp Freshness

The `timestamp` parameter represents seconds since the Unix epoch. To prevent stale request injection:
- The system enforces a strict 300-second (5-minute) drift window: $| \text{current\_time} - \text{timestamp} | \le 300\text{s}$.
- Out-of-window requests are immediately rejected with `HTTP 401 Unauthorized`.

### 4.3 Replay Protection

To defeat replay attacks, the eTMF service maintains an in-memory `InboundEmailReplayCache` tracking processed requests.
- Incoming requests are indexed by their provider `token` or `Message-Id`.
- If a token/id is already present in the cache, the duplicate request is rejected with `HTTP 401 Unauthorized`.
- Cache entries are automatically pruned once they fall outside the 300-second freshness window.

### 4.4 Payload-Size Enforcement

To protect the microservice against Denial of Service (DoS) attacks:
- The `Content-Length` of the HTTP multipart request is evaluated before reading the body. Any payload exceeding `INBOUND_EMAIL_MAX_SIZE_BYTES` is rejected with `HTTP 413 Payload Too Large`.
- Individual file attachments are read as streams and checked against the maximum limit; oversized attachments are rejected.

---

## 5. Idempotency & GxP Ingestion Behavior

To maintain GxP record integrity and prevent duplicate document rows, the webhook implements robust idempotency and GxP controls.

### 5.1 Idempotency Key matching

Webhook duplicate delivery is extremely common in SMTP infrastructures.
- The webhook extracts the standard email natural key: the `Message-Id` header (e.g., `<unique-msg-id@example.com>`).
- Before starting ingestion, the service queries the `tmf_documents` table to see if a record already exists with that `Message-Id` in its `metadata_json`.
- If a match is found, the webhook performs a **no-op** and immediately returns a successful `HTTP 201 Created` with `{"status": "accepted"}`. No new files or audit log records are created.

### 5.2 GxP Auditing and EMAIL_INGEST Action

When a new email is processed, the ingestion service performs the following operations:
1. **Email Body Ingestion:** The plain text email body is ingested as a separate text document in the target study and folder.
2. **Attachments Ingestion:** Each file attachment in the multipart payload is parsed and ingested as its own versioned document under the same study and folder destination.
3. **EMAIL_INGEST Action Logs:** All successful ingestions write an entry into the immutable GxP audit trail `tmf_audit_logs` with the action set to `"EMAIL_INGEST"`, detailing the sender, subject, and unique `Message-Id`.

### 5.3 Immutability Guardrails

If an incoming email is targeted at an artifact/binder that has already been electronically signed (`SIGNED` status) or approved:
- The ingestion is blocked, and the transaction is rolled back.
- An audit entry with action `"MUTATION_REJECTED"` is committed to record the unauthorized attempt.
- The webhook returns `HTTP 403 Forbidden` with the error `IMMUTABILITY_VIOLATION`.

---

## 6. Secret Rotation Guide (Part 11 & GxP Compliance)

To maintain validation integrity under 21 CFR Part 11, the `INBOUND_EMAIL_HMAC_SECRET` must be rotated regularly (at least annually or immediately upon suspected compromise).

### 6.1 Rotation Steps

1. **Step 1 (Generate):** Generate a new cryptographically secure random string (minimum 32 characters).
   ```bash
   openssl rand -hex 32
   ```
2. **Step 2 (SMTP Provider Update):** Update the outbound webhook signing configuration in your SMTP provider dashboard (e.g., Mailgun) to start signing requests with the new key.
3. **Step 3 (eTMF Configuration):** Update the eTMF container environment variable `INBOUND_EMAIL_HMAC_SECRET` to the new value in `docker-compose.yml` or your secret manager.
4. **Step 4 (Grace Period):** If the SMTP provider does not support dual-signing during transitions, SREs must perform the rotation during a scheduled low-traffic window to minimize transient `401` errors.
5. **Step 5 (Audit Logging):** SREs must log a manual system modification record detailing the secret rotation action to satisfy GxP operational documentation logs.

---

## 7. Runnable Pytest Verification Command

System operators can verify the complete signature validation, routing, replay protection, and GxP compliance of the webhook using the automated pytest suite:

```bash
# Run all webhook integration tests
PYTHONPATH=. uv run pytest tests/test_etmf_inbound_email.py --no-cov
```
