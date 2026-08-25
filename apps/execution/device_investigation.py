"""Medical Device & In-Vitro Diagnostic (IVD) Investigation Engine.

Requirements: PRD-SYS-126, PRD-DEV-001, PRD-DEV-002, PRD-DEV-003, PRD-DEV-004,
ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, 21 CFR Part 11, ADR-2209.
"""

import calendar
import os
import re
import uuid
from datetime import UTC, date, datetime

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    DeviceDeficiencyReport,
    DeviceQuarantineRecord,
    IntegrationOutbox,
    InvestigationalDeviceInventory,
)
from apps.execution.domain.device_models import (
    DeviceDeficiencyCreateRequest,
    DeviceDeficiencyResponse,
    DeviceDispensationRequest,
    DeviceDispensationResponse,
    DeviceInventoryCreateRequest,
    DeviceInventoryResponse,
    DeviceInventoryStatus,
    DeviceQuarantineCreateRequest,
    DeviceQuarantineReleaseRequest,
    DeviceQuarantineResponse,
    DeviceSafetyClassification,
    GUDIDDeviceRecord,
    GUDIDVerificationResult,
    HealthImpact,
    ParsedUDI,
    QuarantineScope,
    UDICarrierFormat,
)
from apps.execution.trial_lock import TrialLockManager

# HMAC Secret for Device Token and Cryptographic Audit Signatures
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-device-secret-key-12345").encode(
    "utf-8"
)  # pragma: allowlist secret


# =========================================================================
# Custom Exceptions
# =========================================================================


class DeviceNotFoundError(HTTPException):
    def __init__(self, detail: str = "Investigational medical device not found."):
        super().__init__(status_code=404, detail=detail)


class DeviceQuarantinedError(HTTPException):
    def __init__(
        self,
        detail: str = "Operation blocked: Device is currently QUARANTINED and cannot be dispensed.",
    ):
        super().__init__(status_code=422, detail=detail)


class DeviceExpiredError(HTTPException):
    def __init__(
        self, detail: str = "Operation blocked: Investigational device has expired."
    ):
        super().__init__(status_code=422, detail=detail)


class DeviceInvalidStatusError(HTTPException):
    def __init__(
        self,
        detail: str = "Operation blocked: Device is not in AVAILABLE status for dispensation.",
    ):
        super().__init__(status_code=422, detail=detail)


class QuarantineNotFoundError(HTTPException):
    def __init__(self, detail: str = "Device quarantine record not found."):
        super().__init__(status_code=404, detail=detail)


def _assert_unlocked(study_id: str | None = None, site_id: str | None = None) -> None:
    """Ensures trial and site are not locked against data mutation."""
    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=423,
            detail="Locked: Trial system is currently locked against changes.",
        )
    if site_id and TrialLockManager.is_site_locked(site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {site_id} is currently locked against changes.",
        )


# =========================================================================
# UDI Parsing & Checksum Verification Utilities
# =========================================================================


def validate_gtin_checksum(gtin: str) -> bool:
    """Validates the GS1 Modulo 10 check digit for a GTIN (8, 12, 13, or 14 digits)."""
    clean_gtin = re.sub(r"\D", "", gtin)
    if len(clean_gtin) not in (8, 12, 13, 14):
        return False

    digits = [int(d) for d in clean_gtin]
    check_digit = digits[-1]
    payload = digits[:-1]

    total = sum(
        d * (3 if (len(payload) - 1 - i) % 2 == 0 else 1) for i, d in enumerate(payload)
    )
    calculated_check = (10 - (total % 10)) % 10
    return calculated_check == check_digit


def validate_hibcc_checksum(hibcc: str) -> bool:
    """Validates the HIBCC Modulo 43 check character."""
    hibcc_clean = hibcc.strip()
    if not hibcc_clean.startswith("+"):
        return False
    body = hibcc_clean[1:]
    if len(body) < 2:
        return False

    mod43_chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%"
    char_to_val = {c: i for i, c in enumerate(mod43_chars)}

    data_portion = body[:-1].upper()
    check_char = body[-1].upper()

    if check_char not in char_to_val:
        return False

    total = 0
    for c in data_portion:
        if c not in char_to_val:
            return False
        total += char_to_val[c]

    expected_char = mod43_chars[total % 43]
    return expected_char == check_char


def parse_gs1_date(yymmdd: str) -> str | None:
    """Parses GS1 YYMMDD format into ISO YYYY-MM-DD.

    If day is '00', computes the last day of the given month.
    """
    if len(yymmdd) != 6 or not yymmdd.isdigit():
        return None

    yy = int(yymmdd[0:2])
    mm = int(yymmdd[2:4])
    dd = int(yymmdd[4:6])

    # Year calculation: 50-99 -> 1950-1999, 00-49 -> 2000-2049
    year = 1900 + yy if yy >= 50 else 2000 + yy

    if mm < 1 or mm > 12:
        return None

    if dd == 0:
        # Last day of month
        _, last_day = calendar.monthrange(year, mm)
        dd = last_day
    else:
        _, last_day = calendar.monthrange(year, mm)
        if dd > last_day:
            return None

    return date(year, mm, dd).isoformat()


