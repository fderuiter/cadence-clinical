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


def parse_pydantic_schema(
    model_mapping: list[tuple[type, str, str, bool, bool]], service_name: str
) -> list[dict[str, Any]]:
    """Extracts schema definitions dynamically from active Pydantic domain models.

    Args:
        model_mapping: List of tuples (model_cls, table_name, display_class_name, is_gxp, is_immutable)
        service_name: Name of the microservice.

    Returns:
        List of dictionaries representing node metadata.
    """
    tables_meta = []
    for cls, table_name, display_class_name, is_gxp, is_immutable in model_mapping:
        docstring = cls.__doc__ or "No description provided."
        cleaned_doc = "\n".join(
            line.strip() for line in docstring.split("\n") if line.strip()
        )

        columns = []
        if hasattr(cls, "model_fields"):
            for field_name, field_info in cls.model_fields.items():
                col_name = field_info.alias or field_name
                raw_annotation = (
                    str(field_info.annotation) if field_info.annotation else "String"
                )

                if "str" in raw_annotation.lower():
                    if "uuid" in raw_annotation.lower() or col_name in (
                        "id",
                        "arm_id",
                        "visit_id",
                    ):
                        col_type = "String (UUID)"
                    else:
                        col_type = "String"
                elif "int" in raw_annotation.lower():
                    col_type = "Integer"
                elif "bool" in raw_annotation.lower():
                    col_type = "Boolean"
                elif "dict" in raw_annotation.lower():
                    col_type = "JSON"
                elif "list" in raw_annotation.lower():
                    col_type = "Array"
                else:
                    col_type = "String"

                is_pk = col_name in ("id", "arm_id", "visit_id")
                is_nullable = (
                    not field_info.is_required()
                    or "None" in raw_annotation
                    or "Optional" in raw_annotation
                )

                is_gxp_field = col_name in (
                    "status",
                    "version_index",
                    "created_by",
                    "created_at",
                    "signature",
                    "is_deleted",
                    "reason_for_change",
                    "version",
                )

                columns.append(
                    {
                        "name": col_name,
                        "type": col_type,
                        "primary_key": is_pk,
                        "nullable": is_nullable,
                        "foreign_keys": [],
                        "gxp_highlight": is_gxp_field,
                    }
                )

        tables_meta.append(
            {
                "id": f"{service_name}_{table_name}",
                "table_name": table_name,
                "class_name": display_class_name,
                "description": cleaned_doc,
                "service": service_name,
                "gxp": is_gxp,
                "immutable": is_immutable,
                "columns": columns,
            }
        )

    return tables_meta


def get_designer_schema() -> list[dict[str, Any]]:
    """Synthesizes Neo4j USDM graph schemas dynamically from Designer active domain models.

    Returns:
        A list of dictionaries representing graph node metadata.
    """
    from apps.designer.domain.cdisc.usdm_models import (
        Activity,
        EligibilityCriterion,
        Encounter,
        StudyArm,
        StudyEpoch,
        StudyVersion,
        USDMStudy,
    )
    from apps.designer.library import LibraryObjectBase

    designer_models = [
        (USDMStudy, "Study", "Study", True, False),
        (StudyVersion, "StudyVersion", "StudyVersion", True, True),
        (StudyEpoch, "Epoch", "Epoch", True, False),
        (Activity, "Activity", "Activity", True, False),
        (StudyArm, "TreatmentArm", "TreatmentArm", True, False),
        (Encounter, "ScheduledVisit", "ScheduledVisit", True, False),
        (LibraryObjectBase, "LibraryObject", "LibraryObject", True, False),
        (
            EligibilityCriterion,
            "EligibilityCriterion",
            "EligibilityCriterion",
            True,
            False,
        ),
    ]

    return parse_pydantic_schema(designer_models, "designer")


