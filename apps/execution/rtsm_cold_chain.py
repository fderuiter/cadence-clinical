"""RTSM Cold Chain Logger Ingestion, Mean Kinetic Temperature (MKT) Engine & Lot Expiry Management.

Requirements: PRD-SCM-002, PRD-SCM-003, ADR-2199, USP <1079>, PDA TR 39
"""

import csv
import hashlib
import io
import json
import math
import uuid
import xml.etree.ElementTree as ET
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.context import current_user_id
from apps.execution.database.models import (
    DrugStabilityBudget,
    ExcursionEvaluation,
    IntegrationOutbox,
    IPKit,
    LotExpiryExtension,
    PackingManifest,
    ReLabelingTask,
    TemperatureLogger,
    TemperatureLogIngestion,
    TemperatureReading,
    TransferOrder,
)
from apps.execution.domain.rtsm_models import (
    DiscreteExcursionEvent,
    ExcursionAnalysisResult,
    ExcursionEvaluationStatus,
    FileFormat,
    KitStatus,
    LoggerDeviceStatus,
    LoggerEntityType,
    RawTemperaturePoint,
    ReLabelingTaskStatus,
    StabilityBudgetConfig,
)


class StabilityBudgetNotFoundError(Exception):
    """Raised when no drug stability budget is defined for the requested study and kit type."""

    pass


class LoggerNotFoundError(Exception):
    """Raised when a requested temperature logger cannot be found."""

    pass


class InvalidLoggerFileError(Exception):
    """Raised when an ingested temperature data logger file cannot be parsed or contains invalid data."""

    pass


class QuarantinedKitError(Exception):
    """Raised when attempting to allocate or dispense a quarantined investigational product kit."""

    pass


class ExpiredKitError(Exception):
    """Raised when attempting to allocate or dispense an expired investigational product kit."""

    pass


class LotNotFoundError(Exception):
    """Raised when a requested investigational product lot does not exist."""

    pass


class ReLabelingTaskNotFoundError(Exception):
    """Raised when a requested re-labeling action task cannot be found."""

    pass


# =========================================================================
# Mathematical Mean Kinetic Temperature (MKT) Pure Calculation
# =========================================================================


def calculate_mkt(
    temperatures_celsius: list[float],
    activation_energy_over_r: float = 10000.0,
) -> float:
    """Calculates Mean Kinetic Temperature (MKT) in degrees Celsius using the Arrhenius equation.

    Conforms to USP <1079> / PDA Technical Report 39:
        delta_H / R = 10000 K
        T_K = (delta_H / R) / ( -ln( (1/n) * sum_{i=1}^n exp( -(delta_H / R) / (T_i + 273.15) ) ) )
        MKT_celsius = T_K - 273.15

    Uses the Log-Sum-Exp numerical stabilization technique to eliminate floating point underflow.
    """
    if not temperatures_celsius:
        raise ValueError(
            "At least one temperature reading is required to calculate MKT."
        )

    for t in temperatures_celsius:
        if t + 273.15 <= 0.0:
            raise ValueError(f"Invalid temperature {t}°C below absolute zero.")

    n = len(temperatures_celsius)
    if n == 1:
        return float(temperatures_celsius[0])

    kelvin_temps: list[float] = [t + 273.15 for t in temperatures_celsius]

    # x_i = - (delta_H / R) / T_{K, i}
    x_values = [-activation_energy_over_r / tk for tk in kelvin_temps]
    x_max = max(x_values)

    # Log-Sum-Exp: ln( (1/n) * sum( exp(x_i) ) ) = x_max + ln( (1/n) * sum( exp(x_i - x_max) ) )
    sum_exp_shifted = sum(math.exp(x - x_max) for x in x_values)
    log_mean_exp = x_max + math.log(sum_exp_shifted / n)

    denominator = -log_mean_exp
    if denominator == 0.0:
        raise ValueError("Mathematical singularity in Arrhenius MKT denominator.")

    mkt_kelvin = activation_energy_over_r / denominator
    mkt_celsius = mkt_kelvin - 273.15
    return round(mkt_celsius, 4)


# =========================================================================
# Digital Temperature Data Logger Parsers (CSV, XML, JSON)
# =========================================================================


def _parse_timestamp(ts_raw: str | datetime | int | float) -> datetime:
    """Robust parser for multiple timestamp formats found in temperature loggers."""
    if isinstance(ts_raw, datetime):
        return ts_raw if ts_raw.tzinfo else ts_raw.replace(tzinfo=UTC)
    if isinstance(ts_raw, (int, float)):
        return datetime.fromtimestamp(ts_raw, tz=UTC)

    ts_str = str(ts_raw).strip()
    # Try ISO format
    try:
        dt = datetime.fromisoformat(ts_str.replace("Z", "+00:00"))
        return dt if dt.tzinfo else dt.replace(tzinfo=UTC)
    except ValueError:
        pass

    common_formats = [
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%d %H:%M",
        "%Y/%m/%d %H:%M:%S",
        "%Y/%m/%d %H:%M",
        "%m/%d/%Y %H:%M:%S",
        "%m/%d/%Y %H:%M",
        "%d/%m/%Y %H:%M:%S",
        "%d/%m/%Y %H:%M",
        "%d-%m-%Y %H:%M:%S",
    ]
    for fmt in common_formats:
        try:
            dt = datetime.strptime(ts_str, fmt)
            return dt.replace(tzinfo=UTC)
        except ValueError:
            continue

    raise ValueError(f"Unable to parse timestamp: {ts_raw}")


