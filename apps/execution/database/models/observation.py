from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class ClinicalObservation(AuditedModel):
    """Represents a specific clinical trial measurement observation.

    Stores normalized values, original and normalized units, and outlier flags
    for individual parameters (e.g., vital signs, lab test measurements).
    """

    __tablename__ = "clinical_observations"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    domain: Mapped[str] = mapped_column(String(50), nullable=False)
    observation_date: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    test_code: Mapped[str] = mapped_column(String(100), nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    value: Mapped[float] = mapped_column(Float, nullable=True)
    value_string: Mapped[str] = mapped_column(String, nullable=True)
    unit: Mapped[str] = mapped_column(String(50), nullable=True)
    normalized_value: Mapped[float] = mapped_column(Float, nullable=True)
    normalized_unit: Mapped[str] = mapped_column(String(50), nullable=True)
    is_outlier: Mapped[bool] = mapped_column(Boolean, default=False)
    is_sdv_verified: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    sdv_verified_by: Mapped[str] = mapped_column(String(255), nullable=True)
    sdv_verified_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    page_id: Mapped[str] = mapped_column(String(255), nullable=True)

    is_sdv_flagged: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    sdv_flag_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Dynamic JSON column for laboratory and custom properties (ADR-117)
    additional_properties: Mapped[dict | None] = mapped_column(
        JSON, default=dict, nullable=True
    )

    protocol_version_tag: Mapped[str | None] = mapped_column(String(50), nullable=True)
    protocol_version_index: Mapped[int | None] = mapped_column(Integer, nullable=True)

    def __init__(self, **kwargs):
        if "additional_properties" not in kwargs:
            kwargs["additional_properties"] = {}
        # Pop standard removed attributes from kwargs to avoid SQLAlchemy errors and put them in additional_properties
        removed_attrs = [
            "lab_source",
            "lab_site_id",
            "lab_indicator",
            "lab_out_of_range",
            "matched_normal_bounds",
            "range_indicator",
            "is_out_of_range",
            "reference_range_low",
            "reference_range_high",
        ]
        for attr in removed_attrs:
            if attr in kwargs:
                kwargs["additional_properties"][attr] = kwargs.pop(attr)
        super().__init__(**kwargs)

    def _get_prop(self, key: str) -> Any:
        if self.additional_properties is None:
            return None
        return self.additional_properties.get(key)

    def _set_prop(self, key: str, value: Any) -> None:
        props = dict(self.additional_properties or {})
        props[key] = value
        self.additional_properties = props
        from sqlalchemy.orm.attributes import flag_modified

        flag_modified(self, "additional_properties")

    @property
    def lab_source(self) -> str | None:
        return self._get_prop("lab_source")

    @lab_source.setter
    def lab_source(self, value: str | None) -> None:
        self._set_prop("lab_source", value)

    @property
    def lab_site_id(self) -> str | None:
        return self._get_prop("lab_site_id")

    @lab_site_id.setter
    def lab_site_id(self, value: str | None) -> None:
        self._set_prop("lab_site_id", value)

    @property
    def lab_indicator(self) -> str | None:
        return self._get_prop("lab_indicator")

    @lab_indicator.setter
    def lab_indicator(self, value: str | None) -> None:
        self._set_prop("lab_indicator", value)

    @property
    def lab_out_of_range(self) -> bool | None:
        return self._get_prop("lab_out_of_range")

    @lab_out_of_range.setter
    def lab_out_of_range(self, value: bool | None) -> None:
        self._set_prop("lab_out_of_range", value)

    @property
    def matched_normal_bounds(self) -> str | None:
        return self._get_prop("matched_normal_bounds")

    @matched_normal_bounds.setter
    def matched_normal_bounds(self, value: str | None) -> None:
        self._set_prop("matched_normal_bounds", value)

    @property
    def range_indicator(self) -> str | None:
        return self._get_prop("range_indicator")

    @range_indicator.setter
    def range_indicator(self, value: str | None) -> None:
        self._set_prop("range_indicator", value)

    @property
    def is_out_of_range(self) -> bool | None:
        return self._get_prop("is_out_of_range")

    @is_out_of_range.setter
    def is_out_of_range(self, value: bool | None) -> None:
        self._set_prop("is_out_of_range", value)

    @property
    def reference_range_low(self) -> float | None:
        return self._get_prop("reference_range_low")

    @reference_range_low.setter
    def reference_range_low(self, value: float | None) -> None:
        self._set_prop("reference_range_low", value)

    @property
    def reference_range_high(self) -> float | None:
        return self._get_prop("reference_range_high")

    @reference_range_high.setter
    def reference_range_high(self, value: float | None) -> None:
        self._set_prop("reference_range_high", value)

    def matches_coordinates(self, other: Any) -> bool:
        """Determines if this observation shares coordinates with another coordinate source.

        Evaluates the complete coordinate identifier, specifically including subject_id,
        visit_id, domain, and site_id.
        """
        if hasattr(other, "subject_id"):
            return (
                self.subject_id == other.subject_id
                and self.visit_id == other.visit_id
                and self.domain == other.domain
                and self.site_id == other.site_id
            )
        if isinstance(other, dict):
            return (
                self.subject_id == other.get("subject_id")
                and self.visit_id == other.get("visit_id")
                and self.domain == other.get("domain")
                and self.site_id == other.get("site_id")
            )
        if isinstance(other, tuple) and len(other) == 4:
            return (
                self.subject_id == other[0]
                and self.visit_id == other[1]
                and self.domain == other[2]
                and self.site_id == other[3]
            )
        return False
