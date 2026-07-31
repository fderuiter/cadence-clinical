from enum import Enum


def test_audit_action_imports_bare_name():
    # @req:PRD-SYS-001
    """
    Verify that AuditAction is importable via the bare-name mechanism,
    the same way standard core models are resolved.
    """
    from audit import AuditAction

    assert issubclass(AuditAction, str)
    assert issubclass(AuditAction, Enum)


def test_audit_action_members_value_stability():
    # @req:PRD-SYS-001
    """
    Establish the invariant that each AuditAction member's value is
    byte-identical to its SCREAMING_SNAKE_CASE name.
    """
    from audit import AuditAction

    for member in AuditAction:
        assert member.name == member.value, (
            f"Name/value mismatch for member {member.name}"
        )


def test_audit_action_taxonomy_groupings():
    # @req:PRD-SYS-001
    """
    Confirm presence of standard and required members grouped by their functional concerns.
    """
    from audit import AuditAction

    # 1. Generic CRUD/read
    generic_crud = ["CREATE", "READ", "UPDATE", "DELETE", "VIEW", "LIST"]
    for val in generic_crud:
        assert hasattr(AuditAction, val)
        assert AuditAction[val] == val

    # 2. Document lifecycle
    doc_lifecycle = [
        "INGEST",
        "DOWNLOAD",
        "WATERMARKED_DOWNLOAD",
        "REDACT",
        "SIGN",
        "APPROVE",
        "QC_TRANSITION",
        "BINDER_EXPORT",
        "COMPLETENESS",
        "MUTATION_REJECTED",
        "AUDIT_VIEW",
        "EDL_VIEW",
        "EDL_UPDATE",
        "QC_HISTORY_VIEW",
    ]
    for val in doc_lifecycle:
        assert hasattr(AuditAction, val)
        assert AuditAction[val] == val

    # 3. Approval/sign-off
    approval = ["SIGN_OFF", "SIGN_OFF_VISIT"]
    for val in approval:
        assert hasattr(AuditAction, val)
        assert AuditAction[val] == val

    # 4. Security
    security = ["SECURITY_ALERT"]
    for val in security:
        assert hasattr(AuditAction, val)
        assert AuditAction[val] == val


def test_audit_action_target_service_literals():
    # @req:PRD-SYS-001
    """
    Confirm presence of remaining domain-specific literals emitted by target services.
    """
    from audit import AuditAction

    # eISF domain-specific
    assert AuditAction.CREATE_DOCUMENT == "CREATE_DOCUMENT"
    assert AuditAction.UPDATE_DOCUMENT == "UPDATE_DOCUMENT"
    assert AuditAction.DELETE_DOCUMENT == "DELETE_DOCUMENT"
    assert AuditAction.SYNC == "SYNC"

    # CTMS domain-specific
    ctms_literals = [
        "TRIGGER_MILESTONE",
        "CREATE_STUDY",
        "LIST_STUDIES",
        "VIEW_AUDIT_LOGS",
        "CREATE_VISIT",
        "GENERATE_LETTER",
        "COMPLETE_VISIT",
        "CREATE_FINDING",
        "LIST_VISITS",
        "RETRIEVE_LETTERS",
        "RETRIEVE_LETTER",
        "CREATE_RECRUITMENT_RECORD",
        "LIST_RECRUITMENT_RECORDS",
        "CREATE_MILESTONE",
        "UPDATE_MILESTONE",
        "LIST_SITE_MILESTONES",
        "DEACTIVATE_CRA_ALLOCATION",
        "CREATE_CRA_ALLOCATION",
        "UPDATE_CRA_ALLOCATION",
        "LIST_CRA_ALLOCATIONS",
        "VIEW_WORKLOAD_SUMMARY",
        "CREATE_GRANT",
        "LIST_GRANTS",
        "GET_GRANT",
        "UPDATE_GRANT",
        "CREATE_BUDGET_ITEM",
        "LIST_BUDGET_ITEMS",
        "CREATE_PAYMENT_MILESTONE",
        "LIST_PAYMENT_MILESTONES",
        "MANUAL_TRIGGER_MILESTONE",
        "EVALUATE_MILESTONES",
        "LIST_PAYABLES",
        "MONITORING_VISIT_STRUCTURAL_CONFLICT",
        "MONITORING_VISIT_RECONCILE",
        "DELEGATE_TASK",
        "APPROVE_DELEGATION",
        "REVOKE_DELEGATION",
        "DOA_LOG_MODIFIED",
    ]
    for val in ctms_literals:
        assert getattr(AuditAction, val) == val

    # eConsent domain-specific
    econsent_literals = [
        "ARCHIVAL_ACCEPTED",
        "ARCHIVAL_FAILED",
        "CAPTURE_CONSENT",
        "DEFINE_COMPREHENSION_CHECK",
        "COMPREHENSION_EVALUATION",
        "SIGN_CONSENT",
        "ARCHIVAL_QUEUED",
        "CREATE_TRANSLATION",
        "UPDATE_TRANSLATION",
        "LIST_TRANSLATIONS",
        "VIEW_TRANSLATION",
        "TRANSITION_TRANSLATION",
        "VIEW_APPROVED_TRANSLATION",
        "VIEW_DOCUMENT",
    ]
    for val in econsent_literals:
        assert getattr(AuditAction, val) == val

    # Quality domain-specific
    quality_literals = [
        "DEVIATION_CREATE",
        "DEVIATION_LIST",
        "DEVIATION_VIEW",
        "DEVIATION_UPDATE",
        "CAPA_CREATE",
        "CAPA_TRANSITION",
        "CAPA_UPDATE",
        "AUDIT_LOG_LIST",
    ]
    for val in quality_literals:
        assert getattr(AuditAction, val) == val

    # Safety domain-specific
    safety_literals = [
        "SAFETY_CASE_CREATE",
        "SAFETY_CASE_LIST",
        "SAFETY_CASE_VIEW",
        "SAFETY_EXPORT_JOB_CREATE",
        "SAFETY_EXPORT_JOB_LIST",
        "SAFETY_EXPORT_JOB_VIEW",
        "SAFETY_AUDIT_LOG_LIST",
        "SAE_RECONCILIATION_RUN",
        "RECONCILIATION_ALERT_SENT",
        "RECONCILIATION_ALERT_FAILED",
        "RECONCILIATION_JOB_PROCESSING",
        "RECONCILIATION_JOB_COMPLETED",
        "RECONCILIATION_JOB_FAILED",
        "RECONCILIATION_JOB_CREATE",
        "RECONCILIATION_JOB_LIST",
        "RECONCILIATION_JOB_VIEW",
        "SAE_RECONCILIATION_RUN_LIST",
        "SAE_RECONCILIATION_RUN_VIEW",
        "SAFETY_EXPORT_JOB_COMPLETE",
        "SAFETY_EXPORT_JOB_FAIL",
    ]
    for val in safety_literals:
        assert getattr(AuditAction, val) == val
