#!/usr/bin/env python3
"""Automated OpenAPI to VitePress Markdown Documentation Generator.

Statically extracts OpenAPI 3.1.0 specifications from all Cadence Clinical microservices
and generates comprehensive, searchable Markdown reference documents under docs/api/.

Features:
- Full 21 CFR Part 11 security & step-up signature requirement tags
- Parameter and schema breakdown tables
- RFC 7807 error format documentation
- Copy-ready TypeScript, Python, and cURL request snippets
- Zero live runtime dependencies
"""

import importlib
import json
import os
import sys
from pathlib import Path
from typing import Any

# Ensure repository root is in python path
ROOT_DIR = Path(__file__).resolve().parent.parent
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

# Mock environment variables for clean offline imports
os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "dev-secret-key-12345")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("JWT_SECRET_KEY", "dev-jwt-secret-key-12345")
os.environ.setdefault("BRAND_DOMAIN", "ccrsoft.com")
os.environ.setdefault("NOTIFICATIONS_DATABASE_URL", "sqlite:///:memory:")
os.environ.setdefault("SIGNING_SECRET", "dev-signing-secret-12345")

# Register package paths
packages_dir = ROOT_DIR / "packages"
for name in ["database", "deid", "security", "ui", "storage", "sentinels", "ddf"]:
    pkg_path = packages_dir / name
    if str(pkg_path) not in sys.path:
        sys.path.insert(0, str(pkg_path))


