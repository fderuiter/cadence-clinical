"""Age-of-majority evaluation and adult re-consent milestone engine for eConsent.

Provides deterministic calculation of age, jurisdiction-specific legal age thresholds,
and detection of minor participants transitioning to legal adulthood requiring adult re-consent.
Complies with FDA 21 CFR Part 11, EU CTR 536/2014, and ICH GCP E6(R2)/(R3).
"""

from dataclasses import dataclass
from datetime import date, datetime
from typing import Any

# Standard legal age of majority by ISO jurisdiction code or country/region
JURISDICTION_LEGAL_AGES: dict[str, int] = {
    "DEFAULT": 18,
    "US": 18,
    "US-AL": 19,  # Alabama
    "US-NE": 19,  # Nebraska
    "US-MS": 21,  # Mississippi
    "US-PR": 21,  # Puerto Rico
    "CA": 18,  # Canada Federal / Default
    "CA-AB": 18,  # Alberta
    "CA-BC": 19,  # British Columbia
    "CA-MB": 18,  # Manitoba
    "CA-NB": 19,  # New Brunswick
    "CA-NL": 19,  # Newfoundland and Labrador
    "CA-NT": 19,  # Northwest Territories
    "CA-NS": 19,  # Nova Scotia
    "CA-NU": 19,  # Nunavut
    "CA-ON": 18,  # Ontario
    "CA-PE": 18,  # Prince Edward Island
    "CA-QC": 18,  # Quebec
    "CA-SK": 18,  # Saskatchewan
    "CA-YT": 19,  # Yukon
    "GB": 18,  # United Kingdom
    "EU": 18,  # European Union member states standard
    "DE": 18,  # Germany
    "FR": 18,  # France
    "IT": 18,  # Italy
    "ES": 18,  # Spain
    "NL": 18,  # Netherlands
    "JP": 18,  # Japan (Civil Code revised 2022)
    "KR": 19,  # South Korea
    "AU": 18,  # Australia
    "NZ": 18,  # New Zealand
    "CH": 18,  # Switzerland
    "BR": 18,  # Brazil
    "IN": 18,  # India
}


@dataclass(frozen=True)
class MajorityMilestoneResult:
    """Evaluation result for an individual subject's minor-to-adult milestone status."""

    subject_pseudonym: str
    date_of_birth: str
    jurisdiction: str
    current_age_years: int
    exact_age: float
    legal_age_threshold: int
    majority_reached_date: str
    has_reached_majority: bool
    is_transition_due: bool
    days_until_majority: int
    requires_adult_reconsent: bool
    milestone_status: str


def get_legal_age_threshold(jurisdiction: str | None = None) -> int:
    """Resolves the legal age of majority threshold for a given jurisdiction code."""
    if not jurisdiction:
        return JURISDICTION_LEGAL_AGES["DEFAULT"]
    normalized = jurisdiction.strip().upper()
    return JURISDICTION_LEGAL_AGES.get(normalized, JURISDICTION_LEGAL_AGES["DEFAULT"])


def _parse_date(d: date | datetime | str) -> date:
    """Parses a date, datetime, or ISO string to a datetime.date object."""
    if isinstance(d, datetime):
        return d.date()
    if isinstance(d, date):
        return d
    if isinstance(d, str):
        cleaned = d.strip().split("T")[0]
        return date.fromisoformat(cleaned)
    raise ValueError(f"Unsupported date format or type: {type(d)}")


def _make_safe_date(year: int, month: int, day: int) -> date:
    """Creates a date safely handling leap day on non-leap years."""
    try:
        return date(year, month, day)
    except ValueError:
        return date(year, 3, 1)


def calculate_exact_age(
    date_of_birth: date | datetime | str,
    as_of_date: date | datetime | str | None = None,
) -> tuple[int, float]:
    """Calculates both the integer completed years and exact floating point age.

    Handles leap years deterministically.
    """
    dob = _parse_date(date_of_birth)
    target = _parse_date(as_of_date) if as_of_date is not None else date.today()

    if target < dob:
        raise ValueError(f"as_of_date ({target}) cannot precede date_of_birth ({dob}).")

    # Integer completed years
    years = target.year - dob.year
    if (target.month, target.day) < (dob.month, dob.day):
        years -= 1

    # Exact fractional age
    last_bday = _make_safe_date(dob.year + years, dob.month, dob.day)
    next_bday = _make_safe_date(dob.year + years + 1, dob.month, dob.day)
    year_days = (next_bday - last_bday).days
    progress_days = (target - last_bday).days

    fraction = progress_days / year_days if year_days > 0 else 0.0
    exact_age = round(years + fraction, 3)

    return years, exact_age


