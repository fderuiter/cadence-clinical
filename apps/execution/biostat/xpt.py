"""SAS Transport (XPT v5 and XPT v8) Binary Serializer and Deserializer.

Implements pure-Python generation and parsing of SAS Transport files according to
the SAS TS-140 specification, including IBM 360 64-bit hexadecimal floating-point
encoding and decoding.
"""

import math
import struct
from datetime import UTC, datetime
from typing import Any


def double_to_ibm(val: float | int | None) -> bytes:
    """Encodes a Python numeric value or None into an 8-byte IBM 360 float (big-endian).

    SAS uses IBM 360/System 370 hexadecimal floating point:
    - Byte 0: Bit 7 = Sign (0 positive, 1 negative), Bits 0-6 = Biased Exponent (base 16, excess-64).
    - Bytes 1-7: 56-bit mantissa fraction (0.0625 <= mantissa < 1.0).
    - Missing is represented as ASCII '.' (0x2E) in byte 0 with trailing zeros.
    """
    if val is None:
        # SAS standard missing value
        return b".\x00\x00\x00\x00\x00\x00\x00"
    if val == 0 or val == 0.0:
        return b"\x00" * 8

    f_val = float(val)
    sign = 1 if f_val < 0.0 else 0
    abs_val = abs(f_val)

    # Calculate exponent in base 16: abs_val = mantissa * 16^exp, 1/16 <= mantissa < 1.0
    exp = math.floor(math.log(abs_val, 16)) + 1
    mantissa = abs_val / (16.0**exp)

    # Normalize mantissa
    while mantissa >= 1.0:
        mantissa /= 16.0
        exp += 1
    while mantissa < (1.0 / 16.0) and exp > -64:
        mantissa *= 16.0
        exp -= 1

    biased_exp = exp + 64
    if biased_exp < 0:
        return b"\x00" * 8
    if biased_exp > 127:
        biased_exp = 127
        mantissa = 1.0 - (1.0 / (1 << 56))

    mantissa_int = int(round(mantissa * (1 << 56)))
    if mantissa_int >= (1 << 56):
        mantissa_int = (1 << 56) - 1

    first_byte = (sign << 7) | (biased_exp & 0x7F)
    return bytes([first_byte]) + mantissa_int.to_bytes(7, byteorder="big")


def ibm_to_double(b: bytes) -> float | None:
    """Decodes an 8-byte IBM 360 float (big-endian) into a Python float or None for missing values."""
    if len(b) < 8:
        b = b.ljust(8, b"\x00")
    if b == b"\x00" * 8:
        return 0.0
    if b[0] == 0x2E:  # ASCII '.'
        return None

    sign = 1 if (b[0] & 0x80) else 0
    biased_exp = b[0] & 0x7F
    exp = biased_exp - 64
    mantissa_int = int.from_bytes(b[1:8], byteorder="big")
    mantissa = mantissa_int / float(1 << 56)

    val = mantissa * (16.0**exp)
    return -val if sign else val


def _format_sas_datetime(dt: datetime | None = None) -> str:
    """Formats datetime into SAS 16-character format: ddMMMyy:hh:mm:ss in uppercase."""
    if dt is None:
        dt = datetime.now(UTC)
    day = f"{dt.day:02d}"
    month = dt.strftime("%b").upper()
    year = f"{dt.year % 100:02d}"
    time_str = dt.strftime("%H:%M:%S")
    return f"{day}{month}{year}:{time_str}"


def _pad_card(text: str, length: int = 80) -> bytes:
    """Pads an ASCII string to exact length (default 80 bytes) using spaces."""
    encoded = text.encode("ascii", errors="replace")
    if len(encoded) < length:
        encoded = encoded.ljust(length, b" ")
    elif len(encoded) > length:
        encoded = encoded[:length]
    return encoded


