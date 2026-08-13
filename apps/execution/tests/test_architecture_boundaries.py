from pytest_archon import archrule


def test_clinical_domain_package_isolation():
    """
    Enforce strict clinical domain package isolation via restricted import rules.
    None of the four clinical domains (EDC, RTSM, Medical Coding, Biostatistics)
    may import from one another.
    """
    # 1. Biostatistics isolation
    archrule("Biostat Isolation").match("apps.execution.biostat*").should_not_import(
        "apps.execution.coding*"
    ).should_not_import("apps.execution.rtsm*").should_not_import(
        "apps.execution.edc*"
    ).check("apps")

    # 2. Medical Coding isolation
    archrule("Coding Isolation").match("apps.execution.coding*").should_not_import(
        "apps.execution.biostat*"
    ).should_not_import("apps.execution.rtsm*").should_not_import(
        "apps.execution.edc*"
    ).check("apps")

    # 3. RTSM isolation
    archrule("RTSM Isolation").match("apps.execution.rtsm*").should_not_import(
        "apps.execution.biostat*"
    ).should_not_import("apps.execution.coding*").should_not_import(
        "apps.execution.edc*"
    ).check("apps")

    # 4. EDC isolation
    archrule("EDC Isolation").match("apps.execution.edc*").should_not_import(
        "apps.execution.biostat*"
    ).should_not_import("apps.execution.coding*").should_not_import(
        "apps.execution.rtsm*"
    ).check("apps")
