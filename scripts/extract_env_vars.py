#!/usr/bin/env python3
"""
AST Environment Variable Extractor & Drift Guardrail.

Scans Python source files across `apps/`, `packages/`, and `scripts/` using Python AST
static analysis to detect all environment variable accesses (os.getenv, os.environ.get,
os.environ[...], os.environ.setdefault, Pydantic field aliases/validations, and inline comments).

Auto-generates and validates `.env.example` and `docs/ENVIRONMENT_VARIABLES.md`.
"""

import argparse
import ast
import concurrent.futures
import json
import os
import re
import sys
import time
from pathlib import Path
from typing import Any

# Standard domain categories and header mapping
DOMAIN_CATEGORIES = [
    "Global Platform & Environment",
    "Enterprise Branding & Domain Identity",
    "Cryptographic Secrets & Authentication Keys",
    "Microservice REST Endpoints & URLs",
    "Database Connection URLs",
    "Keycloak / OIDC Identity Provider Configuration",
    "Notifications, SMTP & Webhooks",
    "External Clinical Ontologies & Terminology Services",
    "AI Gateway & LLM Ingestion",
    "Document Storage & Archival",
    "Safety & Pharmacovigilance Service",
    "eTMF Master File Service",
    "Interoperability & Data Exchange Service",
    "Clinical Trial Management Service (CTMS)",
    "Quality & CAPA Service",
    "Helpdesk & Tickets Service",
    "eConsent Service",
    "eISF Site File Service",
    "Site Payments Service",
    "Medical Imaging Service",
    "EDC Execution Service",
    "Study Designer Service",
    "API Gateway Service",
    "Operational Microservice Configuration",
]


def classify_domain(var_name: str, file_path: str) -> str:
    """Classifies an environment variable into a functional domain based on name and source path."""
    v = var_name.upper()
    p = file_path.lower().replace("\\", "/")

    if (
        any(k in v for k in ["AI_", "LLM_", "OLLAMA_", "OPENAI_", "GEMINI_"])
        or "ai_gateway" in p
    ):
        return "AI Gateway & LLM Ingestion"
    if any(k in v for k in ["TERMINOLOGY", "NCI", "EVS", "CDISC", "ONTOLOGY", "UMLS"]):
        return "External Clinical Ontologies & Terminology Services"
    if (
        any(k in v for k in ["NEO4J", "DATABASE", "POSTGRES", "PG_", "SQLITE"])
        or "database_url" in p
        or "db_url" in p
    ):
        return "Database Connection URLs"
    if any(k in v for k in ["KEYCLOAK", "JWT", "OIDC", "AUTH", "TOKEN_EXPIRE"]):
        return "Keycloak / OIDC Identity Provider Configuration"
    if any(
        k in v
        for k in ["SMTP", "WEBHOOK", "EMAIL", "NOTIFICATION", "SENDGRID", "SLACK"]
    ):
        return "Notifications, SMTP & Webhooks"
    if any(
        k in v
        for k in ["STORAGE", "S3", "AWS", "AZURE", "BLOB", "BUCKET", "FILE_UPLOAD"]
    ):
        return "Document Storage & Archival"
    if any(
        k in v
        for k in [
            "SECRET",
            "KEY",
            "SALT",
            "HMAC",
            "SIGNING",
            "ENCRYPTION",
            "PASSPHRASE",
            "CREDENTIAL",
        ]
    ):
        return "Cryptographic Secrets & Authentication Keys"
    if any(k in v for k in ["BRAND", "ORGANIZATION", "COMPANY", "TENANT"]):
        return "Enterprise Branding & Domain Identity"
    if any(
        k in v
        for k in [
            "APP_ENV",
            "CADENCE_",
            "DEBUG",
            "LOG_LEVEL",
            "LOG_FORMAT",
            "ENVIRONMENT",
            "TEST_MODE",
        ]
    ):
        return "Global Platform & Environment"
    if (
        v.endswith("_URL")
        or any(k in v for k in ["GATEWAY_URL", "PORT"])
        or "adapters/clients" in p
    ):
        return "Microservice REST Endpoints & URLs"

    # Microservice domain fallbacks
    if "apps/safety" in p or "SAFETY_" in v:
        return "Safety & Pharmacovigilance Service"
    if "apps/interop" in p or "INTEROP_" in v:
        return "Interoperability & Data Exchange Service"
    if "apps/etmf" in p or "ETMF_" in v:
        return "eTMF Master File Service"
    if "apps/ctms" in p or "CTMS_" in v:
        return "Clinical Trial Management Service (CTMS)"
    if "apps/quality" in p or "QUALITY_" in v:
        return "Quality & CAPA Service"
    if "apps/payments" in p or "PAYMENTS_" in v:
        return "Site Payments Service"
    if "apps/imaging" in p or "IMAGING_" in v:
        return "Medical Imaging Service"
    if "apps/econsent" in p or "ECONSENT_" in v:
        return "eConsent Service"
    if "apps/eisf" in p or "EISF_" in v:
        return "eISF Site File Service"
    if "apps/tickets" in p or "TICKETS_" in v:
        return "Helpdesk & Tickets Service"
    if "apps/execution" in p or "EXECUTION_" in v:
        return "EDC Execution Service"
    if "apps/designer" in p or "DESIGNER_" in v:
        return "Study Designer Service"
    if "apps/gateway" in p or "GATEWAY_" in v:
        return "API Gateway Service"

    return "Operational Microservice Configuration"


