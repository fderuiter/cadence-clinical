import math
from typing import List, Tuple

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import ClinicalObservation


def calculate_cohort_stats(values: List[float]) -> Tuple[float, float]:
    """Calculate mean and population standard deviation of a cohort using pure-Python.

    Args:
        values (List[float]): List of numeric measurement values.

    Returns:
        Tuple[float, float]: A tuple containing (mean, standard_deviation).
    """
    if not values:
        return 0.0, 0.0

    n = len(values)
    mean = sum(values) / n
    variance = sum((x - mean) ** 2 for x in values) / n
    std_dev = math.sqrt(variance)
    return mean, std_dev


def identify_outliers(values: List[float], mean: float, std_dev: float) -> List[bool]:
    """Flag values that fall outside of three standard deviations from the mean.

    Args:
        values (List[float]): List of numeric measurement values.
        mean (float): The mean of the dataset.
        std_dev (float): The standard deviation of the dataset.

    Returns:
        List[bool]: A list of booleans indicating outlier status for each value.
    """
    if std_dev == 0.0 or len(values) < 2:
        return [False] * len(values)

    return [abs(x - mean) > 3.0 * std_dev for x in values]


async def recalculate_cohort_outliers(
    session: AsyncSession, study_id: str, test_code: str
) -> int:
    """Query and update the outlier flags for all observations in a study-test cohort.

    Fetches all active clinical observations for the specified test code within
    the given study, calculates cohort-wide mean and standard deviation on the
    normalized values, identifies outliers, updates the database, and commits.

    Args:
        session (AsyncSession): The database session.
        study_id (str): The unique identifier of the study.
        test_code (str): The test parameter code (e.g. 'SYSBP').

    Returns:
        int: The number of observations identified as outliers.
    """
    # 1. Fetch observations
    stmt = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.test_code == test_code,
        ClinicalObservation.is_deleted.is_(False),
    )
    result = await session.execute(stmt)
    observations = result.scalars().all()

    if len(observations) < 2:
        # Cannot compute standard deviation of cohort with fewer than 2 items
        for obs in observations:
            if obs.is_outlier:
                obs.is_outlier = False
                obs.version += 1
        await session.commit()
        return 0

    # 2. Extract normalized values
    valid_obs = [obs for obs in observations if obs.normalized_value is not None]
    if len(valid_obs) < 2:
        for obs in observations:
            if obs.is_outlier:
                obs.is_outlier = False
                obs.version += 1
        await session.commit()
        return 0

    values = [obs.normalized_value for obs in valid_obs]

    # 3. Calculate statistics
    mean, std_dev = calculate_cohort_stats(values)

    # 4. Identify and update outliers
    outlier_count = 0
    for obs in observations:
        is_out = False
        if obs.normalized_value is not None:
            is_out = (
                abs(obs.normalized_value - mean) > 3.0 * std_dev
                if std_dev > 0.0
                else False
            )

        if obs.is_outlier != is_out:
            obs.is_outlier = is_out
            obs.version += 1

        if is_out:
            outlier_count += 1

    await session.commit()
    return outlier_count
