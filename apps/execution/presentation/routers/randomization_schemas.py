from datetime import datetime

from pydantic import BaseModel


class SubjectRandomizationResponse(BaseModel):
    """Pydantic schema for returning blinded subject randomization details."""

    subject_id: str
    status: str
    stratum_key: str | None = None
    randomized_at: datetime
    kit_reference: str | None = None
    treatment_arm: str | None = None
