# Security Policy

Cadence Clinical is a clinical trial software platform handling sensitive data across protocol design, clinical operations, patient eConsent/ePRO, and regulatory submissions. Protecting patient health information (PHI), clinical trial integrity, and compliance with global regulatory standards (including FDA 21 CFR Part 11, HIPAA, and GCP) is paramount.

---

## Supported Versions

Cadence is pre-release. Security fixes land on `main` only; there are no maintained release branches yet.

| Version       | Supported          |
| ------------- | ------------------ |
| `main`        | :white_check_mark: |
| Anything else | :x:                |

---

## Reporting a Vulnerability

Do **NOT** file public GitHub issues, discussions, or pull requests for suspected security vulnerabilities.

### Private Reporting Channels

- **GitHub Advisory:** Submit a report privately through [GitHub Private Vulnerability Reporting](https://github.com/fderuiter/cadence-clinical/security/advisories/new).
- **Dedicated Security Mailbox:** Email **security@cadenceclinical.org** (or your organization's designated security alias).

### Information to Include

To help us triage and validate your submission efficiently, please provide:

- **Component / Microservice Affected:** (e.g., `apps/gateway`, `apps/ai_gateway`, `apps/execution`, `apps/etmf`, `apps/safety`, `apps/web`).
- **Vulnerability Category:** (e.g., Authentication Bypass, Privilege Escalation, Broken Object-Level Authorization [BOLA], Cryptographic Invariant Failure, De-identification / PHI Leakage, SQLi / Cypher Injection, AS2/S-MIME flaw).
- **Step-by-Step Reproduction Steps:** A minimal Proof-of-Concept (PoC) script, HTTP request payloads, or reproduction steps.
- **Compliance / Clinical Impact Assessment:** Potential impact on clinical trial blinding, audit trail immutability (21 CFR Part 11), patient consent state, or protected health information (PHI/PII).

---

## Response SLAs

| Milestone                                      | Target Timeline                             |
| ---------------------------------------------- | ------------------------------------------- |
| **Initial Acknowledgment**                     | Within **24 to 48 hours**                   |
| **Triage & GxP Impact Assessment**             | Within **3 business days**                  |
| **Remediation & Patch Target (Critical/High)** | Within **7 to 14 business days**            |
| **Public Advisory & Release Notes**            | Coordinated upon validated patch deployment |

---

## Scope & Safe Harbor

### In-Scope Vulnerabilities

- Authorization & authentication bypass across the API Gateway, microservice seams, or Keycloak/OIDC handlers.
- Airgap bypasses or leakage in the AI Gateway de-identification pipelines (`apps/ai_gateway`).
- Integrity violations of cryptographic seals, audit ledgers, and Part 11 electronic signatures (`apps/etmf`, `apps/execution`, `apps/quality`).
- Unintended unblinding or tampering in Randomization and Trial Supply Management (`apps/execution` RTSM).
- Vulnerabilities in clinical file processing, virus scanning bypasses, or media transcoding (`apps/fileshare`).
- Flaws in regulatory transmission gateways (E2B(R3), AS2 S/MIME in `apps/safety`).

### Out-of-Scope Activities

- Non-reproducible automated vulnerability scanner dumps.
- Denial of Service (DoS/DDoS) attacks against production or demonstration infrastructure.
- Social engineering, phishing, or physical security attacks against staff, investigators, or study subjects.
- Any testing that compromises, exfiltrates, or alters real patient/subject data or live clinical trial registries.

### Safe Harbor

Activities conducted in good faith, strictly within the scope of this policy, and without attempting to access or exfiltrate production PHI/PII are considered authorized conduct. We will not pursue legal action against researchers who adhere to coordinated disclosure principles.

---

## Coordinated Disclosure

- **Embargo Period:** We request a standard **90-day embargo** from the date of initial acknowledgment before any public disclosure to allow multi-tenant deployments and compliance validation cycles to complete.
- **Attribution:** Researchers adhering to responsible disclosure will be credited in our public security advisories and release notes upon deployment of the fix.
