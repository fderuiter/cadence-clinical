"""Tests for the MedDRA ASCII distribution file parser."""

import tempfile

import pytest

from apps.execution.coding.parsers import (
    MedDRAParseError,
    MedDRAParser,
    parse_meddra_file,
)


def test_meddra_parser_init_validation() -> None:
    """Verify that MedDRAParser initialization fails if dictionary_version is empty."""
    with pytest.raises(
        ValueError, match="dictionary_version must be a non-empty string."
    ):
        MedDRAParser(dictionary_version="")


def test_detect_file_type() -> None:
    """Verify that file type detection correctly identifies each of the six file types."""
    parser = MedDRAParser(dictionary_version="26.0")

    assert parser.detect_file_type("llt.asc") == "llt"
    assert parser.detect_file_type("/path/to/LLT.ASC") == "llt"
    assert parser.detect_file_type("pt.asc") == "pt"
    assert parser.detect_file_type("hlt.asc") == "hlt"
    assert parser.detect_file_type("hlgt.asc") == "hlgt"
    assert parser.detect_file_type("soc.asc") == "soc"
    assert parser.detect_file_type("mdhier.asc") == "mdhier"

    with pytest.raises(ValueError, match="Cannot auto-detect MedDRA file type"):
        parser.detect_file_type("unknown_file.txt")