SERVICE_METADATA: dict[str, dict[str, Any]] = {
    "gateway": {
        "title": "Central API Gateway & Auth Proxy",
        "domain": "Platform & Gateway Infrastructure",
        "port": 8000,
        "prefix": "/api/v1",
        "desc": "Unified reverse proxy handling Keycloak OIDC authentication, HMAC signature propagation, step-up tokens, rate limiting, and OpenAPI aggregation.",
        "persistence": "Stateless / Keycloak OIDC",
    },
    "designer": {
        "title": "Study Designer & MDR (USDM)",
        "domain": "Protocol & Study Design",
        "port": 8001,
        "prefix": "/api/v1/designer",
        "desc": "Clinical study metadata authoring, CDISC USDM v3.0/v4.0 graph modeling, SoA matrix generation, and Biomedical Concept libraries.",
        "persistence": "Neo4j Graph Database",
    },
    "execution": {
        "title": "EDC Execution Engine",
        "domain": "Clinical Operations & Execution",
        "port": 8002,
        "prefix": "/api/v1/execution",
        "desc": "Runtime Electronic Data Capture (EDC), dynamic subject eCRF schema projection, real-time edit checks, medical coding, lab toxicity, and Dataset-JSON exports.",
        "persistence": "PostgreSQL Relational DB",
    },
    "etmf": {
        "title": "Electronic Trial Master File (eTMF)",
        "domain": "Safety, Quality & Governance",
        "port": 8004,
        "prefix": "/api/v1/etmf",
        "desc": "DIA TMF Reference Model v3.2.0 document classification, Expected Document Lists (EDLs), completeness scoring, and automated QC transitions.",
        "persistence": "PostgreSQL / SQLite",
    },
    "ctms": {
        "title": "Clinical Trial Management System (CTMS)",
        "domain": "Clinical Operations & Execution",
        "port": 8005,
        "prefix": "/api/v1/ctms",
        "desc": "Site operational tracking, milestone governance, CRA monitoring visit reports (MVR), 6-facet site greenlight, and investigator grant budgets.",
        "persistence": "PostgreSQL / SQLite",
    },
    "quality": {
        "title": "Quality & CAPA Management",
        "domain": "Safety, Quality & Governance",
        "port": 8006,
        "prefix": "/api/v1/quality",
        "desc": "Protocol deviation tracking, Root Cause Analysis (RCA), CAPA workflows, Centralized Statistical Monitoring (CSM forensics), and pHash image fraud detection.",
        "persistence": "PostgreSQL / SQLite",
    },
    "interop": {
        "title": "FHIR & eSource Gateway",
        "domain": "Patient Engagement & Interoperability",
        "port": 8007,
        "prefix": "/api/v1/interop",
        "desc": "SMART on FHIR EHR integration, eSource reconciliation drawers, and Open mHealth continuous biometric sensor streaming adapters.",
        "persistence": "PostgreSQL / SQLite",
    },
    "org": {
        "title": "Organization Directory",
        "domain": "Clinical Operations & Execution",
        "port": 8008,
        "prefix": "/api/v1/org",
        "desc": "Global directory of Sponsor/CRO organizations, clinical trial sites, staff credentials, and Principal Investigator Delegation of Authority (DOA) logs.",
        "persistence": "PostgreSQL / SQLite",
    },
    "safety": {
        "title": "Safety & Pharmacovigilance Gateway",
        "domain": "Safety, Quality & Governance",
        "port": 8009,
        "prefix": "/api/v1/safety",
        "desc": "ICH E2B(R3) Individual Case Safety Reports (ICSR) XML compilation, NCI-CTCAE v5.0 toxicity grading, and B2B AS2 S/MIME regulatory EDI gateway.",
        "persistence": "PostgreSQL / SQLite",
    },
    "notifications": {
        "title": "Multi-Channel Notifications Dispatcher",
        "domain": "Patient Engagement & Interoperability",
        "port": 8010,
        "prefix": "/api/v1/notifications",
        "desc": "Automated patient compliance reminders and high-priority GxP alerts across SMTP Email (Jinja2), SMS, HMAC Webhooks, and In-App inbox.",
        "persistence": "PostgreSQL / SQLite",
    },
    "knowledge": {
        "title": "Knowledge & SOP Engine",
        "domain": "Protocol & Study Design",
        "port": 8011,
        "prefix": "/api/v1/knowledge",
        "desc": "Contextual SOP delivery, dynamic help drawer routing, role-specific clinical guidance, and controlled document versioning.",
        "persistence": "PostgreSQL / SQLite",
    },
    "tickets": {
        "title": "Clinical Issue Hub & Query Escalation",
        "domain": "Clinical Operations & Execution",
        "port": 8012,
        "prefix": "/api/v1/tickets",
        "desc": "Clinical query escalation, data management issue tracking, dual-target SLA timers, and controlled reopening windows.",
        "persistence": "PostgreSQL / SQLite",
    },
    "ai_gateway": {
        "title": "Clinical AI Gateway",
        "domain": "Protocol & Study Design",
        "port": 8013,
        "prefix": "/api/v1/ai",
        "desc": "Model tiering (Tier 1–3), PII/PHI de-identification air-gap proxy, medical coding embeddings, and 21 CFR Part 11 dual-attribution AI ledgers.",
        "persistence": "Ephemeral / REST",
    },
    "payments": {
        "title": "Site Payments & Participant Wallet",
        "domain": "Clinical Operations & Execution",
        "port": 8014,
        "prefix": "/api/v1/payments",
        "desc": "Investigator grant contracts, procedure-fee rules, multi-payee splits, closeout holdbacks, participant expense OCR, and self-billing invoices.",
        "persistence": "PostgreSQL / SQLite",
    },
    "imaging": {
        "title": "Blinded DICOM Adjudication",
        "domain": "Safety, Quality & Governance",
        "port": 8015,
        "prefix": "/api/v1/imaging",
        "desc": "Zero-footprint WebGL Multi-Planar Reconstruction (MPR), blinded dual-reader RECIST 1.1 oncology evaluation, and 20% discordance triggers.",
        "persistence": "PostgreSQL / SQLite",
    },
    "eisf": {
        "title": "Electronic Investigator Site Files (eISF)",
        "domain": "Safety, Quality & Governance",
        "port": 8016,
        "prefix": "/api/v1/eisf",
        "desc": "Site-scoped regulatory binder management, DIA eISF taxonomy inheritance, and PI certification audit manifests.",
        "persistence": "PostgreSQL / SQLite",
    },
    "econsent": {
        "title": "Electronic Consent (eConsent)",
        "domain": "Patient Engagement & Interoperability",
        "port": 8017,
        "prefix": "/api/v1/econsent",
        "desc": "Interactive informed consent authoring, dynamic amendment re-consent gating, readability metrics, and digital signature sealing.",
        "persistence": "PostgreSQL / SQLite",
    },
    "fileshare": {
        "title": "Object Storage & FileShare Gateway",
        "domain": "Safety, Quality & Governance",
        "port": 8018,
        "prefix": "/api/v1/fileshare",
        "desc": "StoragePort abstraction over MinIO/S3, presigned transfer URLs, cryptographic guest links, and 21 CFR Part 11 document envelopes.",
        "persistence": "MinIO / S3 & SQL Envelope",
    },
    "compliance": {
        "title": "Compliance & Qualification Engine",
        "domain": "Platform & Gateway Infrastructure",
        "port": 8019,
        "prefix": "/api/v1/compliance",
        "desc": "Centralized regulatory compliance verification, IQ/OQ/PQ execution suites, and automated 21 CFR Part 11 audit trail assertions.",
        "persistence": "Pytest + Audit Ledgers",
    },
}