def is_sensitive_variable(var_name: str) -> bool:
    """Returns True if variable contains sensitive security/credential keywords."""
    v = var_name.upper()
    return any(
        k in v
        for k in [
            "SECRET",
            "KEY",
            "PASSWORD",
            "PASS",
            "TOKEN",
            "CREDENTIAL",
            "SALT",
            "PRIVATE",
            "CERT",
        ]
    )


def sanitize_default_value(var_name: str, default_val: str | None) -> str:
    """Sanitizes default values to ensure sensitive keys use placeholders (Guardrail 2)."""
    if default_val is None:
        return ""

    val_str = str(default_val).strip("'\"")
    if is_sensitive_variable(var_name):
        if (
            val_str
            and not val_str.startswith("dev-")
            and not val_str.startswith("internal-")
            and "placeholder" not in val_str
            and len(val_str) > 16
        ):
            return f"dev-{var_name.lower().replace('_', '-')}-placeholder"
    return val_str


class FastEnvVarVisitor(ast.NodeVisitor):
    """AST NodeVisitor that collects environment variable references and metadata."""

    def __init__(self, rel_path: str, code_lines: list[str]):
        self.rel_path = rel_path
        self.code_lines = code_lines
        self.findings: list[dict[str, Any]] = []

    def _get_preceding_comment(self, lineno: int) -> str:
        """Extracts preceding inline comment if available."""
        if lineno <= 1:
            return ""
        idx = lineno - 2
        comment_lines = []
        while idx >= 0:
            line = self.code_lines[idx].strip()
            if line.startswith("#"):
                comment_lines.insert(0, line.lstrip("#").strip())
                idx -= 1
            else:
                break
        return " ".join(comment_lines)

    def visit_Call(self, node: ast.Call):
        func = node.func
        func_name = ""

        if isinstance(func, ast.Attribute):
            if isinstance(func.value, ast.Name) and func.value.id == "os":
                func_name = f"os.{func.attr}"
            elif (
                isinstance(func.value, ast.Attribute)
                and isinstance(func.value.value, ast.Name)
                and func.value.value.id == "os"
                and func.value.attr == "environ"
            ):
                func_name = f"os.environ.{func.attr}"
        elif isinstance(func, ast.Name):
            func_name = func.id

        if func_name in (
            "os.getenv",
            "os.environ.get",
            "os.environ.setdefault",
            "getenv",
        ):
            if node.args:
                arg0 = node.args[0]
                if isinstance(arg0, ast.Constant) and isinstance(arg0.value, str):
                    var_name = arg0.value
                    default_val = None
                    if len(node.args) > 1:
                        if isinstance(node.args[1], ast.Constant):
                            default_val = node.args[1].value
                        elif isinstance(node.args[1], ast.Name):
                            default_val = node.args[1].id
                    for kw in node.keywords:
                        if kw.arg == "default":
                            if isinstance(kw.value, ast.Constant):
                                default_val = kw.value.value
                            elif isinstance(kw.value, ast.Name):
                                default_val = kw.value.id

                    comment = self._get_preceding_comment(node.lineno)
                    self.findings.append(
                        {
                            "name": var_name,
                            "default": default_val,
                            "location": (self.rel_path, node.lineno),
                            "method": func_name,
                            "comment": comment,
                        }
                    )

        # Pydantic Field(..., alias=...) or validation_alias=... or env=...
        elif (isinstance(func, ast.Name) and func.id == "Field") or (
            isinstance(func, ast.Attribute) and func.attr == "Field"
        ):
            for kw in node.keywords:
                if kw.arg in ("alias", "validation_alias", "env"):
                    if isinstance(kw.value, ast.Constant) and isinstance(
                        kw.value.value, str
                    ):
                        self.findings.append(
                            {
                                "name": kw.value.value,
                                "default": None,
                                "location": (self.rel_path, node.lineno),
                                "method": f"pydantic.Field({kw.arg})",
                                "comment": self._get_preceding_comment(node.lineno),
                            }
                        )

        self.generic_visit(node)

    def visit_Subscript(self, node: ast.Subscript):
        value = node.value
        if (
            isinstance(value, ast.Attribute)
            and isinstance(value.value, ast.Name)
            and value.value.id == "os"
            and value.attr == "environ"
        ):
            if isinstance(node.slice, ast.Constant) and isinstance(
                node.slice.value, str
            ):
                self.findings.append(
                    {
                        "name": node.slice.value,
                        "default": None,
                        "location": (self.rel_path, node.lineno),
                        "method": "os.environ[]",
                        "comment": self._get_preceding_comment(node.lineno),
                    }
                )
        self.generic_visit(node)


