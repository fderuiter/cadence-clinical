"""Pydantic Request/Response DTOs for Gateway authentication and proxying."""

from pydantic import BaseModel


class SignatureVerificationRequest(BaseModel):
    username: str
    password: str
    totp: str | None = None
    action: str
    batch_id: str | None = None
    semantic_action: str | None = None


class DemoSessionRequest(BaseModel):
    username: str | None = None
    roles: list[str] | None = None
    tenant_id: str | None = None