def parse_logger_csv(csv_content: str) -> list[RawTemperaturePoint]:
    """Parses digital temperature logger CSV data.

    Supports comma or semicolon separated files with flexible header conventions:
    (timestamp / datetime / time / recorded_at) and (temperature / temp / temp_celsius / value).
    """
    if not csv_content or not csv_content.strip():
        raise InvalidLoggerFileError("Empty CSV logger content.")

    # Determine delimiter
    first_line = csv_content.strip().split("\n")[0]
    delimiter = ";" if ";" in first_line and "," not in first_line else ","

    reader = csv.DictReader(io.StringIO(csv_content), delimiter=delimiter)
    if not reader.fieldnames:
        raise InvalidLoggerFileError("CSV header row missing.")

    # Identify timestamp and temperature column names
    ts_col = None
    temp_col = None
    for col in reader.fieldnames:
        norm = col.strip().lower().replace(" ", "_")
        if norm in (
            "timestamp",
            "datetime",
            "date_time",
            "time",
            "date",
            "recorded_at",
        ):
            ts_col = col
        elif norm in (
            "temperature",
            "temp",
            "temp_celsius",
            "temperature_c",
            "temp_c",
            "value",
            "reading",
        ):
            temp_col = col

    if not ts_col or not temp_col:
        # Fallback to index-based parsing if headers are simple
        if len(reader.fieldnames) >= 2:
            ts_col = reader.fieldnames[0]
            temp_col = reader.fieldnames[1]
        else:
            raise InvalidLoggerFileError(
                f"Required timestamp and temperature columns not found in CSV fields: {reader.fieldnames}"
            )

    points: list[RawTemperaturePoint] = []
    for row_idx, row in enumerate(reader, start=2):
        ts_val = row.get(ts_col)
        temp_val = row.get(temp_col)
        if not ts_val or temp_val is None or str(temp_val).strip() == "":
            continue
        try:
            ts_dt = _parse_timestamp(ts_val)
            t_float = float(str(temp_val).strip().replace(",", "."))
            points.append(
                RawTemperaturePoint(timestamp=ts_dt, temperature_celsius=t_float)
            )
        except Exception as e:
            raise InvalidLoggerFileError(
                f"Malformed row {row_idx} in CSV logger: {e}"
            ) from e

    if not points:
        raise InvalidLoggerFileError(
            "No valid temperature readings parsed from CSV file."
        )

    return sorted(points, key=lambda p: p.timestamp)


def parse_logger_xml(xml_content: str) -> list[RawTemperaturePoint]:
    """Parses standard temperature logger XML exports (Sensitech, TempTale, Berlinger)."""
    if not xml_content or not xml_content.strip():
        raise InvalidLoggerFileError("Empty XML logger content.")

    try:
        root = ET.fromstring(xml_content.strip())
    except ET.ParseError as e:
        raise InvalidLoggerFileError(f"XML parse error: {e}") from e

    points: list[RawTemperaturePoint] = []
    # Search for common XML reading elements
    reading_tags = [
        "reading",
        "Reading",
        "record",
        "Record",
        "sample",
        "Sample",
        "point",
        "Point",
        "dataPoint",
        "DataPoint",
    ]

    found_nodes = []
    for tag in reading_tags:
        found_nodes.extend(root.iter(tag))

    if not found_nodes:
        # Check all child elements of any list/container
        for elem in root.iter():
            if len(elem) > 0 and any(
                k.lower() in ("timestamp", "temperature", "time", "temp", "val")
                for k in elem.attrib
            ):
                found_nodes.append(elem)

    for node in found_nodes:
        ts_val = None
        temp_val = None

        # Check attributes first
        for k, v in node.attrib.items():
            k_norm = k.lower()
            if k_norm in ("timestamp", "datetime", "time", "date"):
                ts_val = v
            elif k_norm in ("temperature", "temp", "temp_celsius", "value", "val"):
                temp_val = v

        # Check child tags if not in attributes
        if ts_val is None:
            for child in node:
                c_tag = child.tag.lower()
                if c_tag in ("timestamp", "datetime", "time", "date"):
                    ts_val = child.text
                elif c_tag in ("temperature", "temp", "temp_celsius", "value", "val"):
                    temp_val = child.text

        if ts_val and temp_val is not None:
            try:
                ts_dt = _parse_timestamp(ts_val)
                t_float = float(str(temp_val).strip().replace(",", "."))
                points.append(
                    RawTemperaturePoint(timestamp=ts_dt, temperature_celsius=t_float)
                )
            except Exception:
                continue

    if not points:
        raise InvalidLoggerFileError(
            "No valid temperature readings parsed from XML file."
        )

    return sorted(points, key=lambda p: p.timestamp)