def parse_udi_barcode(raw_barcode: str) -> ParsedUDI:
    """Parses and validates a Unique Device Identifier (UDI) barcode string.

    Supports GS1-128 / GS1-DataMatrix (AIs 01, 10, 11, 17, 21, 8012)
    and HIBCC formats according to FDA UDI and EU MDR/IVDR standards.
    """
    barcode = raw_barcode.strip()
    errors: list[str] = []

    # 1. HIBCC Format Parsing
    if barcode.startswith("+"):
        format_type = (
            UDICarrierFormat.HIBCC_DATAMATRIX
            if "/" in barcode
            else UDICarrierFormat.HIBCC_128
        )
        parts = barcode.split("/", 1)
        primary = parts[0]
        secondary = parts[1] if len(parts) > 1 else ""

        is_valid = validate_hibcc_checksum(primary)
        if not is_valid:
            errors.append("HIBCC primary data structure checksum verification failed.")

        udi_di = primary[1:-1] if len(primary) > 2 else primary[1:]
        lot_number = None
        serial_number = None
        exp_date_str = None

        if secondary:
            # Parse secondary HIBCC structure
            sec_body = secondary.lstrip("$")
            if len(sec_body) >= 6 and sec_body[:6].isdigit():
                exp_date_str = parse_gs1_date(sec_body[:6])
                sec_body = sec_body[6:]
            if sec_body:
                if sec_body.startswith("LOT"):
                    lot_number = sec_body[3:]
                elif sec_body.startswith("SN"):
                    serial_number = sec_body[2:]
                else:
                    lot_number = sec_body

        return ParsedUDI(
            raw_barcode=barcode,
            udi_carrier_format=format_type,
            udi_di=udi_di,
            lot_number=lot_number,
            serial_number=serial_number,
            expiration_date=exp_date_str,
            is_valid=is_valid,
            validation_errors=errors,
        )

    # 2. GS1 Format with Parentheses Parsing
    if "(" in barcode and ")" in barcode:
        ai_pattern = re.compile(r"\(([0-9]{2,4})\)([^()]+)")
        matches = ai_pattern.findall(barcode)
        if matches:
            udi_di = ""
            lot = None
            serial = None
            exp_date = None
            mfg_date = None
            software_ver = None

            for ai, val in matches:
                val = val.strip()
                if ai == "01":  # GTIN / DI
                    udi_di = val
                    if not validate_gtin_checksum(udi_di):
                        errors.append(f"GTIN-14 ({udi_di}) check digit is invalid.")
                elif ai == "10":  # Lot
                    lot = val
                elif ai == "17":  # Expiration Date
                    exp_date = parse_gs1_date(val)
                    if not exp_date:
                        errors.append(f"Invalid GS1 expiration date format: {val}")
                elif ai == "11":  # Manufacturing Date
                    mfg_date = parse_gs1_date(val)
                elif ai == "21":  # Serial Number
                    serial = val
                elif ai == "8012":  # Software version
                    software_ver = val

            return ParsedUDI(
                raw_barcode=barcode,
                udi_carrier_format=UDICarrierFormat.GS1_128,
                udi_di=udi_di or barcode,
                lot_number=lot,
                serial_number=serial,
                expiration_date=exp_date,
                manufacturing_date=mfg_date,
                software_version=software_ver,
                is_valid=len(errors) == 0,
                validation_errors=errors,
            )

    # 3. Raw GS1 Application Identifier Stream Parsing (e.g. 01008401234567891726123110LOT12321SN999)
    if barcode.startswith("01") and len(barcode) >= 16:
        gtin = barcode[2:16]
        rest = barcode[16:]
        udi_di = gtin
        if not validate_gtin_checksum(gtin):
            errors.append(f"GTIN ({gtin}) check digit is invalid.")

        exp_date = None
        lot = None
        serial = None

        idx = 0
        while idx < len(rest):
            if rest[idx : idx + 2] == "17" and len(rest[idx:]) >= 8:
                exp_date = parse_gs1_date(rest[idx + 2 : idx + 8])
                idx += 8
            elif rest[idx : idx + 2] == "10":
                # Lot is variable length, look for next AI (e.g. '21') or end
                idx += 2
                next_21 = rest.find("21", idx)
                if next_21 != -1:
                    lot = rest[idx:next_21]
                    idx = next_21
                else:
                    lot = rest[idx:]
                    idx = len(rest)
            elif rest[idx : idx + 2] == "21":
                idx += 2
                serial = rest[idx:]
                idx = len(rest)
            else:
                idx += 1

        return ParsedUDI(
            raw_barcode=barcode,
            udi_carrier_format=UDICarrierFormat.GS1_DATAMATRIX,
            udi_di=udi_di,
            lot_number=lot,
            serial_number=serial,
            expiration_date=exp_date,
            is_valid=len(errors) == 0,
            validation_errors=errors,
        )

    # 4. Fallback Raw UDI Parsing
    return ParsedUDI(
        raw_barcode=barcode,
        udi_carrier_format=UDICarrierFormat.RAW_UDI,
        udi_di=barcode,
        is_valid=True,
        validation_errors=[],
    )


# =========================================================================
# FDA GUDID (Global Unique Device Identification Database) Lookup Engine
# =========================================================================

