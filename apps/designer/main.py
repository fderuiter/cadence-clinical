# ruff: noqa: E402
"""
Cadence Clinical - Designer (MDR/SDR)

This module handles the design and management of clinical studies and MDR components.
"""

import os

from fastapi import Depends, FastAPI, HTTPException, Request, status
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from neo4j import AsyncGraphDatabase

from packages.security.context import audit_context

DEFAULT_JUSTIFICATIONS = {
    "system_operation",
    "system",
    "automated system operation",
    "automated system",
    "default",
    "none",
    "system-operation",
    "system operation",
    "unknown",
    "n/a",
    "na",
    "null",
    "undefined",
}


async def gxp_audit_enforcement_filter(request: Request):
    if request.method in ("GET", "HEAD", "OPTIONS"):
        yield
        return

    # Extract user ID and change reason
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "system"
    )
    change_reason = request.headers.get("X-Change-Reason") or getattr(
        request.state, "change_reason", None
    )

    # Check request body if change_reason is missing
    if not change_reason:
        try:
            body_bytes = await request.body()
            if body_bytes:
                import json

                body_json = json.loads(body_bytes)
                change_reason = body_json.get("change_reason") or body_json.get(
                    "reason_for_change"
                )

                # Restore body receive for Starlette downstream
                async def receive():
                    return {
                        "type": "http.request",
                        "body": body_bytes,
                        "more_body": False,
                    }

                request._receive = receive
        except Exception:
            pass

    # Check if this is a schema modification path (arms, epochs, visits, procedures, blocks, rules)
    path = request.url.path.lower()
    is_schema_mod = any(
        item in path
        for item in (
            "/arms",
            "/epochs",
            "/visits",
            "/procedures",
            "/blocks",
            "/rules",
            "/assignments",
        )
    )

    # Validate that every state-altering request provides a non-default, user-supplied change justification
    is_invalid = False
    if not change_reason or not change_reason.strip():
        is_invalid = True
    elif is_schema_mod and user_id not in ("system", "service"):
        norm_reason = change_reason.strip().lower()
        if norm_reason in DEFAULT_JUSTIFICATIONS:
            is_invalid = True

    if is_invalid:
        raise HTTPException(
            status_code=400, detail="Missing change justification reason"
        )

    # Wrap the route execution inside the audit_context context manager
    with audit_context(user_id=user_id, change_reason=change_reason.strip()):
        yield


import apps.designer.adapters.repositories  # noqa: F401
from apps.designer.adapters.safety_gateway import QuerySafetyError  # noqa: F401
from apps.designer.delta import (
    ConcurrentLockingError,
    ImmutabilityViolationError,
    InvalidSignatureError,
    LibraryObjectInUseError,
    LibraryObjectLockedActiveStudyError,
)
from apps.designer.dependencies import (
    get_neo4j_driver,
    require_study_scope,
)
from apps.designer.domain.exceptions import ConceptLockedError
from apps.designer.presentation.routers.cascade import router as cascade_router
from apps.designer.presentation.routers.comments import router as comments_router
from apps.designer.presentation.routers.designer_routes import (
    MOCK_PROTOCOL_INGESTIONS,
    validate_study_terminology_endpoint,  # noqa: F401
)
from apps.designer.presentation.routers.designer_routes import (
    router as designer_router,
)
from apps.designer.presentation.routers.protocol_export import router as export_router
from apps.designer.presentation.routers.quality_sentinel import (
    router as sentinel_router,
)
from apps.designer.presentation.routers.synopsis import router as synopsis_router
from apps.designer.rendering import TemplateRenderingError
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("designer")
app = FastAPI(
    title=f"{BRAND_NAME} - Designer (MDR/SDR)",
    version="0.1.0",
    dependencies=[Depends(gxp_audit_enforcement_filter)],
)

app.add_middleware(GatewayAuthMiddleware)

assert_secure_secrets("designer", {"SIGNING_SECRET": os.getenv("SIGNING_SECRET")})

app.include_router(synopsis_router)
app.include_router(sentinel_router)
app.include_router(cascade_router)
app.include_router(export_router)
app.include_router(comments_router)
app.include_router(designer_router)


from fastapi.exception_handlers import http_exception_handler


@app.exception_handler(HTTPException)
async def designer_http_exception_handler(request: Request, exc: HTTPException):
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")

    if exc.status_code == status.HTTP_400_BAD_REQUEST:
        return JSONResponse(
            status_code=status.HTTP_400_BAD_REQUEST,
            content={
                "type": f"https://api.{brand_domain}/errors/bad-request",
                "title": "Bad Request",
                "status": status.HTTP_400_BAD_REQUEST,
                "detail": str(exc.detail),
                "instance": request.url.path,
                "code": "BAD_REQUEST",
            },
        )
    return await http_exception_handler(request, exc)