def load_service_spec(service_name: str) -> dict[str, Any] | None:
    """Loads the OpenAPI specification for a microservice via dynamic import or file fallback."""
    try:
        module = importlib.import_module(f"apps.{service_name}.main")
        app = getattr(module, "app", None)
        if app is not None:
            return app.openapi()
    except Exception:
        pass

    openapi_file = ROOT_DIR / "docs" / "openapi" / f"{service_name}_openapi.json"
    if openapi_file.exists():
        try:
            return json.loads(openapi_file.read_text(encoding="utf-8"))
        except Exception:
            pass

    return None


def format_schema_type(schema: dict[str, Any]) -> str:
    """Formats OpenAPI schema type for Markdown tables."""
    if not schema:
        return "`any`"
    if "$ref" in schema:
        ref_name = schema["$ref"].split("/")[-1]
        return f"`{ref_name}`"
    if "type" in schema:
        t = schema["type"]
        if t == "array":
            items = schema.get("items", {})
            return f"`Array<{format_schema_type(items).replace('`', '')}>`"
        return f"`{t}`"
    if "anyOf" in schema:
        types = [format_schema_type(s) for s in schema["anyOf"]]
        return " | ".join(types)
    return "`object`"


def generate_service_markdown(
    service_name: str, meta: dict[str, Any], spec: dict[str, Any]
) -> str:
    """Generates comprehensive Markdown documentation for a microservice."""
    title = meta["title"]
    port = meta["port"]
    prefix = meta["prefix"]
    desc = meta["desc"]
    persistence = meta["persistence"]
    domain = meta["domain"]

    paths = spec.get("paths", {})
    components = spec.get("components", {}).get("schemas", {})

    lines: list[str] = [
        f"# {title}",
        "",
        f"> **Functional Domain:** `{domain}`  ",
        f"> **Service Host Port:** `localhost:{port}`  ",
        f"> **Gateway Proxy Prefix:** `{prefix}`  ",
        f"> **Persistence Backend:** `{persistence}`  ",
        "",
        "## Overview",
        "",
        desc,
        "",
        "---",
        "",
        "## Endpoints Summary",
        "",
        "| Method | Path | Summary | GxP Auth & Signatures |",
        "| :--- | :--- | :--- | :--- |",
    ]

    for path, operations in sorted(paths.items()):
        for method, op in operations.items():
            if method.upper() not in ["GET", "POST", "PUT", "PATCH", "DELETE"]:
                continue
            summary = op.get("summary", "No summary")
            is_mutating = method.upper() in ["POST", "PUT", "PATCH", "DELETE"]
            gxp_badge = (
                "🔒 Step-Up Sig Token"
                if is_mutating and "sign" in path.lower()
                else ("✍️ Audited Write" if is_mutating else "👀 Read-Only")
            )
            method_badge = f"`{method.upper()}`"
            path_link = f"[{path}](#{method.lower()}-{path.strip('/').replace('/', '-').replace('{', '').replace('}', '')})"
            lines.append(f"| {method_badge} | {path_link} | {summary} | {gxp_badge} |")

    lines.extend(
        [
            "",
            "---",
            "",
            "## Granular Endpoint Specifications",
            "",
        ]
    )

    for path, operations in sorted(paths.items()):
        for method, op in operations.items():
            if method.upper() not in ["GET", "POST", "PUT", "PATCH", "DELETE"]:
                continue

            op_id = op.get("operationId", f"{method}_{path}")
            summary = op.get("summary", op_id)
            description = op.get("description", "No detailed description provided.")
            is_mutating = method.upper() in ["POST", "PUT", "PATCH", "DELETE"]

            lines.extend(
                [
                    f"### `{method.upper()}` `{path}`",
                    "",
                    f"**Summary:** {summary}  ",
                    f"**Operation ID:** `{op_id}`  ",
                    "",
                    "**Clinical & GxP Rationale:**  ",
                    f"{description}",
                    "",
                    "#### Security & Access Control (21 CFR Part 11)",
                    "",
                    "- **Authentication:** OIDC Bearer Token (Keycloak)",
                    "- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)",
                    f"- **Step-Up Electronic Signature:** {'Mandatory 60s `sig_token` required for Part 11 sign-off' if is_mutating and 'sign' in path.lower() else ('Required if mutating locked records' if is_mutating else 'Not required (read-only query)')}",
                    "",
                ]
            )

            # Parameters
            params = op.get("parameters", [])
            if params:
                lines.extend(
                    [
                        "#### Request Parameters",
                        "",
                        "| Name | In | Type | Required | Description |",
                        "| :--- | :--- | :--- | :--- | :--- |",
                    ]
                )
                for p in params:
                    p_name = p.get("name", "")
                    p_in = p.get("in", "query")
                    p_req = "✔ Yes" if p.get("required", False) else "No"
                    p_schema = format_schema_type(p.get("schema", {}))
                    p_desc = p.get("description", "—")
                    lines.append(
                        f"| `{p_name}` | `{p_in}` | {p_schema} | {p_req} | {p_desc} |"
                    )
                lines.append("")

            # Request Body
            req_body = op.get("requestBody", {})
            if req_body:
                content = req_body.get("content", {})
                json_content = content.get("application/json", {})
                body_schema = json_content.get("schema", {})
                lines.extend(
                    [
                        "#### Request Payload Schema (`application/json`)",
                        "",
                        f"**Schema Type:** {format_schema_type(body_schema)}",
                        "",
                    ]
                )
                if "$ref" in body_schema:
                    ref_name = body_schema["$ref"].split("/")[-1]
                    if ref_name in components:
                        props = components[ref_name].get("properties", {})
                        if props:
                            lines.extend(
                                [
                                    "| Field | Type | Required | Description |",
                                    "| :--- | :--- | :--- | :--- |",
                                ]
                            )
                            req_props = components[ref_name].get("required", [])
                            for field_name, field_def in props.items():
                                f_type = format_schema_type(field_def)
                                f_req = "✔ Yes" if field_name in req_props else "No"
                                f_desc = field_def.get("description", "—")
                                lines.append(
                                    f"| `{field_name}` | {f_type} | {f_req} | {f_desc} |"
                                )
                            lines.append("")

            # Responses
            responses = op.get("responses", {})
            if responses:
                lines.extend(
                    [
                        "#### Response Codes & Schemas",
                        "",
                        "| HTTP Status | Description | Response Schema |",
                        "| :--- | :--- | :--- |",
                    ]
                )
                for code, resp in responses.items():
                    r_desc = resp.get("description", "Standard response")
                    r_content = resp.get("content", {}).get("application/json", {})
                    r_schema = format_schema_type(r_content.get("schema", {}))
                    lines.append(f"| `{code}` | {r_desc} | {r_schema} |")
                lines.append("")

            # Code Examples
            lines.extend(
                [
                    "#### Client Code Examples",
                    "",
                    "::: code-group",
                    "```typescript [TypeScript (Fetch)]",
                    f"const response = await fetch('http://localhost:8000{prefix}{path}', {{",
                    f"  method: '{method.upper()}',",
                    "  headers: {",
                    "    'Authorization': `Bearer ${userToken}`,",
                    "    'Content-Type': 'application/json',",
                    "    'X-Change-Reason': 'Clinical routine data capture',",
                    "  },",
                    ("  body: JSON.stringify(payload),\n});" if is_mutating else "});"),
                    "const data = await response.json();",
                    "```",
                    "",
                    "```python [Python (HTTPX)]",
                    "import httpx",
                    "",
                    "async with httpx.AsyncClient(base_url='http://localhost:8000') as client:",
                    f"    response = await client.{method.lower()}(",
                    f"        '{prefix}{path}',",
                    "        headers={",
                    "            'Authorization': f'Bearer {user_token}',",
                    "            'X-Change-Reason': 'Clinical routine data capture',",
                    "        },",
                    ("        json=payload,\n    )" if is_mutating else "    )"),
                    "    data = response.json()",
                    "```",
                    "",
                    "```bash [cURL]",
                    f"curl -X {method.upper()} 'http://localhost:8000{prefix}{path}' \\",
                    "  -H 'Authorization: Bearer <TOKEN>' \\",
                    "  -H 'Content-Type: application/json' \\",
                    "  -H 'X-Change-Reason: Routine clinical mutation'",
                    "```",
                    ":::",
                    "",
                    "---",
                    "",
                ]
            )

    return "\n".join(lines)


