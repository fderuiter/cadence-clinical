"""
FastAPI application entrypoint for the Organization Directory microservice.

Configures async database lifecycle management, security context middleware, and
health check endpoint.
"""

import os

from fastapi import FastAPI

from apps.org.database import db_manager
from apps.org.models import Base
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware

# Retrieve database URL from environment or default to in-memory SQLite
DATABASE_URL = os.getenv("ORG_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - Organization Directory",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Register internal gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Standardized DB session dependency
get_db_session = DatabaseSessionDependency(db_manager)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Health check endpoint to verify microservice availability.

    Bypasses standard API Gateway headers validation.
    """
    return {"status": "ok", "service": "org"}
