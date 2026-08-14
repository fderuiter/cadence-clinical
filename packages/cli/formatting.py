"""Rich formatting and Agent DX output helpers for the Cadence CLI."""

import json
from typing import Any

from rich.console import Console
from rich.panel import Panel
from rich.table import Table

console = Console()
error_console = Console(stderr=True)


def is_json_mode(ctx_obj: dict[str, Any] | None) -> bool:
    """Returns True if JSON output mode is active."""
    return bool(ctx_obj and ctx_obj.get("json", False))


def output_json(data: dict[str, Any] | list[Any]) -> None:
    """Prints formatted JSON to stdout for agent consumption."""
    print(json.dumps(data, indent=2, default=str))


def print_header(title: str, subtitle: str | None = None) -> None:
    """Prints a styled banner for interactive terminal sessions."""
    text = f"[bold cyan]{title}[/bold cyan]"
    if subtitle:
        text += f"\n[dim]{subtitle}[/dim]"
    console.print(Panel(text, border_style="cyan", padding=(0, 2)))


def print_success(message: str) -> None:
    """Prints a green success message."""
    console.print(f"[bold green]✔[/bold green] {message}")


def print_error(message: str) -> None:
    """Prints a red error message to stderr."""
    error_console.print(f"[bold red]✘[/bold red] {message}")


def print_warning(message: str) -> None:
    """Prints a yellow warning message."""
    console.print(f"[bold yellow]▲[/bold yellow] {message}")


def print_info(message: str) -> None:
    """Prints a blue info message."""
    console.print(f"[bold blue]ℹ[/bold blue] {message}")


def create_table(title: str, columns: list[tuple[str, str]]) -> Table:
    """Creates a Rich table with title and typed columns."""
    table = Table(title=title, border_style="dim", header_style="bold cyan")
    for col_name, col_style in columns:
        table.add_column(col_name, style=col_style)
    return table