def generate_index_markdown(
    available_services: list[tuple[str, dict[str, Any], dict[str, Any]]],
) -> str:
    """Generates the Master API Reference index page."""
    lines = [
        "# Cadence Clinical REST API Reference",
        "",
        "> **Unified Microservice Architecture & GxP Endpoint Contract.**  ",
        "> _All endpoints exposed through the central API Gateway (`localhost:8000/api/v1/*`) with Keycloak OIDC authentication and HMAC signature verification._",
        "",
        "---",
        "",
        "## 🏛️ Architectural Overview & Gateway Topology",
        "",
        "The Cadence Clinical platform routes all client requests through a centralized reverse proxy (`apps/gateway`). Incoming requests undergo:",
        "",
        "1. **Keycloak OIDC JWT Token Verification**: Validates user credentials, roles (`sponsor_designer`, `site_crc`, `cra_monitor`, etc.), and tenant/site scoping.",
        "2. **Gateway Signature Injection**: Automatically generates and appends canonical `X-Gateway-Signature` (HMAC-SHA256) headers before dispatching to downstream services.",
        "3. **21 CFR Part 11 Electronic Signature Step-Up**: Mutating operations (e.g. batch approvals, subject randomization, unblinding) require a short-lived (60-second) `sig_token` obtained via TOTP + password re-authentication.",
        "4. **Standardized RFC 7807 Error Responses**: All errors emit RFC 7807 `application/problem+json` envelopes with canonical type URIs, status codes, and detail strings.",
        "",
        "---",
        "",
        "## 📁 Layered Microservice API Catalog",
        "",
    ]

    domains: dict[str, list[tuple[str, dict[str, Any], int]]] = {}
    for name, meta, spec in available_services:
        d = meta["domain"]
        if d not in domains:
            domains[d] = []
        endpoint_count = sum(len(ops) for ops in spec.get("paths", {}).values())
        domains[d].append((name, meta, endpoint_count))

    for domain_name, s_list in domains.items():
        lines.extend(
            [
                f"### {domain_name}",
                "",
                "| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |",
                "| :--- | :--- | :--- | :--- | :--- | :--- |",
            ]
        )
        for name, meta, count in s_list:
            lines.append(
                f"| **{meta['title']}** | `{meta['prefix']}` | `{meta['port']}` | `{count}` | `{meta['persistence']}` | [View Reference](./{name}.md) |"
            )
        lines.append("")

    lines.extend(
        [
            "---",
            "",
            "## 🔒 Authentication & Standard Gateway Headers",
            "",
            "All API requests dispatched to the Gateway must include standard Bearer authorization headers:",
            "",
            "```http",
            "GET /api/v1/execution/subjects HTTP/1.1",
            "Host: localhost:8000",
            "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
            "X-Change-Reason: Routine monitoring inspection",
            "Content-Type: application/json",
            "```",
            "",
            "### Injected Downstream Service Headers",
            "",
            "| Header | Source | Description |",
            "| :--- | :--- | :--- |",
            "| `X-User-Id` | Gateway Proxy | Authenticated Keycloak Subject Identifier (UUID) |",
            "| `X-User-Roles` | Gateway Proxy | Comma-separated list of assigned GxP user roles |",
            "| `X-Gateway-Signature` | Gateway Proxy | Canonical HMAC-SHA256 signature verifying internal traffic origin |",
            "| `X-Change-Reason` | Client Caller | Mandatory 21 CFR Part 11 justification string for all mutating writes |",
            "| `X-Signature-Token` | Client Caller | 60-second step-up TOTP re-authentication token for signature gates |",
            "",
            "---",
            "",
            "## ⚠️ Standard RFC 7807 Problem Detail Format",
            "",
            "In the event of an API error, Cadence returns standardized RFC 7807 error envelopes:",
            "",
            "```json",
            "{",
            '  "type": "https://cadence.clinical/errors/resource-locked",',
            '  "title": "Resource Locked",',
            '  "status": 423,',
            '  "detail": "Site SITE-101 has not achieved 6-facet greenlight activation. Randomization blocked.",',
            '  "instance": "/api/v1/execution/subjects/SUBJ-001/randomize"',
            "}",
            "```",
        ]
    )

    return "\n".join(lines)


