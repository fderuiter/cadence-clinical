import re
from enum import Enum
from typing import Any, Dict, List, Optional, Union


class SemanticAction(str, Enum):
    # Coordinated semantic action identifiers with #689
    CAPA_CLOSE = "quality.capa.close"
    CAPA_CANCEL = "quality.capa.cancel"
    GRANT_APPROVE = "ctms.grant.approve"

    # Existing path-gated Execution actions
    EXEC_FORM_APPROVE = "execution.form.approve"
    EXEC_FORM_SIGNOFF = "execution.form.signoff"
    EXEC_SUBJECT_UNBLIND = "execution.subject.unblind"
    EXEC_SUBJECT_RANDOMIZE = "execution.subject.randomize"
    EXEC_QUERIES_SYNC = "execution.queries.sync"
    GENERIC_CLOSE = "generic.close"


class DetectionRule:
    def __init__(
        self,
        action: SemanticAction,
        methods: List[str],
        path_pattern: str,
        is_regex: bool = False,
        body_conditions: Optional[Dict[str, Union[Any, List[Any]]]] = None,
    ):
        self.action = action
        self.methods = [m.upper() for m in methods]
        self.path_pattern = path_pattern
        self.is_regex = is_regex
        self.body_conditions = body_conditions or {}

    def matches_path(self, method: str, path: str) -> bool:
        if method.upper() not in self.methods:
            return False
        path_lower = path.lower()
        if self.is_regex:
            return bool(re.search(self.path_pattern, path_lower))
        else:
            return self.path_pattern in path_lower

    def matches_body(self, body: Optional[dict]) -> bool:
        if not self.body_conditions:
            return True
        if body is None:
            return False
        for field, expected_val in self.body_conditions.items():
            if field not in body:
                return False
            actual_val = body[field]
            if isinstance(expected_val, (list, set, tuple)):
                # Normalize values to uppercase strings for robust comparison if they are strings
                norm_expected = [str(v).upper() for v in expected_val]
                if str(actual_val).upper() not in norm_expected:
                    return False
            else:
                if str(actual_val).upper() != str(expected_val).upper():
                    return False
        return True


# Stable detection rules mapping
DETECTION_RULES: List[DetectionRule] = [
    # Body-driven rules
    DetectionRule(
        action=SemanticAction.CAPA_CLOSE,
        methods=["POST", "PUT"],
        path_pattern=r"quality/capas/[^/]+/transition",
        is_regex=True,
        body_conditions={"to_status": ["CLOSED", "Closed", "closed"]},
    ),
    DetectionRule(
        action=SemanticAction.CAPA_CANCEL,
        methods=["POST", "PUT"],
        path_pattern=r"quality/capas/[^/]+/transition",
        is_regex=True,
        body_conditions={"to_status": ["CANCELLED", "Cancelled", "cancelled"]},
    ),
    DetectionRule(
        action=SemanticAction.GRANT_APPROVE,
        methods=["PUT", "PATCH", "POST"],
        path_pattern=r"ctms/grants/[^/]+$",
        is_regex=True,
        body_conditions={"status": ["APPROVED", "Approved", "approved"]},
    ),
    # Path-only/substring rules
    DetectionRule(
        action=SemanticAction.EXEC_FORM_APPROVE,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="approve",
        is_regex=False,
    ),
    DetectionRule(
        action=SemanticAction.EXEC_FORM_SIGNOFF,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="sign-off",
        is_regex=False,
    ),
    DetectionRule(
        action=SemanticAction.EXEC_SUBJECT_UNBLIND,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="unblind",
        is_regex=False,
    ),
    DetectionRule(
        action=SemanticAction.EXEC_SUBJECT_RANDOMIZE,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="randomize",
        is_regex=False,
    ),
    DetectionRule(
        action=SemanticAction.EXEC_QUERIES_SYNC,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="queries/sync",
        is_regex=False,
    ),
    DetectionRule(
        action=SemanticAction.GENERIC_CLOSE,
        methods=["POST", "PUT", "PATCH", "DELETE"],
        path_pattern="close",
        is_regex=False,
    ),
]


def resolve_regulated_action(
    method: str, path: str, body: Optional[dict]
) -> Optional[SemanticAction]:
    """
    Pure resolver function to determine the semantic regulated action based on HTTP method, path, and parsed body.
    """
    # 1. First, check body-driven rules. If path matches a body-driven rule,
    # we evaluate body conditions. If conditions match, return action.
    # If path matches but body conditions do NOT match, we return None (unguated transition).
    body_driven_matched_path = False
    for rule in DETECTION_RULES:
        if rule.body_conditions and rule.matches_path(method, path):
            body_driven_matched_path = True
            if rule.matches_body(body):
                return rule.action

    if body_driven_matched_path:
        return None

    # 2. Check path-only rules.
    for rule in DETECTION_RULES:
        if not rule.body_conditions and rule.matches_path(method, path):
            return rule.action

    return None


def resolve_regulated_action_by_path(
    method: str, path: str
) -> Optional[SemanticAction]:
    """
    Path-only variant of resolver for fast middleware pre-checks where parsed body is not yet available.
    """
    for rule in DETECTION_RULES:
        if rule.matches_path(method, path):
            return rule.action
    return None
