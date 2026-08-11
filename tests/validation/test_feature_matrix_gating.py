"""
Unit and integration tests for the Feature Matrix gating linter.
"""

from pathlib import Path

from scripts.validate_architecture_drift import validate_feature_matrix


def test_feature_matrix_validation_success():
    """
    Test that the actual FEATURE_MATRIX.md correctly validates and passes.
    """
    repo_root = Path(__file__).resolve().parent.parent.parent
    matrix_path = repo_root / "docs" / "FEATURE_MATRIX.md"

    # Standard active services from docker-compose
    active_services = [
        "designer",
        "execution",
        "org",
        "eisf",
        "etmf",
        "ctms",
        "quality",
        "interop",
        "tickets",
        "safety",
        "notifications",
        "econsent",
        "subject-portal",
        "gateway",
    ]

    assert validate_feature_matrix(matrix_path, active_services) is True


def test_feature_matrix_validation_missing_service(tmp_path):
    """
    Test that the linter fails if an active service is missing from the matrix.
    """
    # Create a mock FEATURE_MATRIX.md under tmp_path
    (tmp_path / "docs").mkdir()
    mock_matrix = tmp_path / "docs" / "FEATURE_MATRIX.md"

    # Create the apps/ folders in tmp_path to mock actual workspace existence
    (tmp_path / "apps").mkdir()
    for s in ["designer", "execution", "safety"]:
        (tmp_path / "apps" / s).mkdir()

    mock_content = """
# Feature & Compatibility Matrix

## 2. Clinical Entities Mapping

| Clinical Entity                 | Sub-system               | Persistence Backend       | Audit Listener Pattern                                                                                                              |
| :------------------------------ | :----------------------- | :------------------------ | :---------------------------------------------------------------------------------------------------------------------------------- |
| **Study Protocols**             | Designer                 | Neo4j                     | Graph Node Versioning                                                                                                               |
| **Subjects**                    | Execution                | PostgreSQL                | App-Layer Event Interceptor                                                                                                         |
"""
    mock_matrix.write_text(mock_content, encoding="utf-8")

    # Active services contain safety, designer, execution
    # but the mock matrix only has Designer and Execution rows -> safety is missing
    active_services = ["designer", "execution", "safety"]

    assert validate_feature_matrix(mock_matrix, active_services) is False


def test_feature_matrix_validation_ignoring_helper_and_excluded_services(tmp_path):
    """
    Test that helper/infrastructure services like postgres, neo4j, and gateway are ignored.
    """
    mock_matrix = tmp_path / "docs" / "FEATURE_MATRIX.md"
    (tmp_path / "docs").mkdir()
    (tmp_path / "apps").mkdir()
    for s in ["designer", "execution"]:
        (tmp_path / "apps" / s).mkdir()

    mock_content = """
# Feature & Compatibility Matrix

## 2. Clinical Entities Mapping

| Clinical Entity                 | Sub-system               | Persistence Backend       | Audit Listener Pattern                                                                                                              |
| :------------------------------ | :----------------------- | :------------------------ | :---------------------------------------------------------------------------------------------------------------------------------- |
| **Study Protocols**             | Designer                 | Neo4j                     | Graph Node Versioning                                                                                                               |
| **Subjects**                    | Execution                | PostgreSQL                | App-Layer Event Interceptor                                                                                                         |
"""
    mock_matrix.write_text(mock_content, encoding="utf-8")

    # Active services contain helper services which should be filtered out/ignored
    active_services = [
        "designer",
        "execution",
        "postgres",
        "neo4j",
        "keycloak",
        "gateway",
    ]

    assert validate_feature_matrix(mock_matrix, active_services) is True
