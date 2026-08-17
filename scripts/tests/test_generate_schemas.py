"""Unit tests for generate_schemas.py environment check and sensitive table filtering.

Requirements: PRD-SYS-001, PRD-SYS-003
"""

from unittest.mock import mock_open, patch

import pytest

from scripts.generate_schemas import MODELS, main


def test_generate_schemas_halts_in_production(monkeypatch):
    monkeypatch.setenv("APP_ENV", "production")
    with pytest.raises(SystemExit) as exc:
        main()
    assert exc.value.code == 1


def test_generate_schemas_omits_sensitive_tables(monkeypatch):
    monkeypatch.delenv("APP_ENV", raising=False)
    monkeypatch.delenv("NODE_ENV", raising=False)
    monkeypatch.delenv("ENVIRONMENT", raising=False)
    monkeypatch.delenv("STAGE", raising=False)

    # Patch MODELS to contain a sensitive model
    from pydantic import BaseModel

    class AuditTestModel(BaseModel):
        id: str

    original_models = list(MODELS)
    # Inject AuditTestModel
    import scripts.generate_schemas

    scripts.generate_schemas.MODELS = original_models + [AuditTestModel]

    m = mock_open()
    with patch("builtins.open", m):
        with patch("os.makedirs"):
            # Execute generator
            scripts.generate_schemas.main()

    # Restore MODELS
    scripts.generate_schemas.MODELS = original_models

    # Retrieve all written data
    written_data = "".join(call.args[0] for call in m().write.call_args_list)
    assert "AuditTestModel" not in written_data