def parse_inline_comments(rel_path: str, lines: list[str]) -> list[dict[str, Any]]:
    """Parses inline `# env-var: VARIABLE_NAME[=default_value]` annotations (Guardrail 4)."""
    results = []
    for idx, line in enumerate(lines):
        if "# env-var:" in line or "# env_var:" in line:
            m = re.search(r"#\s*env[-_]var:\s*([A-Z0-9_]+)(?:=(.*))?", line)
            if m:
                var_name = m.group(1).strip()
                def_val = m.group(2).strip() if m.group(2) else None
                results.append(
                    {
                        "name": var_name,
                        "default": def_val,
                        "location": (rel_path, idx + 1),
                        "method": "inline-comment-annotation",
                        "comment": f"Explicit inline annotation in {rel_path}:{idx + 1}",
                    }
                )
    return results


def process_single_file(full_path_str: str, repo_root_str: str) -> list[dict[str, Any]]:
    """Worker function to process a single Python file for env var references."""
    try:
        with open(full_path_str, encoding="utf-8") as f:
            code = f.read()

        # Fast keyword pre-filter before AST parsing
        keywords = ("os.", "getenv", "environ", "env-var", "env_var", "Field", "alias")
        if not any(kw in code for kw in keywords):
            return []

        rel_path = os.path.relpath(full_path_str, repo_root_str).replace("\\", "/")
        code_lines = code.splitlines()

        findings = []
        try:
            tree = ast.parse(code, filename=full_path_str)
            visitor = FastEnvVarVisitor(rel_path, code_lines)
            visitor.visit(tree)
            findings.extend(visitor.findings)
        except Exception:
            pass

        findings.extend(parse_inline_comments(rel_path, code_lines))
        return findings
    except Exception:
        return []


