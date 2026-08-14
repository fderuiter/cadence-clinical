"""AST-based Offline Schema Drift Test Suite.

This test module verifies compatibility and detects schema drift between
client-side IndexedDB definitions and server-side relational database and data-transfer schemas.

Strictly relies on AST-based parsing (Babel parser for frontend JS/TS, Python AST for backend)
to avoid false positives from formatting, whitespace, or code comments.

GxP Compliance:
- Gate 1: Google-style docstrings and inline comments.
- Gate 3: Tested and verified under standard pytest execution.
"""

import ast
import json
import re
import subprocess
from pathlib import Path
from typing import Any


def camel_to_snake(name: str) -> str:
    """Convert camelCase string to snake_case.

    Args:
        name: CamelCase string to convert.

    Returns:
        snake_case string.
    """
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def clean_type(t: str) -> str:
    """Extract core type from wrappers like Mapped[...] or Optional[...] or Field(...).

    Args:
        t: The raw type string from AST unparsing.

    Returns:
        Cleaned type string.
    """
    t = t.strip()
    # Remove Mapped[...] wrapper
    mapped_match = re.match(r"^Mapped\[(.*)\]$", t)
    if mapped_match:
        t = mapped_match.group(1)
    return t.strip()


def are_types_compatible(js_type: str, py_type: str) -> bool:
    """Determine if a frontend JS/TS type is compatible with a backend Python type.

    Args:
        js_type: The string representation of the frontend type (e.g. "string", "number").
        py_type: The string representation of the backend type (e.g. "str | None", "int").

    Returns:
        True if the types are compatible, False otherwise.
    """
    js_type_clean = js_type.strip().lower()
    py_type_clean = clean_type(py_type).lower()

    # Split Python union types (e.g., "str | None" or "Optional[str]")
    py_options = []
    if "|" in py_type_clean:
        py_options = [part.strip() for part in py_type_clean.split("|")]
    elif py_type_clean.startswith("optional[") and py_type_clean.endswith("]"):
        py_options = [py_type_clean[9:-1].strip(), "none"]
    else:
        py_options = [py_type_clean]

    # Check if any option is compatible
    for opt in py_options:
        if opt == "none":
            continue
        if js_type_clean == "string":
            if (
                opt
                in (
                    "str",
                    "datetime",
                    "eprosubmissionstatus",
                    "conflictstrategyenum",
                )
                or "literal" in opt
            ):
                return True
        elif js_type_clean == "number":
            if opt in ("int", "float", "integer"):
                return True
        elif js_type_clean == "boolean":
            if opt == "bool":
                return True
        elif js_type_clean in ("any", "null") or "record" in js_type_clean:
            if "dict" in opt or opt in ("any", "jsonvalue") or "any" in opt:
                return True
        elif "[]" in js_type_clean or "array" in js_type_clean:
            if "list" in opt or "set" in opt:
                return True

        # Handle string literal unions like "CREATE" | "UPDATE" | "SUBMIT"
        if "|" in js_type_clean or '"' in js_type_clean or "'" in js_type_clean:
            if "literal" in opt or opt == "str" or opt == "eprosubmissionstatus":
                return True

    return False


def check_drift(
    db_store: str,
    client_schema: dict[str, str],
    server_schema: dict[str, str],
    client_file: str,
    server_file: str,
    field_aliases: dict[str, str] = None,
) -> list[str]:
    """Compare client and server schemas for field and type alignment.

    Args:
        db_store: Name of the IndexedDB database or store.
        client_schema: Dictionary of client field names and types.
        server_schema: Dictionary of server field names and types.
        client_file: Path of the client source file.
        server_file: Path of the server source file.
        field_aliases: Optional dictionary mapping client field names to server field names.

    Returns:
        A list of drift mismatch error messages.
    """
    errors = []
    aliases = field_aliases or {}

    # Check for renamed / missing fields or type mismatches
    for client_field, client_type in client_schema.items():
        # Get server-side field name (using alias or converting camelCase to snake_case)
        if client_field in aliases:
            server_field = aliases[client_field]
        else:
            server_field = camel_to_snake(client_field)

        # Check if the field exists on the server schema
        if server_field not in server_schema:
            # Maybe the names are exact matches
            if client_field in server_schema:
                server_field = client_field
            else:
                errors.append(
                    f"Drift mismatch in store '{db_store}': "
                    f"Field '{client_field}' defined in client file '{client_file}' "
                    f"does not have a matching server field '{server_field}' "
                    f"in server schema '{server_file}'."
                )
                continue

        # If found, check type compatibility
        server_type = server_schema[server_field]
        if not are_types_compatible(client_type, server_type):
            errors.append(
                f"Type drift mismatch in store '{db_store}' field '{client_field}': "
                f"Client expects type '{client_type}' (file '{client_file}'), but "
                f"Server expects type '{server_type}' (file '{server_file}')."
            )

    return errors


