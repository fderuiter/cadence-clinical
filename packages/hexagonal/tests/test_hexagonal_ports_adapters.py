"""
Hexagonal Ports and Adapters Verification Test Suite.

This test suite explicitly validates and verifies all six acceptance criteria
for Hexagonal Architecture decoupling across the clinical execution and designer platforms.
"""

import ast
import os

import pytest

from apps.designer.db import (
    MOCK_RULES,
    MOCK_STUDY_VERSIONS,
    assert_mock_study_mutable,
    create_mock_rule,
    create_mock_study_version,
    get_mock_rules,
    update_mock_rule,
)
from apps.execution.adapters.repositories import (
    InMemoryAuditRepository,
    InMemoryConsentRepository,
    InMemorySubjectRepository,
)

# Core Domain models
from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
)

# Service and repository classes/mocks
from apps.execution.subject_lifecycle import (
    InvalidStateTransitionError,
)
from packages.hexagonal.tests.test_hexagonal_architecture import SERVICES

# =====================================================================
# Criterion 1: Zero relational ORM/graph database dependencies in Domain
# =====================================================================


@pytest.mark.parametrize("service", SERVICES)
def test_domain_models_contain_zero_database_imports(service: str):
    """Verify core domain models have zero dependencies on persistence or web packages.

    @req:PRD-SYS-001
    """
    repo_root = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "..", "..")
    )

    # Dynamically find all domain files in the service directory
    service_dir = os.path.join(repo_root, "apps", service)
    domain_paths = []
    if os.path.exists(service_dir):
        for root, _, files in os.walk(service_dir):
            parts = root.split(os.sep)
            if "domain" in parts:
                for file in files:
                    if file.endswith(".py"):
                        domain_paths.append(os.path.join(root, file))

    if not domain_paths:
        pytest.skip(f"Service {service} has no domain files.")

    forbidden_imports = {
        "sqlalchemy",
        "sqlmodel",
        "neo4j",
        "fastapi",
        "starlette",
        "asyncpg",
        "aiosqlite",
    }

    for path in domain_paths:
        with open(path, encoding="utf-8") as f:
            tree = ast.parse(f.read())

        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                for name in node.names:
                    pkg = name.name.split(".")[0]
                    assert pkg not in forbidden_imports, (
                        f"Forbidden direct import of '{pkg}' detected in domain file '{path}'"
                    )
            elif isinstance(node, ast.ImportFrom):
                if node.module:
                    pkg = node.module.split(".")[0]
                    assert pkg not in forbidden_imports, (
                        f"Forbidden from-import of '{pkg}' detected in domain file '{path}'"
                    )


# =====================================================================
# Criterion 2: Pure Python status transitions and guards
# =====================================================================


def test_subject_status_transitions_pure_python_validation():
    """Verify status transitions reject invalid states using pure Python logic rather than DB hooks.

    @req:PRD-SYS-001
    """
    # Initialize screening subject (valid status transition flow start)
    subj = ClinicalSubjectDomain(
        subject_id="SUBJ-T1", study_id="STUDY_TEST", status="SCREENING"
    )
    assert subj.status == "SCREENING"

    # Screening -> Enrolled is valid
    subj.status = "ENROLLED"
    assert subj.status == "ENROLLED"

    # Enrolled -> Active directly is invalid (must go through randomized)
    with pytest.raises(InvalidStateTransitionError):
        subj.status = "ACTIVE"

    # Subject is completed / withdrawn, cannot transition further
    subj.status = "RECONSENT_REQUIRED"
    subj.status = "WITHDRAWN"
    with pytest.raises(InvalidStateTransitionError):
        subj.status = "SCREENING"


# =====================================================================
# Criterion 3: Signed consent records immutability
# =====================================================================


def test_signed_consent_immutability_pure_python_validation():
    """Verify signed consent forms throw validation errors on modification purely in Python.

    @req:PRD-SYS-001
    """
    # Consent Signature Domain model
    sig = ConsentSignatureDomain(
        subject_id="SUBJ-T1",
        icf_version_id="ICF-V1",
        printed_name="John Doe",
        status="SIGNED",
    )

    # Modification of attributes must fail with ValueError
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        sig.printed_name = "Jane Doe"

    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        sig.status = "REVOKED"

    # Deleting attributes must fail with ValueError
    with pytest.raises(ValueError, match="Cannot delete consent records"):
        del sig.printed_name


# =====================================================================
# Criterion 4: Route handlers do not construct SQL/manage transactions directly
# =====================================================================