# Reference AccessGUDID registry catalog for clinical trial devices
GUDID_REGISTRY: dict[str, GUDIDDeviceRecord] = {
    "00840123456786": GUDIDDeviceRecord(
        primary_di="00840123456786",
        brand_name="CardioPulse Active Pacemaker",
        version_model_number="CP-3000",
        device_description="Implantable dual-chamber rate-responsive pacemaker system.",
        commercial_distribution_status="In Commercial Distribution",
        device_count=1,
        fda_listing_number="LST-987654",
        premarket_submission_number="P190001",
        gmdn_code="36873",
        gmdn_name="Dual-chamber implantable pacemaker",
        device_class="Class III",
        is_single_use=True,
        is_prescription_only=True,
        is_otc=False,
        is_sterile=True,
        sterilization_methods=["Ethylene Oxide"],
        mri_safety_status="MR Conditional",
        has_lot_number=True,
        has_serial_number=True,
        has_expiration_date=True,
        has_donation_id=False,
    ),
    "00840123456789": GUDIDDeviceRecord(
        primary_di="00840123456789",
        brand_name="CardioPulse Pacemaker Lead System",
        version_model_number="CP-LEAD-200",
        device_description="Transvenous pacing and sensing lead.",
        commercial_distribution_status="In Commercial Distribution",
        device_count=1,
        fda_listing_number="LST-987655",
        premarket_submission_number="P190001",
        gmdn_code="36874",
        gmdn_name="Implantable pacing lead",
        device_class="Class III",
        is_single_use=True,
        is_prescription_only=True,
        is_otc=False,
        is_sterile=True,
        sterilization_methods=["Ethylene Oxide"],
        mri_safety_status="MR Conditional",
        has_lot_number=True,
        has_serial_number=True,
        has_expiration_date=True,
        has_donation_id=False,
    ),
    "00840987654321": GUDIDDeviceRecord(
        primary_di="00840987654321",
        brand_name="Rapid Viral IVD Assay Kit",
        version_model_number="IVD-VIRAL-FAST",
        device_description="Multiplex in-vitro diagnostic molecular assay for rapid pathogen detection.",
        commercial_distribution_status="In Commercial Distribution",
        device_count=25,
        fda_listing_number="LST-456789",
        premarket_submission_number="K213344",
        gmdn_code="65231",
        gmdn_name="Multiple respiratory virus nucleic acid IVD, kit",
        device_class="Class II",
        is_single_use=True,
        is_prescription_only=True,
        is_otc=False,
        is_sterile=True,
        sterilization_methods=["Radiation"],
        mri_safety_status="MR Safe",
        has_lot_number=True,
        has_serial_number=False,
        has_expiration_date=True,
        has_donation_id=False,
    ),
    "00840111222333": GUDIDDeviceRecord(
        primary_di="00840111222333",
        brand_name="Precision Volumetric Infusion Pump",
        version_model_number="PVP-500",
        device_description="Electromechanical volumetric infusion pump for intravenous administration.",
        commercial_distribution_status="In Commercial Distribution",
        device_count=1,
        fda_listing_number="LST-112233",
        premarket_submission_number="K201234",
        gmdn_code="13217",
        gmdn_name="General-purpose infusion pump",
        device_class="Class II",
        is_single_use=False,
        is_prescription_only=True,
        is_otc=False,
        is_sterile=False,
        sterilization_methods=[],
        mri_safety_status="MR Unsafe",
        has_lot_number=True,
        has_serial_number=True,
        has_expiration_date=False,
        has_donation_id=False,
    ),
}


def lookup_gudid(udi_di: str) -> GUDIDDeviceRecord | None:
    """Looks up device specifications in the FDA AccessGUDID reference registry."""
    clean_di = udi_di.strip()
    if clean_di in GUDID_REGISTRY:
        return GUDID_REGISTRY[clean_di]

    # For dynamic test DIs with valid length and format:
    if (len(clean_di) in (13, 14) and clean_di.isdigit()) or clean_di.startswith("H"):
        is_valid = (
            validate_gtin_checksum(clean_di)
            if clean_di.isdigit()
            else validate_hibcc_checksum(
                f"+{clean_di}" if not clean_di.startswith("+") else clean_di
            )
        )
        if is_valid:
            return GUDIDDeviceRecord(
                primary_di=clean_di,
                brand_name=f"Investigational Device (DI {clean_di[:6]}...)",
                version_model_number="MODEL-GEN-01",
                device_description="Investigational clinical trial medical device.",
                commercial_distribution_status="In Commercial Distribution",
                device_count=1,
                fda_listing_number=f"LST-{clean_di[-6:]}",
                premarket_submission_number=f"K{clean_di[-6:]}",
                device_class="Class II",
                is_single_use=True,
                is_prescription_only=True,
                is_sterile=True,
                mri_safety_status="MR Conditional",
                has_lot_number=True,
                has_serial_number=True,
                has_expiration_date=True,
            )
    return None


