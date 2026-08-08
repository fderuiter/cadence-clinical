"""Dependencies for apps.designer routers.

Requirements: PRD-SYS-001
"""

from fastapi import Depends, HTTPException, Request, status

from apps.designer.application.services.artifact_cascade import ArtifactCascadeEngine
from apps.designer.application.services.quality_sentinel import ProtocolQualitySentinel
from packages.security.rbac import Principal, can_access_study, get_principal


def get_cascade_engine() -> ArtifactCascadeEngine:
    """RETURN an instance of ArtifactCascadeEngine.

    Requirements: PRD-SYS-001
    """
    return ArtifactCascadeEngine()


def get_quality_sentinel() -> ProtocolQualitySentinel:
    """RETURN an instance of ProtocolQualitySentinel.

    Requirements: PRD-SYS-001
    """
    return ProtocolQualitySentinel()


async def get_neo4j_driver(request: Request):
    """Retrieve Neo4j driver from request app state or state."""
    driver = getattr(request.app.state, "driver", None)
    if not driver:
        driver = getattr(request.state, "driver", None)
    return driver


class StudyScopeChecker:
    async def __call__(
        self, request: Request, principal: Principal = Depends(get_principal)
    ) -> Principal:
        study_id = (
            request.path_params.get("study_id")
            or request.query_params.get("study_id")
            or request.headers.get("X-Study-Id")
            or request.headers.get("x-study-id")
        )
        if not study_id and "/protocols/" in request.url.path:
            study_id = request.path_params.get("id")
        sponsor_id = (
            request.path_params.get("sponsor_id")
            or request.query_params.get("sponsor_id")
            or request.headers.get("X-Sponsor-Id")
            or request.headers.get("x-sponsor-id")
        )
        if hasattr(request, "state") and not sponsor_id:
            sponsor_id = getattr(request.state, "sponsor_id", None)

        if not study_id or not sponsor_id:
            try:
                content_type = request.headers.get("content-type", "")
                if "application/json" in content_type:
                    body = await request.json()
                    if isinstance(body, dict):
                        if not study_id:
                            study_id = body.get("study_id") or body.get("id")
                        if not sponsor_id:
                            sponsor_id = body.get("sponsor_id")
                    import json

                    body_bytes = json.dumps(body).encode()

                    async def receive():
                        return {
                            "type": "http.request",
                            "body": body_bytes,
                            "more_body": False,
                        }

                    request._receive = receive
            except Exception:
                pass

        if study_id:
            study_id = str(study_id).strip()
        if sponsor_id:
            sponsor_id = str(sponsor_id).strip()

        if study_id and not can_access_study(principal, study_id):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Insufficient scope access for this study.",
            )

        is_library_or_instance = (
            "/library" in request.url.path
            or "/instance" in request.url.path
            or "/mdr" in request.url.path
        )
        if is_library_or_instance:
            if not sponsor_id or not sponsor_id.strip():
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Forbidden: Missing authenticated sponsor scope",
                )

        return principal


def require_study_scope() -> StudyScopeChecker:
    return StudyScopeChecker()