def scan_source_files(repo_root: Path) -> dict[str, dict[str, Any]]:
    """Scans apps/, packages/, scripts/ in parallel for environment variables."""
    target_dirs = ["apps", "packages", "scripts"]
    py_files: list[str] = []

    for t in target_dirs:
        dir_path = repo_root / t
        if not dir_path.exists():
            continue
        for root, dirs, files in os.walk(dir_path):
            # Prune unneeded directories in-place
            dirs[:] = [
                d
                for d in dirs
                if d
                not in (
                    "__pycache__",
                    ".venv",
                    ".pytest_cache",
                    "node_modules",
                    "obj",
                    "bin",
                    ".git",
                )
            ]
            for f in files:
                if f.endswith(".py"):
                    py_files.append(os.path.join(root, f))

    repo_root_str = str(repo_root)
    all_findings: list[dict[str, Any]] = []

    # Process files concurrently
    with concurrent.futures.ProcessPoolExecutor() as executor:
        futures = [
            executor.submit(process_single_file, pf, repo_root_str) for pf in py_files
        ]
        for future in concurrent.futures.as_completed(futures):
            all_findings.extend(future.result())

    # Aggregate findings by variable name
    env_vars: dict[str, dict[str, Any]] = {}
    for f in all_findings:
        name = f["name"]
        if not name or not re.match(r"^[A-Z0-9_]+$", name):
            continue

        rel_path = f["location"][0]
        if name not in env_vars:
            env_vars[name] = {
                "name": name,
                "defaults": set(),
                "locations": [],
                "methods": set(),
                "comments": [],
                "domain": classify_domain(name, rel_path),
            }

        if f["default"] is not None:
            sanitized_def = sanitize_default_value(name, f["default"])
            if sanitized_def != "":
                env_vars[name]["defaults"].add(sanitized_def)

        if f["location"] not in env_vars[name]["locations"]:
            env_vars[name]["locations"].append(f["location"])
        env_vars[name]["methods"].add(f["method"])
        if f["comment"]:
            env_vars[name]["comments"].append(f["comment"])

    # Sort locations deterministically for every variable
    for name in env_vars:
        env_vars[name]["locations"].sort(key=lambda loc: (loc[0], loc[1]))

    return env_vars


def read_existing_env_example(env_example_path: Path) -> dict[str, dict[str, Any]]:
    """Reads existing .env.example to preserve existing header assignments and values."""
    if not env_example_path.exists():
        return {}

    existing: dict[str, dict[str, Any]] = {}
    current_comment = ""
    lines = env_example_path.read_text(encoding="utf-8").splitlines()

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("#"):
            if not stripped.startswith("# ==") and not stripped.startswith("# --"):
                cmt = stripped.lstrip("#").strip()
                if not re.match(r"^\d+\.\s+", cmt):
                    current_comment = cmt
            continue

        if "=" in stripped:
            parts = stripped.split("=", 1)
            var_name = parts[0].strip()
            val_part = parts[1].strip()
            inline_comment = ""

            if "#" in val_part:
                val_code, inline_comment = val_part.split("#", 1)
                val_str = val_code.strip()
                inline_comment = inline_comment.strip()
            else:
                val_str = val_part

            if re.match(r"^[A-Z0-9_]+$", var_name):
                existing[var_name] = {
                    "value": val_str,
                    "comment": inline_comment or current_comment,
                }
            current_comment = ""

    return existing


