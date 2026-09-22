"""
Unit tests for scripts/extract_env_vars.py
"""

import ast
import unittest

from scripts.extract_env_vars import (
    FastEnvVarVisitor,
    classify_domain,
    generate_env_example_content,
    generate_markdown_docs,
    is_sensitive_variable,
    parse_inline_comments,
    sanitize_default_value,
)


class TestExtractEnvVars(unittest.TestCase):

    def test_classify_domain(self):
        """Validates domain classification rules."""
        self.assertEqual(classify_domain("AI_TIER_3_MODEL", "apps/ai_gateway/main.py"), "AI Gateway & LLM Ingestion")
        self.assertEqual(classify_domain("DATABASE_URL", "apps/execution/main.py"), "Database Connection URLs")
        self.assertEqual(classify_domain("GATEWAY_URL", "apps/gateway/main.py"), "Microservice REST Endpoints & URLs")
        self.assertEqual(classify_domain("KEYCLOAK_REALM", "apps/gateway/auth.py"), "Keycloak / OIDC Identity Provider Configuration")
        self.assertEqual(classify_domain("SMTP_HOST", "apps/notifications/main.py"), "Notifications, SMTP & Webhooks")
        self.assertEqual(classify_domain("NCI_EVS_BASE_URL", "apps/terminology/main.py"), "External Clinical Ontologies & Terminology Services")
        self.assertEqual(classify_domain("STORAGE_BACKEND", "apps/etmf/storage.py"), "Document Storage & Archival")
        self.assertEqual(classify_domain("AUDIT_LOG_SECRET_KEY", "packages/security/audit.py"), "Cryptographic Secrets & Authentication Keys")
        self.assertEqual(classify_domain("BRAND_NAME", "packages/core/config.py"), "Enterprise Branding & Domain Identity")
        self.assertEqual(classify_domain("APP_ENV", "apps/execution/config.py"), "Global Platform & Environment")

    def test_sanitize_default_value(self):
        """Verifies that sensitive credentials are replaced with placeholders (Guardrail 2)."""
        self.assertTrue(is_sensitive_variable("AUDIT_LOG_SECRET_KEY"))
        self.assertFalse(is_sensitive_variable("APP_ENV"))

        # Safe defaults remain intact
        self.assertEqual(sanitize_default_value("APP_ENV", "development"), "development")
        self.assertEqual(sanitize_default_value("LOG_LEVEL", "INFO"), "INFO")

        # Live sensitive secret string gets sanitized
        sensitive_secret = "super-secret-live-production-key-1234567890"
        sanitized = sanitize_default_value("MY_SECRET_KEY", sensitive_secret)
        self.assertTrue(sanitized.startswith("dev-"))
        self.assertIn("placeholder", sanitized)

    def test_ast_extraction_visitor(self):
        """Verifies AST visitor captures getenv, environ.get, environ[...], setdefault, and Pydantic fields."""
        code = '''
import os
from pydantic import BaseModel, Field

# Secret key comment
SECRET_KEY = os.getenv("TEST_AST_VAR_1", "default_val_1")
API_KEY = os.environ.get("TEST_AST_VAR_2", "default_val_2")
HOST = os.environ["TEST_AST_VAR_3"]
PORT = os.environ.setdefault("TEST_AST_VAR_4", "8080")

class ConfigModel(BaseModel):
    field_a: str = Field(..., alias="TEST_AST_VAR_5")
    field_b: str = Field(..., validation_alias="TEST_AST_VAR_6")

# env-var: TEST_AST_VAR_7=inline_default
'''
        code_lines = code.splitlines()
        tree = ast.parse(code, filename="dummy.py")

        visitor = FastEnvVarVisitor("apps/dummy/main.py", code_lines)
        visitor.visit(tree)

        findings = visitor.findings
        found_vars = {f["name"]: f for f in findings}

        self.assertIn("TEST_AST_VAR_1", found_vars)
        self.assertEqual(found_vars["TEST_AST_VAR_1"]["default"], "default_val_1")

        self.assertIn("TEST_AST_VAR_2", found_vars)
        self.assertEqual(found_vars["TEST_AST_VAR_2"]["default"], "default_val_2")

        self.assertIn("TEST_AST_VAR_3", found_vars)

        self.assertIn("TEST_AST_VAR_4", found_vars)
        self.assertEqual(found_vars["TEST_AST_VAR_4"]["default"], "8080")

        self.assertIn("TEST_AST_VAR_5", found_vars)
        self.assertIn("TEST_AST_VAR_6", found_vars)

        inline_findings = parse_inline_comments("apps/dummy/main.py", code_lines)
        found_inline = {f["name"]: f for f in inline_findings}
        self.assertIn("TEST_AST_VAR_7", found_inline)
        self.assertEqual(found_inline["TEST_AST_VAR_7"]["default"], "inline_default")

    def test_env_example_and_markdown_generation(self):
        """Validates .env.example and docs/ENVIRONMENT_VARIABLES.md content generators."""
        scanned = {
            "TEST_GEN_VAR_1": {
                "name": "TEST_GEN_VAR_1",
                "defaults": {"default_a"},
                "locations": [("apps/test/main.py", 10)],
                "methods": {"os.getenv"},
                "comments": ["Test variable 1 comment"],
                "domain": "Global Platform & Environment",
            }
        }
        existing = {
            "TEST_GEN_VAR_2": {
                "value": "val_2",
                "comment": "Existing template var 2",
            }
        }

        env_content = generate_env_example_content(scanned, existing)
        self.assertIn("1. Global Platform & Environment", env_content)
        self.assertIn("TEST_GEN_VAR_1=default_a", env_content)
        self.assertIn("TEST_GEN_VAR_2=val_2", env_content)

        md_docs = generate_markdown_docs(scanned, existing)
        self.assertIn("# Cadence Platform Operational Environment Variables Dictionary", md_docs)
        self.assertIn("`TEST_GEN_VAR_1`", md_docs)
        self.assertIn("`apps/test/main.py:10`", md_docs)


if __name__ == "__main__":
    unittest.main()
