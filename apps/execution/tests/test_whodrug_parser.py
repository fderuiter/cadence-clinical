"""Tests for the WHODrug B3 and ATC parser."""

import os
import tempfile

import pytest

from apps.execution.coding.parsers import (
    WHODrugParseError,
    WHODrugParser,
    parse_whodrug_file,
)


def test_whodrug_parser_init_validation() -> None:
    """Verify that WHODrugParser initialization fails if dictionary_version is empty."""
    with pytest.raises(
        ValueError, match="dictionary_version must be a non-empty string."
    ):
        WHODrugParser(dictionary_version="")


def test_detect_file_type() -> None:
    """Verify that file type detection correctly identifies WHODrug B3 release files."""
    parser = WHODrugParser(dictionary_version="2024-03")

    assert parser.detect_file_type("DD.txt") == "drugs"
    assert parser.detect_file_type("DRUG.ASC") == "drugs"
    assert parser.detect_file_type("ING.TXT") == "ingredients"
    assert parser.detect_file_type("ACTIVE_SUBSTANCE.ASC") == "ingredients"
    assert parser.detect_file_type("ATC.TXT") == "atc"
    assert parser.detect_file_type("DADA.txt") == "drug_atc"
    assert parser.detect_file_type("DRUG_ATC.ASC") == "drug_atc"
    assert parser.detect_file_type("DI.txt") == "drug_ingredients"
    assert parser.detect_file_type("DRUG_ING.ASC") == "drug_ingredients"

    with pytest.raises(ValueError, match="Cannot auto-detect WHODrug file type"):
        parser.detect_file_type("unknown_dictionary.txt")