def parse_logger_json(json_content: str) -> list[RawTemperaturePoint]:
    """Parses digital temperature logger JSON payload."""
    if not json_content or not json_content.strip():
        raise InvalidLoggerFileError("Empty JSON logger content.")

    try:
        data = json.loads(json_content)
    except json.JSONDecodeError as e:
        raise InvalidLoggerFileError(f"JSON decode error: {e}") from e

    items = []
    if isinstance(data, list):
        items = data
    elif isinstance(data, dict):
        for key in (
            "readings",
            "data",
            "points",
            "samples",
            "temperature_readings",
            "logs",
        ):
            if key in data and isinstance(data[key], list):
                items = data[key]
                break

    if not items:
        raise InvalidLoggerFileError(
            "JSON does not contain a list of temperature readings."
        )

    points: list[RawTemperaturePoint] = []
    for idx, item in enumerate(items):
        if not isinstance(item, dict):
            continue
        ts_val = (
            item.get("timestamp")
            or item.get("datetime")
            or item.get("time")
            or item.get("date")
            or item.get("recorded_at")
        )
        temp_val = (
            item.get("temperature_celsius")
            if "temperature_celsius" in item
            else item.get("temperature")
            if "temperature" in item
            else item.get("temp")
            if "temp" in item
            else item.get("value")
        )

        if ts_val is not None and temp_val is not None:
            try:
                ts_dt = _parse_timestamp(ts_val)
                t_float = float(str(temp_val).strip().replace(",", "."))
                points.append(
                    RawTemperaturePoint(timestamp=ts_dt, temperature_celsius=t_float)
                )
            except Exception as e:
                raise InvalidLoggerFileError(
                    f"Malformed reading at index {idx} in JSON: {e}"
                ) from e

    if not points:
        raise InvalidLoggerFileError(
            "No valid temperature readings parsed from JSON file."
        )

    return sorted(points, key=lambda p: p.timestamp)


def parse_logger_data(
    file_format: FileFormat | str, content: str
) -> list[RawTemperaturePoint]:
    """Universal dispatcher for digital temperature logger file formats."""
    fmt = str(file_format).upper()
    if fmt == "CSV":
        return parse_logger_csv(content)
    if fmt == "XML":
        return parse_logger_xml(content)
    if fmt == "JSON":
        return parse_logger_json(content)
    raise InvalidLoggerFileError(
        f"Unsupported logger file format: {file_format}. Supported: CSV, XML, JSON."
    )


# =========================================================================
# Thermal Stability Budget & Excursion Evaluation Pure Logic
# =========================================================================


