"""Main CLI application entrypoint for Cadence Clinical."""

import typer

from packages.cli.commands.cdisc import cdisc_app
from packages.cli.commands.check import check_app
from packages.cli.commands.db import db_app
from packages.cli.commands.dev import dev_app
from packages.cli.commands.doctor import doctor_app
from packages.cli.commands.fix import fix_app
from packages.cli.commands.gxp import gxp_app
from packages.cli.commands.scaffold import scaffold_app
from packages.cli.commands.test import test_app
from packages.cli.mcp.server import CadenceMcpServer

app = typer.Typer(
    name="cadence",
    help="Cadence Clinical — Developer Experience & Unified Platform CLI",
    add_completion=False,
    no_args_is_help=True,
)

mcp_app = typer.Typer(
    help="Run native JSON-RPC Model Context Protocol (MCP) Stdio server for AI agents."
)


@mcp_app.callback(invoke_without_command=True)
def run_mcp_server() -> None:
    """Starts the Cadence Clinical MCP stdio server."""
    server = CadenceMcpServer()
    server.run_stdio()


@app.callback()
def main_callback(
    ctx: typer.Context,
    json_output: bool = typer.Option(
        False,
        "--json",
        "-j",
        help="Emit output in structured, machine-readable JSON for agents",
    ),
    verbose: bool = typer.Option(
        False,
        "--verbose",
        "-v",
        help="Enable verbose output",
    ),
) -> None:
    """Cadence Clinical CLI context configuration."""
    ctx.ensure_object(dict)
    ctx.obj["json"] = json_output
    ctx.obj["verbose"] = verbose


# Register all subcommands
app.add_typer(doctor_app, name="doctor")
app.add_typer(dev_app, name="dev")
app.add_typer(test_app, name="test")
app.add_typer(check_app, name="check")
app.add_typer(fix_app, name="fix")
app.add_typer(db_app, name="db")
app.add_typer(scaffold_app, name="scaffold")
app.add_typer(gxp_app, name="gxp")
app.add_typer(cdisc_app, name="cdisc")
app.add_typer(mcp_app, name="mcp")


def main() -> None:
    """CLI binary execution entrypoint."""
    app()


if __name__ == "__main__":
    main()