def test_parse_valid_fixed_width_drugs() -> None:
    """Verify parsing a valid fixed-width drug (DD.txt) file stream."""
    lines = [
        "00010101001ASPIRIN                        ASPIRIN TABLET\n",
        "00010101002IBUPROFEN                       IBUPROFEN CAP\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    records = list(parser.parse(lines, file_type="drugs", file_name="DD.txt"))

    assert len(records) == 2

    assert records[0]["type"] == "drug_record"
    assert records[0]["data"] == {
        "drug_code": "00010101001",
        "preferred_name": "ASPIRIN",
        "drug_name": "ASPIRIN TABLET",
        "dictionary_version": "2024-03",
    }

    assert records[1]["type"] == "drug_record"
    assert records[1]["data"] == {
        "drug_code": "00010101002",
        "preferred_name": "IBUPROFEN",
        "drug_name": "IBUPROFEN CAP",
        "dictionary_version": "2024-03",
    }


def test_parse_valid_fixed_width_ingredients() -> None:
    """Verify parsing a valid fixed-width active substances/ingredients (ING.txt) file stream."""
    lines = [
        "0000000001ACETYLSALICYLIC ACID\n",
        "0000000002IBUPROFEN\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    records = list(parser.parse(lines, file_type="ingredients", file_name="ING.txt"))

    assert len(records) == 2
    assert records[0]["type"] == "ingredient"
    assert records[0]["data"] == {
        "ingredient_code": "0000000001",
        "ingredient_name": "ACETYLSALICYLIC ACID",
        "dictionary_version": "2024-03",
    }


def test_parse_valid_fixed_width_atc() -> None:
    """Verify parsing a valid fixed-width ATC (ATC.txt) classification file."""
    lines = [
        "N02BA01SALICYLIC ACID AND DERIVATIVES\n",
        "M01AE01IBUPROFEN\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    records = list(parser.parse(lines, file_type="atc", file_name="ATC.txt"))

    assert len(records) == 2
    assert records[0]["type"] == "atc"
    assert records[0]["data"] == {
        "atc_code": "N02BA01",
        "description": "SALICYLIC ACID AND DERIVATIVES",
        "dictionary_version": "2024-03",
    }


def test_parse_valid_fixed_width_drug_atc() -> None:
    """Verify parsing a valid fixed-width drug-ATC relationship (DADA.txt) file."""
    lines = [
        "00010101001N02BA01\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    records = list(parser.parse(lines, file_type="drug_atc", file_name="DADA.txt"))

    assert len(records) == 1
    assert records[0]["type"] == "drug_atc"
    assert records[0]["data"] == {
        "drug_code": "00010101001",
        "atc_code": "N02BA01",
        "dictionary_version": "2024-03",
    }


def test_parse_valid_fixed_width_drug_ingredients() -> None:
    """Verify parsing a valid fixed-width drug-ingredient relationship (DI.txt) file."""
    lines = [
        "000101010010000000001\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    records = list(
        parser.parse(lines, file_type="drug_ingredients", file_name="DI.txt")
    )

    assert len(records) == 1
    assert records[0]["type"] == "drug_ingredient"
    assert records[0]["data"] == {
        "drug_code": "00010101001",
        "ingredient_code": "0000000001",
        "dictionary_version": "2024-03",
    }


def test_delimited_format_parsing() -> None:
    """Verify that delimited parsing works flawlessly with custom delimiters and headers."""
    custom_configs = {
        "drugs": {
            "file_format": "delimited",
            "delimiter": ",",
            "has_header": True,
            "field_mappings": {
                "drug_code": "code",
                "preferred_name": "pref",
                "drug_name": "full_name",
            },
        }
    }
    lines = [
        "code,pref,full_name\n",
        "00010101001,ASPIRIN,ASPIRIN TABLET\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03", custom_configs=custom_configs)
    records = list(parser.parse(lines, file_type="drugs", file_name="drugs_csv.txt"))

    assert len(records) == 1
    assert records[0]["type"] == "drug_record"
    assert records[0]["data"] == {
        "drug_code": "00010101001",
        "preferred_name": "ASPIRIN",
        "drug_name": "ASPIRIN TABLET",
        "dictionary_version": "2024-03",
    }


def test_delimited_format_int_indices_without_header() -> None:
    """Verify delimited parsing using integer index field mappings when there is no header."""
    custom_configs = {
        "drugs": {
            "file_format": "delimited",
            "delimiter": "|",
            "has_header": False,
            "field_mappings": {
                "drug_code": 0,
                "preferred_name": 1,
                "drug_name": 2,
            },
        }
    }
    lines = [
        "00010101001|ASPIRIN|ASPIRIN TABLET\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03", custom_configs=custom_configs)
    records = list(parser.parse(lines, file_type="drugs", file_name="drugs_psv.txt"))

    assert len(records) == 1
    assert records[0]["data"]["drug_code"] == "00010101001"
    assert records[0]["data"]["preferred_name"] == "ASPIRIN"
    assert records[0]["data"]["drug_name"] == "ASPIRIN TABLET"


def test_strict_referential_validation_triggers() -> None:
    """Verify that strict referential validation raises a line-aware parse error when files link undefined records."""
    parser = WHODrugParser(
        dictionary_version="2024-03", strict_referential_validation=True
    )

    # 1. Test drug-ATC strict validation failure
    # Drug '00010101001' and ATC 'N02BA01' are not parsed/seen yet
    with pytest.raises(WHODrugParseError) as exc_info:
        list(
            parser.parse(
                ["00010101001N02BA01\n"], file_type="drug_atc", file_name="DADA.txt"
            )
        )
    assert exc_info.value.line_num == 1
    assert "Referential integrity violation" in exc_info.value.message
    # Raw content (like the codes themselves) must not be in str(exception) to avoid leak or simple print contamination
    assert "DADA.txt" in str(exc_info.value)

    # Now let's populate drugs and ATC codes
    list(
        parser.parse(
            ["00010101001ASPIRIN                        ASPIRIN TABLET\n"],
            file_type="drugs",
            file_name="DD.txt",
        )
    )
    list(
        parser.parse(
            ["N02BA01SALICYLIC ACID AND DERIVATIVES\n"],
            file_type="atc",
            file_name="ATC.txt",
        )
    )

    # This should now succeed because codes are known
    records = list(
        parser.parse(
            ["00010101001N02BA01\n"], file_type="drug_atc", file_name="DADA.txt"
        )
    )
    assert len(records) == 1
    assert records[0]["data"]["drug_code"] == "00010101001"

    # 2. Test drug-ingredients strict validation failure
    # Ingredient '0000000001' is not seen yet
    with pytest.raises(WHODrugParseError) as exc_info2:
        list(
            parser.parse(
                ["000101010010000000001\n"],
                file_type="drug_ingredients",
                file_name="DI.txt",
            )
        )
    assert exc_info2.value.line_num == 1
    assert "ingredient_code" in exc_info2.value.message

    # Populate ingredient code
    list(
        parser.parse(
            ["0000000001ACETYLSALICYLIC ACID\n"],
            file_type="ingredients",
            file_name="ING.txt",
        )
    )

    # Now it succeeds
    records2 = list(
        parser.parse(
            ["000101010010000000001\n"],
            file_type="drug_ingredients",
            file_name="DI.txt",
        )
    )
    assert len(records2) == 1
    assert records2[0]["data"]["ingredient_code"] == "0000000001"


def test_non_strict_referential_validation() -> None:
    """Verify that relationship lines do NOT fail when strict_referential_validation=False."""
    parser = WHODrugParser(
        dictionary_version="2024-03", strict_referential_validation=False
    )
    # Undefined codes are allowed
    records = list(
        parser.parse(
            ["00010101001N02BA01\n"], file_type="drug_atc", file_name="DADA.txt"
        )
    )
    assert len(records) == 1


def test_invalid_and_missing_required_fields() -> None:
    """Verify that missing/empty required fields raise WHODrugParseError with line context."""
    parser = WHODrugParser(dictionary_version="2024-03")

    # Empty drug_code
    with pytest.raises(WHODrugParseError) as exc_info:
        list(
            parser.parse(
                ["           ASPIRIN                        ASPIRIN TABLET\n"],
                file_type="drugs",
                file_name="DD.txt",
            )
        )
    assert "drug_code must not be empty" in exc_info.value.message
    assert exc_info.value.line_num == 1

    # Empty preferred_name
    with pytest.raises(WHODrugParseError) as exc_info2:
        list(
            parser.parse(
                ["00010101001                                               \n"],
                file_type="drugs",
                file_name="DD.txt",
            )
        )
    assert "preferred_name must not be empty" in exc_info2.value.message


def test_max_length_constraints() -> None:
    """Verify that field lengths exceeding database definitions (50 or 255 chars) raise WHODrugParseError."""
    custom_configs = {
        "drugs": {
            "file_format": "fixed",
            "field_mappings": {
                "preferred_name": (11, 400),
            },
        }
    }
    parser = WHODrugParser(dictionary_version="2024-03", custom_configs=custom_configs)

    # preferred_name exceeding 255 chars
    huge_preferred_name = "A" * 260
    line = f"00010101001{huge_preferred_name}\n"

    with pytest.raises(WHODrugParseError) as exc_info:
        list(parser.parse([line], file_type="drugs", file_name="DD.txt"))
    assert "exceeds maximum length of 255 characters" in exc_info.value.message


def test_parse_in_batches_whodrug() -> None:
    """Verify that large dictionary files can be consumed streamingly in bounded batches."""
    lines = [
        "0000000001INGREDIENT A\n",
        "0000000002INGREDIENT B\n",
        "0000000003INGREDIENT C\n",
        "0000000004INGREDIENT D\n",
        "0000000005INGREDIENT E\n",
    ]
    parser = WHODrugParser(dictionary_version="2024-03")
    batches = list(
        parser.parse_in_batches(
            lines, file_type="ingredients", file_name="ING.txt", batch_size=2
        )
    )

    assert len(batches) == 3
    assert len(batches[0]) == 2
    assert len(batches[1]) == 2
    assert len(batches[2]) == 1


def test_public_entry_point_whodrug() -> None:
    """Verify the stable public entry point parses successfully using file path and auto-detection."""
    content = "N02BA01SALICYLIC ACID AND DERIVATIVES\n"
    with tempfile.NamedTemporaryFile(
        mode="w+", delete=False, prefix="atc_test_", suffix="_atc.txt", encoding="utf-8"
    ) as temp_file:
        temp_file.write(content)
        temp_path = temp_file.name

    try:
        # Autodetect file type from path suffix
        records = list(parse_whodrug_file(temp_path, dictionary_version="2024-03"))
        assert len(records) == 1
        assert records[0]["type"] == "atc"
        assert records[0]["data"]["atc_code"] == "N02BA01"
        assert records[0]["data"]["description"] == "SALICYLIC ACID AND DERIVATIVES"
        assert records[0]["data"]["dictionary_version"] == "2024-03"
    finally:
        os.remove(temp_path)


def test_public_entry_point_reusing_parser() -> None:
    """Verify that multiple public parse calls can reuse a single parser instance to maintain validation state."""
    parser = WHODrugParser(
        dictionary_version="2024-03", strict_referential_validation=True
    )

    # Parse drug record first
    drugs_records = list(
        parse_whodrug_file(
            ["00010101001ASPIRIN                        ASPIRIN TABLET\n"],
            dictionary_version="2024-03",
            file_type="drugs",
            file_name="DD.txt",
            parser=parser,
        )
    )
    assert len(drugs_records) == 1

    # Parse ATC record
    atc_records = list(
        parse_whodrug_file(
            ["N02BA01SALICYLIC ACID AND DERIVATIVES\n"],
            dictionary_version="2024-03",
            file_type="atc",
            file_name="ATC.txt",
            parser=parser,
        )
    )
    assert len(atc_records) == 1

    # Now parse link record under strict validation. It should succeed because of the shared parser!
    link_records = list(
        parse_whodrug_file(
            ["00010101001N02BA01\n"],
            dictionary_version="2024-03",
            file_type="drug_atc",
            file_name="DADA.txt",
            parser=parser,
        )
    )
    assert len(link_records) == 1