def evaluate_temperature_stability_budget(
    study_id: str,
    logger_id: str,
    entity_type: LoggerEntityType,
    entity_id: str,
    readings: list[RawTemperaturePoint],
    budget: StabilityBudgetConfig,
    evaluated_by: str = "system",
) -> ExcursionAnalysisResult:
    """Evaluates a timeseries of temperature readings against drug stability budget limits.

    Computes:
      - Mean Kinetic Temperature (MKT) via Arrhenius equation
      - Cumulative hours above upper storage threshold
      - Cumulative hours below lower storage threshold
      - Hard excursion limits (e.g. freezing / extreme heat)
      - Contiguous discrete excursion intervals
    """
    if not readings:
        raise ValueError("Cannot evaluate stability budget with zero readings.")

    sorted_readings = sorted(readings, key=lambda r: r.timestamp)
    temps = [r.temperature_celsius for r in sorted_readings]
    count = len(sorted_readings)

    # Mathematical MKT
    mkt = calculate_mkt(temps, activation_energy_over_r=budget.activation_energy_over_r)
    arithmetic_mean = round(sum(temps) / count, 4)
    min_temp = round(min(temps), 2)
    max_temp = round(max(temps), 2)

    # Time-series cumulative excursion computation
    cumulative_hours_above = 0.0
    cumulative_hours_below = 0.0
    total_duration_hours = 0.0

    # Determine default interval between points if only 1 reading
    default_step_hours = 0.25  # 15 minutes default interval

    excursions: list[DiscreteExcursionEvent] = []
    current_excursion_type: str | None = None
    current_exc_start: datetime | None = None
    current_exc_end: datetime | None = None
    current_exc_extreme: float | None = None
    current_threshold: float = 0.0

    for i in range(count):
        pt = sorted_readings[i]
        t = pt.temperature_celsius

        # Determine time delta to next reading (or use previous step for the last point)
        if i < count - 1:
            step_sec = (sorted_readings[i + 1].timestamp - pt.timestamp).total_seconds()
            step_hours = max(0.0, step_sec / 3600.0)
        elif count > 1:
            step_sec = (pt.timestamp - sorted_readings[i - 1].timestamp).total_seconds()
            step_hours = max(0.0, step_sec / 3600.0)
        else:
            step_hours = default_step_hours

        total_duration_hours += step_hours

        # Check thresholds
        is_above = t > budget.max_temp_celsius
        is_below = t < budget.min_temp_celsius

        if is_above:
            cumulative_hours_above += step_hours
        if is_below:
            cumulative_hours_below += step_hours

        # Detect discrete continuous excursion windows
        exc_type = None
        threshold_val = 0.0
        if (
            budget.hard_max_temp_celsius is not None
            and t >= budget.hard_max_temp_celsius
        ):
            exc_type = "CRITICAL_HARD_MAX"
            threshold_val = budget.hard_max_temp_celsius
        elif (
            budget.hard_min_temp_celsius is not None
            and t <= budget.hard_min_temp_celsius
        ):
            exc_type = "CRITICAL_HARD_MIN"
            threshold_val = budget.hard_min_temp_celsius
        elif is_above:
            exc_type = "HIGH_TEMPERATURE"
            threshold_val = budget.max_temp_celsius
        elif is_below:
            exc_type = "LOW_TEMPERATURE"
            threshold_val = budget.min_temp_celsius

        if exc_type is not None:
            if current_excursion_type == exc_type:
                current_exc_end = pt.timestamp
                if "HIGH" in exc_type or "MAX" in exc_type:
                    current_exc_extreme = max(current_exc_extreme or t, t)
                else:
                    current_exc_extreme = min(current_exc_extreme or t, t)
            else:
                # Flush previous
                if current_excursion_type and current_exc_start and current_exc_end:
                    dur = max(
                        0.0,
                        (current_exc_end - current_exc_start).total_seconds() / 3600.0,
                    )
                    excursions.append(
                        DiscreteExcursionEvent(
                            start_time=current_exc_start.isoformat(),
                            end_time=current_exc_end.isoformat(),
                            duration_hours=round(dur, 2),
                            excursion_type=current_excursion_type,
                            peak_or_nadir_temp_celsius=current_exc_extreme or 0.0,
                            threshold_celsius=current_threshold,
                        )
                    )
                current_excursion_type = exc_type
                current_exc_start = pt.timestamp
                current_exc_end = pt.timestamp
                current_exc_extreme = t
                current_threshold = threshold_val
        else:
            if current_excursion_type and current_exc_start and current_exc_end:
                dur = max(
                    0.0, (current_exc_end - current_exc_start).total_seconds() / 3600.0
                )
                excursions.append(
                    DiscreteExcursionEvent(
                        start_time=current_exc_start.isoformat(),
                        end_time=current_exc_end.isoformat(),
                        duration_hours=round(dur, 2),
                        excursion_type=current_excursion_type,
                        peak_or_nadir_temp_celsius=current_exc_extreme or 0.0,
                        threshold_celsius=current_threshold,
                    )
                )
                current_excursion_type = None
                current_exc_start = None
                current_exc_end = None
                current_exc_extreme = None

    # Flush any lingering excursion window
    if current_excursion_type and current_exc_start and current_exc_end:
        dur = max(0.0, (current_exc_end - current_exc_start).total_seconds() / 3600.0)
        excursions.append(
            DiscreteExcursionEvent(
                start_time=current_exc_start.isoformat(),
                end_time=current_exc_end.isoformat(),
                duration_hours=round(dur, 2),
                excursion_type=current_excursion_type,
                peak_or_nadir_temp_celsius=current_exc_extreme or 0.0,
                threshold_celsius=current_threshold,
            )
        )

    cumulative_hours_above = round(cumulative_hours_above, 2)
    cumulative_hours_below = round(cumulative_hours_below, 2)
    total_duration_hours = round(total_duration_hours, 2)

    # Check breach conditions
    hard_boundary_breached = False
    if (
        budget.hard_max_temp_celsius is not None
        and max_temp >= budget.hard_max_temp_celsius
    ):
        hard_boundary_breached = True
    if (
        budget.hard_min_temp_celsius is not None
        and min_temp <= budget.hard_min_temp_celsius
    ):
        hard_boundary_breached = True

    mkt_breached = mkt > budget.max_mkt_celsius
    cumulative_time_breached = (
        cumulative_hours_above > budget.max_cumulative_hours_above_upper
        or cumulative_hours_below > budget.max_cumulative_hours_below_lower
    )

    is_breached = hard_boundary_breached or mkt_breached or cumulative_time_breached

    if is_breached:
        evaluation_status = ExcursionEvaluationStatus.EXCURSION_BREACH
    elif cumulative_hours_above > 0.0 or cumulative_hours_below > 0.0:
        evaluation_status = ExcursionEvaluationStatus.EXCURSION_ACCEPTABLE
    else:
        evaluation_status = ExcursionEvaluationStatus.COMPLIANT

    summary = (
        f"MKT: {mkt}°C (Max allowable: {budget.max_mkt_celsius}°C). "
        f"Cumul. above {budget.max_temp_celsius}°C: {cumulative_hours_above}h (Budget: {budget.max_cumulative_hours_above_upper}h). "
        f"Cumul. below {budget.min_temp_celsius}°C: {cumulative_hours_below}h (Budget: {budget.max_cumulative_hours_below_lower}h). "
        f"Min: {min_temp}°C, Max: {max_temp}°C. Status: {evaluation_status}."
    )

    return ExcursionAnalysisResult(
        study_id=study_id,
        logger_id=logger_id,
        entity_type=entity_type,
        entity_id=entity_id,
        evaluation_status=evaluation_status,
        mean_kinetic_temperature_celsius=mkt,
        arithmetic_mean_celsius=arithmetic_mean,
        min_temperature_celsius=min_temp,
        max_temperature_celsius=max_temp,
        cumulative_hours_above_upper=cumulative_hours_above,
        cumulative_hours_below_lower=cumulative_hours_below,
        total_duration_hours=total_duration_hours,
        readings_count=count,
        hard_boundary_breached=hard_boundary_breached,
        mkt_breached=mkt_breached,
        cumulative_time_breached=cumulative_time_breached,
        quarantine_triggered=is_breached,
        quarantined_kit_count=0,  # Will be populated during transactional execution
        excursions=excursions,
        evaluated_at=datetime.now(UTC),
        evaluated_by=evaluated_by,
        summary_notes=summary,
    )


