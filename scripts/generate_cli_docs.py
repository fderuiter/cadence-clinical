#!/usr/bin/env python3
"""Programmatic CLI documentation generator for Cadence Clinical.

Introspects the root Typer application in packages/cli/main.py and
renders VitePress-formatted markdown documentation in docs/cli.md.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

import click
import typer

from packages.cli.main import app
from packages.cli.mcp.server import CadenceMcpServer


def clean_help_text(help_text: str | None) -> str:
    """Cleans up docstrings and help strings, removing Google-style Args/Raises blocks."""
    if not help_text:
        return ""
    lines = help_text.strip().split("\n")
    cleaned_lines = []
    for line in lines:
        stripped = line.strip()
        if stripped in ("Args:", "Raises:", "Returns:"):
            break
        cleaned_lines.append(line)
    return "\n".join(cleaned_lines).strip()


def format_param_opts(param: click.Parameter) -> str:
    """Formats parameter option flags or argument names."""
    opts = getattr(param, "opts", [])
    secondary_opts = getattr(param, "secondary_opts", [])
    all_opts = list(opts) + list(secondary_opts)

    if all_opts and all_opts[0].startswith("-"):
        return ", ".join(f"`{o}`" for o in all_opts)
    return f"`<{param.name}>`"


def format_param_default(param: click.Parameter) -> str:
    """Formats default value for parameter."""
    default = param.default
    if default is None or default is Ellipsis or str(default) == "...":
        return "-"
    if isinstance(default, bool):
        return f"`{str(default).lower()}`"
    if isinstance(default, str) and not default:
        return '`""`'
    return f"`{default}`"


def format_param_type(param: click.Parameter) -> str:
    """Formats type description for parameter."""
    if isinstance(param, click.Option) and param.is_flag:
        return "flag"
    if hasattr(param.type, "name"):
        return param.type.name
    return "string"


USAGE_EXAMPLES: dict[str, list[str]] = {
    "doctor": [
        "cadence doctor",
        "cadence doctor --auto-fix",
        "cadence doctor --json",
    ],
    "dev": [
        "cadence dev",
        "cadence dev --service designer",
        "cadence dev --tui",
        "cadence dev --host 0.0.0.0 --no-reload",
    ],
    "test": [
        "cadence test",
        "cadence test --service execution",
        "cadence test --unit",
        "cadence test --frontend",
        "cadence test --watch",
        "cadence test --fast",
        "cadence test --failed-first",
    ],
    "check": [
        "cadence check",
        "cadence check --gate ruff-lint",
        "cadence check --sequential",
    ],
    "fix": [
        "cadence fix",
        "cadence fix --all",
        "cadence fix --sync-gxp",
    ],
    "db": [
        "cadence db reset",
        "cadence db migrate",
        "cadence db seed --scenario CADENCE-101 --tier full",
        "cadence db snapshot baseline-clean",
        "cadence db restore baseline-clean",
        "cadence db status",
    ],
    "scaffold": [
        "cadence scaffold service analytics",
        'cadence scaffold adr "Use Event Driven Architecture" --domain core-platform --req PRD-SYS-001',
    ],
    "setup": [
        "cadence setup",
        "cadence setup credentials --dev",
    ],
    "record": [
        "cadence record run --suite designer",
        "cadence record list",
        "cadence record setup-gdrive --credentials-path credentials.json",
    ],
    "gxp": [
        "cadence gxp sync",
        "cadence gxp validate",
        "cadence gxp verify --path docs/SDLC",
        "cadence gxp export-cdisc --output docs/CDISC/cdisc_usdm_compliance.json",
    ],
    "cdisc": [
        "cadence cdisc export --output docs/CDISC/cdisc_usdm_compliance.json",
    ],
    "mcp": [
        "cadence mcp",
    ],
}


def generate_markdown() -> str:
    """Introspects Typer application and builds VitePress markdown CLI documentation."""
    click_app = typer.main.get_command(app)

    md = []
    md.append("# Cadence Unified Developer CLI Reference\n")
    md.append(
        "The `cadence` CLI (`packages/cli`) provides a unified developer and AI agent interface "
        "for local microservice orchestration, quality sentinel validation, GxP compliance synchronization, "
        "multi-engine database management, code scaffolding, and Model Context Protocol (MCP) integrations.\n"
    )

    # Global options section
    md.append("## Global Options\n")
    md.append("All `cadence` commands support the following global flags:\n")
    md.append("| Option | Short | Description | Default |")
    md.append("| --- | --- | --- | --- |")
    md.append(
        "| `--json` | `-j` | Emit output in structured, machine-readable JSON for agents | `false` |"
    )
    md.append("| `--verbose` | `-v` | Enable verbose output | `false` |\n")

    md.append("```bash")
    md.append("cadence --json <command>")
    md.append("cadence --verbose <command>")
    md.append("```\n")

    md.append("## Command Reference\n")

    commands = click_app.commands

    for name, cmd in commands.items():
        md.append(f"### `cadence {name}`\n")
        help_text = clean_help_text(cmd.help)
        if help_text:
            md.append(f"{help_text}\n")

        # Subcommands or top-level options
        subcmds = getattr(cmd, "commands", {})
        if subcmds:
            # Check if root command has top-level params (like setup)
            params = [p for p in cmd.params if not p.name.startswith("ctx")]
            if params:
                md.append("#### Options & Arguments\n")
                md.append("| Option / Argument | Type | Description | Default |")
                md.append("| --- | --- | --- | --- |")
                for p in params:
                    opts = format_param_opts(p)
                    ptype = format_param_type(p)
                    phelp = clean_help_text(p.help or "") or "-"
                    pdefault = format_param_default(p)
                    md.append(f"| {opts} | {ptype} | {phelp} | {pdefault} |")
                md.append("")

            md.append("#### Subcommands\n")
            for sub_name, sub_cmd in subcmds.items():
                sub_help = clean_help_text(sub_cmd.help)
                md.append(f"##### `cadence {name} {sub_name}`\n")
                if sub_help:
                    md.append(f"{sub_help}\n")

                params = [p for p in sub_cmd.params if not p.name.startswith("ctx")]
                if params:
                    md.append("| Option / Argument | Type | Description | Default |")
                    md.append("| --- | --- | --- | --- |")
                    for p in params:
                        opts = format_param_opts(p)
                        ptype = format_param_type(p)
                        phelp = clean_help_text(p.help or "") or "-"
                        pdefault = format_param_default(p)
                        md.append(f"| {opts} | {ptype} | {phelp} | {pdefault} |")
                    md.append("")

        else:
            params = [p for p in cmd.params if not p.name.startswith("ctx")]
            if params:
                md.append("#### Options & Arguments\n")
                md.append("| Option / Argument | Type | Description | Default |")
                md.append("| --- | --- | --- | --- |")
                for p in params:
                    opts = format_param_opts(p)
                    ptype = format_param_type(p)
                    phelp = clean_help_text(p.help or "") or "-"
                    pdefault = format_param_default(p)
                    md.append(f"| {opts} | {ptype} | {phelp} | {pdefault} |")
                md.append("")

        # MCP specific documentation
        if name == "mcp":
            md.append("#### Native JSON-RPC MCP Tools\n")
            md.append(
                "The `mcp` command launches the `CadenceMcpServer` over stdio using the JSON-RPC 2.0 protocol "
                "(Model Context Protocol specification). It exposes the following 11 native MCP tools:\n"
            )

            for tool in CadenceMcpServer.TOOLS:
                t_name = tool["name"]
                t_desc = tool["description"]
                md.append(f"##### `{t_name}`\n")
                md.append(f"{t_desc}\n")
                props = tool.get("inputSchema", {}).get("properties", {})
                req_fields = tool.get("inputSchema", {}).get("required", [])
                if props:
                    md.append(
                        "| Input Parameter | Type | Required | Description | Default |"
                    )
                    md.append("| --- | --- | --- | --- | --- |")
                    for prop_name, prop_info in props.items():
                        p_type = prop_info.get("type", "string")
                        p_req = "`yes`" if prop_name in req_fields else "`no`"
                        p_desc = prop_info.get("description", "-")
                        p_def = (
                            f"`{prop_info['default']}`"
                            if "default" in prop_info
                            else "-"
                        )
                        md.append(
                            f"| `{prop_name}` | {p_type} | {p_req} | {p_desc} | {p_def} |"
                        )
                    md.append("")

            md.append("##### JSON-RPC Stdio Protocol Specification\n")
            md.append(
                "The MCP server reads JSON-RPC 2.0 requests from `sys.stdin` and writes JSON-RPC 2.0 responses to `sys.stdout`. "
                "Supported JSON-RPC methods:\n"
                "- `initialize`: Handshakes and exchanges capabilities and server metadata.\n"
                "- `tools/list`: Returns array of available tools and input schemas.\n"
                "- `tools/call`: Executes specified tool with input arguments and returns JSON output envelope.\n"
            )

        # Examples
        if name in USAGE_EXAMPLES:
            md.append("#### Usage Examples\n")
            md.append("```bash")
            for ex in USAGE_EXAMPLES[name]:
                md.append(ex)
            md.append("```\n")

    return "\n".join(md)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate docs/cli.md reference documentation from Typer application."
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=REPO_ROOT / "docs" / "cli.md",
        help="Path to output markdown reference file.",
    )
    parser.add_argument(
        "--check",
        "--validate",
        action="store_true",
        help="Check whether current checked-in reference file matches generated docs.",
    )

    args = parser.parse_args()
    content = generate_markdown()

    out_path: Path = args.output
    if not out_path.is_absolute():
        out_path = REPO_ROOT / out_path

    if args.check:
        if not out_path.exists():
            print(f"Error: Target documentation file {out_path} does not exist.")
            sys.exit(1)

        existing_content = out_path.read_text(encoding="utf-8")
        if existing_content.strip() != content.strip():
            print(f"CLI documentation drift detected in {out_path}!")
            print(
                "Run 'uv run cadence fix' or 'python scripts/generate_cli_docs.py' to synchronize."
            )
            sys.exit(1)

        print(
            f"✔ CLI documentation at {out_path.relative_to(REPO_ROOT)} is up-to-date."
        )
        sys.exit(0)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(content, encoding="utf-8")
    print(
        f"✔ Successfully generated CLI reference documentation at {out_path.relative_to(REPO_ROOT)}"
    )


if __name__ == "__main__":
    main()
