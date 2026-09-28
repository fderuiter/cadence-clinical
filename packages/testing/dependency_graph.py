"""Static AST Test Dependency Graph for Cadence Clinical.

Parses Python source file imports to construct an AST dependency DAG, allowing
sub-second resolution of transitive test suites affected by source file changes.

Requirements: PRD-SYS-049, ADR-2190
"""

from __future__ import annotations

import ast
import json
import os
from pathlib import Path


class TestDependencyGraph:
    """Static AST reverse dependency graph for fast, deterministic test targeting."""

    __test__ = False

    def __init__(
        self,
        repo_root: Path | None = None,
        cache_file: Path | None = None,
    ) -> None:
        self.repo_root = repo_root or Path(__file__).resolve().parents[2]
        self.cache_file = cache_file or (
            self.repo_root / ".cadence" / "test_graph.json"
        )
        self.forward_graph: dict[str, list[str]] = {}
        self.reverse_graph: dict[str, list[str]] = {}
        self.file_mtimes: dict[str, float] = {}

    def _normalize_rel_path(self, path: Path | str) -> str:
        """Normalizes path relative to repository root."""
        p = Path(path)
        if p.is_absolute():
            try:
                return str(p.relative_to(self.repo_root))
            except ValueError:
                return str(p)
        return str(p)

    def _module_to_file(self, module_name: str, current_file: Path) -> str | None:
        """Resolves a Python module import string to a relative file path."""
        parts = module_name.split(".")

        # 1. Check relative to repo root (e.g. apps.execution.models)
        candidate = self.repo_root.joinpath(*parts)
        if candidate.with_suffix(".py").exists():
            return str(candidate.with_suffix(".py").relative_to(self.repo_root))
        if (candidate / "__init__.py").exists():
            return str((candidate / "__init__.py").relative_to(self.repo_root))

        # 2. Check relative to current file directory (relative imports)
        candidate_rel = current_file.parent.joinpath(*parts)
        if candidate_rel.with_suffix(".py").exists():
            return str(candidate_rel.with_suffix(".py").relative_to(self.repo_root))
        if (candidate_rel / "__init__.py").exists():
            return str((candidate_rel / "__init__.py").relative_to(self.repo_root))

        return None

    def _parse_file_imports(self, file_path: Path) -> set[str]:
        """Extracts imported module file paths from a single Python file using AST."""
        imports: set[str] = set()
        try:
            content = file_path.read_text(encoding="utf-8")
            tree = ast.parse(content, filename=str(file_path))
        except Exception:
            return imports

        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                for alias in node.names:
                    target = self._module_to_file(alias.name, file_path)
                    if target:
                        imports.add(target)
            elif isinstance(node, ast.ImportFrom):
                if node.module:
                    # e.g. from apps.execution.models import ClinicalObservation
                    target = self._module_to_file(node.module, file_path)
                    if target:
                        imports.add(target)
                elif node.level and node.level > 0:
                    # relative import e.g. from . import models
                    parent = file_path.parent
                    for _ in range(node.level - 1):
                        parent = parent.parent
                    target = self._module_to_file("", parent)
                    if target:
                        imports.add(target)

        return imports

    def scan_files(self) -> list[Path]:
        """Finds all Python source and test files across the repository."""
        target_dirs = ["apps", "packages", "scripts", "tests"]
        found: list[Path] = []
        for d in target_dirs:
            p = self.repo_root / d
            if p.exists():
                for root, _, files in os.walk(p):
                    for f in files:
                        if f.endswith(".py"):
                            found.append(Path(root) / f)
        return found

    def build(self, force: bool = False) -> None:
        """Constructs the forward and reverse dependency DAGs and caches them."""
        files = self.scan_files()
        forward: dict[str, set[str]] = {}
        reverse: dict[str, set[str]] = {}
        mtimes: dict[str, float] = {}

        for f in files:
            rel = self._normalize_rel_path(f)
            mtimes[rel] = f.stat().st_mtime
            imported_files = self._parse_file_imports(f)
            forward[rel] = imported_files

            if rel not in reverse:
                reverse[rel] = set()

            for imp in imported_files:
                if imp not in reverse:
                    reverse[imp] = set()
                reverse[imp].add(rel)

        self.forward_graph = {k: sorted(v) for k, v in forward.items()}
        self.reverse_graph = {k: sorted(v) for k, v in reverse.items()}
        self.file_mtimes = mtimes

        self._save_cache()

    def _save_cache(self) -> None:
        """Persists graph state to JSON cache."""
        try:
            self.cache_file.parent.mkdir(parents=True, exist_ok=True)
            data = {
                "forward_graph": self.forward_graph,
                "reverse_graph": self.reverse_graph,
                "file_mtimes": self.file_mtimes,
            }
            self.cache_file.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except Exception:
            pass

    def load_or_build(self) -> None:
        """Loads graph from cache or builds if missing/stale."""
        if not self.cache_file.exists():
            self.build()
            return

        try:
            data = json.loads(self.cache_file.read_text(encoding="utf-8"))
            self.forward_graph = data.get("forward_graph", {})
            self.reverse_graph = data.get("reverse_graph", {})
            self.file_mtimes = data.get("file_mtimes", {})
        except Exception:
            self.build()

    def get_transitive_dependents(self, rel_path: str) -> set[str]:
        """Computes all files that directly or transitively depend on the given file."""
        visited: set[str] = set()
        queue = [rel_path]

        while queue:
            curr = queue.pop(0)
            direct_dependents = self.reverse_graph.get(curr, [])
            for dep in direct_dependents:
                if dep not in visited:
                    visited.add(dep)
                    queue.append(dep)

        return visited

    def resolve_affected_tests(self, modified_files: list[Path | str]) -> list[str]:
        """Resolves a list of modified files to all affected unit/integration test paths."""
        if not self.forward_graph:
            self.load_or_build()

        affected_tests: set[str] = set()

        for m_file in modified_files:
            rel = self._normalize_rel_path(m_file)

            # If the modified file is already a test file
            if (
                "test_" in Path(rel).name
                or "/tests/" in rel
                or rel.startswith("tests/")
            ):
                if (self.repo_root / rel).exists():
                    affected_tests.add(rel)

            # Transitive downstream files
            transitive = self.get_transitive_dependents(rel)
            for dep in transitive:
                if (
                    "test_" in Path(dep).name
                    or "/tests/" in dep
                    or dep.startswith("tests/")
                ):
                    if (self.repo_root / dep).exists():
                        affected_tests.add(dep)

            # Fallback: if no direct or transitive test was found, map to service test dir
            if not affected_tests:
                parts = Path(rel).parts
                if len(parts) > 1 and parts[0] in ("apps", "packages"):
                    test_dir = self.repo_root / parts[0] / parts[1] / "tests"
                    if test_dir.exists():
                        affected_tests.add(str(test_dir.relative_to(self.repo_root)))

        return sorted(affected_tests)
