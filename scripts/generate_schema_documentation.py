#!/usr/bin/env python3
"""Automated System Schema Boundary and Documentation Generator.

Statically parses Relational (PostgreSQL/SQLAlchemy) and Graph (Neo4j/USDM) schemas
to synthesize a unified interactive dashboard. Highlights GxP, audit-logged, and
write-protected tables and fields to satisfy FDA 21 CFR Part 11 and EU Annex 11
auditing requirements.

Compliance:
- Zero active database connections required.
- Zero-overhead microservice isolation.
"""

import json
import os
import sys
from pathlib import Path
from typing import Any

# Set up mock environment variables before importing any other modules
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault("GATEWAY_SECRET", "test-gateway-secret-placeholder-123")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("SIGNING_SECRET", "test-signing-secret-123")

# Ensure codebase root is in python path
ROOT_DIR = Path(__file__).resolve().parent.parent
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))


def get_all_subclasses(cls: Any) -> set[Any]:
    """Recursively retrieves all descendants of a given class.

    Args:
        cls: The base class.

    Returns:
        A set of all subclasses of the class.
    """
    subclasses = set()
    for sub in cls.__subclasses__():
        subclasses.add(sub)
        subclasses.update(get_all_subclasses(sub))
    return subclasses


def parse_sqlalchemy_schema(base_class: Any, service_name: str) -> list[dict[str, Any]]:
    """Extracts schema, field definitions, and GxP flags from SQLAlchemy models.

    Args:
        base_class: The declarative SQLAlchemy base class.
        service_name: The name of the microservice.

    Returns:
        A list of dictionaries representing table metadata.
    """
    tables_meta = []
    classes = [
        cls for cls in get_all_subclasses(base_class) if hasattr(cls, "__tablename__")
    ]

    for cls in sorted(classes, key=lambda x: x.__name__):
        table_name = getattr(cls, "__tablename__")
        docstring = cls.__doc__ or "No description provided."
        # Clean docstring indentation and whitespace
        cleaned_doc = "\n".join(
            line.strip() for line in docstring.split("\n") if line.strip()
        )

        # Detect GxP audited status
        is_gxp = any(
            b.__name__ in ("AuditedModel", "Part11AuditMixin") for b in cls.__mro__
        )

        # Detect specific audit columns
        audit_cols = {
            "created_at",
            "created_by",
            "reason_for_change",
            "version_index",
            "change_reason",
        }

        columns = []
        # Inspect columns
        from sqlalchemy import inspect as sqla_inspect

        try:
            mapper = sqla_inspect(cls)
            for col in mapper.columns:
                col_name = col.name
                col_type = str(col.type)
                is_pk = col.primary_key
                is_nullable = col.nullable
                fks = [str(fk.target_fullname) for fk in col.foreign_keys]

                if col_name in audit_cols:
                    is_gxp = True

                # Highlight specific audit and sensitive fields
                is_gxp_field = col_name in audit_cols or col_name in (
                    "unblinded_reason",
                    "withdrawal_reason",
                    "cryptographic_seal",
                    "unblinded_signature",
                )

                columns.append(
                    {
                        "name": col_name,
                        "type": col_type,
                        "primary_key": is_pk,
                        "nullable": is_nullable,
                        "foreign_keys": fks,
                        "gxp_highlight": is_gxp_field,
                    }
                )
        except Exception as e:
            # Fallback if inspection fails
            print(f"Warning: could not inspect class {cls.__name__}: {e}")

        # Detect write-protected / immutable models
        is_immutable = cls.__name__ in ("ConsentSignature", "DocumentQCTransition")

        tables_meta.append(
            {
                "id": f"{service_name}_{table_name}",
                "table_name": table_name,
                "class_name": cls.__name__,
                "description": cleaned_doc,
                "service": service_name,
                "gxp": is_gxp,
                "immutable": is_immutable,
                "columns": columns,
            }
        )

    return tables_meta


