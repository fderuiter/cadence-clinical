"""Unit and contract tests for the static AST test dependency graph.

@req:PRD-SYS-049
"""

from pathlib import Path

from packages.testing.dependency_graph import TestDependencyGraph


def test_dependency_graph_ast_parsing(tmp_path: Path):
    """Verify that TestDependencyGraph correctly parses AST imports and resolves transitive tests.

    @req:PRD-SYS-049
    """
    # Create a mock repository structure
    apps_dir = tmp_path / "apps" / "execution"
    apps_dir.mkdir(parents=True)
    tests_dir = apps_dir / "tests"
    tests_dir.mkdir()

    models_file = apps_dir / "models.py"
    models_file.write_text("class ClinicalModel: pass\n")

    services_file = apps_dir / "service.py"
    services_file.write_text("from apps.execution.models import ClinicalModel\n")

    test_file = tests_dir / "test_service.py"
    test_file.write_text(
        "from apps.execution.service import ClinicalModel\ndef test_service(): pass\n"
    )

    graph = TestDependencyGraph(
        repo_root=tmp_path, cache_file=tmp_path / ".cadence" / "test_graph.json"
    )
    graph.build(force=True)

    # Modifying models.py should transitively resolve test_service.py
    affected = graph.resolve_affected_tests([models_file])
    assert "apps/execution/tests/test_service.py" in affected

    # Modifying test_service.py directly should return itself
    affected_test = graph.resolve_affected_tests([test_file])
    assert "apps/execution/tests/test_service.py" in affected_test


def test_dependency_graph_caching(tmp_path: Path):
    """Verify that dependency graph caches to disk and reloads when mtime is unchanged.

    @req:PRD-SYS-049
    """
    cache_path = tmp_path / ".cadence" / "test_graph.json"
    graph = TestDependencyGraph(repo_root=tmp_path, cache_file=cache_path)

    # Create dummy file
    dummy = tmp_path / "apps" / "designer" / "test_dummy.py"
    dummy.parent.mkdir(parents=True)
    dummy.write_text("def test_dummy(): pass\n")

    graph.build(force=True)
    assert cache_path.exists()

    # Load second instance and verify from cache
    graph2 = TestDependencyGraph(repo_root=tmp_path, cache_file=cache_path)
    graph2.load_or_build()
    assert "apps/designer/test_dummy.py" in graph2.resolve_affected_tests([dummy])
