from pydantic import BaseModel


class ExportBundleRequest(BaseModel):
    """Pydantic schema representing clinical dataset export request parameters."""

    study_id: str
