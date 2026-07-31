"""
Security verification and single-use validation helper for 21 CFR Part 11 signature tokens (sig_token).
"""

import hashlib
import os
import time
from typing import Any, Dict, Optional
from fastapi import HTTPException
from jose import jwt, JWTError


class TokenConsumptionCache:
    """
    An in-memory cache to track consumed signature tokens (single-use / replay protection).
    """

    def __init__(self) -> None:
        self.consumed_tokens: Dict[str, float] = {}

    def is_consumed(self, token: str, exp: float, jti: Optional[str] = None) -> bool:
        """
        Check if a token has already been consumed.
        """
        now = time.time()
        # Prune expired tokens
        self.consumed_tokens = {
            t: e for t, e in self.consumed_tokens.items() if e > now
        }
        key = jti if jti else token
        return key in self.consumed_tokens

    def consume(self, token: str, exp: float, jti: Optional[str] = None) -> None:
        """
        Consume a token to prevent subsequent reuse.
        """
        key = jti if jti else token
        self.consumed_tokens[key] = exp

    def reset(self) -> None:
        """
        Clear consumed tokens cache (primarily for test isolation).
        """
        self.consumed_tokens.clear()


token_consumption_cache = TokenConsumptionCache()


def verify_and_consume_sig_token(
    sig_token: Optional[str],
    user_id: str,
    request_path: str,
    payload_dict: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """
    Standalone function to verify a signature token (JWT).
    Validates presence, signature, expiration, identity binding, action/path binding,
    single-use replay protection, and batch-id binding.

    If any validation fails, raises HTTPException(status_code=401, detail="REAUTHENTICATION_REQUIRED").
    Otherwise, consumes the token and returns the parsed payload.
    """
    if not sig_token:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()

    try:
        sig_payload = jwt.decode(sig_token, secret, algorithms=["HS256"])
    except JWTError:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Check expiration
    exp = sig_payload.get("exp", 0)
    if exp < time.time():
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Check user binding
    if sig_payload.get("sub") != user_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Check loose path binding
    bound_action = sig_payload.get("action", "")
    request_path_lower = request_path.lower()
    if (
        request_path_lower != bound_action.lower()
        and bound_action.lower() not in request_path_lower
        and request_path_lower not in bound_action.lower()
    ):
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Check replay / single-use protection
    jti = sig_payload.get("jti")
    if token_consumption_cache.is_consumed(sig_token, exp, jti):
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Check batch_id binding if present in token
    token_batch_id = sig_payload.get("batch_id")
    if token_batch_id and payload_dict:
        req_study_id = payload_dict.get("study_id")
        req_target_type = payload_dict.get("target_type", "FORM")
        req_target_ids = (
            payload_dict.get("target_ids")
            or payload_dict.get("target_form_ids")
            or []
        )
        req_signing_reason = payload_dict.get("signing_reason") or payload_dict.get(
            "reason_for_change"
        )

        if req_study_id and req_signing_reason:
            norm_study = str(req_study_id).strip()
            norm_type = str(req_target_type).strip().upper()
            sorted_ids = sorted([str(tid).strip() for tid in req_target_ids])
            norm_ids = ",".join(sorted_ids)
            norm_reason = str(req_signing_reason).strip()

            binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
            computed_batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

            if token_batch_id != computed_batch_id:
                raise HTTPException(
                    status_code=401,
                    detail="REAUTHENTICATION_REQUIRED",
                )

    # Consume the token (single-use / replay protection)
    token_consumption_cache.consume(sig_token, exp, jti)

    return sig_payload
