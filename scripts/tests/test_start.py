import sys
from unittest.mock import MagicMock, patch

from scripts import start


def test_find_migration_script():
    # Test with a service that has a migration script defined in registry
    migrate_path = start.find_migration_script("etmf")
    assert migrate_path is not None
    assert "migrate.py" in migrate_path

    # Test with a service that does not have a migration script
    assert start.find_migration_script("designer") is None
    assert start.find_migration_script("non_existent_service") is None


@patch("scripts.start.subprocess.run")
def test_run_pre_boot_migrations_success(mock_run):
    mock_run.return_value = MagicMock(returncode=0)

    # Should run fine without raising any exceptions or calling sys.exit
    start.run_pre_boot_migrations("etmf", "apps/etmf/migrate.py")

    mock_run.assert_called_once()
    assert mock_run.call_args[0][0] == [
        sys.executable,
        "apps/etmf/migrate.py",
    ]
    assert "PYTHONPATH" in mock_run.call_args[1]["env"]


@patch("scripts.start.subprocess.run")
@patch("scripts.start.sys.exit")
def test_run_pre_boot_migrations_failure(mock_exit, mock_run):
    mock_run.return_value = MagicMock(returncode=42)

    start.run_pre_boot_migrations("etmf", "apps/etmf/migrate.py")

    mock_run.assert_called_once()
    assert mock_run.call_args[0][0] == [
        sys.executable,
        "apps/etmf/migrate.py",
    ]
    assert "PYTHONPATH" in mock_run.call_args[1]["env"]
    mock_exit.assert_called_once_with(42)


@patch("scripts.start.os.name", "posix")
@patch("scripts.start.os.execvp")
def test_run_web_server_posix_success(mock_exec):
    start.run_web_server("designer", "127.0.0.1", 8001, ["--log-level", "debug"])

    mock_exec.assert_called_once_with(
        "dotnet",
        [
            "dotnet",
            "run",
            "--project",
            "apps/designer/src/Cadence.Apps.Designer.csproj",
            "--log-level",
            "debug",
        ],
    )


@patch("scripts.start.os.name", "nt")
@patch("scripts.start.subprocess.run")
@patch("scripts.start.sys.exit")
def test_run_web_server_nt(mock_exit, mock_run):
    mock_run.return_value = MagicMock(returncode=0)

    start.run_web_server("designer", "127.0.0.1", 8001, [])

    mock_run.assert_called_once_with(
        [
            "dotnet",
            "run",
            "--project",
            "apps/designer/src/Cadence.Apps.Designer.csproj",
        ],
        env=start.os.environ,
    )
    mock_exit.assert_called_once_with(0)


@patch("scripts.start.run_web_server")
@patch("scripts.start.run_pre_boot_migrations")
@patch("scripts.start.find_migration_script")
def test_main_execution_service(mock_find, mock_migrate, mock_server):
    mock_find.return_value = None

    start.main(["execution", "--host", "0.0.0.0", "--port", "8002", "--reload"])  # nosec B104

    mock_find.assert_called_once_with("execution")
    mock_migrate.assert_not_called()
    mock_server.assert_called_once_with("execution", "0.0.0.0", 8002, ["--reload"])  # nosec B104


@patch("scripts.start.run_web_server")
@patch("scripts.start.run_pre_boot_migrations")
@patch("scripts.start.find_migration_script")
def test_main_etmf_service(mock_find, mock_migrate, mock_server):
    mock_find.return_value = "apps/etmf/migrate.py"

    start.main(["etmf", "--host", "0.0.0.0", "--port", "8003"])  # nosec B104

    mock_find.assert_called_once_with("etmf")
    mock_migrate.assert_called_once_with("etmf", "apps/etmf/migrate.py")
    mock_server.assert_called_once_with("etmf", "0.0.0.0", 8003, [])  # nosec B104
