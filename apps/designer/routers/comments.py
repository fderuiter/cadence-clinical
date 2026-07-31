"""FastAPI router for collaborative review, form field-level inline comment threads.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, Field

from apps.designer.db import MOCK_DESIGNER_AUDIT_LOGS
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/designer", tags=["FormComments"])


class CommentCreatePayload(BaseModel):
    field_id: str = Field(
        ..., description="The ID of the eCRF field this comment anchors to"
    )
    comment_text: str = Field(..., description="The text content of the comment")


class FormReviewCommentResponse(BaseModel):
    id: str
    form_id: str
    field_id: str
    author_id: str
    comment_text: str
    status: str
    created_at: str

    # Frontend compatibility fields
    isResolved: bool
    authorName: str
    createdAt: str
    text: str


# Global in-memory storage for FormReviewComments
MOCK_FORM_COMMENTS: List[Dict[str, Any]] = []


@router.get("/forms/{form_id}/comments", response_model=List[FormReviewCommentResponse])
async def get_form_comments(
    form_id: str,
    current_user: dict = Depends(get_current_user),
) -> List[FormReviewCommentResponse]:
    """Fetch all review comments for a given form."""
    results = []
    for c in MOCK_FORM_COMMENTS:
        if c["form_id"] == form_id:
            results.append(
                FormReviewCommentResponse(
                    id=c["id"],
                    form_id=c["form_id"],
                    field_id=c["field_id"],
                    author_id=c["author_id"],
                    comment_text=c["comment_text"],
                    status=c["status"],
                    created_at=c["created_at"],
                    isResolved=(c["status"] == "Resolved"),
                    authorName=c["author_id"],
                    createdAt=c["created_at"],
                    text=c["comment_text"],
                )
            )
    return results


@router.post(
    "/forms/{form_id}/comments",
    response_model=FormReviewCommentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def post_form_comment(
    form_id: str,
    payload: CommentCreatePayload,
    current_user: dict = Depends(get_current_user),
) -> FormReviewCommentResponse:
    """Post a new review comment anchored to a field_id."""
    author_id = current_user.get("sub") if current_user else "test_author"
    comment_id = f"comment_{uuid.uuid4().hex[:12]}"
    created_at_str = datetime.now(timezone.utc).isoformat()

    new_comment = {
        "id": comment_id,
        "form_id": form_id,
        "field_id": payload.field_id,
        "author_id": author_id,
        "comment_text": payload.comment_text,
        "status": "Open",
        "created_at": created_at_str,
    }
    MOCK_FORM_COMMENTS.append(new_comment)

    return FormReviewCommentResponse(
        id=comment_id,
        form_id=form_id,
        field_id=payload.field_id,
        author_id=author_id,
        comment_text=payload.comment_text,
        status="Open",
        created_at=created_at_str,
        isResolved=False,
        authorName=author_id,
        createdAt=created_at_str,
        text=payload.comment_text,
    )


@router.patch(
    "/comments/{comment_id}/resolve", response_model=FormReviewCommentResponse
)
async def resolve_comment(
    comment_id: str,
    current_user: dict = Depends(get_current_user),
) -> FormReviewCommentResponse:
    """Mark a comment thread/item as resolved and log a GxP audit event."""
    actor_id = current_user.get("sub") if current_user else "test_author"

    found_comment = None
    for c in MOCK_FORM_COMMENTS:
        if c["id"] == comment_id:
            found_comment = c
            break

    if not found_comment:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Comment with ID {comment_id} not found.",
        )

    # Transition status
    found_comment["status"] = "Resolved"

    # Part 11 Compliant GxP Audit Log entry
    audit_entry = {
        "id": str(uuid.uuid4()),
        "actor": actor_id,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "change_reason": f"Resolved review comment thread: {comment_id}",
        "comment_id": comment_id,
        "type": "FORM_COMMENT_RESOLVE",
    }
    MOCK_DESIGNER_AUDIT_LOGS.append(audit_entry)

    return FormReviewCommentResponse(
        id=found_comment["id"],
        form_id=found_comment["form_id"],
        field_id=found_comment["field_id"],
        author_id=found_comment["author_id"],
        comment_text=found_comment["comment_text"],
        status="Resolved",
        created_at=found_comment["created_at"],
        isResolved=True,
        authorName=found_comment["author_id"],
        createdAt=found_comment["created_at"],
        text=found_comment["comment_text"],
    )
