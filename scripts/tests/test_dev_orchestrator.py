import unittest
from unittest.mock import MagicMock, patch

from scripts import dev_orchestrator


class TestDevOrchestrator(unittest.TestCase):
    def test_build_compose_command_designer(self):
        action, profiles, cmd = dev_orchestrator.build_compose_command(
            action_or_profile="designer",
            secondary=None,
            profiles_opt=None,
            compose_file="/app/cadence-clinical/docker/docker-compose.yml",
            extra_args=[],
        )
        self.assertEqual(action, "up")
        self.assertEqual(profiles, ["designer"])
        self.assertEqual(
            cmd,
            [
                "docker",
                "compose",
                "-f",
                "/app/cadence-clinical/docker/docker-compose.yml",
                "--profile",
                "designer",
                "up",
                "-d",
            ],
        )

    def test_build_compose_command_execution_with_flag(self):
        action, profiles, cmd = dev_orchestrator.build_compose_command(
            action_or_profile="up",
            secondary=None,
            profiles_opt=["execution"],
            compose_file="/app/cadence-clinical/docker/docker-compose.yml",
            extra_args=[],
        )
        self.assertEqual(action, "up")
        self.assertEqual(profiles, ["execution"])
        self.assertEqual(
            cmd,
            [
                "docker",
                "compose",
                "-f",
                "/app/cadence-clinical/docker/docker-compose.yml",
                "--profile",
                "execution",
                "up",
                "-d",
            ],
        )

    def test_build_compose_command_down_operations(self):
        action, profiles, cmd = dev_orchestrator.build_compose_command(
            action_or_profile="down",
            secondary="operations",
            profiles_opt=None,
            compose_file="/app/cadence-clinical/docker/docker-compose.yml",
            extra_args=[],
        )
        self.assertEqual(action, "down")
        self.assertEqual(profiles, ["operations"])
        self.assertEqual(
            cmd,
            [
                "docker",
                "compose",
                "-f",
                "/app/cadence-clinical/docker/docker-compose.yml",
                "--profile",
                "operations",
                "down",
            ],
        )

    def test_build_compose_command_all(self):
        action, profiles, cmd = dev_orchestrator.build_compose_command(
            action_or_profile="all",
            secondary=None,
            profiles_opt=None,
            compose_file="/app/cadence-clinical/docker/docker-compose.yml",
            extra_args=[],
        )
        self.assertEqual(action, "up")
        self.assertEqual(profiles, ["designer", "execution", "operations"])
        self.assertEqual(
            cmd,
            [
                "docker",
                "compose",
                "-f",
                "/app/cadence-clinical/docker/docker-compose.yml",
                "--profile",
                "designer",
                "--profile",
                "execution",
                "--profile",
                "operations",
                "up",
                "-d",
            ],
        )

    def test_build_compose_command_no_detach(self):
        action, profiles, cmd = dev_orchestrator.build_compose_command(
            action_or_profile="up",
            secondary=None,
            profiles_opt=["designer"],
            compose_file="/app/cadence-clinical/docker/docker-compose.yml",
            extra_args=["--no-detach"],
        )
        self.assertEqual(action, "up")
        self.assertEqual(profiles, ["designer"])
        self.assertNotIn("-d", cmd)
        self.assertNotIn("--no-detach", cmd)

    def test_main_dry_run(self):
        ret = dev_orchestrator.main(["designer", "--dry-run"])
        self.assertEqual(ret, 0)

    @patch("scripts.dev_orchestrator.subprocess.run")
    def test_main_executes_subprocess(self, mock_run):
        mock_run.return_value = MagicMock(returncode=0)
        ret = dev_orchestrator.main(["execution"])
        self.assertEqual(ret, 0)
        mock_run.assert_called_once()


if __name__ == "__main__":
    unittest.main()
