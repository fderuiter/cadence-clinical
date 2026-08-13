import pytest
from pydantic import ValidationError

from apps.designer.domain.cdisc.usdm_models import Code


def test_usdm_model_immutability():
    # Instantiate Code model
    code = Code(code="C123", codeSystem="NCI", decode="NCI Concept")

    # Verify that trying to mutate a field raises ValidationError or AttributeError (due to frozen=True)
    with pytest.raises((ValidationError, AttributeError)):
        code.code = "C456"