def test_parse_llt_valid() -> None:
    """Verify parsing a valid llt.asc stream, including trailing $ handling."""
    lines = [
        "10019211$Headache$10019211$$$$$Y$\n",
        "10019212$Migraine$10019211$$$$$Y$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="llt", file_name="llt.asc"))

    # Each line should yield 2 records: 1 term and 1 hierarchy link
    assert len(records) == 4

    # Check first term
    assert records[0]["type"] == "term"
    assert records[0]["data"] == {
        "code": "10019211",
        "term_name": "Headache",
        "level": "LLT",
        "dictionary_version": "26.0",
    }

    # Check first hierarchy
    assert records[1]["type"] == "hierarchy"
    assert records[1]["data"] == {
        "llt_code": "10019211",
        "pt_code": "10019211",
        "hlt_code": "NONE",
        "hlgt_code": "NONE",
        "soc_code": "NONE",
        "primary_soc_flag": None,
        "dictionary_version": "26.0",
    }


def test_parse_llt_invalid_code() -> None:
    """Verify that a non-8-digit llt_code raises a descriptive MedDRAParseError with line context."""
    lines = [
        "10019211$Headache$10019211$$$$$Y$\n",
        "123$Migraine$10019211$$$$$Y$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines, file_type="llt", file_name="llt.asc"))

    assert exc_info.value.line_num == 2
    assert exc_info.value.file_name == "llt.asc"
    assert "llt_code must be an 8-digit numeric string" in exc_info.value.message
    # Assert that the sensitive content of the line itself is not in the stringified exception
    assert "Migraine" not in str(exc_info.value)


def test_parse_llt_invalid_pt_code() -> None:
    """Verify that a non-8-digit pt_code in llt.asc raises a descriptive MedDRAParseError."""
    lines = [
        "10019211$Headache$ABC$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines, file_type="llt", file_name="llt.asc"))

    assert exc_info.value.line_num == 1
    assert "pt_code must be an 8-digit numeric string" in exc_info.value.message


def test_parse_pt_valid() -> None:
    """Verify parsing a valid pt.asc stream."""
    lines = [
        "10019211$Headache$10019211$$$$$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="pt", file_name="pt.asc"))

    assert len(records) == 1
    assert records[0]["type"] == "term"
    assert records[0]["data"] == {
        "code": "10019211",
        "term_name": "Headache",
        "level": "PT",
        "dictionary_version": "26.0",
    }


def test_parse_hlt_valid() -> None:
    """Verify parsing a valid hlt.asc stream."""
    lines = [
        "10019231$Headaches$10019231$$$$$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="hlt", file_name="hlt.asc"))

    assert len(records) == 1
    assert records[0]["type"] == "term"
    assert records[0]["data"] == {
        "code": "10019231",
        "term_name": "Headaches",
        "level": "HLT",
        "dictionary_version": "26.0",
    }


def test_parse_hlgt_valid() -> None:
    """Verify parsing a valid hlgt.asc stream."""
    lines = [
        "10029214$Neurological disorders NEC$10029214$$$$$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="hlgt", file_name="hlgt.asc"))

    assert len(records) == 1
    assert records[0]["type"] == "term"
    assert records[0]["data"] == {
        "code": "10029214",
        "term_name": "Neurological disorders NEC",
        "level": "HLGT",
        "dictionary_version": "26.0",
    }


def test_parse_soc_valid() -> None:
    """Verify parsing a valid soc.asc stream."""
    lines = [
        "10029205$Nervous system disorders$ND$$$$$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="soc", file_name="soc.asc"))

    assert len(records) == 1
    assert records[0]["type"] == "term"
    assert records[0]["data"] == {
        "code": "10029205",
        "term_name": "Nervous system disorders",
        "level": "SOC",
        "dictionary_version": "26.0",
    }


def test_parse_mdhier_valid() -> None:
    """Verify parsing a valid mdhier.asc stream."""
    lines = [
        "10019211$10019231$10029214$10029205$Nervous system disorders$Neurological disorders NEC$Headaches$Headache$Y$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")
    records = list(parser.parse(lines, file_type="mdhier", file_name="mdhier.asc"))

    assert len(records) == 1
    assert records[0]["type"] == "hierarchy"
    assert records[0]["data"] == {
        "llt_code": "NONE",
        "pt_code": "10019211",
        "hlt_code": "10019231",
        "hlgt_code": "10029214",
        "soc_code": "10029205",
        "primary_soc_flag": "Y",
        "dictionary_version": "26.0",
    }


def test_parse_mdhier_invalid_flag() -> None:
    """Verify that an invalid primary_soc_flag (not Y or N) raises a MedDRAParseError."""
    lines = [
        "10019211$10019231$10029214$10029205$Nervous system disorders$Neurological disorders NEC$Headaches$Headache$X$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines, file_type="mdhier", file_name="mdhier.asc"))

    assert exc_info.value.line_num == 1
    assert "primary_soc_flag must be either 'Y' or 'N'" in exc_info.value.message


def test_parse_mdhier_missing_fields() -> None:
    """Verify that a mdhier line with missing fields raises a MedDRAParseError."""
    lines = [
        "10019211$10019231$10029214$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines, file_type="mdhier", file_name="mdhier.asc"))

    assert exc_info.value.line_num == 1
    assert "Invalid MDHIER line: expected at least 9 fields" in exc_info.value.message


def test_parse_empty_fields_validation() -> None:
    """Verify that empty required fields (like code or term name) raise a MedDRAParseError."""
    # Empty code field
    lines_empty_code = [
        "$Headache$10019211$$$$$\n",
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines_empty_code, file_type="llt", file_name="llt.asc"))
    assert "llt_code must not be empty" in exc_info.value.message

    # Empty term name
    lines_empty_name = [
        "10019211$$10019211$$$$$\n",
    ]
    with pytest.raises(MedDRAParseError) as exc_info:
        list(parser.parse(lines_empty_name, file_type="llt", file_name="llt.asc"))
    assert "llt_name must not be empty" in exc_info.value.message


def test_parser_incremental_batched_consumption() -> None:
    """Verify that the parser supports incremental, batched consumption."""
    lines = [
        "10019211$Headache$10019211$$$$$Y$\n",  # yields 2 records (1 term, 1 hierarchy)
        "10019212$Migraine$10019211$$$$$Y$\n",  # yields 2 records (1 term, 1 hierarchy)
        "10019213$Cephalea$10019211$$$$$Y$\n",  # yields 2 records (1 term, 1 hierarchy)
    ]
    parser = MedDRAParser(dictionary_version="26.0")

    # Group into batches of size 3
    batches = list(
        parser.parse_in_batches(
            lines, file_type="llt", file_name="llt.asc", batch_size=3
        )
    )

    assert len(batches) == 2
    assert len(batches[0]) == 3
    assert len(batches[1]) == 3


def test_public_entry_point_file_path() -> None:
    """Verify the stable public entry point parses successfully using a file path."""
    content = "10019211$Headache$10019211$$$$$\n"
    with tempfile.NamedTemporaryFile(
        mode="w+", delete=False, suffix="pt.asc", encoding="utf-8"
    ) as temp_file:
        temp_file.write(content)
        temp_path = temp_file.name

    try:
        # Autodetect file type from path suffix
        records = list(parse_meddra_file(temp_path, dictionary_version="26.0"))
        assert len(records) == 1
        assert records[0]["type"] == "term"
        assert records[0]["data"]["code"] == "10019211"
        assert records[0]["data"]["term_name"] == "Headache"
        assert records[0]["data"]["level"] == "PT"
        assert records[0]["data"]["dictionary_version"] == "26.0"
    finally:
        import os

        os.remove(temp_path)


def test_parse_in_batches_invalid_batch_size() -> None:
    """Verify that an invalid batch_size parameter raises ValueError."""
    parser = MedDRAParser(dictionary_version="26.0")
    with pytest.raises(ValueError, match="batch_size must be a positive integer."):
        list(parser.parse_in_batches([""], file_type="llt", batch_size=0))