def generate_env_example_content(
    scanned_vars: dict[str, dict[str, Any]],
    existing_env: dict[str, dict[str, Any]],
) -> str:
    """Generates standardized .env.example content grouped by functional domain headers."""
    all_var_names = set(scanned_vars.keys()) | set(existing_env.keys())

    domain_groups: dict[str, list[str]] = {cat: [] for cat in DOMAIN_CATEGORIES}

    for vname in sorted(all_var_names):
        if vname in scanned_vars:
            domain = scanned_vars[vname]["domain"]
        else:
            domain = classify_domain(vname, "")

        if domain not in domain_groups:
            domain_groups[domain] = []
        domain_groups[domain].append(vname)

    output = [
        "# ==============================================================================",
        "# Cadence Clinical Research Software Platform - Environment Template",
        "# ==============================================================================",
        "# Copy this file to `.env.local` or `.env` for local development.",
        "# For production and staging deployments, supply strong cryptographic secrets.",
        "# ==============================================================================",
        "",
    ]

    section_num = 1
    for domain in DOMAIN_CATEGORIES:
        vars_in_domain = domain_groups.get(domain, [])
        if not vars_in_domain:
            continue

        output.append(
            "# ------------------------------------------------------------------------------"
        )
        output.append(f"# {section_num}. {domain}")
        output.append(
            "# ------------------------------------------------------------------------------"
        )

        for vname in sorted(vars_in_domain):
            val_str = ""
            if vname in existing_env and existing_env[vname]["value"] != "":
                val_str = existing_env[vname]["value"]
            elif vname in scanned_vars and scanned_vars[vname]["defaults"]:
                val_str = sorted(list(scanned_vars[vname]["defaults"]))[0]
            else:
                val_str = sanitize_default_value(vname, None)

            comment_str = ""
            if vname in existing_env and existing_env[vname]["comment"]:
                comment_str = existing_env[vname]["comment"]
            elif vname in scanned_vars and scanned_vars[vname]["comments"]:
                comment_str = scanned_vars[vname]["comments"][0]
            elif vname in scanned_vars and scanned_vars[vname]["locations"]:
                first_loc = scanned_vars[vname]["locations"][0]
                comment_str = f"Source: {first_loc[0]}:{first_loc[1]}"

            if comment_str:
                line = f"{vname}={val_str}".ljust(42) + f" # {comment_str}"
            else:
                line = f"{vname}={val_str}"

            output.append(line)

        output.append("")
        section_num += 1

    return "\n".join(output) + "\n"


def generate_markdown_docs(
    scanned_vars: dict[str, dict[str, Any]],
    existing_env: dict[str, dict[str, Any]],
) -> str:
    """Generates `docs/ENVIRONMENT_VARIABLES.md` operational dictionary."""
    all_var_names = set(scanned_vars.keys()) | set(existing_env.keys())

    domain_groups: dict[str, list[str]] = {cat: [] for cat in DOMAIN_CATEGORIES}

    for vname in sorted(all_var_names):
        if vname in scanned_vars:
            domain = scanned_vars[vname]["domain"]
        else:
            domain = classify_domain(vname, "")

        if domain not in domain_groups:
            domain_groups[domain] = []
        domain_groups[domain].append(vname)

    doc = [
        "# Cadence Platform Operational Environment Variables Dictionary",
        "",
        "This document is automatically generated by `scripts/extract_env_vars.py` using AST static analysis.",
        "It provides a complete reference for all operational environment variables used across Cadence microservices and packages.",
        "",
        f"**Total Operational Variables:** {len(all_var_names)}",
        "",
        "---",
        "",
        "## Table of Contents",
        "",
    ]

    section_num = 1
    for domain in DOMAIN_CATEGORIES:
        vars_in_domain = domain_groups.get(domain, [])
        if vars_in_domain:
            anchor = (
                domain.lower()
                .replace(" ", "-")
                .replace("&", "")
                .replace("/", "")
                .replace("(", "")
                .replace(")", "")
                .replace("--", "-")
            )
            doc.append(
                f"{section_num}. [{domain}](#{anchor}) ({len(vars_in_domain)} variables)"
            )
            section_num += 1

    doc.append("")
    doc.append("---")
    doc.append("")

    for domain in DOMAIN_CATEGORIES:
        vars_in_domain = domain_groups.get(domain, [])
        if not vars_in_domain:
            continue

        doc.append(f"## {domain}")
        doc.append("")
        doc.append(
            "| Variable | Default Value | Required | Description / Comments | Source Code Locations |"
        )
        doc.append("| :--- | :--- | :---: | :--- | :--- |")

        for vname in sorted(vars_in_domain):
            val_str = ""
            if vname in existing_env and existing_env[vname]["value"] != "":
                val_str = existing_env[vname]["value"]
            elif vname in scanned_vars and scanned_vars[vname]["defaults"]:
                val_str = sorted(list(scanned_vars[vname]["defaults"]))[0]

            is_req = "No" if val_str != "" else "Yes"
            val_display = f"`{val_str}`" if val_str != "" else "*(None)*"

            desc = ""
            if (
                vname in existing_env
                and existing_env[vname]["comment"]
                and not existing_env[vname]["comment"].startswith("Source: ")
            ):
                desc = existing_env[vname]["comment"]
            elif vname in scanned_vars and scanned_vars[vname]["comments"]:
                desc = scanned_vars[vname]["comments"][0]
            else:
                desc = f"Operational setting for {domain}."

            loc_str = ""
            if vname in scanned_vars and scanned_vars[vname]["locations"]:
                locs = scanned_vars[vname]["locations"][:3]
                loc_parts = [f"`{loc[0]}:{loc[1]}`" for loc in locs]
                if len(scanned_vars[vname]["locations"]) > 3:
                    loc_parts.append(
                        f"*(+{len(scanned_vars[vname]['locations']) - 3} more)*"
                    )
                loc_str = "<br>".join(loc_parts)
            else:
                loc_str = "*Pre-configured template variable*"

            doc.append(f"| `{vname}` | {val_display} | {is_req} | {desc} | {loc_str} |")

        doc.append("")

    return "\n".join(doc) + "\n"