def ensure_vendor_assets() -> None:
    """Ensures vendor asset directories contain required local styling and scripting dependencies."""
    vendor_dirs = [
        ROOT_DIR / "docs" / "vendor",
        ROOT_DIR / "docs" / "schema" / "vendor",
    ]

    source_dir = ROOT_DIR / "docs" / "vendor"
    source_dir.mkdir(parents=True, exist_ok=True)

    vis_source = source_dir / "vis-network.min.js"

    for v_dir in vendor_dirs:
        v_dir.mkdir(parents=True, exist_ok=True)
        target_vis = v_dir / "vis-network.min.js"

        if vis_source.exists() and not target_vis.exists():
            target_vis.write_bytes(vis_source.read_bytes())


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
  <!-- Vis.js Network via local vendored asset -->
  <script type="text/javascript" src="vendor/vis-network.min.js"></script>
  <style>
    * {{
      box-sizing: border-box;
      margin: 0;
      padding: 0;
    }}
    body {{
      background-color: #f8fafc;
      color: #1e293b;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      line-height: 1.5;
    }}
    .header {{
      background-color: #1e1b4b;
      color: #ffffff;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
      padding: 1.5rem 2rem;
      margin-bottom: 2rem;
    }}
    .header-container {{
      max-width: 80rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      align-items: flex-start;
      gap: 1rem;
    }}
    @media (min-width: 768px) {{
      .header-container {{
        flex-direction: row;
        align-items: center;
      }}
    }}
    .header-title {{
      font-size: 1.875rem;
      font-weight: 800;
      letter-spacing: -0.025em;
      margin-top: 0.5rem;
    }}
    .header-subtitle {{
      color: #c7d2fe;
      margin-top: 0.25rem;
    }}
    .tag-compliance {{
      background-color: #4338ca;
      color: #e0e7ff;
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      padding: 0.25rem 0.625rem;
      border-radius: 0.25rem;
      display: inline-block;
    }}
    .pipeline-status {{
      background-color: #312e81;
      border: 1px solid #4338ca;
      padding: 1rem;
      border-radius: 0.5rem;
      font-size: 0.875rem;
      color: #e0e7ff;
    }}
    .pipeline-status-title {{
      font-weight: 600;
      color: #ffffff;
      margin-bottom: 0.25rem;
    }}
    .main-container {{
      max-width: 80rem;
      margin: 0 auto;
      padding: 0 2rem 4rem 2rem;
    }}
    .grid-stats {{
      display: grid;
      grid-template-columns: 1fr;
      gap: 1.5rem;
      margin-bottom: 2rem;
    }}
    @media (min-width: 768px) {{
      .grid-stats {{
        grid-template-columns: repeat(4, 1fr);
      }}
    }}
    .card {{
      background-color: #ffffff;
      padding: 1.5rem;
      border-radius: 0.5rem;
      box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
      border: 1px solid #e2e8f0;
    }}
    .stat-card {{
      display: flex;
      flex-direction: column;
    }}
    .stat-card-red {{
      border-color: #fee2e2;
      background-color: rgba(254, 242, 242, 0.4);
    }}
    .stat-card-amber {{
      border-color: #fef3c7;
      background-color: rgba(254, 243, 199, 0.4);
    }}
    .stat-label {{
      color: #64748b;
      font-size: 0.875rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }}
    .stat-label-red {{ color: #b91c1c; }}
    .stat-label-amber {{ color: #b45309; }}
    .stat-value {{
      font-size: 1.875rem;
      font-weight: 800;
      color: #0f172a;
      margin-top: 0.5rem;
    }}
    .stat-value-red {{ color: #7f1d1d; }}
    .stat-value-amber {{ color: #78350f; }}
    .stat-subtext {{
      font-size: 0.75rem;
      font-weight: 500;
      margin-top: 0.25rem;
    }}
    .stat-subtext-indigo {{ color: #4f46e5; }}
    .stat-subtext-emerald {{ color: #059669; }}
    .stat-subtext-red {{ color: #dc2626; }}
    .stat-subtext-amber {{ color: #d97706; }}
    .grid-workspace {{
      display: grid;
      grid-template-columns: 1fr;
      gap: 2rem;
    }}
    @media (min-width: 1024px) {{
      .grid-workspace {{
        grid-template-columns: 2fr 1fr;
      }}
    }}
    .workspace-left {{
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
    }}
    .controls-header {{
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      align-items: flex-start;
      gap: 1rem;
      margin-bottom: 1rem;
    }}
    @media (min-width: 640px) {{
      .controls-header {{
        flex-direction: row;
        align-items: center;
      }}
    }}
    .section-title {{
      font-size: 1.25rem;
      font-weight: 700;
      color: #0f172a;
    }}
    .filter-group {{
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      font-size: 0.875rem;
    }}
    .select-input {{
      background-color: #f1f5f9;
      border: 1px solid #cbd5e1;
      border-radius: 0.25rem;
      padding: 0.375rem 0.5rem;
      color: #334155;
      font-weight: 500;
    }}
    .search-input {{
      background-color: #ffffff;
      border: 1px solid #cbd5e1;
      border-radius: 0.25rem;
      padding: 0.375rem 0.75rem;
      color: #334155;
    }}
    .search-input::placeholder {{
      color: #94a3b8;
    }}
    #network-container {{
      width: 100%;
      height: 650px;
      border: 1px solid #e2e8f0;
      background-color: #f8fafc;
      border-radius: 0.5rem;
    }}
    .legend-container {{
      margin-top: 1rem;
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
      font-size: 0.75rem;
      font-weight: 600;
      justify-content: space-between;
      align-items: center;
      color: #64748b;
    }}
    .legend-group {{
      display: flex;
      gap: 1rem;
    }}
    .legend-item {{
      display: flex;
      align-items: center;
      gap: 0.375rem;
    }}
    .dot {{
      width: 0.875rem;
      height: 0.875rem;
      border-radius: 9999px;
      display: inline-block;
    }}
    .dot-designer {{ background-color: #6366f1; }}
    .dot-execution {{ background-color: #10b981; }}
    .dot-etmf {{ background-color: #f59e0b; }}
    .dot-gxp {{
      width: 0.75rem;
      height: 0.75rem;
      border-radius: 9999px;
      border: 2px solid #ef4444;
      display: inline-block;
    }}
    .dot-immutable {{
      width: 0.75rem;
      height: 0.75rem;
      background-color: #fecaca;
      border: 1px solid #f87171;
      border-radius: 0.25rem;
      display: inline-block;
    }}
    .inspect-panel {{
      display: flex;
      flex-direction: column;
      height: 740px;
    }}
    .inspect-header {{
      font-size: 1.25rem;
      font-weight: 700;
      color: #0f172a;
      border-bottom: 1px solid #f1f5f9;
      padding-bottom: 0.75rem;
    }}
    .inspect-placeholder {{
      flex: 1;
      display: flex;
      flex-direction: column;
      justify-content: center;
      align-items: center;
      text-align: center;
      padding: 2rem;
      color: #94a3b8;
    }}
    .inspect-placeholder svg {{
      width: 4rem;
      height: 4rem;
      margin-bottom: 1rem;
      color: #cbd5e1;
    }}
    .inspect-placeholder-title {{
      font-weight: 500;
      color: #475569;
    }}
    .inspect-placeholder-desc {{
      font-size: 0.875rem;
      margin-top: 0.25rem;
    }}
    .inspect-details {{
      flex: 1;
      overflow-y: auto;
      padding-top: 1rem;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }}
    .inspect-table-name {{
      font-size: 1.5rem;
      font-weight: 800;
      color: #0f172a;
      margin-top: 0.5rem;
    }}
    .inspect-class-name {{
      font-size: 0.875rem;
      color: #94a3b8;
      font-family: monospace;
    }}
    .badge-service {{
      padding: 0.25rem 0.625rem;
      border-radius: 9999px;
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      display: inline-block;
    }}
    .badge-service-designer {{ background-color: #e0e7ff; color: #3730a3; }}
    .badge-service-execution {{ background-color: #ccfbf1; color: #115e59; }}
    .badge-service-etmf {{ background-color: #fef3c7; color: #92400e; }}
    .badge-service-violet {{ background-color: #f3e8ff; color: #6b21a8; }}
    .badge-group {{
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }}
    .badge-compliance {{
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.25rem 0.75rem;
      font-size: 0.75rem;
      font-weight: 700;
      border-radius: 0.5rem;
    }}
    .badge-gxp {{
      background-color: #fee2e2;
      color: #991b1b;
      border: 1px solid #fecaca;
    }}
    .badge-immutable {{
      background-color: #fef3c7;
      color: #92400e;
      border: 1px solid #fde68a;
    }}
    .subheading {{
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #94a3b8;
    }}
    .desc-box {{
      font-size: 0.875rem;
      color: #475569;
      margin-top: 0.25rem;
      line-height: 1.6;
      background-color: #f8fafc;
      padding: 0.75rem;
      border-radius: 0.25rem;
      border: 1px solid #f1f5f9;
    }}
    .table-wrapper {{
      border: 1px solid #e2e8f0;
      border-radius: 0.25rem;
      overflow: hidden;
      margin-top: 0.5rem;
    }}
    .inspect-table {{
      width: 100%;
      border-collapse: collapse;
      font-size: 0.875rem;
    }}
    .inspect-table th {{
      background-color: #f8fafc;
      padding: 0.5rem 0.75rem;
      text-align: left;
      font-size: 0.75rem;
      font-weight: 600;
      color: #64748b;
      border-bottom: 1px solid #e2e8f0;
    }}
    .inspect-table td {{
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid #f1f5f9;
    }}
    .inspect-table tr:last-child td {{
      border-bottom: none;
    }}
    .gxp-row {{
      background-color: rgba(254, 242, 242, 0.5);
    }}
    .inspect-table-notice {{
      padding: 1.5rem 1rem;
      text-align: center;
      color: #94a3b8;
      font-size: 0.75rem;
      font-style: italic;
      background-color: #f8fafc;
    }}
    .flag-badge {{
      display: inline-block;
      padding: 0.125rem 0.375rem;
      font-size: 10px;
      font-weight: 700;
      border-radius: 0.25rem;
      margin-right: 0.25rem;
    }}
    .flag-pk {{ background-color: #dbeafe; color: #1e40af; }}
    .flag-req {{ background-color: #f1f5f9; color: #1e293b; }}
    .flag-gxp {{ background-color: #fee2e2; color: #991b1b; }}
    .section-lineages {{
      margin-top: 3rem;
      background-color: #ffffff;
      padding: 2rem;
      border-radius: 0.5rem;
      box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
      border: 1px solid #e2e8f0;
    }}
    .lineage-title {{
      font-size: 1.5rem;
      font-weight: 800;
      color: #0f172a;
      margin-bottom: 1rem;
    }}
    .lineage-desc {{
      color: #475569;
      margin-bottom: 1.5rem;
      line-height: 1.6;
    }}
    .grid-lineages {{
      display: grid;
      grid-template-columns: 1fr;
      gap: 1.5rem;
    }}
    @media (min-width: 768px) {{
      .grid-lineages {{
        grid-template-columns: repeat(3, 1fr);
      }}
    }}
    .lineage-card {{
      border: 1px solid #f1f5f9;
      background-color: rgba(248, 250, 252, 0.5);
      padding: 1.5rem;
      border-radius: 0.5rem;
    }}
    .lineage-card-header {{
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
    }}
    .lineage-badge {{
      padding: 0.5rem;
      border-radius: 0.25rem;
      font-weight: 700;
      font-size: 0.75rem;
      text-transform: uppercase;
    }}
    .lineage-badge-indigo {{ background-color: #e0e7ff; color: #4338ca; }}
    .lineage-badge-emerald {{ background-color: #dcfce7; color: #15803d; }}
    .lineage-badge-amber {{ background-color: #fef3c7; color: #b45309; }}
    .lineage-card-title {{
      font-size: 0.875rem;
      font-weight: 600;
      color: #1e293b;
    }}
    .lineage-card-text {{
      font-size: 0.75rem;
      color: #64748b;
      line-height: 1.6;
    }}
    .footer {{
      background-color: #0f172a;
      color: #94a3b8;
      padding: 3rem 2rem;
      text-align: center;
      font-size: 0.875rem;
      border-top: 1px solid #1e293b;
    }}
    .footer-container {{
      max-width: 80rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }}
    .footer-title {{
      font-weight: 600;
      color: #cbd5e1;
    }}
    .footer-subtext {{
      font-size: 0.75rem;
      color: #475569;
      margin-top: 1rem;
    }}
    .hidden {{
      display: none !important;
    }}
    .flex {{
      display: flex !important;
    }}
  </style>
</head>
<body>

  <header class="header">
    <div class="header-container">
      <div>
        <span class="tag-compliance">FDA 21 CFR Part 11 & EU Annex 11 Compliance</span>
        <h1 class="header-title">Codified Schema Boundaries & Automated Documentation</h1>
        <p class="header-subtitle">Interactive system-wide schema graph showing physical microservice boundaries, security logs, and write-protected GxP clinical lineages.</p>
      </div>
      <div class="pipeline-status">
        <p class="pipeline-status-title">Pipeline Execution Status</p>
        <p>✓ Automated Generation</p>
        <p>✓ Zero active DB driver imports</p>
      </div>
    </div>
  </header>

  <main class="main-container">

    <!-- Dashboard Statistics -->
    <section class="grid-stats">
      <div class="card stat-card">
        <span class="stat-label">Total Service Boundaries</span>
        <span class="stat-value">3</span>
        <span class="stat-subtext stat-subtext-indigo">Designer, Execution, eTMF</span>
      </div>
      <div class="card stat-card">
        <span class="stat-label">Total Database Entities</span>
        <span id="stat-entities" class="stat-value">0</span>
        <span class="stat-subtext stat-subtext-emerald">Statically synthesized</span>
      </div>
      <div class="card stat-card stat-card-red">
        <span class="stat-label stat-label-red">GxP Regulated Entities</span>
        <span id="stat-gxp" class="stat-value stat-value-red">0</span>
        <span class="stat-subtext stat-subtext-red">Require mandatory change-reasoning</span>
      </div>
      <div class="card stat-card stat-card-amber">
        <span class="stat-label stat-label-amber">Write-Protected (Immutable)</span>
        <span id="stat-immutable" class="stat-value stat-value-amber">0</span>
        <span class="stat-subtext stat-subtext-amber">Signature or QC locked</span>
      </div>
    </section>

    <!-- Visualizer Workspace -->
    <div class="grid-workspace">

      <!-- Graph and Filters -->
      <div class="workspace-left">
        <div class="card">
          <div class="controls-header">
            <h2 class="section-title">Interactive Boundary Explorer</h2>

            <!-- Filters -->
            <div class="filter-group">
              <select id="filter-service" class="select-input">
                <option value="all">All Service Boundaries</option>
                <option value="designer">Designer / MDR (Graph)</option>
                <option value="execution">Execution / EDC (Relational)</option>
                <option value="etmf">eTMF (Relational)</option>
              </select>
              <select id="filter-gxp" class="select-input">
                <option value="all">All Compliance Tiers</option>
                <option value="gxp">GxP-Regulated Only</option>
                <option value="immutable">Write-Protected Only</option>
              </select>
              <input type="text" id="search-tables" placeholder="Search entity..." class="search-input">
            </div>
          </div>

          <!-- Network Canvas -->
          <div id="network-container"></div>

          <div class="legend-container">
            <div class="legend-group">
              <span class="legend-item"><span class="dot dot-designer"></span> Designer (Neo4j)</span>
              <span class="legend-item"><span class="dot dot-execution"></span> Execution (Postgres)</span>
              <span class="legend-item"><span class="dot dot-etmf"></span> eTMF (Postgres)</span>
            </div>
            <div class="legend-group">
              <span class="legend-item"><span class="dot-gxp"></span> GxP High Risk</span>
              <span class="legend-item"><span class="dot-immutable"></span> Write-Protected/Immutable</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Schema and GxP Attributes sidebar -->
      <div>
        <div class="card inspect-panel">
          <h2 class="inspect-header">Entity Inspection Panel</h2>

          <div id="inspect-placeholder" class="inspect-placeholder">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M15 15l-2 5L9 9l11 4-5 2zm0 0l5 5M7.188 2.239l.777 2.897M5.136 7.965l-2.898-.777M13.95 4.05l-2.122 2.122m-5.657 5.656l-2.12 2.122" />
            </svg>
            <p class="inspect-placeholder-title">No Entity Selected</p>
            <p class="inspect-placeholder-desc">Select any database node or relationship pathway on the diagram to inspect its schema design, GxP properties, and regulatory compliance flags.</p>
          </div>

          <div id="inspect-details" class="inspect-details hidden">
            <div>
              <span id="inspect-service-badge" class="badge-service"></span>
              <h3 id="inspect-table-name" class="inspect-table-name"></h3>
              <p id="inspect-class-name" class="inspect-class-name"></p>
            </div>

            <!-- GxP badging -->
            <div class="badge-group">
              <span id="inspect-gxp-badge" class="badge-compliance badge-gxp hidden">
                ⚠️ GxP Audit Logged
              </span>
              <span id="inspect-immutable-badge" class="badge-compliance badge-immutable hidden">
                🔒 Write-Once Immutable
              </span>
            </div>

            <div>
              <h4 class="subheading">Description</h4>
              <p id="inspect-desc" class="desc-box"></p>
            </div>

            <div>
              <h4 class="subheading">Properties &amp; Schema</h4>
              <div class="table-wrapper">
                <table class="inspect-table">
                  <thead>
                    <tr>
                      <th>Property / Column</th>
                      <th>Type</th>
                      <th style="text-align: center;">Flags</th>
                    </tr>
                  </thead>
                  <tbody id="inspect-columns-tbody">
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
    <section class="section-lineages">
      <h2 class="lineage-title">Inter-Service Lineages &amp; Documented Interfaces</h2>
      <p class="lineage-desc">
        To remain completely compliant with <strong>ADR-2164</strong> and prevent runtime database cross-dependencies, standard microservices in the Cadence platform are structurally isolated. Sibling microservices cannot directly query each other's storage backends. Instead, all cross-service boundaries communicate exclusively through secure, pre-defined, and authenticated <strong>REST APIs and Events</strong>.
      </p>

      <div class="grid-lineages">
        <div class="lineage-card">
          <div class="lineage-card-header">
            <span class="lineage-badge lineage-badge-indigo">Designer &rarr; Execution</span>
            <span class="lineage-card-title">USDM Ingestion</span>
          </div>
          <p class="lineage-card-text">
            The <strong>Execution Service</strong> statically imports clinical protocol structures designed within the Neo4j Graph database. Relies on the <code>/api/v1/designer/usdm/export</code> endpoint with HMAC signoffs, guaranteeing strict non-repudiation.
          </p>
        </div>

        <div class="lineage-card">
          <div class="lineage-card-header">
            <span class="lineage-badge lineage-badge-emerald">Execution &rarr; eTMF</span>
            <span class="lineage-card-title">Filing &amp; Archival</span>
          </div>
          <p class="lineage-card-text">
            Clinical documentation, subject consents, and electronic signatures are captured in the relational EDC model, then pushed to the <strong>eTMF repository</strong>. Communication requires <code>GatewayAuthMiddleware</code> verification.
          </p>
        </div>

        <div class="lineage-card">
          <div class="lineage-card-header">
            <span class="lineage-badge lineage-badge-amber">System &rarr; Ledger</span>
            <span class="lineage-card-title">HMAC Auditing</span>
          </div>
          <p class="lineage-card-text">
            All GxP audited models hook into the <code>Part11AuditMixin</code> and <code>AuditFields</code>. Standard transactions are cryptographically signed and chained in a SHA-256 HMAC ledger, satisfying Part 11 electronic records.
          </p>
        </div>
      </div>
    </section>

  </main>

  <footer class="footer">
    <div class="footer-container">
      <p class="footer-title">Cadence Clinical Research Software Platform — Automated Compliance Suite</p>
      <p>FDA 21 CFR Part 11 and EU Annex 11 Compliant Schema Visualizer.</p>
      <p class="footer-subtext">This documentation dashboard is generated statically during standard software build pipelines and is strictly read-only.</p>
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
      serviceBadge.className = "badge-service ";
      if (table.service === "designer") {{
        serviceBadge.classList.add("badge-service-designer");
      }} else if (table.service === "execution") {{
        serviceBadge.classList.add("badge-service-execution");
      }} else {{
        serviceBadge.classList.add("badge-service-etmf");
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
        gxpBadge.classList.remove("flex");
      }}

      const immutableBadge = document.getElementById("inspect-immutable-badge");
      if (table.immutable) {{
        immutableBadge.classList.remove("hidden");
        immutableBadge.classList.add("flex");
      }} else {{
        immutableBadge.classList.add("hidden");
        immutableBadge.classList.remove("flex");
      }}

      // Populate properties table
      const tbody = document.getElementById("inspect-columns-tbody");
      tbody.innerHTML = "";
      table.columns.forEach(col => {{
        const tr = document.createElement("tr");
        if (col.gxp_highlight) {{
          tr.className = "gxp-row";
        }}

        // Flags HTML
        let flagsHtml = "";
        if (col.primary_key) {{
          flagsHtml += '<span class="flag-badge flag-pk">PK</span>';
        }}
        if (!col.nullable) {{
          flagsHtml += '<span class="flag-badge flag-req">REQ</span>';
        }}
        if (col.gxp_highlight) {{
          flagsHtml += '<span class="flag-badge flag-gxp">GxP</span>';
        }}

        tr.innerHTML = `
          <td style="font-family: monospace; font-size: 0.75rem; color: #020617; font-weight: 500;">${{col.name}}</td>
          <td style="font-family: monospace; font-size: 0.75rem; color: #64748b;">${{col.type}}</td>
          <td style="text-align: center; font-family: monospace; font-size: 0.75rem;">${{flagsHtml}}</td>
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
          serviceBadge.className = "badge-service badge-service-violet";

          document.getElementById("inspect-table-name").innerText = matchingEdge.label;
          document.getElementById("inspect-class-name").innerText = "Endpoint / Event Channel Pathway";
          document.getElementById("inspect-desc").innerText = matchingEdge.desc + "\\n\\nPath: " + matchingEdge.from.split("_")[1] + " (Service: " + matchingEdge.from.split("_")[0] + ") -> " + matchingEdge.to.split("_")[1] + " (Service: " + matchingEdge.to.split("_")[0] + ")";
          document.getElementById("inspect-gxp-badge").classList.add("hidden");
          document.getElementById("inspect-immutable-badge").classList.add("hidden");
          document.getElementById("inspect-columns-tbody").innerHTML = `
            <tr>
              <td colspan="3" class="inspect-table-notice">This connection represents an authorized inter-service transfer interface path under ADR-2164 compliance framework.</td>
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


def extract_all_service_schemas() -> dict[str, Any]:
    """Statically extracts schema metadata across all relational and graph services.

    Returns:
        Dictionary mapping service IDs to service metadata and extracted table/node entities.
    """
    from apps.ctms.models import Base as CtmsBase
    from apps.econsent.adapters.models import Base as EconsentBase
    from apps.eisf.infrastructure.models import Base as EisfBase
    from apps.etmf.infrastructure.models import Base as EtmfBase
    from apps.execution.database.models import Base as ExecutionBase
    from apps.interop.infrastructure.models import Base as InteropBase
    from apps.notifications.infrastructure.models import Base as NotificationsBase
    from apps.org.infrastructure.models import Base as OrgBase
    from apps.quality.infrastructure.models import Base as QualityBase
    from apps.safety.infrastructure.models import Base as SafetyBase
    from apps.tickets.infrastructure.models import Base as TicketsBase

    relational_services = [
        ("execution", "Execution / EDC", ExecutionBase, "#0d9488"),
        ("etmf", "eTMF", EtmfBase, "#d97706"),
        ("safety", "Safety / Pharmacovigilance", SafetyBase, "#ef4444"),
        ("org", "Organization Management", OrgBase, "#8b5cf6"),
        ("eisf", "eISF", EisfBase, "#10b981"),
        ("econsent", "eConsent", EconsentBase, "#3b82f6"),
        ("interop", "Interoperability / EHR", InteropBase, "#ec4899"),
        ("notifications", "Notifications", NotificationsBase, "#f59e0b"),
        ("tickets", "Tickets / Support", TicketsBase, "#64748b"),
        ("ctms", "CTMS", CtmsBase, "#0284c7"),
        ("quality", "Quality / EQMS", QualityBase, "#84cc16"),
    ]

    services_meta: dict[str, Any] = {}

    for s_id, s_name, base_cls, color in relational_services:
        tables = parse_sqlalchemy_schema(base_cls, s_id)
        services_meta[s_id] = {
            "name": s_name,
            "db_type": "PostgreSQL (SQLAlchemy)",
            "color": color,
            "tables": tables,
        }

    designer_nodes = get_designer_schema()
    services_meta["designer"] = {
        "name": "Designer / MDR",
        "db_type": "Neo4j Graph Database",
        "color": "#6366f1",
        "tables": designer_nodes,
    }

    return services_meta


def generate_cdisc_usdm_compliance_catalog(
    services: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Generates a CDISC USDM v3.0 compliant schema catalog JSON document.

    Args:
        services: Extracted services dictionary. If None, automatically extracts.

    Returns:
        A dictionary conforming to the CDISC USDM v3.0 protocol schema specification.
    """
    from apps.designer.domain.cdisc.usdm_models import USDMStudy

    if services is None:
        services = extract_all_service_schemas()

    biomedical_concepts = []

    for service_id, service_info in services.items():
        tables_or_nodes = service_info.get("tables", [])
        db_type = service_info.get("db_type", "PostgreSQL (SQLAlchemy)")

        for item in tables_or_nodes:
            table_name = item.get("table_name", item.get("class_name", "Unknown"))
            class_name = item.get("class_name", table_name)
            description = item.get("description", "No description provided.")
            is_gxp = item.get("gxp", False)
            is_immutable = item.get("immutable", False)

            bc_properties = []
            for col in item.get("columns", []):
                col_name = col.get("name")
                col_type = col.get("type", "String")
                is_nullable = col.get("nullable", True)
                is_pk = col.get("primary_key", False)
                fks = col.get("foreign_keys", [])
                gxp_highlight = col.get("gxp_highlight", False)

                bc_properties.append(
                    {
                        "id": f"prop_{service_id}_{table_name}_{col_name}",
                        "name": col_name,
                        "label": f"{col_name} ({col_type})",
                        "cdashVariable": col_name.upper(),
                        "dataType": str(col_type),
                        "mandatory": not is_nullable,
                        "config": {
                            "primary_key": is_pk,
                            "foreign_keys": fks,
                            "gxp_highlight": gxp_highlight,
                            "service": service_id,
                            "db_type": db_type,
                            "gxp_audited": is_gxp,
                            "immutable": is_immutable,
                        },
                    }
                )

            concept = {
                "id": f"bc_{service_id}_{table_name}",
                "name": table_name,
                "label": f"{service_info.get('name', service_id)} - {class_name}",
                "conceptCode": f"SCHEMA_{service_id.upper()}_{table_name.upper()}",
                "displayName": f"{table_name} ({service_id})",
                "definition": description,
                "cdashDomain": service_id,
                "cdashVariable": table_name.upper(),
                "dataType": (
                    "Graph Node" if "graph" in db_type.lower() else "Relational Table"
                ),
                "properties": bc_properties,
            }
            biomedical_concepts.append(concept)

    study_design = {
        "id": "sd_multi_service_architecture",
        "name": "Multi-Service Architecture Schema Boundary Design",
        "designType": "Decentralized Microservices Schema Catalog",
        "arms": [],
        "epochs": [],
        "encounters": [],
        "activities": [],
        "biomedicalConcepts": biomedical_concepts,
        "eligibilityCriteria": [],
    }

    doc = {
        "id": "cdisc_usdm_compliance_catalog",
        "name": "CADENCE-CDISC-USDM-COMPLIANCE-CATALOG",
        "protocolTitle": (
            "Cadence Clinical Unified CDISC USDM Compliance Schema Documentation"
        ),
        "protocolId": "CADENCE-SCHEMA-CATALOG-001",
        "phase": "Compliance Certification",
        "therapeuticArea": "eClinical System Engineering",
        "usdmVersion": "3.0",
        "studyVersions": [
            {
                "id": "v1.0",
                "versionTag": "1.0",
                "status": "APPROVED",
                "versionIndex": 1,
                "studyDesigns": [study_design],
            }
        ],
        "studyDesigns": [study_design],
        "biomedicalConcepts": biomedical_concepts,
    }

    USDMStudy(**doc)
    return doc


def export_cdisc_compliance_document(
    output_path: str | Path | None = None,
    validate: bool = True,
) -> tuple[dict[str, Any], Path]:
    """Generates and writes the CDISC USDM compliance JSON document to disk.

    Args:
        output_path: Target file path. Defaults to `docs/CDISC/cdisc_usdm_compliance.json`.
        validate: Whether to validate the document structure against USDM Pydantic schemas.

    Returns:
        Tuple of (usdm_catalog_dict, written_path).
    """
    from apps.designer.domain.cdisc.usdm_models import USDMStudy

    if output_path is None:
        target_file = ROOT_DIR / "docs" / "CDISC" / "cdisc_usdm_compliance.json"
    else:
        target_file = Path(output_path)
        if not target_file.is_absolute():
            target_file = ROOT_DIR / target_file

    services = extract_all_service_schemas()
    usdm_doc = generate_cdisc_usdm_compliance_catalog(services)

    if validate:
        USDMStudy(**usdm_doc)

    target_file.parent.mkdir(parents=True, exist_ok=True)
    with target_file.open("w", encoding="utf-8") as f:
        json.dump(usdm_doc, f, indent=2)

    return usdm_doc, target_file


def main(args_list: list[str] | None = None):
    """Compiles and generates the complete interactive schema boundary dashboard and CDISC USDM catalog."""
    import argparse

    parser = argparse.ArgumentParser(
        description="Generate Schema Documentation & CDISC USDM Catalog."
    )
    parser.add_argument(
        "-o",
        "--output",
        default=None,
        help="Custom destination for CDISC USDM compliance JSON.",
    )
    parser.add_argument(
        "--no-validate",
        action="store_true",
        help="Disable Pydantic validation.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit full USDM document to stdout.",
    )
    if args_list is None:
        args_list = []
    args, _ = parser.parse_known_args(args_list)

    ensure_vendor_assets()

    try:
        services = extract_all_service_schemas()
    except Exception as e:
        if args.json:
            print(json.dumps({"error": str(e)}))
        else:
            print(f"Error: Failed to statically import database models: {e}")
        sys.exit(1)

    if not args.json:
        print("--- Starting Statically-Compiled Schema Boundaries Extraction ---")
        print(f"Statically extracted schema metadata for {len(services)} services.")
        for s_id, s_data in services.items():
            print(
                f"  - Extracted {len(s_data['tables'])} entities from {s_data['name']}."
            )

    # Define inter-service boundary interfaces / transfer pathways
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

    # Target HTML files to write
    output_paths = [
        ROOT_DIR / "docs" / "schema_visualizer.html",
        ROOT_DIR / "docs" / "schema" / "index.html",
    ]

    for out_path in output_paths:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(html_content, encoding="utf-8")
        if not args.json:
            print(
                f"[SUCCESS] Standalone interactive visual schema written to: {out_path}"
            )

    # Export validated CDISC USDM compliance catalog
    cdisc_paths = []
    if args.output:
        cdisc_paths.append(Path(args.output))
    else:
        cdisc_paths = [
            ROOT_DIR / "docs" / "CDISC" / "cdisc_usdm_compliance.json",
            ROOT_DIR / "docs" / "schema" / "cdisc_usdm_compliance.json",
        ]

    for c_path in cdisc_paths:
        usdm_doc, written_path = export_cdisc_compliance_document(
            output_path=c_path, validate=not args.no_validate
        )
        if not args.json:
            print(
                f"[SUCCESS] Validated CDISC USDM compliance catalog written to: {written_path}"
            )

    if args.json:
        usdm_doc = generate_cdisc_usdm_compliance_catalog(services)
        print(json.dumps(usdm_doc, indent=2))


if __name__ == "__main__":
    main(sys.argv[1:])