def calculate_majority_date(
    date_of_birth: date | datetime | str,
    legal_age_threshold: int,
) -> date:
    """Calculates the exact date a subject attains legal majority."""
    dob = _parse_date(date_of_birth)
    try:
        majority_year = dob.year + legal_age_threshold
        return date(majority_year, dob.month, dob.day)
    except ValueError:
        # Handles Feb 29 leap day born individuals turning majority in non-leap year
        return date(majority_year, 3, 1)


def evaluate_subject_majority_milestone(
    subject_pseudonym: str,
    date_of_birth: date | datetime | str,
    jurisdiction: str | None = "DEFAULT",
    as_of_date: date | datetime | str | None = None,
    lead_time_days: int = 30,
    current_consent_role: str | None = "MINOR_ASSENT",
) -> MajorityMilestoneResult:
    """Evaluates a subject against legal majority rules and computes transition requirements.

    Args:
        subject_pseudonym: Unique pseudonym ID of the participant.
        date_of_birth: Date of birth (string, date, or datetime).
        jurisdiction: ISO jurisdiction/region code (e.g. "US-AL", "GB", "DE").
        as_of_date: Reference evaluation date (defaults to today).
        lead_time_days: Advance notification window (days prior to birthday).
        current_consent_role: Current consent signer role (e.g. MINOR_ASSENT, LAR, SUBJECT).

    Returns:
        MajorityMilestoneResult with milestone classification.
    """
    dob = _parse_date(date_of_birth)
    ref_date = _parse_date(as_of_date) if as_of_date is not None else date.today()
    jur = jurisdiction.strip().upper() if jurisdiction else "DEFAULT"
    legal_age = get_legal_age_threshold(jur)

    completed_years, exact_age = calculate_exact_age(dob, ref_date)
    majority_date = calculate_majority_date(dob, legal_age)

    days_until_majority = (majority_date - ref_date).days
    has_reached_majority = completed_years >= legal_age
    is_transition_due = has_reached_majority or (
        0 <= days_until_majority <= lead_time_days
    )

    # If subject has already signed adult consent directly (role == SUBJECT), no reconsent needed
    already_adult_consented = current_consent_role == "SUBJECT"

    if already_adult_consented:
        milestone_status = "ADULT_RECONSENT_COMPLETED"
        requires_adult_reconsent = False
    elif has_reached_majority:
        milestone_status = "MAJORITY_REACHED_RECONSENT_REQUIRED"
        requires_adult_reconsent = True
    elif is_transition_due:
        milestone_status = "APPROACHING_MAJORITY"
        requires_adult_reconsent = False
    else:
        milestone_status = "ACTIVE_MINOR"
        requires_adult_reconsent = False

    return MajorityMilestoneResult(
        subject_pseudonym=subject_pseudonym,
        date_of_birth=dob.isoformat(),
        jurisdiction=jur,
        current_age_years=completed_years,
        exact_age=exact_age,
        legal_age_threshold=legal_age,
        majority_reached_date=majority_date.isoformat(),
        has_reached_majority=has_reached_majority,
        is_transition_due=is_transition_due,
        days_until_majority=days_until_majority,
        requires_adult_reconsent=requires_adult_reconsent,
        milestone_status=milestone_status,
    )


def batch_evaluate_minor_cohort(
    subjects: list[dict[str, Any]],
    as_of_date: date | datetime | str | None = None,
    lead_time_days: int = 30,
) -> list[MajorityMilestoneResult]:
    """Batch evaluates a cohort of minor or pediatric subjects for age of majority milestones."""
    results: list[MajorityMilestoneResult] = []
    for s in subjects:
        pseudonym = (
            s.get("subject_pseudonym") or s.get("subject_id") or s.get("id", "UNKNOWN")
        )
        dob = s.get("date_of_birth") or s.get("dob") or s.get("birth_date")
        if not dob:
            continue
        jur = s.get("jurisdiction") or s.get("country_code") or "DEFAULT"
        role = s.get("current_consent_role") or s.get("role") or "MINOR_ASSENT"
        evaluated = evaluate_subject_majority_milestone(
            subject_pseudonym=pseudonym,
            date_of_birth=dob,
            jurisdiction=jur,
            as_of_date=as_of_date,
            lead_time_days=lead_time_days,
            current_consent_role=role,
        )
        results.append(evaluated)
    return results