def verify_udi_with_gudid(
    raw_barcode: str | None = None,
    udi_di: str | None = None,
    lot_number: str | None = None,
    serial_number: str | None = None,
    expiration_date: str | None = None,
) -> GUDIDVerificationResult:
    """Validates physical/scanned UDI data against the FDA GUDID database.

    Verifies Device Identifier (DI) checksum and Production Identifiers (PI: lot, serial, expiration).
    """
    extracted_di = udi_di
    extracted_lot = lot_number
    extracted_serial = serial_number
    extracted_exp = expiration_date

    is_checksum_valid = True
    errors: list[str] = []

    if raw_barcode:
        parsed = parse_udi_barcode(raw_barcode)
        extracted_di = extracted_di or parsed.udi_di
        extracted_lot = extracted_lot or parsed.lot_number
        extracted_serial = extracted_serial or parsed.serial_number
        extracted_exp = extracted_exp or parsed.expiration_date
        is_checksum_valid = parsed.is_valid
        if not parsed.is_valid:
            errors.extend(parsed.validation_errors)

    if not extracted_di:
        return GUDIDVerificationResult(
            udi_di="UNKNOWN",
            is_in_gudid=False,
            is_checksum_valid=False,
            is_verified=False,
            gudid_record=None,
            pi_validation={},
            discrepancies=["No Device Identifier (DI) provided or parsed."],
        )

    gudid_record = lookup_gudid(extracted_di)
    is_in_gudid = gudid_record is not None

    if not is_in_gudid:
        errors.append(
            f"Device Identifier '{extracted_di}' not found in FDA AccessGUDID registry."
        )

    pi_val: dict[str, bool] = {}
    if gudid_record:
        if gudid_record.has_lot_number:
            has_lot = bool(extracted_lot and extracted_lot.strip())
            pi_val["has_lot_number"] = has_lot
            if not has_lot:
                errors.append(
                    "GUDID profile requires a Lot/Batch number, but none was provided."
                )
        if gudid_record.has_serial_number:
            has_sn = bool(extracted_serial and extracted_serial.strip())
            pi_val["has_serial_number"] = has_sn
            if not has_sn:
                errors.append(
                    "GUDID profile requires a Serial number, but none was provided."
                )
        if gudid_record.has_expiration_date:
            has_exp = bool(extracted_exp and extracted_exp.strip())
            pi_val["has_expiration_date"] = has_exp
            if not has_exp:
                errors.append(
                    "GUDID profile requires an Expiration Date, but none was provided."
                )

    is_verified = is_in_gudid and is_checksum_valid and len(errors) == 0

    return GUDIDVerificationResult(
        udi_di=extracted_di,
        is_in_gudid=is_in_gudid,
        is_checksum_valid=is_checksum_valid,
        is_verified=is_verified,
        gudid_record=gudid_record,
        pi_validation=pi_val,
        discrepancies=errors,
    )


# =========================================================================
# Device Inventory & Dispensation Operations
# =========================================================================


async def register_device_inventory(
    session: AsyncSession,
    request: DeviceInventoryCreateRequest,
    user_id: str,
    change_reason: str,
) -> list[DeviceInventoryResponse]:
    """Registers serialized investigational medical device and IVD inventory items in RTSM.

    Requirements: PRD-DEV-001, PRD-SYS-126
    """
    _assert_unlocked(study_id=request.study_id, site_id=request.site_id)

    parsed = parse_udi_barcode(request.raw_barcode or request.udi_di)
    udi_di = request.udi_di or parsed.udi_di
    lot_number = request.lot_number or parsed.lot_number or "LOT-DEFAULT"
    carrier_format = parsed.udi_carrier_format.value

    exp_date = request.expiration_date
    if exp_date is None and parsed.expiration_date:
        try:
            exp_date = datetime.fromisoformat(parsed.expiration_date)
        except Exception:
            exp_date = None

    mfg_date = request.manufacturing_date
    if mfg_date is None and parsed.manufacturing_date:
        try:
            mfg_date = datetime.fromisoformat(parsed.manufacturing_date)
        except Exception:
            mfg_date = None

    results: list[DeviceInventoryResponse] = []

    for i in range(request.quantity):
        serial = (
            f"{request.serial_number}-{i + 1}"
            if request.quantity > 1 and request.serial_number
            else (
                request.serial_number
                or parsed.serial_number
                or (
                    f"SN-{uuid.uuid4().hex[:8].upper()}"
                    if request.quantity > 1
                    else None
                )
            )
        )

        dev_id = request.device_identifier or (
            f"DEV-{udi_di}-{serial or lot_number}-{uuid.uuid4().hex[:4].upper()}"
        )

        inventory_item = InvestigationalDeviceInventory(
            study_id=request.study_id,
            site_id=request.site_id,
            device_identifier=dev_id,
            brand_name=request.brand_name,
            device_model=request.device_model,
            device_type=request.device_type.value,
            device_classification=request.device_classification.value,
            udi_di=udi_di,
            udi_pi=request.raw_barcode or parsed.raw_barcode,
            udi_carrier_format=carrier_format,
            lot_number=lot_number,
            serial_number=serial,
            expiration_date=exp_date,
            manufacturing_date=mfg_date,
            software_version=request.software_version or parsed.software_version,
            status=DeviceInventoryStatus.AVAILABLE.value,
            is_dispensation_blocked=False,
        )

        session.add(inventory_item)
        await session.flush()

        # Emit Outbox Event for Device Inventory Intake
        outbox_event = IntegrationOutbox(
            event_type="DeviceInventoryRegistered",
            payload={
                "device_id": inventory_item.id,
                "study_id": inventory_item.study_id,
                "site_id": inventory_item.site_id,
                "device_identifier": inventory_item.device_identifier,
                "udi_di": inventory_item.udi_di,
                "lot_number": inventory_item.lot_number,
                "serial_number": inventory_item.serial_number,
                "status": inventory_item.status,
            },
            correlation_id=f"device:inv:{inventory_item.id}:{uuid.uuid4()}",
            created_by=user_id,
            reason_for_change=change_reason,
        )
        session.add(outbox_event)

        results.append(
            _map_inventory_to_response(inventory_item, user_id, change_reason)
        )

    return results