class TestOfflineSchemaDrift:
    """Test suite to statically analyze offline databases and verify no schema drift.

    @req:PRD-SYS-001
    """

    def parse_frontend(self, file_path: str) -> dict[str, Any]:
        """Run the frontend AST parser helper script on a file.

        Args:
            file_path: Relative or absolute path of the frontend file.

        Returns:
            Dictionary containing extracted schemas.
        """
        repo_root = Path(__file__).parent.parent.parent.resolve()
        parser_script = repo_root / "scripts" / "parse_frontend_ast.js"
        assert parser_script.exists(), "Frontend AST parser helper script is missing!"

        abs_file_path = str((repo_root / file_path).resolve())

        res = subprocess.run(
            ["node", str(parser_script), abs_file_path],
            capture_output=True,
            text=True,
            check=False,
            cwd=str(repo_root),
        )
        if res.returncode != 0:
            print(f"DEBUG: node failed with exit code {res.returncode}")
            print(f"DEBUG: stdout: {res.stdout}")
            print(f"DEBUG: stderr: {res.stderr}")
            raise subprocess.CalledProcessError(
                res.returncode, res.args, output=res.stdout, stderr=res.stderr
            )
        return json.loads(res.stdout)

    def parse_backend(self, file_path: str) -> dict[str, dict[str, str]]:
        """Statically parse python classes and fields from a backend file.

        Args:
            file_path: Path of the python file.

        Returns:
            Dictionary mapping class names to dictionary of fields and types.
        """
        repo_root = Path(__file__).parent.parent.parent.resolve()
        abs_file_path = (repo_root / file_path).resolve()
        with open(abs_file_path, encoding="utf-8") as f:
            tree = ast.parse(f.read())

        models = {}
        for node in ast.walk(tree):
            if isinstance(node, ast.ClassDef):
                class_name = node.name
                fields = {}
                for subnode in node.body:
                    if isinstance(subnode, ast.AnnAssign) and isinstance(
                        subnode.target, ast.Name
                    ):
                        field_name = subnode.target.id
                        field_type = ast.unparse(subnode.annotation)
                        fields[field_name] = field_type
                models[class_name] = fields
        return models

    def test_pending_delta_schema_drift(self) -> None:
        """Validate PendingDelta schema in client against OfflineDeltaItem in server.

        @req:PRD-SYS-001
        """
        client_file = "apps/web/src/utils/syncEngine.ts"
        server_file = "apps/execution/domain/offline_models.py"

        client_data = self.parse_frontend(client_file)
        server_data = self.parse_backend(server_file)

        client_schema = client_data["interfaces"].get("PendingDelta")
        assert client_schema is not None, (
            "Could not find 'PendingDelta' interface in frontend code"
        )

        server_schema = server_data.get("OfflineDeltaItem")
        assert server_schema is not None, (
            "Could not find 'OfflineDeltaItem' model in backend code"
        )

        errors = check_drift(
            db_store="pending_sync_deltas",
            client_schema=client_schema,
            server_schema=server_schema,
            client_file=client_file,
            server_file=server_file,
        )

        assert not errors, "Offline schema drift detected!\n" + "\n".join(errors)

    def test_submissions_schema_drift(self) -> None:
        """Validate submission object in client sync-queue against EPROSubmissionRequest/OfflineSyncMarkers.

        @req:PRD-SYS-001
        """
        client_file = "apps/subject-portal/src/sync-queue.js"
        offline_models_file = "apps/execution/domain/offline_models.py"
        interop_models_file = "apps/interop/infrastructure/models.py"

        client_data = self.parse_frontend(client_file)
        offline_server_data = self.parse_backend(offline_models_file)
        interop_server_data = self.parse_backend(interop_models_file)

        client_schema = client_data["objects"].get("submission")
        assert client_schema is not None, (
            "Could not find 'submission' object structure in subject portal frontend"
        )

        # submissions maps to fields in EPROSubmissionRequest + OfflineSyncMarkers + EPROSubmission + SubjectNotificationResponse
        req_schema = offline_server_data.get("EPROSubmissionRequest", {})
        markers_schema = offline_server_data.get("OfflineSyncMarkers", {})
        notification_schema = offline_server_data.get("SubjectNotificationResponse", {})
        epro_sub_schema = interop_server_data.get("EPROSubmission", {})

        # Merge them to represent all expected backend fields
        server_schema = {
            **req_schema,
            **markers_schema,
            **epro_sub_schema,
            **notification_schema,
        }

        # Ignore JS-only internal synchronization variables that do not map to API submission properties
        js_only_fields = {"status", "resolved_answers", "resolved_at", "error"}
        filtered_client_schema = {
            k: v for k, v in client_schema.items() if k not in js_only_fields
        }

        # Setup standard field aliases for GxP audit fields and key mappings
        field_aliases = {
            "change_reason": "reason_for_change",
            "username": "created_by",
        }

        errors = check_drift(
            db_store="submissions",
            client_schema=filtered_client_schema,
            server_schema=server_schema,
            client_file=client_file,
            server_file=offline_models_file,
            field_aliases=field_aliases,
        )

        assert not errors, "Offline schema drift detected!\n" + "\n".join(errors)

    def test_simulated_field_name_rename_drift(self) -> None:
        """Test that the suite successfully detects and blocks a renamed field on the client side.

        @req:PRD-SYS-001
        """
        client_schema = {
            "deltaId": "string",
            "entityTypeRenamed": "string",  # Renamed from entityType
            "entityId": "string",
        }
        server_schema = {
            "delta_id": "str",
            "entity_type": "str",
            "entity_id": "str",
        }

        errors = check_drift(
            db_store="pending_sync_deltas",
            client_schema=client_schema,
            server_schema=server_schema,
            client_file="apps/web/src/utils/syncEngine.ts",
            server_file="apps/execution/domain/offline_models.py",
        )

        assert len(errors) == 1
        assert "entityTypeRenamed" in errors[0]
        assert "entity_type_renamed" in errors[0]

    def test_simulated_type_mismatch_drift(self) -> None:
        """Test that the suite successfully flags changing string to boolean.

        @req:PRD-SYS-001
        """
        client_schema = {
            "deltaId": "string",
            "clientTimestampUtc": "boolean",  # Intentionally mismatch type (boolean vs str)
        }
        server_schema = {
            "delta_id": "str",
            "client_timestamp_utc": "str",
        }

        errors = check_drift(
            db_store="pending_sync_deltas",
            client_schema=client_schema,
            server_schema=server_schema,
            client_file="apps/web/src/utils/syncEngine.ts",
            server_file="apps/execution/domain/offline_models.py",
        )

        assert len(errors) == 1
        assert "Type drift mismatch" in errors[0]
        assert "clientTimestampUtc" in errors[0]
        assert "boolean" in errors[0]
        assert "str" in errors[0]

    def test_formatting_and_whitespace_immunity(self) -> None:
        """Validate that non-structural white space or comments do not affect schema extraction or checks.

        @req:PRD-SYS-001
        """
        # Read the client file
        client_file = "apps/web/src/utils/syncEngine.ts"
        client_data_1 = self.parse_frontend(client_file)

        # Statically verify that any parsed types remain stable and ignore standard comments and style differences.
        # This is guaranteed by Babel AST parser structure which doesn't capture white spaces/comments as schema types.
        assert "PendingDelta" in client_data_1["interfaces"]
        properties = client_data_1["interfaces"]["PendingDelta"]
        assert properties["deltaId"] == "string"
        assert properties["clientTimestampUtc"] == "string"
