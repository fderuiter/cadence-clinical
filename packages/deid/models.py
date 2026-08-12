from enum import StrEnum

from pydantic import BaseModel, Field


class DetectorCategory(StrEnum):
    """
    Standard categories of PII/PHI supported by the detection engine.
    """

    EMAIL = "email"
    TELEPHONE_FAX = "telephone_fax"
    SSN_NATIONAL_ID = "ssn_national_id"
    DATES = "dates"
    ZIP_GEOGRAPHIC = "zip_geographic"
    URLS = "urls"
    IP_MAC_ADDRESSES = "ip_mac_addresses"
    MEDICAL_RECORD_ACCOUNT = "medical_record_account"
    AGE = "age"
    CUSTOM = "custom"


class DynamicStrEnumMeta(type):
    def __getattr__(cls, name):
        if name in cls._members:
            return cls._members[name]
        raise AttributeError(f"'{cls.__name__}' object has no attribute '{name}'")

    def __iter__(cls):
        return iter(cls._members.values())

    def __len__(cls):
        return len(cls._members)

    def __contains__(cls, item):
        if isinstance(item, cls):
            return item._value_ in cls._members
        return item in cls._members or item in [
            m._value_ for m in cls._members.values()
        ]

    def __getitem__(cls, name):
        if name in cls._members:
            return cls._members[name]
        raise KeyError(name)

    @property
    def __members__(cls):
        return cls._members


class DynamicStrEnum(str, metaclass=DynamicStrEnumMeta):
    _members = {}

    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        cls._members = {}
        # Parse initial class attributes
        for k, v in list(cls.__dict__.items()):
            if not k.startswith("_") and isinstance(v, str):
                cls._add_member(k, v)

    def __new__(cls, value):
        obj = str.__new__(cls, value)
        obj._value_ = value
        obj._name_ = value
        return obj

    @property
    def value(self):
        return self._value_

    @property
    def name(self):
        return self._name_

    @classmethod
    def __get_pydantic_core_schema__(cls, _source_type, _handler):
        from pydantic_core import core_schema

        return core_schema.str_schema()

    @classmethod
    def _add_member(cls, name, value):
        inst = cls(value)
        inst._name_ = name
        setattr(cls, name, inst)
        cls._members[name] = inst


class ComplianceProfile(DynamicStrEnum):
    """
    Compliance profiles that govern which PII/PHI categories are active.
    """

    _members = {}

    HIPAA = "HIPAA"
    GDPR = "GDPR"


# Mapping from compliance profile to enabled detector categories
PROFILE_CATEGORIES: dict[ComplianceProfile, set[DetectorCategory]] = {
    ComplianceProfile.HIPAA: {
        DetectorCategory.EMAIL,
        DetectorCategory.TELEPHONE_FAX,
        DetectorCategory.SSN_NATIONAL_ID,
        DetectorCategory.DATES,
        DetectorCategory.ZIP_GEOGRAPHIC,
        DetectorCategory.URLS,
        DetectorCategory.IP_MAC_ADDRESSES,
        DetectorCategory.MEDICAL_RECORD_ACCOUNT,
        DetectorCategory.AGE,
        DetectorCategory.CUSTOM,
    },
    ComplianceProfile.GDPR: {
        DetectorCategory.EMAIL,
        DetectorCategory.TELEPHONE_FAX,
        DetectorCategory.SSN_NATIONAL_ID,
        DetectorCategory.DATES,
        DetectorCategory.ZIP_GEOGRAPHIC,
        DetectorCategory.URLS,
        DetectorCategory.IP_MAC_ADDRESSES,
        DetectorCategory.MEDICAL_RECORD_ACCOUNT,
        DetectorCategory.AGE,
        DetectorCategory.CUSTOM,
    },
}


def register_compliance_profile(name: str, categories: set[DetectorCategory]):
    """Dynamically register a compliance profile and its active categories."""
    ComplianceProfile._add_member(name, name)
    PROFILE_CATEGORIES[ComplianceProfile[name]] = categories


class DetectionResult(BaseModel):
    """
    Structured model representing a detected PII/PHI candidate match.
    """

    category: str = Field(..., description="The category of PII/PHI detected")
    start: int = Field(..., description="The character start offset in the source text")
    end: int = Field(..., description="The character end offset in the source text")
    value: str = Field(..., description="The matched text value")
