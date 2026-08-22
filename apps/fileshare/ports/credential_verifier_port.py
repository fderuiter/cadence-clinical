"""Port interface for user re-authentication credential verification (21 CFR Part 11).

Requirements: PRD-SYS-001, PRD-DOC-001
"""

from abc import ABC, abstractmethod


class CredentialVerifierPort(ABC):
    """Port for verifying user credentials (PIN/password) during Part 11 re-authentication."""

    @abstractmethod
    async def verify_credential(self, user_id: str, credential: str) -> bool:
        """Verify user re-authentication credential against identity provider.

        Args:
            user_id: The ID of the authenticated user.
            credential: The re-entered PIN or password.

        Returns:
            True if credentials are valid; False otherwise.
        """
        ...
