import os
from unittest.mock import patch

import pytest
from fastapi.testclient import TestClient

from apps.gateway.main import (
    app,
    reset_brand_mappings_cache,
    resolve_brand_by_host,
    validate_branding_and_auth,
)


@pytest.fixture(autouse=True)
def clean_cache_each_test():
    reset_brand_mappings_cache()
    yield
    reset_brand_mappings_cache()


def test_resolve_brand_by_host_fallback():
    # When no mappings are present and host is unrecognized
    with patch.dict(
        os.environ, {"BRAND_NAME": "Fallback Brand", "BRAND_DOMAIN": "fallback.com"}
    ):
        name, domain = resolve_brand_by_host("unregistered-sponsor.com")
        assert name == "Fallback Brand"
        assert domain == "fallback.com"


def test_resolve_brand_by_host_mappings_json():
    # JSON mappings with nested dictionary or simple string values
    mappings_json = (
        '{"sponsor1.com": {"name": "Sponsor One", "domain": "sponsor1.com"}, '
        '"sponsor2.com": "Sponsor Two"}'
    )
    with patch.dict(os.environ, {"BRAND_MAPPINGS": mappings_json}):
        name1, dom1 = resolve_brand_by_host("sponsor1.com")
        assert name1 == "Sponsor One"
        assert dom1 == "sponsor1.com"

        name2, dom2 = resolve_brand_by_host("sponsor2.com")
        assert name2 == "Sponsor Two"
        assert dom2 == "sponsor2.com"


def test_resolve_brand_by_host_mappings_env_vars():
    # Individual env var configurations
    with patch.dict(
        os.environ,
        {
            "BRAND_MAPPING_sponsor3_com": '{"name": "Sponsor Three", "domain": "sponsor3.com", "theme": {"--color-primary": "#111111"}}',
            "BRAND_MAPPING_sponsor4_com": "Sponsor Four",
            "BRAND_NAME_sponsor5": "Sponsor Five",
            "BRAND_DOMAIN_sponsor5": "sponsor5.org",
        },
    ):
        name3, dom3 = resolve_brand_by_host("sponsor3.com")
        assert name3 == "Sponsor Three"
        assert dom3 == "sponsor3.com"

        name4, dom4 = resolve_brand_by_host("sponsor4.com")
        assert name4 == "Sponsor Four"
        assert dom4 == "sponsor4.com"

        name5, dom5 = resolve_brand_by_host("sponsor5.org")
        assert name5 == "Sponsor Five"
        assert dom5 == "sponsor5.org"


def test_resolve_brand_by_host_case_insensitivity_and_port():
    mappings_json = (
        '{"sponsor1.com": {"name": "Sponsor One", "domain": "sponsor1.com"}}'
    )
    with patch.dict(os.environ, {"BRAND_MAPPINGS": mappings_json}):
        # Port in host header should be ignored, and lowercase matching enforced
        name, dom = resolve_brand_by_host("SPONSOR1.com:8080")
        assert name == "Sponsor One"
        assert dom == "sponsor1.com"


def test_get_gateway_config_endpoint():
    client = TestClient(app)
    mappings_json = (
        '{"sponsor1.com": {"name": "Sponsor One", "domain": "sponsor1.com", '
        '"theme": {"--color-primary": "#f00f00"}}}'
    )
    with patch.dict(os.environ, {"BRAND_MAPPINGS": mappings_json}):
        response = client.get(
            "/api/v1/gateway/config", headers={"Host": "sponsor1.com"}
        )
        assert response.status_code == 200
        data = response.json()
        assert data["brand_name"] == "Sponsor One"
        assert data["brand_domain"] == "sponsor1.com"
        assert data["theme"]["--color-primary"] == "#f00f00"
        assert data["theme"]["--color-primary-dark"] == "#014d76"  # fallback default


def test_get_gateway_config_endpoint_unrecognized_fallback():
    client = TestClient(app)
    with patch.dict(
        os.environ, {"BRAND_NAME": "Default Platform", "BRAND_DOMAIN": "default.com"}
    ):
        response = client.get("/api/v1/config", headers={"Host": "unrecognized.com"})
        assert response.status_code == 200
        data = response.json()
        assert data["brand_name"] == "Default Platform"
        assert data["brand_domain"] == "default.com"
        assert data["theme"]["--color-primary"] == "#026597"  # default style


def test_skip_branding_validation_on_startup():
    # In production/staging, booting fails if branding configs are unset
    with patch.dict(
        os.environ,
        {
            "APP_ENV": "production",
            "BRAND_NAME": "Cadence Clinical",  # unchanged/default
            "BRAND_DOMAIN": "cadenceclinical.com",
            "KEYCLOAK_REALM": "cadence",
            "KEYCLOAK_CLIENT_ID": "cadence-clinical",
        },
    ):
        with pytest.raises(SystemExit):
            validate_branding_and_auth()

        # But with SKIP_BRANDING_VALIDATION active, it should skip the branding check and only check keycloak parameters
        # and since Keycloak configurations are defaults, it still fails on those (realm, client_id)
        # So let's provide secure values for Keycloak parameters
        with patch.dict(
            os.environ,
            {
                "APP_ENV": "production",
                "SKIP_BRANDING_VALIDATION": "true",
                "BRAND_NAME": "Cadence Clinical",
                "BRAND_DOMAIN": "cadenceclinical.com",
                "KEYCLOAK_REALM": "secure-realm-abc",
                "KEYCLOAK_CLIENT_ID": "secure-client-xyz",
            },
        ):
            # Should boot successfully (no SystemExit raised)
            validate_branding_and_auth()