def _infer_variable_type_and_length(
    var_name: str, values: list[Any], default_meta: dict[str, Any] | None = None
) -> tuple[int, int, str]:
    """Infers SAS variable type (1=numeric, 2=character), byte length, and label.

    Returns:
        tuple: (type_code: int, byte_length: int, label: str)
    """
    if default_meta:
        ty = default_meta.get("type", "string").lower()
        lbl = default_meta.get("label", var_name)
        if ty in ("integer", "float", "double", "decimal", "numeric"):
            return 1, 8, lbl
        length = default_meta.get("length") or 8
        # Check actual values to ensure string length isn't exceeded
        max_str_len = 0
        for v in values:
            if v is not None:
                max_str_len = max(
                    max_str_len, len(str(v).encode("utf-8", errors="replace"))
                )
        return 2, max(length, max_str_len, 8), lbl

    # Infer from values
    is_numeric = True
    max_len = 0
    has_values = False

    for v in values:
        if v is None:
            continue
        has_values = True
        if isinstance(v, bool):
            is_numeric = False
            max_len = max(max_len, 5)
        elif isinstance(v, (int, float)):
            pass
        elif isinstance(v, str):
            # Try float conversion
            s = v.strip()
            if not s:
                continue
            try:
                float(s)
            except ValueError:
                is_numeric = False
            max_len = max(max_len, len(s.encode("utf-8", errors="replace")))
        else:
            is_numeric = False
            max_len = max(max_len, len(str(v).encode("utf-8", errors="replace")))

    if not has_values or is_numeric:
        return 1, 8, var_name
    return 2, max(max_len, 8), var_name


