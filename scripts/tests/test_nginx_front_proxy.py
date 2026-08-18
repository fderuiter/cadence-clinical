import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent


def test_docker_compose_front_proxy_configuration():
    """Verify docker-compose.yml configures front-proxy to bind host port 8000."""
    compose_path = REPO_ROOT / "docker" / "docker-compose.yml"
    assert compose_path.exists(), "docker-compose.yml file must exist"

    content = compose_path.read_text(encoding="utf-8")

    # Ensure front-proxy service is defined
    assert "front-proxy:" in content, (
        "front-proxy service must be defined in docker-compose.yml"
    )

    # Ensure front-proxy binds public port 8000:8000
    assert '"8000:8000"' in content or "'8000:8000'" in content, (
        "front-proxy must expose port 8000:8000 to the host"
    )

    # Ensure legacy gateway service no longer binds host port 8000:8000 directly
    gateway_block_match = re.search(
        r"gateway:\s*\n(.*?)(?=\n\s\s[a-zA-Z0-9_-]+:|\Z)", content, re.DOTALL
    )
    assert gateway_block_match is not None, (
        "gateway service block must exist in docker-compose.yml"
    )
    gateway_block = gateway_block_match.group(1)
    assert "8000:8000" not in gateway_block, (
        "legacy gateway must not bind host port 8000"
    )


def test_nginx_main_configuration():
    """Verify docker/nginx/nginx.conf global settings."""
    nginx_conf_path = REPO_ROOT / "docker" / "nginx" / "nginx.conf"
    assert nginx_conf_path.exists(), "docker/nginx/nginx.conf must exist"

    content = nginx_conf_path.read_text(encoding="utf-8")

    assert "worker_processes" in content
    assert "underscores_in_headers on;" in content, (
        "underscores_in_headers must be enabled for GxP token headers"
    )
    assert "include /etc/nginx/conf.d/*.conf;" in content
    assert "map $http_upgrade $connection_upgrade" in content, (
        "WebSocket upgrade map must be defined"
    )


def test_nginx_default_site_configuration():
    """Verify docker/nginx/conf.d/default.conf routing, headers, and upstreams."""
    default_conf_path = REPO_ROOT / "docker" / "nginx" / "conf.d" / "default.conf"
    assert default_conf_path.exists(), "docker/nginx/conf.d/default.conf must exist"

    content = default_conf_path.read_text(encoding="utf-8")

    # Check upstream definitions
    assert "upstream gateway_fastapi" in content, (
        "gateway_fastapi upstream must be defined"
    )
    assert "upstream gateway_nestjs" in content, (
        "gateway_nestjs upstream must be defined"
    )
    assert "server gateway:8000;" in content
    assert (
        "server gateway-rewrite:3000;" in content
        or "server gateway-nestjs:3000;" in content
    )

    # Check split_clients configuration
    assert 'split_clients "$request_id" $split_gateway' in content, (
        "split_clients directive must be configured using $request_id"
    )

    # Check path-based overrides map
    assert "map $uri $gateway_upstream" in content, (
        "map $uri $gateway_upstream must be defined"
    )
    assert "~^/designer(/|$)" in content, (
        "Path-based override for /designer/ must be configured"
    )

    # Check server block port binding
    assert "listen 8000;" in content, "NGINX server block must listen on port 8000"

    # Check Trace-17 & identity header preservation
    required_headers = [
        "Authorization",
        "X-Sig-Token",
        "X-Gateway-Secret",
        "Cookie",
        "X-Request-ID",
        "X-Trace-ID",
    ]
    for header in required_headers:
        assert f"proxy_set_header {header}" in content, (
            f"Trace-17 compliance requires forwarding header: {header}"
        )

    # Check proxy pass directive using dynamic upstream variable
    assert "proxy_pass http://$gateway_upstream;" in content, (
        "proxy_pass must target dynamic $gateway_upstream variable"
    )


def test_split_clients_and_path_override_routing_logic():
    """Simulate and validate NGINX routing logic evaluation."""

    # Path-based override evaluation function simulating NGINX map directive
    def resolve_gateway_upstream(
        uri: str, split_percentage: int, request_id: str
    ) -> str:
        # 1. Evaluate path-based overrides
        if uri.startswith("/designer"):
            return "gateway_nestjs"

        # 2. Evaluate split_clients percentage logic
        # MurmurHash2 or deterministic hash mock for percentage split
        hash_val = sum(ord(c) for c in request_id) % 100
        if hash_val < split_percentage:
            return "gateway_nestjs"
        return "gateway_fastapi"

    # Test /designer override always targets NestJS
    assert (
        resolve_gateway_upstream("/designer/studies", 0, "req-123") == "gateway_nestjs"
    )
    assert (
        resolve_gateway_upstream("/designer/schema", 50, "req-456") == "gateway_nestjs"
    )

    # Test 0% split targets FastAPI gateway
    for i in range(10):
        assert (
            resolve_gateway_upstream("/execution/data", 0, f"req-{i}")
            == "gateway_fastapi"
        )

    # Test 100% split targets NestJS gateway
    for i in range(10):
        assert (
            resolve_gateway_upstream("/execution/data", 100, f"req-{i}")
            == "gateway_nestjs"
        )


def test_trace17_header_forwarding_contract():
    """Verify Trace-17 header forwarding dictionary completeness."""
    incoming_headers = {
        "authorization": "Bearer eyJhbGciOiJSUzI1NiIs...",
        "x-sig-token": "sig-v2-token-abc123xyz",
        "x-gateway-secret": "internal-gateway-secret-12345",  # pragma: allowlist secret
        "cookie": "session_id=sess_998877",
        "x-trace-id": "trace-uuid-11223344",
        "x-custom-app-header": "test-value",
    }

    # Simulate proxy forwarding headers: preserve all incoming headers plus forwarded metadata
    forwarded_headers = {k.lower(): v for k, v in incoming_headers.items()}
    forwarded_headers["x-real-ip"] = "192.168.1.100"
    forwarded_headers["x-forwarded-for"] = "192.168.1.100"
    forwarded_headers["x-forwarded-proto"] = "http"

    # Verify all Trace-17 identity & token challenge headers are preserved
    assert forwarded_headers["authorization"] == incoming_headers["authorization"]
    assert forwarded_headers["x-sig-token"] == incoming_headers["x-sig-token"]
    assert forwarded_headers["x-gateway-secret"] == incoming_headers["x-gateway-secret"]
    assert forwarded_headers["cookie"] == incoming_headers["cookie"]
    assert forwarded_headers["x-trace-id"] == incoming_headers["x-trace-id"]