def main():
    """Main execution function generating all API documentation pages."""
    api_docs_dir = ROOT_DIR / "docs" / "api"
    api_docs_dir.mkdir(parents=True, exist_ok=True)

    print("--- Starting Programmatic OpenAPI Documentation Generator ---")
    available_services = []

    for service_name, meta in SERVICE_METADATA.items():
        spec = load_service_spec(service_name)
        if not spec:
            print(
                f"[WARN] No OpenAPI spec found for '{service_name}', generating fallback metadata..."
            )
            spec = {
                "paths": {
                    f"{meta['prefix']}/health": {
                        "get": {
                            "summary": f"{meta['title']} Health Check",
                            "description": f"Verifies live operational status and database connectivity for {meta['title']}.",
                            "responses": {
                                "200": {
                                    "description": "Service is healthy and operational"
                                }
                            },
                        }
                    }
                },
                "components": {},
            }

        md_content = generate_service_markdown(service_name, meta, spec)
        target_path = api_docs_dir / f"{service_name}.md"
        target_path.write_text(md_content, encoding="utf-8")
        available_services.append((service_name, meta, spec))
        print(
            f"[SUCCESS] Generated API documentation: docs/api/{service_name}.md ({len(spec.get('paths', {}))} routes)"
        )

    # Generate master index
    index_content = generate_index_markdown(available_services)
    index_path = api_docs_dir / "index.md"
    index_path.write_text(index_content, encoding="utf-8")
    print(
        f"[SUCCESS] Generated Master API Index: docs/api/index.md ({len(available_services)} microservices cataloged)"
    )


if __name__ == "__main__":
    main()
