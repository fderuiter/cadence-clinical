"""Cadence Clinical Centralized Test Infrastructure Toolkit.

Provides reusable domain entity factories, in-memory repository fakes,
mock security context fixtures, and isolated test harnesses for fast test execution.
"""

from packages.testing.factories import (
    AuditLogFactory,
    ClinicalObservationFactory,
    ConsentRecordFactory,
    DocumentMetadataFactory,
    ProtocolDefinitionFactory,
    QueryDiscrepancyFactory,
    SubjectFactory,
)
from packages.testing.fakes import InMemoryRepository, InMemoryStoragePort
from packages.testing.security import (
    create_test_auth_headers,
    create_test_security_context,
    create_test_token,
    generate_signature,
)

__all__ = [
    "AuditLogFactory",
    "ClinicalObservationFactory",
    "ConsentRecordFactory",
    "DocumentMetadataFactory",
    "InMemoryRepository",
    "InMemoryStoragePort",
    "ProtocolDefinitionFactory",
    "QueryDiscrepancyFactory",
    "SubjectFactory",
    "create_test_auth_headers",
    "create_test_security_context",
    "create_test_token",
    "generate_signature",
]