# =========================================================================
# Transactional Services for Cold Chain Ingestion & Automated Quarantine
# =========================================================================


async def create_or_update_stability_budget_transaction(
    session: AsyncSession,
    budget_config: StabilityBudgetConfig,
) -> DrugStabilityBudget:
    """Creates or updates a study drug stability budget under 21 CFR Part 11."""
    stmt = select(DrugStabilityBudget).where(
        DrugStabilityBudget.study_id == budget_config.study_id,
        DrugStabilityBudget.kit_type == budget_config.kit_type,
        DrugStabilityBudget.lot_number == budget_config.lot_number,
        DrugStabilityBudget.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    existing = res.scalars().first()

    if existing:
        existing.storage_condition = budget_config.storage_condition
        existing.min_temp_celsius = budget_config.min_temp_celsius
        existing.max_temp_celsius = budget_config.max_temp_celsius
        existing.max_mkt_celsius = budget_config.max_mkt_celsius
        existing.max_cumulative_hours_above_upper = (
            budget_config.max_cumulative_hours_above_upper
        )
        existing.max_cumulative_hours_below_lower = (
            budget_config.max_cumulative_hours_below_lower
        )
        existing.activation_energy_over_r = budget_config.activation_energy_over_r
        existing.hard_min_temp_celsius = budget_config.hard_min_temp_celsius
        existing.hard_max_temp_celsius = budget_config.hard_max_temp_celsius
        return existing

    budget = DrugStabilityBudget(
        study_id=budget_config.study_id,
        kit_type=budget_config.kit_type,
        lot_number=budget_config.lot_number,
        storage_condition=budget_config.storage_condition,
        min_temp_celsius=budget_config.min_temp_celsius,
        max_temp_celsius=budget_config.max_temp_celsius,
        max_mkt_celsius=budget_config.max_mkt_celsius,
        max_cumulative_hours_above_upper=budget_config.max_cumulative_hours_above_upper,
        max_cumulative_hours_below_lower=budget_config.max_cumulative_hours_below_lower,
        activation_energy_over_r=budget_config.activation_energy_over_r,
        hard_min_temp_celsius=budget_config.hard_min_temp_celsius,
        hard_max_temp_celsius=budget_config.hard_max_temp_celsius,
    )
    session.add(budget)
    return budget


async def ingest_and_evaluate_logger_transaction(
    session: AsyncSession,
    study_id: str,
    logger_id: str,
    file_format: FileFormat | str,
    raw_payload: str,
    filename: str,
    entity_type: LoggerEntityType | str,
    entity_id: str,
    stability_budget_override: StabilityBudgetConfig | None = None,
) -> ExcursionAnalysisResult:
    """Ingests a digital logger file, computes MKT excursion analytics, and automatically quarantines breached kits."""
    user = current_user_id.get() or "system_ingest"
    entity_type_enum = (
        LoggerEntityType(entity_type)
        if isinstance(entity_type, str) and entity_type in LoggerEntityType.__members__
        else entity_type
        if isinstance(entity_type, LoggerEntityType)
        else LoggerEntityType.SHIPMENT
    )

    # 1. Parse temperature data points
    readings = parse_logger_data(file_format, raw_payload)

    # 2. Register or update Logger device record
    stmt_log = select(TemperatureLogger).where(
        TemperatureLogger.study_id == study_id,
        TemperatureLogger.logger_id == logger_id,
        TemperatureLogger.is_deleted.is_(False),
    )
    res_log = await session.execute(stmt_log)
    logger_device = res_log.scalars().first()
    if not logger_device:
        logger_device = TemperatureLogger(
            study_id=study_id,
            logger_id=logger_id,
            device_model="Sensitech TempTale Ultra",
            logger_type=str(entity_type_enum),
            assigned_transfer_order_id=entity_id
            if entity_type_enum == LoggerEntityType.SHIPMENT
            else None,
            assigned_node_id=entity_id
            if entity_type_enum != LoggerEntityType.SHIPMENT
            else None,
            status=LoggerDeviceStatus.INGESTED,
        )
        session.add(logger_device)
    else:
        logger_device.status = LoggerDeviceStatus.INGESTED

    # 3. Store raw ingestion record with SHA256 hash for Part 11 integrity
    raw_hash = hashlib.sha256(raw_payload.encode("utf-8")).hexdigest()
    ingestion_record = TemperatureLogIngestion(
        study_id=study_id,
        logger_id=logger_id,
        file_format=str(file_format).upper(),
        filename=filename,
        reading_count=len(readings),
        ingested_at=datetime.now(UTC),
        ingested_by=user,
        raw_data_hash=raw_hash,
    )
    session.add(ingestion_record)

    # 4. Save sample readings (first 1000 for timeseries queries)
    for r in readings[:1000]:
        t_record = TemperatureReading(
            study_id=study_id,
            logger_id=logger_id,
            timestamp=r.timestamp,
            temperature_celsius=r.temperature_celsius,
        )
        session.add(t_record)

    # 5. Resolve stability budget
    budget = stability_budget_override
    if not budget:
        stmt_b = select(DrugStabilityBudget).where(
            DrugStabilityBudget.study_id == study_id,
            DrugStabilityBudget.is_deleted.is_(False),
        )
        res_b = await session.execute(stmt_b)
        budget_db = res_b.scalars().first()
        if budget_db:
            budget = StabilityBudgetConfig(
                study_id=budget_db.study_id,
                kit_type=budget_db.kit_type,
                lot_number=budget_db.lot_number,
                storage_condition=budget_db.storage_condition,
                min_temp_celsius=budget_db.min_temp_celsius,
                max_temp_celsius=budget_db.max_temp_celsius,
                max_mkt_celsius=budget_db.max_mkt_celsius,
                max_cumulative_hours_above_upper=budget_db.max_cumulative_hours_above_upper,
                max_cumulative_hours_below_lower=budget_db.max_cumulative_hours_below_lower,
                activation_energy_over_r=budget_db.activation_energy_over_r,
                hard_min_temp_celsius=budget_db.hard_min_temp_celsius,
                hard_max_temp_celsius=budget_db.hard_max_temp_celsius,
            )
        else:
            # Standard default refrigerated 2-8°C budget
            budget = StabilityBudgetConfig(
                study_id=study_id,
                kit_type="DEFAULT",
                storage_condition="REFRIGERATED_2_TO_8",
                min_temp_celsius=2.0,
                max_temp_celsius=8.0,
                max_mkt_celsius=8.0,
                max_cumulative_hours_above_upper=24.0,
                max_cumulative_hours_below_lower=0.0,
                activation_energy_over_r=10000.0,
                hard_min_temp_celsius=0.0,
                hard_max_temp_celsius=25.0,
            )

    # 6. Execute pure stability budget & MKT evaluation
    analysis = evaluate_temperature_stability_budget(
        study_id=study_id,
        logger_id=logger_id,
        entity_type=entity_type_enum,
        entity_id=entity_id,
        readings=readings,
        budget=budget,
        evaluated_by=user,
    )

    # 7. Automated Quarantine Execution if breached
    quarantined_count = 0
    if analysis.evaluation_status == ExcursionEvaluationStatus.EXCURSION_BREACH:
        now_dt = datetime.now(UTC)
        reason_note = (
            f"Automated Cold Chain Excursion Breach on {entity_type_enum} {entity_id}. "
            f"MKT: {analysis.mean_kinetic_temperature_celsius}°C (Budget: {budget.max_mkt_celsius}°C), "
            f"Max Temp: {analysis.max_temperature_celsius}°C, Min Temp: {analysis.min_temperature_celsius}°C."
        )

        if entity_type_enum == LoggerEntityType.SHIPMENT:
            # Find PackingManifest and all kits in this shipment
            stmt_pm = select(PackingManifest).where(
                PackingManifest.study_id == study_id,
                (PackingManifest.transfer_order_id == entity_id)
                | (PackingManifest.manifest_number == entity_id)
                | (PackingManifest.data_logger_id == logger_id),
                PackingManifest.is_deleted.is_(False),
            )
            res_pm = await session.execute(stmt_pm)
            manifest = res_pm.scalars().first()

            kit_nums_to_quarantine: list[str] = []
            if manifest and manifest.serialized_kit_numbers:
                kit_nums_to_quarantine.extend(manifest.serialized_kit_numbers)

            if kit_nums_to_quarantine:
                stmt_kits = select(IPKit).where(
                    IPKit.study_id == study_id,
                    IPKit.kit_number.in_(kit_nums_to_quarantine),
                    IPKit.is_deleted.is_(False),
                )
                res_kits = await session.execute(stmt_kits)
                kits_found = list(res_kits.scalars().all())
                for kit in kits_found:
                    kit.status = KitStatus.QUARANTINED
                    kit.quarantine_reason = reason_note
                    kit.quarantined_at = now_dt
                    kit.quarantined_by = user
                quarantined_count = len(kits_found)

            # Update transfer order status if active
            stmt_to = select(TransferOrder).where(
                TransferOrder.study_id == study_id,
                (TransferOrder.id == entity_id)
                | (TransferOrder.transfer_order_number == entity_id),
                TransferOrder.is_deleted.is_(False),
            )
            res_to = await session.execute(stmt_to)
            to_obj = res_to.scalars().first()
            if to_obj:
                to_obj.rejection_reason = reason_note

        else:
            # Site or Depot storage unit breach: quarantine available kits located at this node
            stmt_site_kits = select(IPKit).where(
                IPKit.study_id == study_id,
                IPKit.node_id == entity_id,
                IPKit.status == KitStatus.AVAILABLE,
                IPKit.is_deleted.is_(False),
            )
            res_site_kits = await session.execute(stmt_site_kits)
            site_kits = list(res_site_kits.scalars().all())
            for kit in site_kits:
                kit.status = KitStatus.QUARANTINED
                kit.quarantine_reason = reason_note
                kit.quarantined_at = now_dt
                kit.quarantined_by = user
            quarantined_count = len(site_kits)

        # Trigger Outbox notification for Supply Chain Manager and QA
        outbox_event = IntegrationOutbox(
            event_type="COLD_CHAIN_TEMPERATURE_EXCURSION_QUARANTINE",
            payload={
                "study_id": study_id,
                "logger_id": logger_id,
                "entity_type": str(entity_type_enum),
                "entity_id": entity_id,
                "mkt_celsius": analysis.mean_kinetic_temperature_celsius,
                "max_temp_celsius": analysis.max_temperature_celsius,
                "min_temp_celsius": analysis.min_temperature_celsius,
                "quarantined_kit_count": quarantined_count,
                "reason": reason_note,
                "timestamp": now_dt.isoformat(),
            },
            status="PENDING",
            correlation_id=str(uuid.uuid4()),
            created_by=user,
            reason_for_change=reason_note,
        )
        session.add(outbox_event)

    analysis.quarantined_kit_count = quarantined_count

    # 8. Persist evaluation audit record
    eval_record = ExcursionEvaluation(
        study_id=study_id,
        logger_id=logger_id,
        entity_type=str(entity_type_enum),
        entity_id=entity_id,
        evaluation_status=str(analysis.evaluation_status),
        mean_kinetic_temperature_celsius=analysis.mean_kinetic_temperature_celsius,
        arithmetic_mean_celsius=analysis.arithmetic_mean_celsius,
        min_temperature_celsius=analysis.min_temperature_celsius,
        max_temperature_celsius=analysis.max_temperature_celsius,
        cumulative_hours_above_upper=analysis.cumulative_hours_above_upper,
        cumulative_hours_below_lower=analysis.cumulative_hours_below_lower,
        hard_boundary_breached=analysis.hard_boundary_breached,
        mkt_breached=analysis.mkt_breached,
        cumulative_time_breached=analysis.cumulative_time_breached,
        quarantine_triggered=analysis.quarantine_triggered,
        quarantined_kit_count=quarantined_count,
        evaluated_at=analysis.evaluated_at,
        evaluated_by=user,
        excursion_events=[e.model_dump() for e in analysis.excursions]
        if analysis.excursions
        else None,
        summary_notes=analysis.summary_notes,
    )
    session.add(eval_record)

    return analysis


async def quarantine_kits_transaction(
    session: AsyncSession,
    study_id: str,
    kit_numbers: list[str],
    reason: str,
) -> int:
    """Manually transitions specific investigational product kits to QUARANTINED."""
    user = current_user_id.get() or "system"
    stmt = select(IPKit).where(
        IPKit.study_id == study_id,
        IPKit.kit_number.in_(kit_numbers),
        IPKit.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    kits = list(res.scalars().all())

    now_dt = datetime.now(UTC)
    for kit in kits:
        kit.status = KitStatus.QUARANTINED
        kit.quarantine_reason = reason
        kit.quarantined_at = now_dt
        kit.quarantined_by = user

    return len(kits)


async def release_quarantined_kits_transaction(
    session: AsyncSession,
    study_id: str,
    kit_numbers: list[str],
    qa_release_justification: str,
) -> int:
    """Authorizes QA disposition release of quarantined kits back to AVAILABLE."""
    user = current_user_id.get() or "qa_manager"
    stmt = select(IPKit).where(
        IPKit.study_id == study_id,
        IPKit.kit_number.in_(kit_numbers),
        IPKit.status == KitStatus.QUARANTINED,
        IPKit.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    kits = list(res.scalars().all())

    for kit in kits:
        kit.status = KitStatus.AVAILABLE
        kit.quarantine_reason = f"QA Released: {qa_release_justification} (by {user})"

    return len(kits)


# =========================================================================
# Lot Expiration Extension & Automated Re-Labeling Workflows (PRD-SCM-003)
# =========================================================================


async def extend_lot_expiration_transaction(
    session: AsyncSession,
    study_id: str,
    lot_number: str,
    kit_type: str,
    extended_expiry_date: datetime,
    extension_reason: str,
    stability_protocol_reference: str,
) -> tuple[LotExpiryExtension, list[ReLabelingTask]]:
    """Executes automated lot expiration extension workflow with 21 CFR Part 11 records.

    1. Updates expiration dates across all non-dispensed kits for the lot.
    2. Updates expiration dates on active packing manifests.
    3. Automatically generates physical Re-Labeling Action Tasks for all depots and sites holding inventory.
    """
    user = current_user_id.get() or "qa_sponsor_admin"

    # Find all non-dispensed kits for this lot
    stmt_kits = select(IPKit).where(
        IPKit.study_id == study_id,
        IPKit.lot_number == lot_number,
        IPKit.status.in_([KitStatus.AVAILABLE, KitStatus.QUARANTINED]),
        IPKit.is_deleted.is_(False),
    )
    res_kits = await session.execute(stmt_kits)
    kits = list(res_kits.scalars().all())

    initial_expiry = (
        kits[0].expiration_date
        if kits and kits[0].expiration_date
        else datetime.now(UTC)
    )

    # 1. Update all kit expiration dates
    for kit in kits:
        kit.expiration_date = extended_expiry_date

    # 2. Update packing manifests
    stmt_pm = select(PackingManifest).where(
        PackingManifest.study_id == study_id,
        PackingManifest.lot_number == lot_number,
        PackingManifest.is_deleted.is_(False),
    )
    res_pm = await session.execute(stmt_pm)
    for pm in res_pm.scalars().all():
        pm.expiration_date = extended_expiry_date

    # 3. Group kits by location (node_id) to create facility Re-Labeling tasks
    kits_by_node: dict[str, list[IPKit]] = {}
    for kit in kits:
        loc = kit.node_id or "CENTRAL_DEPOT"
        kits_by_node.setdefault(loc, []).append(kit)

    tasks: list[ReLabelingTask] = []
    now_dt = datetime.now(UTC)
    for node_id, node_kits in kits_by_node.items():
        task_num = (
            f"RELABEL-{study_id}-{lot_number}-{node_id}-{uuid.uuid4().hex[:6].upper()}"
        )
        task = ReLabelingTask(
            study_id=study_id,
            task_number=task_num,
            lot_number=lot_number,
            node_id=node_id,
            affected_kit_count=len(node_kits),
            old_expiry_date=initial_expiry,
            new_expiry_date=extended_expiry_date,
            status=ReLabelingTaskStatus.PENDING_RELABEL,
        )
        session.add(task)
        tasks.append(task)

    # 4. Create LotExpiryExtension master record
    extension_rec = LotExpiryExtension(
        study_id=study_id,
        lot_number=lot_number,
        kit_type=kit_type,
        initial_expiry_date=initial_expiry,
        extended_expiry_date=extended_expiry_date,
        extension_reason=extension_reason,
        stability_protocol_reference=stability_protocol_reference,
        authorized_by=user,
        approval_status="APPROVED",
        affected_kits_count=len(kits),
        relabeling_tasks_generated=len(tasks),
        created_at=now_dt,
    )
    session.add(extension_rec)

    # 5. Outbox event for supply operations
    outbox = IntegrationOutbox(
        event_type="LOT_EXPIRATION_EXTENDED",
        payload={
            "study_id": study_id,
            "lot_number": lot_number,
            "kit_type": kit_type,
            "old_expiry": initial_expiry.isoformat(),
            "new_expiry": extended_expiry_date.isoformat(),
            "affected_kits_count": len(kits),
            "relabeling_tasks_count": len(tasks),
            "authorized_by": user,
        },
        status="PENDING",
        correlation_id=str(uuid.uuid4()),
        created_by=user,
        reason_for_change=f"Lot expiry extended: {extension_reason}",
    )
    session.add(outbox)

    return extension_rec, tasks


async def complete_relabeling_task_transaction(
    session: AsyncSession,
    task_id: str,
    notes: str | None = None,
) -> ReLabelingTask:
    """Records physical completion of an IP kit re-labeling task at a facility."""
    user = current_user_id.get() or "site_pharmacist"
    stmt = select(ReLabelingTask).where(
        (ReLabelingTask.id == task_id) | (ReLabelingTask.task_number == task_id),
        ReLabelingTask.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    task = res.scalars().first()
    if not task:
        raise ReLabelingTaskNotFoundError(f"Re-labeling task {task_id} not found.")

    task.status = ReLabelingTaskStatus.COMPLETED
    task.completed_at = datetime.now(UTC)
    task.completed_by = user
    task.relabeling_notes = notes
    return task


async def verify_relabeling_task_transaction(
    session: AsyncSession,
    task_id: str,
    label_barcode_hash: str | None = None,
    notes: str | None = None,
) -> ReLabelingTask:
    """Dual QA verification of completed re-labeling with 21 CFR Part 11 electronic signoff."""
    user = current_user_id.get() or "qa_monitor"
    stmt = select(ReLabelingTask).where(
        (ReLabelingTask.id == task_id) | (ReLabelingTask.task_number == task_id),
        ReLabelingTask.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    task = res.scalars().first()
    if not task:
        raise ReLabelingTaskNotFoundError(f"Re-labeling task {task_id} not found.")

    task.status = ReLabelingTaskStatus.VERIFIED
    task.verified_at = datetime.now(UTC)
    task.verified_by = user
    task.label_barcode_hash = label_barcode_hash
    if notes:
        task.relabeling_notes = (
            f"{task.relabeling_notes or ''} | Verified: {notes}".strip(" |")
        )
    return task