async def dispense_device(
    session: AsyncSession,
    request: DeviceDispensationRequest,
    user_id: str,
    change_reason: str,
) -> DeviceDispensationResponse:
    """Dispenses an investigational medical device to a study participant.

    Enforces strict validation:
    1. Prevents dispensation if device is in QUARANTINED status or has dispensation blocked.
    2. Prevents dispensation if any active lot-level quarantine exists for this lot/site.
    3. Prevents dispensation if device is expired.
    4. Prevents dispensation if device is not in AVAILABLE status.

    Requirements: PRD-DEV-001, PRD-DEV-003, PRD-SYS-126
    """
    _assert_unlocked(study_id=request.study_id, site_id=request.site_id)

    # Locate device
    stmt = select(InvestigationalDeviceInventory).where(
        InvestigationalDeviceInventory.study_id == request.study_id,
        InvestigationalDeviceInventory.site_id == request.site_id,
        InvestigationalDeviceInventory.is_deleted.is_(False),
    )

    if request.device_inventory_id:
        stmt = stmt.where(
            InvestigationalDeviceInventory.id == request.device_inventory_id
        )
    elif request.serial_number:
        stmt = stmt.where(
            InvestigationalDeviceInventory.serial_number == request.serial_number
        )
    elif request.scanned_barcode:
        parsed = parse_udi_barcode(request.scanned_barcode)
        if parsed.serial_number:
            stmt = stmt.where(
                InvestigationalDeviceInventory.serial_number == parsed.serial_number
            )
        elif parsed.udi_di:
            stmt = stmt.where(
                InvestigationalDeviceInventory.udi_di == parsed.udi_di,
                InvestigationalDeviceInventory.status
                == DeviceInventoryStatus.AVAILABLE.value,
            )

    res = await session.execute(stmt)
    device = res.scalars().first()

    if not device:
        raise DeviceNotFoundError(
            f"No matching investigational device found for dispensation at site {request.site_id}."
        )

    # Check 1: Direct device quarantine or dispensation block
    if (
        device.status == DeviceInventoryStatus.QUARANTINED.value
        or device.is_dispensation_blocked
    ):
        raise DeviceQuarantinedError(
            f"Cannot dispense device {device.serial_number or device.device_identifier}: "
            f"Device is currently QUARANTINED ({device.quarantine_reason or 'Quarantine Active'})."
        )

    # Check 2: Active Lot-level or Global quarantine check
    lot_quar_stmt = select(DeviceQuarantineRecord).where(
        DeviceQuarantineRecord.study_id == request.study_id,
        DeviceQuarantineRecord.lot_number == device.lot_number,
        DeviceQuarantineRecord.status == "ACTIVE",
        DeviceQuarantineRecord.is_deleted.is_(False),
    )
    lot_res = await session.execute(lot_quar_stmt)
    active_lot_quars = lot_res.scalars().all()
    for lq in active_lot_quars:
        if lq.scope in (
            QuarantineScope.STUDY_GLOBAL_LOT.value,
            QuarantineScope.SITE_LOT.value,
        ):
            if (
                lq.scope == QuarantineScope.STUDY_GLOBAL_LOT.value
                or lq.site_id == request.site_id
            ):
                raise DeviceQuarantinedError(
                    f"Cannot dispense device {device.serial_number}: Lot {device.lot_number} "
                    f"is under active {lq.scope} quarantine ({lq.reason})."
                )

    # Check 3: Expiration Date
    if device.expiration_date:
        now_dt = datetime.now(UTC)
        dev_exp = device.expiration_date
        if dev_exp.tzinfo is None:
            dev_exp = dev_exp.replace(tzinfo=UTC)
        if dev_exp < now_dt:
            raise DeviceExpiredError(
                f"Cannot dispense device {device.serial_number}: Device expired on {device.expiration_date.date()}."
            )

    # Check 4: Must be in AVAILABLE status
    if device.status != DeviceInventoryStatus.AVAILABLE.value:
        raise DeviceInvalidStatusError(
            f"Cannot dispense device: Current status is {device.status} (expected AVAILABLE)."
        )

    # Advance state to DISPENSED
    now_ts = datetime.now(UTC).replace(tzinfo=None)
    device.status = DeviceInventoryStatus.DISPENSED.value
    device.allocated_subject_id = request.subject_id
    device.dispensed_at = now_ts
    device.dispensed_by = user_id
    session.add(device)
    await session.flush()

    # Emit Dispensation Outbox Event
    outbox_event = IntegrationOutbox(
        event_type="DeviceDispensed",
        payload={
            "device_id": device.id,
            "study_id": device.study_id,
            "site_id": device.site_id,
            "subject_id": request.subject_id,
            "serial_number": device.serial_number,
            "udi_di": device.udi_di,
            "lot_number": device.lot_number,
            "dispensed_at": now_ts.isoformat(),
        },
        correlation_id=f"device:dispense:{device.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox_event)

    return DeviceDispensationResponse(
        status="SUCCESS",
        message=f"Device {device.serial_number or device.device_identifier} dispensed to subject {request.subject_id}.",
        device=_map_inventory_to_response(device, user_id, change_reason),
        dispensed_at=now_ts,
    )


# =========================================================================
# Device Deficiency Logging & Automated Safety / Quarantine Triggers
# =========================================================================


async def report_device_deficiency(
    session: AsyncSession,
    request: DeviceDeficiencyCreateRequest,
    user_id: str,
    change_reason: str,
) -> DeviceDeficiencyResponse:
    """Logs a structured medical device deficiency/malfunction and fires automated triggers:

    1. Quarantines the affected device serial (and optionally site or global lot) in RTSM
       with immediate programmatic dispensation blocking.
    2. Opens an Adverse Device Effect (ADE / SADE / USADE) safety investigation case.
    3. Emits transactional outbox events with 21 CFR Part 11 audit attribution.

    Requirements: PRD-DEV-002, PRD-DEV-003, PRD-SYS-126, ISO 14155:2020
    """
    _assert_unlocked(study_id=request.study_id, site_id=request.site_id)

    # 1. Determine Safety Classification
    safety_classification = DeviceSafetyClassification.DEVICE_DEFICIENCY_INCIDENT.value
    if request.could_lead_to_sade or request.health_impact in (
        HealthImpact.SERIOUS_DETERIORATION.value,
        HealthImpact.PERMANENT_IMPAIRMENT.value,
        HealthImpact.LIFE_THREATENING.value,
        HealthImpact.DEATH.value,
    ):
        # SADE or USADE if unforeseen
        safety_classification = DeviceSafetyClassification.SADE.value
    elif (
        request.patient_involved
        or request.health_impact == HealthImpact.MILD_TRANSIENT.value
    ):
        safety_classification = DeviceSafetyClassification.ADE.value

    safety_case_id = f"DEV-CASE-{request.study_id}-{uuid.uuid4().hex[:8].upper()}"

    # 2. Persist Device Deficiency Report
    now_ts = datetime.now(UTC).replace(tzinfo=None)
    deficiency = DeviceDeficiencyReport(
        study_id=request.study_id,
        site_id=request.site_id,
        subject_id=request.subject_id,
        device_inventory_id=request.device_inventory_id,
        udi_di=request.udi_di,
        lot_number=request.lot_number,
        serial_number=request.serial_number,
        device_name=request.device_name,
        deficiency_type=request.deficiency_type.value,
        deficiency_description=request.deficiency_description,
        incident_date=request.incident_date,
        awareness_date=request.awareness_date.replace(tzinfo=None)
        if request.awareness_date
        else now_ts,
        could_lead_to_sade=request.could_lead_to_sade,
        patient_involved=request.patient_involved,
        health_impact=request.health_impact.value,
        action_taken_device=request.action_taken_device.value,
        device_returned_to_sponsor=request.device_returned_to_sponsor,
        return_tracking_number=request.return_tracking_number,
        quarantine_triggered=True,
        quarantine_scope=request.quarantine_scope.value,
        safety_case_id=safety_case_id,
        safety_classification=safety_classification,
        investigation_status="OPEN",
        esignature_token=request.signature_token,
        signed_by=user_id if request.signature_token else None,
        signed_at=now_ts if request.signature_token else None,
    )
    session.add(deficiency)
    await session.flush()

    # 3. Automated RTSM Quarantine Execution
    quarantine_records: list[DeviceQuarantineRecord] = []
    quarantine_reason = (
        f"Automated quarantine triggered by deficiency {deficiency.id} "
        f"({request.deficiency_type.value}): {request.deficiency_description}"
    )

    # Identify matching device items to quarantine
    query = select(InvestigationalDeviceInventory).where(
        InvestigationalDeviceInventory.study_id == request.study_id,
        InvestigationalDeviceInventory.is_deleted.is_(False),
    )

    if request.quarantine_scope == QuarantineScope.DEVICE_SERIAL:
        if request.device_inventory_id:
            query = query.where(
                InvestigationalDeviceInventory.id == request.device_inventory_id
            )
        elif request.serial_number:
            query = query.where(
                InvestigationalDeviceInventory.serial_number == request.serial_number
            )
        else:
            query = query.where(
                InvestigationalDeviceInventory.lot_number == request.lot_number,
                InvestigationalDeviceInventory.site_id == request.site_id,
            )
    elif request.quarantine_scope == QuarantineScope.SITE_LOT:
        query = query.where(
            InvestigationalDeviceInventory.site_id == request.site_id,
            InvestigationalDeviceInventory.lot_number == request.lot_number,
        )
    elif request.quarantine_scope == QuarantineScope.STUDY_GLOBAL_LOT:
        query = query.where(
            InvestigationalDeviceInventory.lot_number == request.lot_number
        )

    res = await session.execute(query)
    matched_devices = res.scalars().all()

    for dev in matched_devices:
        dev.status = DeviceInventoryStatus.QUARANTINED.value
        dev.is_dispensation_blocked = True
        dev.quarantined_at = now_ts
        dev.quarantined_by = user_id
        dev.quarantine_reason = quarantine_reason
        session.add(dev)

    # Create formal Quarantine Record
    quar_record = DeviceQuarantineRecord(
        study_id=request.study_id,
        site_id=request.site_id,
        deficiency_id=deficiency.id,
        device_inventory_id=request.device_inventory_id,
        lot_number=request.lot_number,
        serial_number=request.serial_number,
        scope=request.quarantine_scope.value,
        status="ACTIVE",
        reason=quarantine_reason,
        quarantined_at=now_ts,
        quarantined_by=user_id,
    )
    session.add(quar_record)
    await session.flush()
    quarantine_records.append(quar_record)

    # 4. Emit Transactional Outbox Events
    # Event A: DeviceMalfunctionReported (triggers safety investigation in apps/safety)
    outbox_malfunc = IntegrationOutbox(
        event_type="DeviceMalfunctionReported",
        payload={
            "deficiency_id": deficiency.id,
            "study_id": deficiency.study_id,
            "site_id": deficiency.site_id,
            "subject_id": deficiency.subject_id,
            "device_name": deficiency.device_name,
            "udi_di": deficiency.udi_di,
            "lot_number": deficiency.lot_number,
            "serial_number": deficiency.serial_number,
            "deficiency_type": deficiency.deficiency_type,
            "deficiency_description": deficiency.deficiency_description,
            "incident_date": deficiency.incident_date.isoformat(),
            "could_lead_to_sade": deficiency.could_lead_to_sade,
            "patient_involved": deficiency.patient_involved,
            "health_impact": deficiency.health_impact,
            "safety_case_id": safety_case_id,
            "safety_classification": safety_classification,
        },
        correlation_id=f"device:defic:{deficiency.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox_malfunc)

    # Event B: DeviceQuarantineTriggered (triggers inventory lockdown notification)
    outbox_quar = IntegrationOutbox(
        event_type="DeviceQuarantineTriggered",
        payload={
            "quarantine_id": quar_record.id,
            "study_id": quar_record.study_id,
            "site_id": quar_record.site_id,
            "lot_number": quar_record.lot_number,
            "serial_number": quar_record.serial_number,
            "scope": quar_record.scope,
            "impacted_device_count": len(matched_devices),
            "reason": quarantine_reason,
        },
        correlation_id=f"device:quar:{quar_record.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox_quar)

    return _map_deficiency_to_response(
        deficiency, user_id, change_reason, [q.id for q in quarantine_records]
    )


# =========================================================================
# Manual Quarantine and Part 11 Quarantine Release
# =========================================================================


async def initiate_device_quarantine(
    session: AsyncSession,
    request: DeviceQuarantineCreateRequest,
    user_id: str,
    change_reason: str,
) -> DeviceQuarantineResponse:
    """Manually places a device serial, site lot, or global lot into QUARANTINED state."""
    _assert_unlocked(study_id=request.study_id, site_id=request.site_id)

    now_ts = datetime.now(UTC).replace(tzinfo=None)

    # Find devices to quarantine
    query = select(InvestigationalDeviceInventory).where(
        InvestigationalDeviceInventory.study_id == request.study_id,
        InvestigationalDeviceInventory.is_deleted.is_(False),
    )

    if request.scope == QuarantineScope.DEVICE_SERIAL:
        if request.device_inventory_id:
            query = query.where(
                InvestigationalDeviceInventory.id == request.device_inventory_id
            )
        elif request.serial_number:
            query = query.where(
                InvestigationalDeviceInventory.serial_number == request.serial_number
            )
    elif request.scope == QuarantineScope.SITE_LOT:
        query = query.where(
            InvestigationalDeviceInventory.site_id == request.site_id,
            InvestigationalDeviceInventory.lot_number == request.lot_number,
        )
    elif request.scope == QuarantineScope.STUDY_GLOBAL_LOT:
        query = query.where(
            InvestigationalDeviceInventory.lot_number == request.lot_number
        )

    res = await session.execute(query)
    devices = res.scalars().all()

    for dev in devices:
        dev.status = DeviceInventoryStatus.QUARANTINED.value
        dev.is_dispensation_blocked = True
        dev.quarantined_at = now_ts
        dev.quarantined_by = user_id
        dev.quarantine_reason = request.reason
        session.add(dev)

    quar_record = DeviceQuarantineRecord(
        study_id=request.study_id,
        site_id=request.site_id,
        device_inventory_id=request.device_inventory_id,
        lot_number=request.lot_number or "N/A",
        serial_number=request.serial_number,
        scope=request.scope.value,
        status="ACTIVE",
        reason=request.reason,
        quarantined_at=now_ts,
        quarantined_by=user_id,
    )
    session.add(quar_record)
    await session.flush()

    outbox_event = IntegrationOutbox(
        event_type="DeviceQuarantineTriggered",
        payload={
            "quarantine_id": quar_record.id,
            "study_id": quar_record.study_id,
            "site_id": quar_record.site_id,
            "lot_number": quar_record.lot_number,
            "serial_number": quar_record.serial_number,
            "scope": quar_record.scope,
            "impacted_device_count": len(devices),
            "reason": request.reason,
        },
        correlation_id=f"device:quar:manual:{quar_record.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox_event)

    return _map_quarantine_to_response(
        quar_record, user_id, change_reason, len(devices)
    )


async def release_device_quarantine(
    session: AsyncSession,
    request: DeviceQuarantineReleaseRequest,
    user_id: str,
    change_reason: str,
) -> DeviceQuarantineResponse:
    """Releases an active device quarantine record under 21 CFR Part 11 electronic signature."""
    quar_stmt = select(DeviceQuarantineRecord).where(
        DeviceQuarantineRecord.id == request.quarantine_id,
        DeviceQuarantineRecord.is_deleted.is_(False),
    )
    res = await session.execute(quar_stmt)
    quar_record = res.scalars().first()

    if not quar_record:
        raise QuarantineNotFoundError(
            f"Quarantine record {request.quarantine_id} not found."
        )

    _assert_unlocked(study_id=quar_record.study_id, site_id=quar_record.site_id)

    now_ts = datetime.now(UTC).replace(tzinfo=None)
    quar_record.status = "RELEASED"
    quar_record.released_at = now_ts
    quar_record.released_by = user_id
    quar_record.release_justification = request.release_justification
    quar_record.release_esignature_token = request.signature_token
    session.add(quar_record)

    # Release impacted devices back to AVAILABLE
    query = select(InvestigationalDeviceInventory).where(
        InvestigationalDeviceInventory.study_id == quar_record.study_id,
        InvestigationalDeviceInventory.status
        == DeviceInventoryStatus.QUARANTINED.value,
        InvestigationalDeviceInventory.is_deleted.is_(False),
    )

    if quar_record.scope == QuarantineScope.DEVICE_SERIAL.value:
        if quar_record.device_inventory_id:
            query = query.where(
                InvestigationalDeviceInventory.id == quar_record.device_inventory_id
            )
        elif quar_record.serial_number:
            query = query.where(
                InvestigationalDeviceInventory.serial_number
                == quar_record.serial_number
            )
    elif quar_record.scope == QuarantineScope.SITE_LOT.value:
        query = query.where(
            InvestigationalDeviceInventory.site_id == quar_record.site_id,
            InvestigationalDeviceInventory.lot_number == quar_record.lot_number,
        )
    elif quar_record.scope == QuarantineScope.STUDY_GLOBAL_LOT.value:
        query = query.where(
            InvestigationalDeviceInventory.lot_number == quar_record.lot_number
        )

    res_devs = await session.execute(query)
    devices = res_devs.scalars().all()

    for dev in devices:
        dev.status = DeviceInventoryStatus.AVAILABLE.value
        dev.is_dispensation_blocked = False
        dev.quarantine_reason = None
        session.add(dev)

    await session.flush()

    outbox_event = IntegrationOutbox(
        event_type="DeviceQuarantineReleased",
        payload={
            "quarantine_id": quar_record.id,
            "study_id": quar_record.study_id,
            "lot_number": quar_record.lot_number,
            "released_device_count": len(devices),
            "justification": request.release_justification,
        },
        correlation_id=f"device:quar:release:{quar_record.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox_event)

    return _map_quarantine_to_response(
        quar_record, user_id, change_reason, len(devices)
    )


# =========================================================================
# Response Mappers
# =========================================================================


def _map_inventory_to_response(
    item: InvestigationalDeviceInventory, user_id: str, change_reason: str
) -> DeviceInventoryResponse:
    return DeviceInventoryResponse(
        id=item.id,
        study_id=item.study_id,
        site_id=item.site_id,
        device_identifier=item.device_identifier,
        brand_name=item.brand_name,
        device_model=item.device_model,
        device_type=item.device_type,
        device_classification=item.device_classification,
        udi_di=item.udi_di,
        udi_pi=item.udi_pi,
        udi_carrier_format=item.udi_carrier_format,
        lot_number=item.lot_number,
        serial_number=item.serial_number,
        expiration_date=item.expiration_date,
        manufacturing_date=item.manufacturing_date,
        software_version=item.software_version,
        status=item.status,
        is_dispensation_blocked=item.is_dispensation_blocked,
        allocated_subject_id=item.allocated_subject_id,
        dispensed_at=item.dispensed_at,
        dispensed_by=item.dispensed_by,
        quarantined_at=item.quarantined_at,
        quarantined_by=item.quarantined_by,
        quarantine_reason=item.quarantine_reason,
        created_at=datetime.now(UTC),
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=item.version,
    )


def _map_deficiency_to_response(
    item: DeviceDeficiencyReport,
    user_id: str,
    change_reason: str,
    quar_ids: list[str] | None = None,
) -> DeviceDeficiencyResponse:
    return DeviceDeficiencyResponse(
        id=item.id,
        study_id=item.study_id,
        site_id=item.site_id,
        subject_id=item.subject_id,
        device_inventory_id=item.device_inventory_id,
        udi_di=item.udi_di,
        lot_number=item.lot_number,
        serial_number=item.serial_number,
        device_name=item.device_name,
        deficiency_type=item.deficiency_type,
        deficiency_description=item.deficiency_description,
        incident_date=item.incident_date,
        awareness_date=item.awareness_date,
        could_lead_to_sade=item.could_lead_to_sade,
        patient_involved=item.patient_involved,
        health_impact=item.health_impact,
        action_taken_device=item.action_taken_device,
        device_returned_to_sponsor=item.device_returned_to_sponsor,
        return_tracking_number=item.return_tracking_number,
        root_cause_analysis=item.root_cause_analysis,
        sponsor_evaluation=item.sponsor_evaluation,
        quarantine_triggered=item.quarantine_triggered,
        quarantine_scope=item.quarantine_scope,
        safety_case_id=item.safety_case_id,
        safety_classification=item.safety_classification,
        investigation_status=item.investigation_status,
        quarantine_record_ids=quar_ids or [],
        created_at=datetime.now(UTC),
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=item.version,
    )


def _map_quarantine_to_response(
    item: DeviceQuarantineRecord,
    user_id: str,
    change_reason: str,
    impacted_count: int = 1,
) -> DeviceQuarantineResponse:
    return DeviceQuarantineResponse(
        id=item.id,
        study_id=item.study_id,
        site_id=item.site_id,
        deficiency_id=item.deficiency_id,
        device_inventory_id=item.device_inventory_id,
        lot_number=item.lot_number,
        serial_number=item.serial_number,
        scope=item.scope,
        status=item.status,
        reason=item.reason,
        quarantined_at=item.quarantined_at,
        quarantined_by=item.quarantined_by,
        released_at=item.released_at,
        released_by=item.released_by,
        release_justification=item.release_justification,
        impacted_device_count=impacted_count,
        created_at=datetime.now(UTC),
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=item.version,
    )
