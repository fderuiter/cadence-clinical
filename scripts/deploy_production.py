#!/usr/bin/env python3
import argparse
import logging
import os
import subprocess
import sys

logging.basicConfig(
    level=logging.INFO,
    format="[%(asctime)s] [GxP-PIPELINE] [%(levelname)s] %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger("ContinuousDeliveryPipeline")


def run_step(name: str, cmd: list, env: dict = None) -> bool:
    logger.info(f"=== STARTING STEP: {name} ===")
    logger.info(f"Command: {' '.join(cmd)}")

    custom_env = dict(os.environ)
    if env:
        custom_env.update(env)
    custom_env["PATH"] = f"/app/bin:{custom_env.get('PATH', '')}"

    try:
        result = subprocess.run(cmd, env=custom_env, check=False)
        if result.returncode == 0:
            logger.info(f"=== SUCCESS: {name} complete ===")
            return True
        else:
            logger.error(
                f"=== FAILURE: {name} failed with exit code {result.returncode} ==="
            )
            return False
    except Exception as e:
        logger.error(f"=== FAILURE: Unexpected error executing step '{name}': {e} ===")
        return False


def main():
    parser = argparse.ArgumentParser(
        description="Fully Automated Continuous Delivery Pipeline Orchestrator"
    )
    parser.add_argument(
        "--ticket-id",
        default=os.getenv("CHANGE_TICKET_ID") or os.getenv("CHANGE_TICKET"),
        help="Change Ticket ID",
    )
    parser.add_argument(
        "--db-url",
        default=os.getenv("DATABASE_URL", "sqlite+aiosqlite:///production.db"),
        help="Database URL",
    )
    parser.add_argument(
        "--image-tag",
        default=os.getenv("GITHUB_SHA", "da39a3ee5e6b4b0d3255bfef95601890afd80709"),
        help="Image tag to deploy",
    )
    parser.add_argument(
        "--color",
        default="blue",
        choices=["blue", "green"],
        help="Active promotion color for blue-green rollout",
    )
    args = parser.parse_args()

    logger.info(
        "Initializing Fully Automated continuous delivery pipeline promotion sequence..."
    )
    logger.info(
        "Regulatory Standard Enforced: FDA 21 CFR Part 11 / EU Annex 11 / GAMP 5"
    )
    logger.info("Target Environment: PRODUCTION (cadence-prod)")
    logger.info(f"Active Release Tag: {args.image_tag}")
    logger.info(f"Active Blue-Green Target: {args.color}")

    ticket_id = args.ticket_id
    if not ticket_id:
        logger.error(
            "Regulatory Block: No CHANGE_TICKET_ID detected. Cannot promote unapproved candidates."
        )
        sys.exit(1)

    # STEP 1: External Audit API Verification (QA Signature Validation)
    logger.info("PHASE 1: External Audit Compliance Validation Gate")
    verify_cmd = [
        sys.executable,
        "scripts/verify_approvals.py",
        "--ticket-id",
        ticket_id,
        "--target",
        "PROD",
    ]
    if not run_step("QA Signature Validation Gate", verify_cmd):
        logger.critical(
            "PRODUCTION PROMOTION ABORTED: Missing clinical QA approvals and signature clearance."
        )
        sys.exit(1)

    # STEP 2: Database Migration & Verification
    logger.info("PHASE 2: Pre-Deployment Database Migrations & Integrity Verification")
    migrate_cmd = [
        sys.executable,
        "apps/execution/database/migrate.py",
        "--db-url",
        args.db_url,
    ]
    if not run_step("Pre-Deployment Schema Migrations", migrate_cmd):
        logger.critical(
            "PRODUCTION PROMOTION ABORTED: Database schema migration failed."
        )
        sys.exit(1)

    # Run pytest schema integrity checks
    test_cmd = [
        "uv",
        "run",
        "--extra",
        "dev",
        "pytest",
        "tests/test_migrate.py",
        "--cov-fail-under=0",
    ]
    if not run_step("Database Schema Verification Suite", test_cmd):
        logger.critical("PRODUCTION PROMOTION ABORTED: Schema integrity checks failed.")
        sys.exit(1)

    # STEP 3: Production Rollout via Helm Upgrade
    logger.info("PHASE 3: Helm Blue-Green Application Promotion Rollout")
    helm_cmd = [
        "helm",
        "upgrade",
        "--install",
        f"cadence-clinical-{args.color}",
        "./docker/helm/cadence-clinical",
        "--namespace",
        "cadence-prod",
        "--values",
        "./docker/helm/values-production.yaml",
        "--set",
        f"global.image.tag={args.image_tag},global.deployment.color={args.color}",
        "--atomic",
        "--timeout",
        "20m0s",
    ]
    if not run_step("Production Namespace Helm Upgrade", helm_cmd):
        logger.critical("PRODUCTION PROMOTION ABORTED: Helm deployment rollout failed.")
        sys.exit(1)

    # STEP 4: Requirements Traceability and Archival Registry Upload
    logger.info(
        "PHASE 4: Compliance Traceability Compilation & eTMF Registry Preservation"
    )
    archive_cmd = [
        sys.executable,
        "scripts/archive_etmf.py",
        "--output-dir",
        "tmp_etmf",
    ]
    if not run_step("eTMF Archival Registry Upload", archive_cmd):
        logger.critical(
            "PRODUCTION PROMOTION ABORTED: eTMF archival registry upload failed."
        )
        sys.exit(1)

    logger.info("=== FULLY AUTOMATED PRODUCTION DEPLOYMENT SEQUENCING SUCCESSFUL ===")
    logger.info("Regulatory Compliance Status: VERIFIED AND COMPLIANT (GxP Category 5)")
    logger.info(
        "Release Promote Sequence ended successfully with zero manual intervention required."
    )


if __name__ == "__main__":
    main()
