from enum import Enum

from pydantic import BaseModel, Field


class DetectorCategory(str, Enum):
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


class ComplianceProfile(str, Enum):
    """
    Compliance profiles that govern which PII/PHI categories are active.
    """

    HIPAA = "HIPAA"
    GDPR = "GDPR"
    EU_CTR = "EU_CTR"


# Mapping from compliance profile to enabled detector categories
PROFILE_CATEGORIES = {
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
    ComplianceProfile.EU_CTR: {
        # CTR focuses on patient anonymity in clinical trials: removing direct clinical trial patient identifiers
        DetectorCategory.EMAIL,
        DetectorCategory.TELEPHONE_FAX,
        DetectorCategory.SSN_NATIONAL_ID,
        DetectorCategory.DATES,
        DetectorCategory.MEDICAL_RECORD_ACCOUNT,
        DetectorCategory.AGE,
        DetectorCategory.CUSTOM,
    },
}


class DetectionResult(BaseModel):
    """
    Structured model representing a detected PII/PHI candidate match.
    """

    category: str = Field(..., description="The category of PII/PHI detected")
    start: int = Field(..., description="The character start offset in the source text")
    end: int = Field(..., description="The character end offset in the source text")
    value: str = Field(..., description="The matched text value")
