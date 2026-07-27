import os
import subprocess
import sys


def test_verify_approvals_success(monkeypatch):
    monkeypatch.setenv("MOCK_AUDIT_SERVICE", "true")
    monkeypatch.setenv("CHANGE_TICKET_ID", "CHG-TEST-1111")

    cmd = [sys.executable, "scripts/verify_approvals.py"]
    result = subprocess.run(cmd, capture_output=True, text=True)
    assert result.returncode == 0
    assert "QA Signature verification complete." in result.stderr


def test_verify_approvals_failure_missing_ticket(monkeypatch):
    monkeypatch.delenv("CHANGE_TICKET_ID", raising=False)
    monkeypatch.delenv("CHANGE_TICKET", raising=False)

    cmd = [sys.executable, "scripts/verify_approvals.py"]
    result = subprocess.run(cmd, capture_output=True, text=True)
    assert result.returncode != 0
    assert "GxP Verification Failed" in result.stderr


def test_archive_etmf_creates_files_and_hashes(tmp_path, monkeypatch):
    output_dir = tmp_path / "etmf_out"
    monkeypatch.setenv("MOCK_ETMF_SERVICE", "true")

    cmd = [sys.executable, "scripts/archive_etmf.py", "--output-dir", str(output_dir)]
    result = subprocess.run(cmd, capture_output=True, text=True)
    assert result.returncode == 0

    assert (output_dir / "Requirements_Traceability_Matrix.md").is_file()
    assert (output_dir / "IQ_OQ_PQ_Execution_Report.md").is_file()

    assert "Cryptographic Audit Trail" in result.stderr
    assert "SHA-256" in result.stderr


def test_deploy_orchestrator_success(tmp_path, monkeypatch):
    monkeypatch.setenv("MOCK_AUDIT_SERVICE", "true")
    monkeypatch.setenv("MOCK_ETMF_SERVICE", "true")
    monkeypatch.setenv("CHANGE_TICKET_ID", "CHG-TEST-ROLLOUT")

    db_file = tmp_path / "test_prod.db"
    db_url = f"sqlite+aiosqlite:///{db_file}"

    cmd = [
        sys.executable,
        "scripts/deploy_production.py",
        "--db-url",
        db_url,
        "--ticket-id",
        "CHG-TEST-ROLLOUT",
    ]

    custom_env = dict(os.environ)
    custom_env["PATH"] = f"/app/bin:{custom_env.get('PATH', '')}"

    result = subprocess.run(cmd, env=custom_env, capture_output=True, text=True)
    assert result.returncode == 0
    assert "QA Signature Validation Gate complete" in result.stderr
    assert "Pre-Deployment Schema Migrations complete" in result.stderr
    assert "Production Namespace Helm Upgrade complete" in result.stderr
    assert "eTMF Archival Registry Upload complete" in result.stderr
    assert (
        "FULLY AUTOMATED PRODUCTION DEPLOYMENT SEQUENCING SUCCESSFUL" in result.stderr
    )