def get_designer_schema() -> list[dict[str, Any]]:
    """Synthesizes Neo4j USDM graph schemas statically for the Designer microservice.

    Returns:
        A list of dictionaries representing graph node metadata.
    """
    return [
        {
            "id": "designer_Study",
            "table_name": "Study",
            "class_name": "Study",
            "description": (
                "Core USDM clinical trial representation detailing high-level "
                "protocol specifications, title, and study-wide status constraints."
            ),
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "title",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "current_version",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "status",
                    "type": "String (Active-Recruiting, Locked, etc.)",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "desc",
                    "type": "String",
                    "primary_key": False,
                    "nullable": True,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
            ],
        },
        {
            "id": "designer_StudyVersion",
            "table_name": "StudyVersion",
            "class_name": "StudyVersion",
            "description": (
                "A snapshot version of the study protocol incorporating cryptographic "
                "signatures to guarantee GxP audit-trail integrity for FDA 21 CFR Part 11."
            ),
            "service": "designer",
            "gxp": True,
            "immutable": True,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "version_tag",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "version_index",
                    "type": "Integer",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "status",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "created_by",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "created_at",
                    "type": "Datetime (UTC)",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "signature",
                    "type": "String (HMAC-SHA256)",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
            ],
        },
        {
            "id": "designer_Epoch",
            "table_name": "Epoch",
            "class_name": "Epoch",
            "description": (
                "A trial subdivision describing periods of conduct such as Screening, "
                "Treatment, or Follow-Up."
            ),
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "order",
                    "type": "Integer",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
            ],
        },
        {
            "id": "designer_Activity",
            "table_name": "Activity",
            "class_name": "Activity",
            "description": "An action performed on clinical study subjects (e.g., Blood Draw, Vitals).",
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
            ],
        },
        {
            "id": "designer_TreatmentArm",
            "table_name": "TreatmentArm",
            "class_name": "TreatmentArm",
            "description": "A standardized treatment path designating study arms.",
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "arm_id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "type_concept_id",
                    "type": "String (NCI Concept)",
                    "primary_key": False,
                    "nullable": True,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
            ],
        },
        {
            "id": "designer_ScheduledVisit",
            "table_name": "ScheduledVisit",
            "class_name": "ScheduledVisit",
            "description": "A planned encounter or visit within the clinical study.",
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "visit_id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "visit_type_concept_id",
                    "type": "String (NCI Concept)",
                    "primary_key": False,
                    "nullable": True,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
            ],
        },
        {
            "id": "designer_LibraryObject",
            "table_name": "LibraryObject",
            "class_name": "LibraryObject",
            "description": "CDISC Terminology and study element templates.",
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "version",
                    "type": "Integer",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
                {
                    "name": "status",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
            ],
        },
        {
            "id": "designer_EligibilityCriterion",
            "table_name": "EligibilityCriterion",
            "class_name": "EligibilityCriterion",
            "description": "Structured criteria used to gauge subject eligibility.",
            "service": "designer",
            "gxp": True,
            "immutable": False,
            "columns": [
                {
                    "name": "id",
                    "type": "String (UUID)",
                    "primary_key": True,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "name",
                    "type": "String",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "expression",
                    "type": "String (Boolean logic)",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": False,
                },
                {
                    "name": "is_deleted",
                    "type": "Boolean",
                    "primary_key": False,
                    "nullable": False,
                    "foreign_keys": [],
                    "gxp_highlight": True,
                },
            ],
        },
    ]


def generate_html_visualizer(
    services: dict[str, dict[str, Any]], edges: list[dict[str, Any]]
) -> str:
    """Generates the single-page interactive Vis.js HTML document.

    Args:
        services: A dictionary mapping service names to service metadata.
        edges: A list of edge metadata dictionaries.

    Returns:
        The full string content of the HTML visualizer page.
    """
    serialized_data = json.dumps({"services": services, "edges": edges}, indent=2)

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Cadence Clinical System Schema Boundaries & GxP Traceability Matrix</title>
  <!-- Tailwind CSS via CDN -->
  <script src="https://cdn.tailwindcss.com"></script>
  <!-- Vis.js Network via CDN -->
  <script type="text/javascript" src="https://unpkg.com/vis-network/standalone/umd/vis-network.min.js"></script>
  <style>
    #network-container {{
      width: 100%;
      height: 650px;
      border: 1px solid #e2e8f0;
      background-color: #f8fafc;
      border-radius: 0.5rem;
    }}
  </style>