@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    invalid_params = []
    for err in exc.errors():
        loc_items = err.get("loc", [])
        field_name = (
            ".".join(str(item) for item in loc_items if item != "body") or "unknown"
        )
        invalid_params.append(
            {
                "field": field_name,
                "reason": err.get("msg", "Invalid value"),
                "value": err.get("input", None),
            }
        )
    return JSONResponse(
        status_code=status.HTTP_400_BAD_REQUEST,
        content=jsonable_encoder(
            {
                "type": f"https://api.{brand_domain}/errors/validation-failed",
                "title": "Request Validation Failed",
                "status": status.HTTP_400_BAD_REQUEST,
                "detail": "Input validation failed. Please check the 'errors' array for details.",
                "code": "REQUEST_VALIDATION_ERROR",
                "instance": request.url.path,
                "errors": exc.errors(),
                "invalid_params": invalid_params,
            }
        ),
    )


@app.exception_handler(ImmutabilityViolationError)
async def immutability_violation_handler(
    request: Request, exc: ImmutabilityViolationError
):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "IMMUTABILITY_VIOLATION",
            "message": "Cannot modify entity because its status is LOCKED, PUBLISHED, or ARCHIVED.",
            "code": "IMMUTABILITY_VIOLATION",
        },
    )


@app.exception_handler(ConcurrentLockingError)
async def concurrent_locking_handler(request: Request, exc: ConcurrentLockingError):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "Stale version index detected. The study version has been modified concurrently.",
            "code": "CONCURRENT_LOCKING_ERROR",
        },
    )


@app.exception_handler(InvalidSignatureError)
async def invalid_signature_handler(request: Request, exc: InvalidSignatureError):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "Security Audit Alert: Invalid digital signature on study version record.",
            "code": "INVALID_SIGNATURE",
        },
    )


@app.exception_handler(LibraryObjectInUseError)
async def library_object_in_use_handler(request: Request, exc: LibraryObjectInUseError):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "Cannot modify or delete library object because it is referenced in active study definitions.",
            "code": "LIBRARY_OBJECT_IN_USE",
        },
    )


@app.exception_handler(ConceptLockedError)
async def concept_locked_handler(request: Request, exc: ConceptLockedError):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "CONCEPT_LOCKED_ACTIVE_STUDY",
            "message": exc.message,
            "code": "CONCEPT_LOCKED",
            "concept_id": exc.concept_id,
            "workflow_suggestion": "/api/v1/studies/{study_id}/amend",
        },
    )


@app.exception_handler(LibraryObjectLockedActiveStudyError)
async def library_object_locked_active_study_handler(
    request: Request, exc: LibraryObjectLockedActiveStudyError
):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "LIBRARY_OBJECT_LOCKED_ACTIVE_STUDY",
            "message": "Library object is referenced by an Active-Recruiting study and is locked against direct modifications. Please use the protocol amendment workflow.",
            "code": "LIBRARY_OBJECT_LOCKED_ACTIVE_STUDY",
            "object_id": getattr(exc, "object_id", None),
            "workflow_suggestion": "/api/v1/studies/{study_id}/amend",
        },
    )


@app.exception_handler(TemplateRenderingError)
async def template_rendering_error_handler(
    request: Request, exc: TemplateRenderingError
):
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    detail_str = str(exc) or "Template file is missing or corrupted."
    return JSONResponse(
        status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
        content={
            "type": f"https://api.{brand_domain}/errors/template-unavailable",
            "title": "Template Unavailable",
            "status": status.HTTP_503_SERVICE_UNAVAILABLE,
            "detail": detail_str,
            "code": "TEMPLATE_UNAVAILABLE",
            "instance": request.url.path,
        },
    )


@app.on_event("startup")
async def startup() -> None:
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    auth = (
        os.getenv("NEO4J_USER", "neo4j"),
        os.getenv("NEO4J_PASSWORD", default="cadence_secret_pass"),
    )

    try:
        driver = AsyncGraphDatabase.driver(uri, auth=auth)
        app.state.driver = driver
    except Exception as e:
        print(
            f"[NEO4J] Warning: Failed to connect to Neo4j database at {uri}: {e}. Operating in mock mode."
        )
        app.state.driver = None


@app.on_event("shutdown")
async def shutdown() -> None:
    driver = getattr(app.state, "driver", None)
    if driver:
        await driver.close()
    app.state.driver = None


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        Dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "designer"}


__all__ = [
    "MOCK_PROTOCOL_INGESTIONS",
    "app",
    "get_neo4j_driver",
    "health_check",
    "require_study_scope",
]
