"""Model tier resolution and routing tests for AI Gateway."""

import os
from unittest import mock

from apps.ai_gateway.adapters.litellm_adapter import LiteLLMAdapter
from apps.ai_gateway.domain.models import ModelTier


def test_tier_model_resolution_defaults():
    """Verify default model resolution for each execution tier.

    @req:PRD-SYS-090
    """
    adapter = LiteLLMAdapter(
        tier_1_model="ollama/llama3",
        tier_2_model="gpt-4o-mini",
        tier_3_model="gpt-4o",
        tier_1_embed_model="text-embedding-3-small",
    )

    assert adapter.resolve_model(ModelTier.TIER_1_LOCAL) == "ollama/llama3"
    assert adapter.resolve_model(ModelTier.TIER_2_FAST) == "gpt-4o-mini"
    assert adapter.resolve_model(ModelTier.TIER_3_FRONTIER) == "gpt-4o"
    assert (
        adapter.resolve_model(ModelTier.TIER_1_LOCAL, is_embedding=True)
        == "text-embedding-3-small"
    )


def test_tier_model_override():
    """Verify explicit model override takes precedence over tier defaults.

    @req:PRD-SYS-090
    """
    adapter = LiteLLMAdapter()
    resolved = adapter.resolve_model(
        ModelTier.TIER_2_FAST,
        override="anthropic/claude-3-5-sonnet-20241022",
    )
    assert resolved == "anthropic/claude-3-5-sonnet-20241022"


def test_tier_environment_variable_configuration():
    """Verify tier model destinations can be configured via environment variables.

    @req:PRD-SYS-090
    """
    env_vars = {
        "AI_TIER_1_MODEL": "local-vllm/mistral-7b",
        "AI_TIER_2_MODEL": "azure/gpt-4o-mini",
        "AI_TIER_3_MODEL": "anthropic/claude-3-opus",
        "AI_TIER_1_EMBED_MODEL": "local/bge-large-en",
    }
    with mock.patch.dict(os.environ, env_vars):
        adapter = LiteLLMAdapter()
        assert adapter.resolve_model(ModelTier.TIER_1_LOCAL) == "local-vllm/mistral-7b"
        assert adapter.resolve_model(ModelTier.TIER_2_FAST) == "azure/gpt-4o-mini"
        assert (
            adapter.resolve_model(ModelTier.TIER_3_FRONTIER)
            == "anthropic/claude-3-opus"
        )
        assert (
            adapter.resolve_model(ModelTier.TIER_1_LOCAL, is_embedding=True)
            == "local/bge-large-en"
        )


def test_get_tier_info_structure():
    """Verify tier information contains all required clinical capabilities.

    @req:PRD-SYS-090
    """
    adapter = LiteLLMAdapter()
    tier_info_list = adapter.get_tier_info()
    assert len(tier_info_list) == 3

    tier_map = {t.tier: t for t in tier_info_list}
    assert ModelTier.TIER_1_LOCAL in tier_map
    assert ModelTier.TIER_2_FAST in tier_map
    assert ModelTier.TIER_3_FRONTIER in tier_map

    assert "embeddings" in tier_map[ModelTier.TIER_1_LOCAL].capabilities
    assert "structured_outputs" in tier_map[ModelTier.TIER_2_FAST].capabilities
    assert "protocol_synthesis" in tier_map[ModelTier.TIER_3_FRONTIER].capabilities