@pytest.mark.parametrize("service", SERVICES)
def test_api_routers_contain_no_direct_db_calls(service: str):
    """Verify endpoint handlers coordinate actions without constructing ORM queries or managing sessions directly.

    @req:PRD-SYS-001
    """
    repo_root = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "..", "..")
    )

    # Dynamically find all decoupled router files inside the service
    service_dir = os.path.join(repo_root, "apps", service)
    router_paths = []
    if os.path.exists(service_dir):
        for root, _, files in os.walk(service_dir):
            parts = root.split(os.sep)
            if "routers" in parts and "presentation" not in parts:
                for file in files:
                    if file.endswith(".py"):
                        router_paths.append(os.path.join(root, file))

    if not router_paths:
        pytest.skip(f"Service {service} has no decoupled router files.")

    for path in router_paths:
        with open(path, encoding="utf-8") as f:
            content = f.read()
            tree = ast.parse(content)

        for node in ast.walk(tree):
            if isinstance(node, ast.Call):
                func_name = ""
                if isinstance(node.func, ast.Name):
                    func_name = node.func.id
                elif isinstance(node.func, ast.Attribute):
                    func_name = node.func.attr

                # Route handlers must not invoke select, session.commit, session.execute, or Base.metadata
                assert func_name not in ("select", "commit", "execute"), (
                    f"Direct database operation '{func_name}' detected inside route handlers in '{path}'"
                )


# =====================================================================
# Criterion 5: Designer service graph modifications using simple mock repositories
# =====================================================================


@pytest.mark.asyncio
async def test_designer_graph_modifications_with_mock_repositories():
    """Verify graph and rule modifications in Designer work seamlessly database-free via mock repositories.

    @req:PRD-SYS-001
    """
    study_id = "mock_study_hex"
    MOCK_STUDY_VERSIONS[study_id] = []
    create_mock_study_version(
        study_id,
        {
            "id": "ver_1",
            "study_id": study_id,
            "version_index": 1,
            "version_tag": "1.0",
            "status": "DRAFT",
            "created_by": "system",
            "change_reason": "Setup study",
        },
    )
    MOCK_RULES[study_id] = []

    # 1. Assert study is mutable initially
    assert_mock_study_mutable(study_id)

    # 2. Add rule node using mock dictionary repository
    rule_payload = {
        "type": "skip_logic",
        "condition": {"type": "constant", "value": True},
        "target_field": "VSSBP",
        "action": "hide",
    }
    created_rule = create_mock_rule(study_id, rule_payload)
    assert created_rule["id"].startswith("rule_")
    assert created_rule["target_field"] == "VSSBP"
    assert created_rule["version_index"] == 1

    # 3. Modify/update rule node through simple mock repository
    updated_payload = {"target_field": "VSDBP"}
    updated_rule = update_mock_rule(study_id, created_rule["id"], updated_payload)
    assert updated_rule is not None
    assert updated_rule["target_field"] == "VSDBP"
    assert updated_rule["version_index"] == 2

    # Verify latest state in mock store
    rules = get_mock_rules(study_id)
    assert len(rules) == 1
    assert rules[0]["target_field"] == "VSDBP"


# =====================================================================
# Criterion 6: Relational services execute unit tests with database disabled
# =====================================================================


@pytest.mark.asyncio
async def test_relational_services_execute_database_disabled():
    """Verify relational service flows execute perfectly with database configurations disabled using mock/in-memory adapters.

    @req:PRD-SYS-001
    """
    # 1. Instantiate in-memory database-free repositories
    subj_repo = InMemorySubjectRepository()
    consent_repo = InMemoryConsentRepository()
    audit_repo = InMemoryAuditRepository()

    # 2. Test Clinical Subject flow database-free
    subj = ClinicalSubjectDomain(
        id="subj-t2",
        subject_id="SUBJ-002",
        study_id="STUDY_B",
        status="SCREENING",
        strat_factors={"age": "LT_65"},
    )
    await subj_repo.save(subj)

    fetched = await subj_repo.get_by_id("subj-t2")
    assert fetched is not None
    assert fetched.subject_id == "SUBJ-002"
    assert fetched.status == "SCREENING"

    # Mutate state in-memory
    fetched.status = "ENROLLED"
    await subj_repo.save(fetched)

    updated_fetched = await subj_repo.get_by_id("subj-t2")
    assert updated_fetched.status == "ENROLLED"

    # 3. Test signed consent form flow database-free
    form_record = ConsentFormRecordDomain(
        id="record-t2",
        subject_id="SUBJ-002",
        icf_version_id="ICF-V2",
        status="SIGNED",
    )
    await consent_repo.save_form_record(form_record)

    fetched_form = await consent_repo.get_form_record_by_id("record-t2")
    assert fetched_form is not None
    assert fetched_form.status == "SIGNED"

    # 4. Test safety audit log flow database-free
    audit_log = AuditLogDomain(
        id="audit-t2",
        table_name="consent_form_records",
        record_id="record-t2",
        action="INSERT",
        user_id="user_admin",
        change_reason="Created consent form record",
    )
    await audit_repo.save(audit_log)

    fetched_audit = await audit_repo.get_by_id("audit-t2")
    assert fetched_audit is not None
    assert fetched_audit.action == "INSERT"
    assert fetched_audit.user_id == "user_admin"

    # Deleting signed form is blocked in mock repository
    with pytest.raises(ValueError, match="Cannot delete consent records"):
        await consent_repo.delete_form_record(fetched_form)