def write_xpt_v5(
    dataset_name: str,
    records: list[dict[str, Any]],
    variables_metadata: list[dict[str, Any]] | None = None,
) -> bytes:
    """Serializes dataset records to SAS Transport Version 5 (XPT v5) binary format.

    According to SAS TS-140:
    - 80-byte header card images.
    - 140-byte NAMESTR records packed in 80-byte multiples.
    - Observation data serialized consecutively with IBM 360 floats and fixed-width strings,
      padded with spaces to an 80-byte boundary.
    """
    ds_name_clean = dataset_name.strip().upper()[:8]
    if not ds_name_clean:
        ds_name_clean = "DATASET"

    # 1. Determine variables and metadata
    var_names: list[str] = []
    if variables_metadata:
        var_names = [m["name"].upper() for m in variables_metadata]

    # Collect any extra variables present in rows
    row_keys: set[str] = set()
    for r in records:
        row_keys.update(k.upper() for k in r)

    # Exclude internal auditable fields
    internal_fields = {
        "CREATED_AT",
        "CREATED_BY",
        "REASON_FOR_CHANGE",
        "VERSION_INDEX",
    }
    row_keys -= internal_fields

    for k in sorted(row_keys):
        if k not in var_names:
            var_names.append(k)

    meta_lookup = {}
    if variables_metadata:
        for m in variables_metadata:
            meta_lookup[m["name"].upper()] = m

    var_specs = []
    current_pos = 0
    for idx, var_name in enumerate(var_names, start=1):
        vals = [r.get(var_name, r.get(var_name.lower())) for r in records]
        default_meta = meta_lookup.get(var_name)
        type_code, byte_len, label = _infer_variable_type_and_length(
            var_name, vals, default_meta
        )
        var_specs.append(
            {
                "name": var_name[:8],
                "label": label[:40],
                "type": type_code,  # 1=numeric, 2=character
                "length": byte_len,
                "var_num": idx,
                "pos": current_pos,
            }
        )
        current_pos += byte_len

    # 2. Build Library & Member Headers
    now_sas = _format_sas_datetime()
    out = bytearray()

    # Header Card 1: Library Header Record
    out.extend(
        _pad_card(
            "HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )
    # Header Card 2: SASLIB
    out.extend(_pad_card("SAS     SAS     SASLIB  6.06    bsd4.3  "))
    # Header Card 3: Date
    date_line = f"{now_sas:<20}{now_sas:<20}"
    out.extend(_pad_card(date_line))

    # Member Header Card
    out.extend(
        _pad_card(
            "HEADER RECORD*******MEMBER  HEADER RECORD!!!!!!!000000000000000001600000000000  "
        )
    )
    # Descriptor Header Card
    out.extend(
        _pad_card(
            "HEADER RECORD*******DSCRPTR HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )
    # SASDATA Header Card
    ds_line = f"SAS     SASDATA {ds_name_clean:<8}6.06    bsd4.3  "
    out.extend(_pad_card(ds_line))
    # Member Date Card
    out.extend(_pad_card(date_line))

    # Namestr Header Card
    num_vars = len(var_specs)
    namestr_card = f"HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!00000000000000000140{num_vars:04d}000000  "
    out.extend(_pad_card(namestr_card))

    # 3. Build Namestr Records (140 bytes each)
    namestr_bytes = bytearray()
    for spec in var_specs:
        nlng = spec["type"]  # 1=numeric, 2=character
        nhfun = 0
        nlen = spec["length"]
        nvar0 = spec["var_num"]
        nname = spec["name"].ljust(8, " ").encode("ascii")[:8]
        nlabel = spec["label"].ljust(40, " ").encode("ascii", errors="replace")[:40]
        nform = b" " * 8
        nfl = 0
        nfd = 0
        nfj = 0
        nfill = b"\x00\x00"
        niform = b" " * 8
        nifl = 0
        nifd = 0
        npos = spec["pos"]
        rest = b" " * 52

        # Pack struct: >hhhh 8s 40s 8s hhh 2s 8s hh i 52s = 140 bytes
        entry = struct.pack(
            ">hhhh8s40s8shhh2s8shhi52s",
            nlng,
            nhfun,
            nlen,
            nvar0,
            nname,
            nlabel,
            nform,
            nfl,
            nfd,
            nfj,
            nfill,
            niform,
            nifl,
            nifd,
            npos,
            rest,
        )
        namestr_bytes.extend(entry)

    # Pad namestrs to a multiple of 80 bytes
    remainder = len(namestr_bytes) % 80
    if remainder != 0:
        namestr_bytes.extend(b" " * (80 - remainder))
    out.extend(namestr_bytes)

    # 4. Observation Header Record
    out.extend(
        _pad_card(
            "HEADER RECORD*******OBS     HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )

    # 5. Observation Records
    obs_bytes = bytearray()
    for r in records:
        for spec in var_specs:
            v_name = spec["name"]
            val = r.get(v_name, r.get(v_name.lower()))
            if spec["type"] == 1:  # numeric
                if val is None or val == "":
                    obs_bytes.extend(double_to_ibm(None))
                else:
                    try:
                        obs_bytes.extend(double_to_ibm(float(val)))
                    except ValueError, TypeError:
                        obs_bytes.extend(double_to_ibm(None))
            else:  # character
                b_len = spec["length"]
                if val is None:
                    obs_bytes.extend(b" " * b_len)
                else:
                    s_enc = str(val).encode("utf-8", errors="replace")
                    if len(s_enc) < b_len:
                        s_enc = s_enc.ljust(b_len, b" ")
                    elif len(s_enc) > b_len:
                        s_enc = s_enc[:b_len]
                    obs_bytes.extend(s_enc)

    # Pad observation block to a multiple of 80 bytes
    obs_rem = len(obs_bytes) % 80
    if obs_rem != 0:
        obs_bytes.extend(b" " * (80 - obs_rem))
    out.extend(obs_bytes)

    return bytes(out)


def write_xpt_v8(
    dataset_name: str,
    records: list[dict[str, Any]],
    variables_metadata: list[dict[str, Any]] | None = None,
) -> bytes:
    """Serializes dataset records to SAS Transport Version 8 (XPT v8) format.

    Extends XPT v5 to support longer variable names (up to 32 characters)
    and labels (up to 256 characters) with v8 header card images.
    """
    ds_name_clean = dataset_name.strip().upper()[:32]
    if not ds_name_clean:
        ds_name_clean = "DATASET"

    var_names: list[str] = []
    if variables_metadata:
        var_names = [m["name"].upper() for m in variables_metadata]

    row_keys: set[str] = set()
    for r in records:
        row_keys.update(k.upper() for k in r)

    internal_fields = {
        "CREATED_AT",
        "CREATED_BY",
        "REASON_FOR_CHANGE",
        "VERSION_INDEX",
    }
    row_keys -= internal_fields

    for k in sorted(row_keys):
        if k not in var_names:
            var_names.append(k)

    meta_lookup = {}
    if variables_metadata:
        for m in variables_metadata:
            meta_lookup[m["name"].upper()] = m

    var_specs = []
    current_pos = 0
    for idx, var_name in enumerate(var_names, start=1):
        vals = [r.get(var_name, r.get(var_name.lower())) for r in records]
        default_meta = meta_lookup.get(var_name)
        type_code, byte_len, label = _infer_variable_type_and_length(
            var_name, vals, default_meta
        )
        var_specs.append(
            {
                "name": var_name[:32],
                "label": label[:256],
                "type": type_code,
                "length": byte_len,
                "var_num": idx,
                "pos": current_pos,
            }
        )
        current_pos += byte_len

    now_sas = _format_sas_datetime()
    out = bytearray()

    # V8 Library Header Card
    out.extend(
        _pad_card(
            "HEADER RECORD*******LIBV8   HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )
    out.extend(_pad_card("SAS     SAS     SASLIB  8.00    LIN X64 "))
    date_line = f"{now_sas:<20}{now_sas:<20}"
    out.extend(_pad_card(date_line))

    # V8 Member Header
    out.extend(
        _pad_card(
            "HEADER RECORD*******MEMBV8  HEADER RECORD!!!!!!!000000000000000001600000000000  "
        )
    )
    out.extend(
        _pad_card(
            "HEADER RECORD*******DSCPV8  HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )
    ds_line = f"SAS     SASDATA {ds_name_clean:<32}8.00    LIN X64 "
    out.extend(_pad_card(ds_line))
    out.extend(_pad_card(date_line))

    # V8 Namestr Header
    num_vars = len(var_specs)
    namestr_card = f"HEADER RECORD*******NAMSTRV8HEADER RECORD!!!!!!!00000000000000000512{num_vars:04d}000000  "
    out.extend(_pad_card(namestr_card))

    # V8 Namestr records (512 bytes each, padded)
    namestr_bytes = bytearray()
    for spec in var_specs:
        nlng = spec["type"]
        nhfun = 0
        nlen = spec["length"]
        nvar0 = spec["var_num"]
        nname = spec["name"].ljust(32, " ").encode("ascii")[:32]
        nlabel = spec["label"].ljust(256, " ").encode("ascii", errors="replace")[:256]
        nform = b" " * 32
        npos = spec["pos"]
        rest = b" " * 180

        # Pack struct: >hhhh 32s 256s 32s i 180s = 512 bytes
        entry = struct.pack(
            ">hhhh32s256s32si180s",
            nlng,
            nhfun,
            nlen,
            nvar0,
            nname,
            nlabel,
            nform,
            npos,
            rest,
        )
        namestr_bytes.extend(entry)

    rem = len(namestr_bytes) % 80
    if rem != 0:
        namestr_bytes.extend(b" " * (80 - rem))
    out.extend(namestr_bytes)

    # V8 Obs Header
    out.extend(
        _pad_card(
            "HEADER RECORD*******OBSV8   HEADER RECORD!!!!!!!000000000000000000000000000000  "
        )
    )

    # Obs data
    obs_bytes = bytearray()
    for r in records:
        for spec in var_specs:
            v_name = spec["name"]
            val = r.get(v_name, r.get(v_name.lower()))
            if spec["type"] == 1:
                if val is None or val == "":
                    obs_bytes.extend(double_to_ibm(None))
                else:
                    try:
                        obs_bytes.extend(double_to_ibm(float(val)))
                    except ValueError, TypeError:
                        obs_bytes.extend(double_to_ibm(None))
            else:
                b_len = spec["length"]
                if val is None:
                    obs_bytes.extend(b" " * b_len)
                else:
                    s_enc = str(val).encode("utf-8", errors="replace")
                    if len(s_enc) < b_len:
                        s_enc = s_enc.ljust(b_len, b" ")
                    elif len(s_enc) > b_len:
                        s_enc = s_enc[:b_len]
                    obs_bytes.extend(s_enc)

    obs_rem = len(obs_bytes) % 80
    if obs_rem != 0:
        obs_bytes.extend(b" " * (80 - obs_rem))
    out.extend(obs_bytes)

    return bytes(out)


def write_xpt(
    dataset_name: str,
    records: list[dict[str, Any]],
    version: str = "v5",
    variables_metadata: list[dict[str, Any]] | None = None,
) -> bytes:
    """Entry point to serialize dataset records to SAS XPT format (v5 or v8)."""
    ver_clean = version.strip().lower()
    if ver_clean in ("v8", "8"):
        return write_xpt_v8(dataset_name, records, variables_metadata)
    return write_xpt_v5(dataset_name, records, variables_metadata)


def read_xpt(data: bytes) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    """Parses SAS XPT (v5 or v8) binary data and returns metadata and extracted records.

    Returns:
        tuple: (metadata: dict, records: list[dict])
    """
    if len(data) < 160:
        raise ValueError("XPT file is too short to contain valid SAS Transport header.")

    is_v8 = b"LIBV8" in data[:80] or b"NAMSTRV8" in data[:600]

    if is_v8:
        return _read_xpt_v8(data)
    return _read_xpt_v5(data)


def _read_xpt_v5(data: bytes) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    """Internal reader for XPT v5."""
    # Find NAMESTR header
    namestr_idx = data.find(b"HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!")
    if namestr_idx == -1:
        raise ValueError("Invalid XPT v5 file: NAMESTR header record not found.")

    header_card = data[namestr_idx : namestr_idx + 80].decode("ascii", errors="replace")
    count_str = header_card[68:72].strip()
    num_vars = int(count_str) if count_str.isdigit() else 0

    cur = namestr_idx + 80
    variables = []
    for _ in range(num_vars):
        chunk = data[cur : cur + 140]
        if len(chunk) < 140:
            break
        (
            nlng,
            nhfun,
            nlen,
            nvar0,
            nname,
            nlabel,
            nform,
            nfl,
            nfd,
            nfj,
            nfill,
            niform,
            nifl,
            nifd,
            npos,
            _,
        ) = struct.unpack(">hhhh8s40s8shhh2s8shhi52s", chunk)
        variables.append(
            {
                "name": nname.decode("ascii", errors="replace").strip(),
                "label": nlabel.decode("ascii", errors="replace").strip(),
                "type": nlng,  # 1=numeric, 2=character
                "length": nlen,
                "var_num": nvar0,
                "pos": npos,
            }
        )
        cur += 140

    # Advance to OBS header
    obs_idx = data.find(b"HEADER RECORD*******OBS     HEADER RECORD!!!!!!!", cur)
    if obs_idx == -1:
        raise ValueError("Invalid XPT v5 file: OBS header record not found.")

    obs_start = obs_idx + 80
    obs_data = data[obs_start:]

    row_len = sum(v["length"] for v in variables)
    records: list[dict[str, Any]] = []

    if row_len > 0:
        num_rows = len(obs_data) // row_len
        for r_idx in range(num_rows):
            r_chunk = obs_data[r_idx * row_len : (r_idx + 1) * row_len]
            # Check if padding reached
            if all(b == 32 for b in r_chunk):
                break
            rec = {}
            for v in variables:
                v_pos = v["pos"]
                v_len = v["length"]
                v_bytes = r_chunk[v_pos : v_pos + v_len]
                if v["type"] == 1:
                    rec[v["name"]] = ibm_to_double(v_bytes)
                else:
                    rec[v["name"]] = v_bytes.decode("utf-8", errors="replace").rstrip()
            records.append(rec)

    meta = {
        "version": "v5",
        "variables": variables,
        "record_count": len(records),
    }
    return meta, records


def _read_xpt_v8(data: bytes) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    """Internal reader for XPT v8."""
    namestr_idx = data.find(b"HEADER RECORD*******NAMSTRV8HEADER RECORD!!!!!!!")
    if namestr_idx == -1:
        raise ValueError("Invalid XPT v8 file: NAMSTRV8 header record not found.")

    header_card = data[namestr_idx : namestr_idx + 80].decode("ascii", errors="replace")
    count_str = header_card[68:72].strip()
    num_vars = int(count_str) if count_str.isdigit() else 0

    cur = namestr_idx + 80
    variables = []
    for _ in range(num_vars):
        chunk = data[cur : cur + 512]
        if len(chunk) < 512:
            break
        nlng, nhfun, nlen, nvar0, nname, nlabel, nform, npos, _ = struct.unpack(
            ">hhhh32s256s32si180s", chunk
        )
        variables.append(
            {
                "name": nname.decode("ascii", errors="replace").strip(),
                "label": nlabel.decode("ascii", errors="replace").strip(),
                "type": nlng,
                "length": nlen,
                "var_num": nvar0,
                "pos": npos,
            }
        )
        cur += 512

    obs_idx = data.find(b"HEADER RECORD*******OBSV8   HEADER RECORD!!!!!!!", cur)
    if obs_idx == -1:
        raise ValueError("Invalid XPT v8 file: OBSV8 header record not found.")

    obs_start = obs_idx + 80
    obs_data = data[obs_start:]

    row_len = sum(v["length"] for v in variables)
    records: list[dict[str, Any]] = []

    if row_len > 0:
        num_rows = len(obs_data) // row_len
        for r_idx in range(num_rows):
            r_chunk = obs_data[r_idx * row_len : (r_idx + 1) * row_len]
            if all(b == 32 for b in r_chunk):
                break
            rec = {}
            for v in variables:
                v_pos = v["pos"]
                v_len = v["length"]
                v_bytes = r_chunk[v_pos : v_pos + v_len]
                if v["type"] == 1:
                    rec[v["name"]] = ibm_to_double(v_bytes)
                else:
                    rec[v["name"]] = v_bytes.decode("utf-8", errors="replace").rstrip()
            records.append(rec)

    meta = {
        "version": "v8",
        "variables": variables,
        "record_count": len(records),
    }
    return meta, records
