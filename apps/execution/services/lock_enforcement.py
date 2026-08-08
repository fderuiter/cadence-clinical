"""Execution enforcement preventing modifications to frozen or locked clinical eCRF fields.

Requirements: PRD-SYS-001
"""

from typing import Any

import packages  # noqa: F401
from apps.execution.domain.lock_models import (
    DataLockRecord,
    LockScopeEnum,
    LockStatusEnum,
)


class FormLockedError(Exception):
    """Exception raised when an attempt is made to modify locked or frozen eCRF data.

    Requirements: PRD-SYS-001
    """

    pass


class DataLockEnforcer:
    """Enforcement engine validating data locks prior to eCRF data mutations.

    Requirements: PRD-SYS-001
    """

    def assert_submission_allowed(
        self,
        form_id: str,
        field_updates: dict[str, Any],
        active_locks: list[DataLockRecord],
    ) -> None:
        """Validate that target eCRF form or field updates do not violate active data locks.

        Args:
            form_id: Target eCRF form ID.
            field_updates: Dictionary of field variable names and values to update.
            active_locks: List of active DataLockRecord instances for the form.

        Raises:
            FormLockedError: If form or target fields are in LOCKED or FROZEN status.
        """
        for lock in active_locks:
            if lock.form_id != form_id:
                continue

            if lock.status in (LockStatusEnum.LOCKED, LockStatusEnum.FROZEN):
                # Form-level lock check
                if lock.scope == LockScopeEnum.FORM:
                    raise FormLockedError(
                        f"eCRF form '{form_id}' is in {lock.status.value} state. Modifications blocked."
                    )

                # Field-level lock check
                if (
                    lock.scope == LockScopeEnum.FIELD
                    and lock.field_name
                    and lock.field_name in field_updates
                ):
                    raise FormLockedError(
                        f"Field '{lock.field_name}' on form '{form_id}' is in {lock.status.value} state. Modifications blocked."
                    )
