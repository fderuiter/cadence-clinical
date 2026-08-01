import importlib.util
import os
import sys

# Load the shared usdm_ingestion.py module directly from packages/core-models to avoid name conflicts.
_current_dir = os.path.dirname(os.path.abspath(__file__))
_shared_path = os.path.abspath(
    os.path.join(
        _current_dir, "..", "..", "packages", "core-models", "usdm_ingestion.py"
    )
)

spec = importlib.util.spec_from_file_location("usdm_ingestion_shared", _shared_path)
_shared_mod = importlib.util.module_from_spec(spec)
sys.modules["usdm_ingestion_shared"] = _shared_mod
spec.loader.exec_module(_shared_mod)

ExpressionNode = _shared_mod.ExpressionNode
FieldReference = _shared_mod.FieldReference
USDMValidationReport = _shared_mod.USDMValidationReport
ValidationIssue = _shared_mod.ValidationIssue
detect_circular_dependencies = _shared_mod.detect_circular_dependencies
detect_stochastic_operators = _shared_mod.detect_stochastic_operators
extract_field_references = _shared_mod.extract_field_references
normalize_usdm_payload = _shared_mod.normalize_usdm_payload
resolve_usdm_version = _shared_mod.resolve_usdm_version
safe_parse_payload = _shared_mod.safe_parse_payload
traverse_rules_in_payload = _shared_mod.traverse_rules_in_payload
validate_usdm_payload = _shared_mod.validate_usdm_payload
