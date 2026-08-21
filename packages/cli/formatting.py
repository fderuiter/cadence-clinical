"""Rich formatting and Agent DX output helpers for the Cadence CLI.

Provides authored TerminalDocument rendering, agent-first JSON streaming,
and clickable file link helpers.
"""

import io
import json
import sys
from pathlib import Path
from typing import Any, Self

from rich.console import Console
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

console = Console()
error_console = Console(stderr=True)


def is_json_mode(ctx_obj: dict[str, Any] | None) -> bool:
    """Returns True if JSON output mode is active."""
    return bool(ctx_obj and ctx_obj.get("json", False))


def output_json(data: dict[str, Any] | list[Any]) -> None:
    """Prints formatted JSON to stdout for agent consumption."""
    print(json.dumps(data, indent=2, default=str))


def format_file_link(file_path: str | Path, line: int | None = None) -> str:
    """Formats a path into a clickable markdown file:// URI link."""
    path_obj = Path(file_path)
    file_name = path_obj.name
    abs_path = str(file_path)
    if line is not None:
        return f"[{file_name}:L{line}](file://{abs_path}#L{line})"
    return f"[{file_name}](file://{abs_path})"


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


class TerminalDocument:
    """Authored terminal document builder providing visual hierarchy, metric badges,

    aligned sections, and automatic JSON fallback for non-TTY/agent execution.
    """

    def __init__(self, title: str, subtitle: str | None = None) -> None:
        self.title = title
        self.subtitle = subtitle
        self.metrics: list[dict[str, Any]] = []
        self.key_values: dict[str, Any] = {}
        self.items: list[dict[str, Any]] = []
        self.tables: list[dict[str, Any]] = []
        self.cta: str | None = None
        self.remediation: str | None = None
        self.zoom_token: str | None = None

    def add_metric(self, label: str, value: Any, style: str = "cyan") -> Self:
        """Adds a summary metric badge (e.g. 10 Passed, 0 Failed, 1.2s)."""
        self.metrics.append({"label": label, "value": value, "style": style})
        return self

    def add_key_value(self, key: str, value: Any) -> Self:
        """Adds an aligned key-value pair."""
        self.key_values[key] = str(value)
        return self

    def add_item(self, name: str, status: str = "pass", detail: str = "") -> Self:
        """Adds a checklist item with a status indicator (pass, fail, warn, info)."""
        self.items.append({"name": name, "status": status, "detail": detail})
        return self

    def add_table_data(
        self, title: str, columns: list[tuple[str, str]], rows: list[list[Any]]
    ) -> Self:
        """Adds a structured data table to the document."""
        self.tables.append({"title": title, "columns": columns, "rows": rows})
        return self

    def set_cta(self, cta: str) -> Self:
        """Sets a contextual Next-Action Call To Action at the document footer."""
        self.cta = cta
        return self

    def set_remediation(self, remediation: str) -> Self:
        """Sets an actionable machine-executable remediation command for agents."""
        self.remediation = remediation
        return self

    def set_zoom_token(self, zoom_token: str) -> Self:
        """Sets a progressive disclosure zoom token for detailed inspection."""
        self.zoom_token = zoom_token
        return self

    def to_dict(self) -> dict[str, Any]:
        """Produces a structured dictionary representation of the document."""
        return {
            "title": self.title,
            "subtitle": self.subtitle,
            "metrics": self.metrics,
            "key_values": self.key_values,
            "items": self.items,
            "tables": self.tables,
            "cta": self.cta,
            "remediation": self.remediation,
            "zoom_token": self.zoom_token,
        }

    def to_json(self) -> str:
        """Serializes the document to structured JSON."""
        return json.dumps(self.to_dict(), indent=2, default=str)

    def render_text(self) -> str:
        """Renders the styled Rich terminal document to a string."""
        buf = io.StringIO()
        target_console = Console(file=buf, force_terminal=False, color_system=None)

        # 1. Header
        header_text = f"=== {self.title} ===\n"
        if self.subtitle:
            header_text += f"{self.subtitle}\n"
        buf.write(header_text)

        # 2. Metrics Bar
        if self.metrics:
            metrics_str = " | ".join(
                f"[{m['label']}: {m['value']}]" for m in self.metrics
            )
            buf.write(f"\n{metrics_str}\n\n")

        # 3. Key-Values
        if self.key_values:
            max_k_len = max(len(k) for k in self.key_values)
            for k, v in self.key_values.items():
                buf.write(f"  {k.ljust(max_k_len)} : {v}\n")
            buf.write("\n")

        # 4. Items
        if self.items:
            for item in self.items:
                stat = item["status"].lower()
                icon = (
                    "✔"
                    if stat == "pass"
                    else ("✘" if stat == "fail" else ("▲" if stat == "warn" else "ℹ"))
                )
                detail_str = f" - {item['detail']}" if item.get("detail") else ""
                buf.write(f"  {icon} {item['name']}{detail_str}\n")
            buf.write("\n")

        # 5. Tables
        for tbl_data in self.tables:
            table = create_table(tbl_data["title"], tbl_data["columns"])
            for row in tbl_data["rows"]:
                table.add_row(*[str(cell) for cell in row])
            target_console.print(table)

        # 6. Next Action CTA
        if self.cta:
            buf.write(f"\nNext Action: {self.cta}\n")
        if self.remediation:
            buf.write(f"Remediation: {self.remediation}\n")
        if self.zoom_token:
            buf.write(f"Zoom Token: {self.zoom_token}\n")

        return buf.getvalue()

    def display(
        self, console_instance: Console | None = None, force_json: bool = False
    ) -> None:
        """Displays the document to stdout (styled Rich if TTY, or JSON if requested/piped)."""
        if force_json or not sys.stdout.isatty():
            print(self.to_json())
            return

        target = console_instance or console

        # Render Header Panel
        header_content = Text(self.title, style="bold cyan")
        if self.subtitle:
            header_content.append(f"\n{self.subtitle}", style="dim")
        target.print(Panel(header_content, border_style="cyan", padding=(0, 2)))

        # Render Metrics
        if self.metrics:
            metrics_table = Table.grid(padding=(0, 2))
            for _ in self.metrics:
                metrics_table.add_column()
            badge_cells = [
                Panel(
                    f"[bold {m['style']}]{m['value']}[/bold {m['style']}]\n[dim]{m['label']}[/dim]",
                    border_style=m["style"],
                    padding=(0, 1),
                )
                for m in self.metrics
            ]
            metrics_table.add_row(*badge_cells)
            target.print(metrics_table)
            target.print()

        # Render Key-Values
        if self.key_values:
            kv_table = Table.grid(padding=(0, 2))
            kv_table.add_column(style="bold dim")
            kv_table.add_column(style="cyan")
            for k, v in self.key_values.items():
                kv_table.add_row(f"{k}:", v)
            target.print(kv_table)
            target.print()

        # Render Items
        if self.items:
            for item in self.items:
                stat = item["status"].lower()
                if stat == "pass":
                    target.print(
                        f"[bold green]✔[/bold green] {item['name']} [dim]{item.get('detail', '')}[/dim]"
                    )
                elif stat == "fail":
                    target.print(
                        f"[bold red]✘[/bold red] {item['name']} [dim]{item.get('detail', '')}[/dim]"
                    )
                elif stat == "warn":
                    target.print(
                        f"[bold yellow]▲[/bold yellow] {item['name']} [dim]{item.get('detail', '')}[/dim]"
                    )
                else:
                    target.print(
                        f"[bold blue]ℹ[/bold blue] {item['name']} [dim]{item.get('detail', '')}[/dim]"
                    )
            target.print()

        # Render Tables
        for tbl_data in self.tables:
            table = create_table(tbl_data["title"], tbl_data["columns"])
            for row in tbl_data["rows"]:
                table.add_row(*[str(cell) for cell in row])
            target.print(table)

        # Render Next Action CTA & Remediation
        if self.cta:
            target.print(
                Panel(
                    f"[bold yellow]▶ Next Action:[/bold yellow] {self.cta}",
                    border_style="yellow",
                    padding=(0, 1),
                )
            )
        if self.remediation:
            target.print(
                Panel(
                    f"[bold magenta]⚡ Auto-Remediation:[/bold magenta] [code]{self.remediation}[/code]",
                    border_style="magenta",
                    padding=(0, 1),
                )
            )
        if self.zoom_token:
            target.print(f"[dim]Zoom Token: {self.zoom_token}[/dim]")