def main():
    parser = argparse.ArgumentParser(
        description="AST Environment Variable Extractor & Validation Sentinel"
    )
    parser.add_argument(
        "--check", action="store_true", help="Check for drift without modifying files"
    )
    parser.add_argument("--json", action="store_true", help="Output raw JSON summary")
    args = parser.parse_args()

    start_time = time.time()
    repo_root = Path(__file__).resolve().parents[1]

    scanned_vars = scan_source_files(repo_root)
    existing_env = read_existing_env_example(repo_root / ".env.example")

    target_env_content = generate_env_example_content(scanned_vars, existing_env)
    target_docs_content = generate_markdown_docs(scanned_vars, existing_env)

    env_path = repo_root / ".env.example"
    docs_path = repo_root / "docs" / "ENVIRONMENT_VARIABLES.md"

    duration = round(time.time() - start_time, 2)

    if args.json:
        print(
            json.dumps(
                {
                    "scanned_variables_count": len(scanned_vars),
                    "existing_env_count": len(existing_env),
                    "total_combined_variables": len(
                        set(scanned_vars.keys()) | set(existing_env.keys())
                    ),
                    "duration_seconds": duration,
                    "variables": {
                        k: {
                            "domain": v["domain"],
                            "defaults": list(v["defaults"]),
                            "locations_count": len(v["locations"]),
                        }
                        for k, v in scanned_vars.items()
                    },
                },
                indent=2,
            )
        )
        sys.exit(0)

    if args.check:
        errors = []

        missing_in_env = set(scanned_vars.keys()) - set(existing_env.keys())
        if missing_in_env:
            errors.append(
                f"Missing {len(missing_in_env)} variable(s) in .env.example: {', '.join(sorted(missing_in_env))}"
            )

        if env_path.exists():
            current_env = env_path.read_text(encoding="utf-8")
            if current_env != target_env_content:
                errors.append(
                    ".env.example is out of sync with AST extraction output. Run 'python scripts/extract_env_vars.py' to update."
                )
        else:
            errors.append(".env.example file does not exist.")

        if docs_path.exists():
            current_docs = docs_path.read_text(encoding="utf-8")
            if current_docs != target_docs_content:
                errors.append(
                    "docs/ENVIRONMENT_VARIABLES.md is out of sync with AST extraction output. Run 'python scripts/extract_env_vars.py' to update."
                )
        else:
            errors.append("docs/ENVIRONMENT_VARIABLES.md file does not exist.")

        if errors:
            print("❌ Environment Variable Consistency Drift Detected:")
            for err in errors:
                print(f"  - {err}")
            sys.exit(1)
        else:
            print(
                f"✔ Environment variable consistency check passed cleanly across {len(scanned_vars)} AST variables in {duration}s."
            )
            sys.exit(0)

    # Write files
    env_path.write_text(target_env_content, encoding="utf-8")
    docs_path.parent.mkdir(parents=True, exist_ok=True)
    docs_path.write_text(target_docs_content, encoding="utf-8")

    print(
        f"✔ Successfully extracted {len(scanned_vars)} AST environment variables in {duration}s."
    )
    print(f"  - Updated: {env_path}")
    print(f"  - Updated: {docs_path}")


if __name__ == "__main__":
    main()