</head>
<body class="bg-slate-50 text-slate-800 font-sans">

  <header class="bg-indigo-900 text-white shadow-md py-6 px-8 mb-8">
    <div class="max-w-7xl mx-auto flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
      <div>
        <span class="bg-indigo-700 text-indigo-100 text-xs font-bold uppercase tracking-wider px-2.5 py-1 rounded">FDA 21 CFR Part 11 & EU Annex 11 Compliance</span>
        <h1 class="text-3xl font-extrabold tracking-tight mt-2">Codified Schema Boundaries & Automated Documentation</h1>
        <p class="text-indigo-200 mt-1">Interactive system-wide schema graph showing physical microservice boundaries, security logs, and write-protected GxP clinical lineages.</p>
      </div>
      <div class="bg-indigo-800 border border-indigo-700 p-4 rounded-lg text-sm text-indigo-100">
        <p class="font-semibold text-white">Pipeline Execution Status</p>
        <p class="mt-1">✓ Automated Generation</p>
        <p>✓ Zero active DB driver imports</p>
      </div>
    </div>
  </header>

  <main class="max-w-7xl mx-auto px-8 pb-16">

    <!-- Dashboard Statistics -->
    <section class="grid grid-cols-1 md:grid-cols-4 gap-6 mb-8">
      <div class="bg-white p-6 rounded-lg shadow-sm border border-slate-200 flex flex-col">
        <span class="text-slate-500 text-sm font-semibold uppercase tracking-wider">Total Service Boundaries</span>
        <span class="text-3xl font-extrabold text-slate-900 mt-2">3</span>
        <span class="text-xs text-indigo-600 mt-1 font-medium">Designer, Execution, eTMF</span>
      </div>
      <div class="bg-white p-6 rounded-lg shadow-sm border border-slate-200 flex flex-col">
        <span class="text-slate-500 text-sm font-semibold uppercase tracking-wider">Total Database Entities</span>
        <span id="stat-entities" class="text-3xl font-extrabold text-slate-900 mt-2">0</span>
        <span class="text-xs text-emerald-600 mt-1 font-medium">Statically synthesized</span>
      </div>
      <div class="bg-white p-6 rounded-lg shadow-sm border border-red-100 flex flex-col bg-red-50/20">
        <span class="text-red-700 text-sm font-semibold uppercase tracking-wider">GxP Regulated Entities</span>
        <span id="stat-gxp" class="text-3xl font-extrabold text-red-900 mt-2">0</span>
        <span class="text-xs text-red-600 mt-1 font-medium">Require mandatory change-reasoning</span>
      </div>
      <div class="bg-white p-6 rounded-lg shadow-sm border border-amber-100 flex flex-col bg-amber-50/20">
        <span class="text-amber-700 text-sm font-semibold uppercase tracking-wider">Write-Protected (Immutable)</span>
        <span id="stat-immutable" class="text-3xl font-extrabold text-amber-900 mt-2">0</span>
        <span class="text-xs text-amber-600 mt-1 font-medium">Signature or QC locked</span>
      </div>
    </section>

    <!-- Visualizer Workspace -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
      
      <!-- Graph and Filters -->
      <div class="lg:col-span-2 space-y-6">
        <div class="bg-white p-6 rounded-lg shadow-sm border border-slate-200">
          <div class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 mb-4">
            <h2 class="text-xl font-bold text-slate-900">Interactive Boundary Explorer</h2>
            
            <!-- Filters -->
            <div class="flex flex-wrap gap-2 text-sm">
              <select id="filter-service" class="bg-slate-100 border border-slate-300 rounded px-2 py-1.5 text-slate-700 font-medium">
                <option value="all">All Service Boundaries</option>
                <option value="designer">Designer / MDR (Graph)</option>
                <option value="execution">Execution / EDC (Relational)</option>
                <option value="etmf">eTMF (Relational)</option>
              </select>
              <select id="filter-gxp" class="bg-slate-100 border border-slate-300 rounded px-2 py-1.5 text-slate-700 font-medium">
                <option value="all">All Compliance Tiers</option>
                <option value="gxp">GxP-Regulated Only</option>
                <option value="immutable">Write-Protected Only</option>
              </select>
              <input type="text" id="search-tables" placeholder="Search entity..." class="bg-white border border-slate-300 rounded px-3 py-1.5 text-slate-700 placeholder-slate-400">
            </div>
          </div>

          <!-- Network Canvas -->
          <div id="network-container"></div>
          
          <div class="mt-4 flex flex-wrap gap-4 text-xs font-semibold justify-between items-center text-slate-500">
            <div class="flex gap-4">
              <span class="flex items-center gap-1.5"><span class="w-3.5 h-3.5 rounded-full bg-indigo-500 inline-block"></span> Designer (Neo4j)</span>
              <span class="flex items-center gap-1.5"><span class="w-3.5 h-3.5 rounded-full bg-emerald-500 inline-block"></span> Execution (Postgres)</span>
              <span class="flex items-center gap-1.5"><span class="w-3.5 h-3.5 rounded-full bg-amber-500 inline-block"></span> eTMF (Postgres)</span>
            </div>
            <div class="flex gap-4">
              <span class="flex items-center gap-1.5"><span class="w-3 h-3 rounded-full border-2 border-red-500 inline-block"></span> GxP High Risk</span>
              <span class="flex items-center gap-1.5"><span class="w-3 h-3 bg-red-200 border border-red-400 rounded inline-block"></span> Write-Protected/Immutable</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Schema and GxP Attributes sidebar -->
      <div class="space-y-6">
        <div class="bg-white p-6 rounded-lg shadow-sm border border-slate-200 flex flex-col h-[740px]">
          <h2 class="text-xl font-bold text-slate-900 border-b border-slate-100 pb-3">Entity Inspection Panel</h2>
          
          <div id="inspect-placeholder" class="flex-1 flex flex-col justify-center items-center text-center p-8 text-slate-400">
            <svg class="w-16 h-16 mb-4 text-slate-300" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M15 15l-2 5L9 9l11 4-5 2zm0 0l5 5M7.188 2.239l.777 2.897M5.136 7.965l-2.898-.777M13.95 4.05l-2.122 2.122m-5.657 5.656l-2.12 2.122" />
            </svg>
            <p class="font-medium text-slate-600">No Entity Selected</p>
            <p class="text-sm mt-1">Select any database node or relationship pathway on the diagram to inspect its schema design, GxP properties, and regulatory compliance flags.</p>
          </div>

          <div id="inspect-details" class="flex-1 overflow-y-auto hidden space-y-4 pt-4">
            <div>
              <span id="inspect-service-badge" class="px-2.5 py-1 rounded-full text-xs font-bold uppercase tracking-wider"></span>
              <h3 id="inspect-table-name" class="text-2xl font-extrabold text-slate-900 mt-2"></h3>
              <p id="inspect-class-name" class="text-sm text-slate-400 font-mono"></p>
            </div>

            <!-- GxP badging -->
            <div class="flex flex-wrap gap-2">
              <span id="inspect-gxp-badge" class="hidden items-center gap-1 px-3 py-1 bg-red-100 text-red-800 text-xs font-bold rounded-lg border border-red-200">
                ⚠️ GxP Audit Logged
              </span>
              <span id="inspect-immutable-badge" class="hidden items-center gap-1 px-3 py-1 bg-amber-100 text-amber-800 text-xs font-bold rounded-lg border border-amber-200">
                🔒 Write-Once Immutable
              </span>
            </div>

            <div>
              <h4 class="text-xs font-bold uppercase tracking-wider text-slate-400">Description</h4>
              <p id="inspect-desc" class="text-sm text-slate-600 mt-1 leading-relaxed bg-slate-50 p-3 rounded border border-slate-100"></p>
            </div>

            <div>
              <h4 class="text-xs font-bold uppercase tracking-wider text-slate-400 mb-2">Properties & Schema</h4>
              <div class="border border-slate-200 rounded overflow-hidden">
                <table class="min-w-full divide-y divide-slate-200 text-sm">
                  <thead class="bg-slate-50">
                    <tr>
                      <th class="px-3 py-2 text-left text-xs font-semibold text-slate-500">Property / Column</th>
                      <th class="px-3 py-2 text-left text-xs font-semibold text-slate-500">Type</th>
                      <th class="px-3 py-2 text-center text-xs font-semibold text-slate-500">Flags</th>
                    </tr>
                  </thead>
                  <tbody id="inspect-columns-tbody" class="divide-y divide-slate-100 bg-white">
                    <!-- Dynamic -->
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Data Lineages and Compliance Verification -->
    <section class="mt-12 bg-white p-8 rounded-lg shadow-sm border border-slate-200">
      <h2 class="text-2xl font-extrabold text-slate-900 mb-4">Inter-Service Lineages & Documented Interfaces</h2>
      <p class="text-slate-600 mb-6 leading-relaxed">
        To remain completely compliant with <strong>ADR-2164</strong> and prevent runtime database cross-dependencies, standard microservices in the Cadence platform are structurally isolated. Sibling microservices cannot directly query each other's storage backends. Instead, all cross-service boundaries communicate exclusively through secure, pre-defined, and authenticated <strong>REST APIs and Events</strong>.
      </p>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div class="border border-slate-100 bg-slate-50/50 p-6 rounded-lg">
          <div class="flex items-center gap-2 mb-2">
            <span class="p-2 bg-indigo-100 text-indigo-700 rounded font-bold text-xs uppercase">Designer &rarr; Execution</span>
            <span class="text-sm font-semibold text-slate-800">USDM Ingestion</span>
          </div>
          <p class="text-xs text-slate-500 leading-relaxed">
            The <strong>Execution Service</strong> statically imports clinical protocol structures designed within the Neo4j Graph database. Relies on the <code>/api/v1/designer/usdm/export</code> endpoint with HMAC signoffs, guaranteeing strict non-repudiation.
          </p>
        </div>

        <div class="border border-slate-100 bg-slate-50/50 p-6 rounded-lg">
          <div class="flex items-center gap-2 mb-2">
            <span class="p-2 bg-emerald-100 text-emerald-700 rounded font-bold text-xs uppercase">Execution &rarr; eTMF</span>
            <span class="text-sm font-semibold text-slate-800">Filing & Archival</span>
          </div>
          <p class="text-xs text-slate-500 leading-relaxed">
            Clinical documentation, subject consents, and electronic signatures are captured in the relational EDC model, then pushed to the <strong>eTMF repository</strong>. Communication requires <code>GatewayAuthMiddleware</code> verification.
          </p>
        </div>

        <div class="border border-slate-100 bg-slate-50/50 p-6 rounded-lg">
          <div class="flex items-center gap-2 mb-2">
            <span class="p-2 bg-amber-100 text-amber-700 rounded font-bold text-xs uppercase">System &rarr; Ledger</span>
            <span class="text-sm font-semibold text-slate-800">HMAC Auditing</span>
          </div>
          <p class="text-xs text-slate-500 leading-relaxed">
            All GxP audited models hook into the <code>Part11AuditMixin</code> and <code>AuditFields</code>. Standard transactions are cryptographically signed and chained in a SHA-256 HMAC ledger, satisfying Part 11 electronic records.
          </p>
        </div>
      </div>
    </section>

  </main>

  <footer class="bg-slate-900 text-slate-400 py-12 px-8 text-center text-sm border-t border-slate-800">
    <div class="max-w-7xl mx-auto space-y-2">
      <p class="font-semibold text-slate-300">Cadence Clinical Research Software Platform — Automated Compliance Suite</p>
      <p>FDA 21 CFR Part 11 and EU Annex 11 Compliant Schema Visualizer.</p>
      <p class="text-xs text-slate-600 mt-4">This documentation dashboard is generated statically during standard software build pipelines and is strictly read-only.</p>
    </div>
  </footer>

  <script>
    // Injected schema JSON representation
    const schemaData = {serialized_data};

    // Global Statistics
    let entitiesCount = 0;
    let gxpCount = 0;
    let immutableCount = 0;

    // Build the Vis.js nodes and edges
    const nodesArray = [];
    const edgesArray = [];

    // Helper map of table IDs to their details
    const tableLookup = {{}};

    // Colors
    const colors = {{
      designer: {{ background: "#6366f1", border: "#4f46e5", text: "#ffffff", hover: "#818cf8" }},
      execution: {{ background: "#0d9488", border: "#0f766e", text: "#ffffff", hover: "#14b8a6" }},
      etmf: {{ background: "#d97706", border: "#b45309", text: "#ffffff", hover: "#f59e0b" }}
    }};

    // Iterate services and build node properties
    Object.keys(schemaData.services).forEach(serviceKey => {{
      const service = schemaData.services[serviceKey];
      service.tables.forEach(table => {{
        entitiesCount++;
        if (table.gxp) gxpCount++;
        if (table.immutable) immutableCount++;

        tableLookup[table.id] = table;

        // Visual properties
        const colorSet = colors[table.service] || colors.execution;
        let nodeBorderColor = colorSet.border;
        let nodeBgColor = colorSet.background;
        let fontColor = colorSet.text;
        let borderWidth = 1;

        if (table.gxp) {{
          // Red thick border for GxP High Risk
          nodeBorderColor = "#ef4444";
          borderWidth = 3.5;
        }}
        if (table.immutable) {{
          // Slightly dotted or custom colored if write-protected
          nodeBgColor = "#fee2e2";
          fontColor = "#991b1b";
          borderWidth = table.gxp ? 3.5 : 2;
        }}

        nodesArray.push({{
          id: table.id,
          label: table.table_name,
          title: table.class_name + " (" + service.name + ")",
          shape: "box",
          margin: 12,
          borderWidth: borderWidth,
          color: {{
            background: nodeBgColor,
            border: nodeBorderColor,
            highlight: {{
              background: colorSet.hover,
              border: "#ffffff"
            }}
          }},
          font: {{
            color: fontColor,
            face: "monospace",
            size: 14,
            bold: table.gxp
          }},
          group: table.service
        }});

        // Add standard relational foreign-key edges
        table.columns.forEach(col => {{
          if (col.foreign_keys && col.foreign_keys.length > 0) {{
            col.foreign_keys.forEach(fkCol => {{
              // Find matching table in same service
              const targetTableName = fkCol.split(".")[0];
              const targetNodeId = table.service + "_" + targetTableName;
              edgesArray.push({{
                from: table.id,
                to: targetNodeId,
                arrows: "to",
                color: {{ color: "#cbd5e1", highlight: "#4f46e5" }},
                width: 1.5,
                title: "FK: " + col.name + " &rarr; " + fkCol
              }});
            }});
          }}
        }});
      }});
    }});

    // Add inter-service data transfer paths
    schemaData.edges.forEach(edge => {{
      edgesArray.push({{
        from: edge.from,
        to: edge.to,
        arrows: "to",
        dashes: true,
        width: 3.5,
        color: {{ color: "#6366f1", highlight: "#ef4444" }},
        title: "<b>" + edge.label + "</b><br>" + edge.desc,
        id: "edge_" + edge.from + "_" + edge.to
      }});
    }});

    // Populate stats in UI
    document.getElementById("stat-entities").innerText = entitiesCount;
    document.getElementById("stat-gxp").innerText = gxpCount;
    document.getElementById("stat-immutable").innerText = immutableCount;

    // Setup network datasets
    const nodes = new vis.DataSet(nodesArray);
    const edges = new vis.DataSet(edgesArray);

    const container = document.getElementById("network-container");
    const data = {{ nodes: nodes, edges: edges }};
    const options = {{
      physics: {{
        enabled: true,
        stabilization: {{
          enabled: true,
          iterations: 150,
          fit: true
        }},
        barnesHut: {{
          gravitationalConstant: -2500,
          centralGravity: 0.35,
          springLength: 120,
          springConstant: 0.04
        }}
      }},
      interaction: {{
        hover: true,
        tooltipDelay: 200,
        selectable: true,
        selectConnectedEdges: true
      }}
    }};

    const network = new vis.Network(container, data, options);

    // Sidebar Inspection Handler
    function inspectEntity(nodeId) {{
      const table = tableLookup[nodeId];
      if (!table) return;

      document.getElementById("inspect-placeholder").classList.add("hidden");
      const details = document.getElementById("inspect-details");
      details.classList.remove("hidden");

      // Set Service badge
      const serviceBadge = document.getElementById("inspect-service-badge");
      serviceBadge.innerText = schemaData.services[table.service].name + " (" + schemaData.services[table.service].db_type + ")";
      serviceBadge.className = "px-2.5 py-1 rounded-full text-xs font-bold uppercase tracking-wider ";
      if (table.service === "designer") {{
        serviceBadge.classList.add("bg-indigo-100", "text-indigo-800");
      }} else if (table.service === "execution") {{
        serviceBadge.classList.add("bg-teal-100", "text-teal-800");
      }} else {{
        serviceBadge.classList.add("bg-amber-100", "text-amber-800");
      }}

      // Text fields
      document.getElementById("inspect-table-name").innerText = table.table_name;
      document.getElementById("inspect-class-name").innerText = "Class: " + table.class_name;
      document.getElementById("inspect-desc").innerText = table.description;

      // GxP and Immutable badges
      const gxpBadge = document.getElementById("inspect-gxp-badge");
      if (table.gxp) {{
        gxpBadge.classList.remove("hidden");
        gxpBadge.classList.add("flex");
      }} else {{
        gxpBadge.classList.add("hidden");
      }}

      const immutableBadge = document.getElementById("inspect-immutable-badge");
      if (table.immutable) {{
        immutableBadge.classList.remove("hidden");
        immutableBadge.classList.add("flex");
      }} else {{
        immutableBadge.classList.add("hidden");
      }}

      // Populate properties table
      const tbody = document.getElementById("inspect-columns-tbody");
      tbody.innerHTML = "";
      table.columns.forEach(col => {{
        const tr = document.createElement("tr");
        if (col.gxp_highlight) {{
          tr.className = "bg-red-50/50";
        }}

        // Flags HTML
        let flagsHtml = "";
        if (col.primary_key) {{
          flagsHtml += '<span class="inline-block px-1.5 py-0.5 bg-blue-100 text-blue-800 font-bold text-[10px] rounded mr-1">PK</span>';
        }}
        if (!col.nullable) {{
          flagsHtml += '<span class="inline-block px-1.5 py-0.5 bg-slate-100 text-slate-800 font-bold text-[10px] rounded mr-1">REQ</span>';
        }}
        if (col.gxp_highlight) {{
          flagsHtml += '<span class="inline-block px-1.5 py-0.5 bg-red-100 text-red-800 font-bold text-[10px] rounded">GxP</span>';
        }}

        tr.innerHTML = `
          <td class="px-3 py-2 font-mono text-xs text-slate-950 font-medium">${{col.name}}</td>
          <td class="px-3 py-2 font-mono text-xs text-slate-500">${{col.type}}</td>
          <td class="px-3 py-2 text-center font-mono text-xs">${{flagsHtml}}</td>
        `;
        tbody.appendChild(tr);
      }});
    }}

    // Network listeners
    network.on("click", function(params) {{
      if (params.nodes && params.nodes.length > 0) {{
        inspectEntity(params.nodes[0]);
      }} else if (params.edges && params.edges.length > 0) {{
        // We clicked an edge path
        const edgeId = params.edges[0];
        const matchingEdge = schemaData.edges.find(e => "edge_" + e.from + "_" + e.to === edgeId);
        if (matchingEdge) {{
          document.getElementById("inspect-placeholder").classList.add("hidden");
          const details = document.getElementById("inspect-details");
          details.classList.remove("hidden");

          const serviceBadge = document.getElementById("inspect-service-badge");
          serviceBadge.innerText = "Inter-Service Interface Communication Boundary";
          serviceBadge.className = "px-2.5 py-1 rounded-full text-xs font-bold uppercase tracking-wider bg-violet-100 text-violet-800";

          document.getElementById("inspect-table-name").innerText = matchingEdge.label;
          document.getElementById("inspect-class-name").innerText = "Endpoint / Event Channel Pathway";
          document.getElementById("inspect-desc").innerText = matchingEdge.desc + "\\n\\nPath: " + matchingEdge.from.split("_")[1] + " (Service: " + matchingEdge.from.split("_")[0] + ") -> " + matchingEdge.to.split("_")[1] + " (Service: " + matchingEdge.to.split("_")[0] + ")";
          document.getElementById("inspect-gxp-badge").classList.add("hidden");
          document.getElementById("inspect-immutable-badge").classList.add("hidden");
          document.getElementById("inspect-columns-tbody").innerHTML = `
            <tr>
              <td colspan="3" class="px-4 py-6 text-center text-slate-400 text-xs italic bg-slate-50">This connection represents an authorized inter-service transfer interface path under ADR-2164 compliance framework.</td>
            </tr>
          `;
        }}
      }} else {{
        // Reset
        document.getElementById("inspect-placeholder").classList.remove("hidden");
        document.getElementById("inspect-details").classList.add("hidden");
      }}
    }});

    // Live filtering and search logic
    function applyFilters() {{
      const serviceVal = document.getElementById("filter-service").value;
      const gxpVal = document.getElementById("filter-gxp").value;
      const searchVal = document.getElementById("search-tables").value.toLowerCase();

      const visibleNodeIds = [];

      nodesArray.forEach(node => {{
        const table = tableLookup[node.id];
        let visible = true;

        if (serviceVal !== "all" && table.service !== serviceVal) {{
          visible = false;
        }}
        if (gxpVal === "gxp" && !table.gxp) {{
          visible = false;
        }}
        if (gxpVal === "immutable" && !table.immutable) {{
          visible = false;
        }}
        if (searchVal && !table.table_name.toLowerCase().includes(searchVal) && !table.class_name.toLowerCase().includes(searchVal)) {{
          visible = false;
        }}

        if (visible) {{
          visibleNodeIds.push(node.id);
          // Set opacity to 100%
          nodes.update({{ id: node.id, hidden: false }});
        }} else {{
          // Hide node
          nodes.update({{ id: node.id, hidden: true }});
        }}
      }});

      // Show edges only if both ends are visible
      edgesArray.forEach(edge => {{
        const isFromVisible = visibleNodeIds.includes(edge.from);
        const isToVisible = visibleNodeIds.includes(edge.to);
        if (isFromVisible && isToVisible) {{
          edges.update({{ id: edge.id || (edge.from + "_" + edge.to), hidden: false }});
        }} else {{
          edges.update({{ id: edge.id || (edge.from + "_" + edge.to), hidden: true }});
        }}
      }});
    }}

    document.getElementById("filter-service").addEventListener("change", applyFilters);
    document.getElementById("filter-gxp").addEventListener("change", applyFilters);
    document.getElementById("search-tables").addEventListener("input", applyFilters);

  </script>
