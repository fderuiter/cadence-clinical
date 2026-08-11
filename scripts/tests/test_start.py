import sys
from unittest.mock import MagicMock, patch

from scripts import start


def test_find_migration_script():
    # Test with a service that has a migration script
    migrate_path = start.find_migration_script("execution")
    assert migrate_path is not None
    assert "migrate.py" in migrate_path

    # Test with a service that does not have a migration script
    assert start.find_migration_script("designer") is None
    assert start.find_migration_script("non_existent_service") is None


@patch("scripts.start.subprocess.run")
def test_run_pre_boot_migrations_success(mock_run):
    mock_run.return_value = MagicMock(returncode=0)
    
    # Should run fine without raising any exceptions or calling sys.exit
    start.run_pre_boot_migrations("execution", "apps/execution/database/migrate.py")
    
    mock_run.assert_called_once_with([sys.executable, "apps/execution/database/migrate.py"])


@patch("scripts.start.subprocess.run")
@patch("scripts.start.sys.exit")
def test_run_pre_boot_migrations_failure(mock_exit, mock_run):
    mock_run.return_value = MagicMock(returncode=42)
    
    start.run_pre_boot_migrations("execution", "apps/execution/database/migrate.py")
    
    mock_run.assert_called_once_with([sys.executable, "apps/execution/database/migrate.py"])
    mock_exit.assert_called_once_with(42)


@patch("scripts.start.os.name", "posix")
@patch("scripts.start.os.execvp")
def test_run_web_server_posix_success(mock_exec):
    start.run_web_server("designer", "127.0.0.1", 8080, ["--reload", "--log-level", "debug"])
    
    mock_exec.assert_called_once_with(
        "uvicorn",
        [
            "uvicorn",
            "apps.designer.main:app",
            "--host",
            "127.0.0.1",
            "--port",
            "8080",
            "--reload",
            "--log-level",
            "debug",
        ],
    )


@patch("scripts.start.os.name", "posix")
@patch("scripts.start.os.execvp")
def test_run_web_server_posix_fallback(mock_exec):
    # Simulate uvicorn not on path, triggering FileNotFoundError
    mock_exec.side_effect = [FileNotFoundError, None]
    
    start.run_web_server("designer", "127.0.0.1", 8080, ["--reload"])
    
    assert mock_exec.call_count == 2
    mock_exec.assert_any_call(
        "uvicorn",
        [
            "uvicorn",
            "apps.designer.main:app",
            "--host",
            "127.0.0.1",
            "--port",
            "8080",
            "--reload",
        ],
    )
    mock_exec.assert_any_call(
        sys.executable,
        [
            sys.executable,
            "-m",
            "uvicorn",
            "apps.designer.main:app",
            "--host",
            "127.0.0.1",
            "--port",
            "8080",
            "--reload",
        ],
    )


@patch("scripts.start.os.name", "nt")
@patch("scripts.start.subprocess.run")
@patch("scripts.start.sys.exit")
def test_run_web_server_nt(mock_exit, mock_run):
    mock_run.return_value = MagicMock(returncode=0)
    
    start.run_web_server("designer", "127.0.0.1", 8080, [])
    
    mock_run.assert_called_once_with(
        [
            "uvicorn",
            "apps.designer.main:app",
            "--host",
            "127.0.0.1",
            "--port",
            "8080",
        ]
    )
    mock_exit.assert_called_once_with(0)


@patch("scripts.start.run_web_server")
@patch("scripts.start.run_pre_boot_migrations")
@patch("scripts.start.find_migration_script")
def test_main_execution_service(mock_find, mock_migrate, mock_server):
    mock_find.return_value = "apps/execution/database/migrate.py"
    
    start.main(["execution", "--host", "0.0.0.0", "--port", "8002", "--reload"])
    
    mock_find.assert_called_once_with("execution")
    mock_migrate.assert_called_once_with("execution", "apps/execution/database/migrate.py")
    mock_server.assert_called_once_with("execution", "0.0.0.0", 8002, ["--reload"])


@patch("scripts.start.run_web_server")
@patch("scripts.start.run_pre_boot_migrations")
@patch("scripts.start.find_migration_script")
def test_main_designer_service(mock_find, mock_migrate, mock_server):
    mock_find.return_value = None
    
    start.main(["designer", "--host", "0.0.0.0", "--port", "8001"])
    
    mock_find.assert_called_once_with("designer")
    mock_migrate.assert_not_called()
    mock_server.assert_called_once_with("designer", "0.0.0.0", 8001, [])