</body>
</html>
"""


def main():
    """Compiles and generates the complete interactive schema boundary dashboard."""
    print("--- Starting Statically-Compiled Schema Boundaries Extraction ---")

    try:
        # Import Relational Base Classes
        from apps.etmf.infrastructure.models import Base as EtmfBase
        from apps.execution.database.models import Base as ExecutionBase

        print("Imported SQL bases statically under mocked environment keys.")
    except Exception as e:
        print(f"Error: Failed to statically import database models: {e}")
        sys.exit(1)

    # 1. Parse relational metadata
    execution_tables = parse_sqlalchemy_schema(ExecutionBase, "execution")
    print(
        f"  - Extracted {len(execution_tables)} entities from Execution microservice."
    )

    etmf_tables = parse_sqlalchemy_schema(EtmfBase, "etmf")
    print(f"  - Extracted {len(etmf_tables)} entities from eTMF microservice.")

    # 2. Add graph metadata
    designer_nodes = get_designer_schema()
    print(f"  - Extracted {len(designer_nodes)} entities from Designer graph model.")

    # Aggregate into unified services dictionary
    services = {
        "designer": {
            "name": "Designer / MDR",
            "db_type": "Neo4j Graph Database",
            "color": "#6366f1",
            "tables": designer_nodes,
        },
        "execution": {
            "name": "Execution / EDC",
            "db_type": "PostgreSQL (SQLAlchemy)",
            "color": "#0d9488",
            "tables": execution_tables,
        },
        "etmf": {
            "name": "eTMF",
            "db_type": "PostgreSQL (SQLAlchemy)",
            "color": "#d97706",
            "tables": etmf_tables,
        },
    }

    # 3. Define inter-service boundary interfaces / transfer pathways
    edges = [
        {
            "from": "designer_Study",
            "to": "execution_clinical_subjects",
            "label": "REST: Study Protocol USDM Ingestion",
            "desc": (
                "Loads active Study configuration designed dynamically in Designer (Neo4j) "
                "into EDC Subject-level and Trial-conduct structures."
            ),
        },
        {
            "from": "execution_consent_signatures",
            "to": "etmf_tmf_documents",
            "label": "REST: Signed Consent eTMF Filing",
            "desc": (
                "Uploads locked digital consent signatures and PDF structures once "
                "countersigned under FDA Part 11 parameters."
            ),
        },
        {
            "from": "execution_clinical_observations",
            "to": "etmf_tmf_documents",
            "label": "REST: CRF Document Archival",
            "desc": (
                "Pushes archived case report form records and completed clinical document "
                "structures to the compliance master file."
            ),
        },
    ]

    # Render standalone visual document
    html_content = generate_html_visualizer(services, edges)

    # Target files to write
    output_paths = [
        ROOT_DIR / "docs" / "schema_visualizer.html",
        ROOT_DIR / "docs" / "schema" / "index.html",
    ]

    for out_path in output_paths:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(html_content, encoding="utf-8")
        print(f"[SUCCESS] Standalone interactive visual schema written to: {out_path}")


if __name__ == "__main__":
    main()
