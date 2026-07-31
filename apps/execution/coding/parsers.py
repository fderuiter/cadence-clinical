"""MedDRA ASCII distribution file parser.

This module implements a production-oriented, streaming parser for MedDRA ASCII
distribution files that yields versioned five-level terminology and hierarchy
records in accordance with FDA 21 CFR Part 11 and GxP standards.
"""

import os
from typing import Any, Dict, Iterable, Iterator, List


class MedDRAParseError(ValueError):
    """Exception raised for errors in parsing MedDRA files.

    Attributes:
        message (str): Explanation of the error.
        file_name (str): Name of the source file.
        line_num (int): 1-based index of the line.
    """

    def __init__(self, message: str, file_name: str, line_num: int):
        self.message = message
        self.file_name = file_name
        self.line_num = line_num
        super().__init__(f"Parse error in '{file_name}' at line {line_num}: {message}")


class MedDRAParser:
    """Streaming parser for MedDRA ASCII distribution files.

    Yields versioned five-level terminology (LLT, PT, HLT, HLGT, SOC)
    and hierarchy records.
    """

    def __init__(self, dictionary_version: str, encoding: str = "utf-8"):
        """Initializes the MedDRAParser.

        Args:
            dictionary_version: The version tag of the MedDRA dictionary (e.g. "26.0").
            encoding: The file encoding to use when reading files (default "utf-8").

        Raises:
            ValueError: If dictionary_version is empty or None.
        """
        if not dictionary_version:
            raise ValueError("dictionary_version must be a non-empty string.")
        self.dictionary_version = dictionary_version
        self.encoding = encoding

    def detect_file_type(self, file_name: str) -> str:
        """Detects the MedDRA file type from a file name.

        Args:
            file_name: The file name (e.g. "llt.asc" or "MDHIER.ASC").

        Returns:
            str: One of "llt", "pt", "hlt", "hlgt", "soc", "mdhier".

        Raises:
            ValueError: If the file type cannot be auto-detected.
        """
        name = os.path.basename(file_name).lower()
        if "llt" in name:
            return "llt"
        elif "pt" in name:
            return "pt"
        elif "hlt" in name and "hlgt" not in name:
            return "hlt"
        elif "hlgt" in name:
            return "hlgt"
        elif "soc" in name:
            return "soc"
        elif "mdhier" in name:
            return "mdhier"
        else:
            raise ValueError(
                f"Cannot auto-detect MedDRA file type from file name: {file_name}"
            )

    def parse(
        self,
        file_input: Any,
        file_type: str = None,
        file_name: str = None,
    ) -> Iterator[Dict[str, Any]]:
        """Parses a MedDRA ASCII file streamingly and yields parsed records.

        Args:
            file_input: A file path, a file-like object, or an iterable of lines.
            file_type: One of "llt", "pt", "hlt", "hlgt", "soc", "mdhier". If None,
                it will try to auto-detect from file_name or file_input path.
            file_name: Optional file name for error context.

        Yields:
            Dict[str, Any]: A record dict with "type" and "data" keys.

        Raises:
            ValueError: If file_type is missing and cannot be auto-detected.
            MedDRAParseError: If a line fails to pass validation rules.
        """
        parsed_file_name = file_name or ""
        if isinstance(file_input, (str, bytes, os.PathLike)):
            if not parsed_file_name:
                parsed_file_name = os.path.basename(file_input)
            if not file_type:
                file_type = self.detect_file_type(parsed_file_name)

            with open(file_input, mode="r", encoding=self.encoding) as f:
                yield from self._parse_stream(f, file_type, parsed_file_name)
        else:
            if not parsed_file_name:
                parsed_file_name = "stream"
            if not file_type:
                file_type = self.detect_file_type(parsed_file_name)

            yield from self._parse_stream(file_input, file_type, parsed_file_name)

    def parse_in_batches(
        self,
        file_input: Any,
        file_type: str = None,
        file_name: str = None,
        batch_size: int = 1000,
    ) -> Iterator[List[Dict[str, Any]]]:
        """Parses the input file streamingly and yields records in batches.

        Args:
            file_input: A file path, a file-like object, or an iterable of lines.
            file_type: One of "llt", "pt", "hlt", "hlgt", "soc", "mdhier".
            file_name: Optional file name for error context.
            batch_size: The number of records per batch.

        Yields:
            List[Dict[str, Any]]: A list of parsed record dicts.

        Raises:
            ValueError: If batch_size is not a positive integer.
        """
        if batch_size <= 0:
            raise ValueError("batch_size must be a positive integer.")

        batch = []
        for record in self.parse(file_input, file_type, file_name):
            batch.append(record)
            if len(batch) == batch_size:
                yield batch
                batch = []
        if batch:
            yield batch

    def _parse_stream(
        self, stream: Iterable[str], file_type: str, file_name: str
    ) -> Iterator[Dict[str, Any]]:
        """Internal helper to parse lines from a stream."""
        file_type = file_type.lower()
        allowed_types = {"llt", "pt", "hlt", "hlgt", "soc", "mdhier"}
        if file_type not in allowed_types:
            raise ValueError(
                f"Invalid file_type: {file_type}. Must be one of {allowed_types}"
            )

        for idx, line in enumerate(stream, start=1):
            line_str = line.rstrip("\r\n")
            if not line_str:
                continue

            # Handle the standard trailing delimiter without creating a spurious empty field
            if line_str.endswith("$"):
                line_str = line_str[:-1]

            fields = [f.strip() for f in line_str.split("$")]

            if file_type == "llt":
                if len(fields) < 3:
                    raise MedDRAParseError(
                        f"Invalid LLT line: expected at least 3 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                llt_code, llt_name, pt_code = fields[0], fields[1], fields[2]

                if not llt_code:
                    raise MedDRAParseError(
                        "llt_code must not be empty.", file_name, idx
                    )
                if not llt_code.isdigit() or len(llt_code) != 8:
                    raise MedDRAParseError(
                        "llt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not llt_name:
                    raise MedDRAParseError(
                        "llt_name must not be empty.", file_name, idx
                    )
                if len(llt_name) > 255:
                    raise MedDRAParseError(
                        "llt_name exceeds maximum length of 255 characters.",
                        file_name,
                        idx,
                    )
                if not pt_code:
                    raise MedDRAParseError("pt_code must not be empty.", file_name, idx)
                if not pt_code.isdigit() or len(pt_code) != 8:
                    raise MedDRAParseError(
                        "pt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "term",
                    "data": {
                        "code": llt_code,
                        "term_name": llt_name,
                        "level": "LLT",
                        "dictionary_version": self.dictionary_version,
                    },
                }
                yield {
                    "type": "hierarchy",
                    "data": {
                        "llt_code": llt_code,
                        "pt_code": pt_code,
                        "hlt_code": "NONE",
                        "hlgt_code": "NONE",
                        "soc_code": "NONE",
                        "primary_soc_flag": None,
                        "dictionary_version": self.dictionary_version,
                    },
                }

            elif file_type == "pt":
                if len(fields) < 2:
                    raise MedDRAParseError(
                        f"Invalid PT line: expected at least 2 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                pt_code, pt_name = fields[0], fields[1]

                if not pt_code:
                    raise MedDRAParseError("pt_code must not be empty.", file_name, idx)
                if not pt_code.isdigit() or len(pt_code) != 8:
                    raise MedDRAParseError(
                        "pt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not pt_name:
                    raise MedDRAParseError("pt_name must not be empty.", file_name, idx)
                if len(pt_name) > 255:
                    raise MedDRAParseError(
                        "pt_name exceeds maximum length of 255 characters.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "term",
                    "data": {
                        "code": pt_code,
                        "term_name": pt_name,
                        "level": "PT",
                        "dictionary_version": self.dictionary_version,
                    },
                }

            elif file_type == "hlt":
                if len(fields) < 2:
                    raise MedDRAParseError(
                        f"Invalid HLT line: expected at least 2 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                hlt_code, hlt_name = fields[0], fields[1]

                if not hlt_code:
                    raise MedDRAParseError(
                        "hlt_code must not be empty.", file_name, idx
                    )
                if not hlt_code.isdigit() or len(hlt_code) != 8:
                    raise MedDRAParseError(
                        "hlt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not hlt_name:
                    raise MedDRAParseError(
                        "hlt_name must not be empty.", file_name, idx
                    )
                if len(hlt_name) > 255:
                    raise MedDRAParseError(
                        "hlt_name exceeds maximum length of 255 characters.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "term",
                    "data": {
                        "code": hlt_code,
                        "term_name": hlt_name,
                        "level": "HLT",
                        "dictionary_version": self.dictionary_version,
                    },
                }

            elif file_type == "hlgt":
                if len(fields) < 2:
                    raise MedDRAParseError(
                        f"Invalid HLGT line: expected at least 2 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                hlgt_code, hlgt_name = fields[0], fields[1]

                if not hlgt_code:
                    raise MedDRAParseError(
                        "hlgt_code must not be empty.", file_name, idx
                    )
                if not hlgt_code.isdigit() or len(hlgt_code) != 8:
                    raise MedDRAParseError(
                        "hlgt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not hlgt_name:
                    raise MedDRAParseError(
                        "hlgt_name must not be empty.", file_name, idx
                    )
                if len(hlgt_name) > 255:
                    raise MedDRAParseError(
                        "hlgt_name exceeds maximum length of 255 characters.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "term",
                    "data": {
                        "code": hlgt_code,
                        "term_name": hlgt_name,
                        "level": "HLGT",
                        "dictionary_version": self.dictionary_version,
                    },
                }

            elif file_type == "soc":
                if len(fields) < 2:
                    raise MedDRAParseError(
                        f"Invalid SOC line: expected at least 2 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                soc_code, soc_name = fields[0], fields[1]

                if not soc_code:
                    raise MedDRAParseError(
                        "soc_code must not be empty.", file_name, idx
                    )
                if not soc_code.isdigit() or len(soc_code) != 8:
                    raise MedDRAParseError(
                        "soc_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not soc_name:
                    raise MedDRAParseError(
                        "soc_name must not be empty.", file_name, idx
                    )
                if len(soc_name) > 255:
                    raise MedDRAParseError(
                        "soc_name exceeds maximum length of 255 characters.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "term",
                    "data": {
                        "code": soc_code,
                        "term_name": soc_name,
                        "level": "SOC",
                        "dictionary_version": self.dictionary_version,
                    },
                }

            elif file_type == "mdhier":
                if len(fields) < 9:
                    raise MedDRAParseError(
                        f"Invalid MDHIER line: expected at least 9 fields, got {len(fields)}.",
                        file_name,
                        idx,
                    )
                pt_code, hlt_code, hlgt_code, soc_code = (
                    fields[0],
                    fields[1],
                    fields[2],
                    fields[3],
                )
                primary_soc_flag = fields[8].upper() if fields[8] else ""

                if not pt_code or not pt_code.isdigit() or len(pt_code) != 8:
                    raise MedDRAParseError(
                        "pt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not hlt_code or not hlt_code.isdigit() or len(hlt_code) != 8:
                    raise MedDRAParseError(
                        "hlt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not hlgt_code or not hlgt_code.isdigit() or len(hlgt_code) != 8:
                    raise MedDRAParseError(
                        "hlgt_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if not soc_code or not soc_code.isdigit() or len(soc_code) != 8:
                    raise MedDRAParseError(
                        "soc_code must be an 8-digit numeric string.",
                        file_name,
                        idx,
                    )
                if primary_soc_flag not in {"Y", "N"}:
                    raise MedDRAParseError(
                        "primary_soc_flag must be either 'Y' or 'N'.",
                        file_name,
                        idx,
                    )

                yield {
                    "type": "hierarchy",
                    "data": {
                        "llt_code": "NONE",
                        "pt_code": pt_code,
                        "hlt_code": hlt_code,
                        "hlgt_code": hlgt_code,
                        "soc_code": soc_code,
                        "primary_soc_flag": primary_soc_flag,
                        "dictionary_version": self.dictionary_version,
                    },
                }


def parse_meddra_file(
    file_input: Any,
    dictionary_version: str,
    file_type: str = None,
    file_name: str = None,
    encoding: str = "utf-8",
) -> Iterator[Dict[str, Any]]:
    """Stable public entry point to parse a MedDRA ASCII file streamingly.

    Args:
        file_input: A file path, a file-like object, or an iterable of lines.
        dictionary_version: The version tag of the MedDRA dictionary (e.g. "26.0").
        file_type: One of "llt", "pt", "hlt", "hlgt", "soc", "mdhier". If None,
            it will try to auto-detect from file_name or file_input path.
        file_name: Optional file name for error context.
        encoding: The file encoding to use when reading files (default "utf-8").

    Yields:
        Dict[str, Any]: Parsed MedDRA term or hierarchy record dictionaries.
    """
    parser = MedDRAParser(dictionary_version=dictionary_version, encoding=encoding)
    yield from parser.parse(file_input, file_type=file_type, file_name=file_name)


class WHODrugParseError(ValueError):
    """Exception raised for errors in parsing WHODrug files.

    Attributes:
        message (str): Explanation of the error.
        file_name (str): Name of the source file.
        line_num (int): 1-based index of the line.
    """

    def __init__(self, message: str, file_name: str, line_num: int):
        self.message = message
        self.file_name = file_name
        self.line_num = line_num
        super().__init__(f"Parse error in '{file_name}' at line {line_num}: {message}")


class WHODrugParser:
    """Streaming parser for licensed WHODrug B3 distribution files.

    Produces versioned drug, ingredient, and ATC classification records.
    Supports fixed-width and delimited text file formats with configurable mappings.
    """

    DEFAULT_CONFIGS = {
        "drugs": {
            "file_format": "fixed",
            "delimiter": None,
            "has_header": False,
            "field_mappings": {
                "drug_code": (0, 11),
                "preferred_name": (11, 41),
                "drug_name": (41, None),
            },
        },
        "ingredients": {
            "file_format": "fixed",
            "delimiter": None,
            "has_header": False,
            "field_mappings": {
                "ingredient_code": (0, 10),
                "ingredient_name": (10, None),
            },
        },
        "atc": {
            "file_format": "fixed",
            "delimiter": None,
            "has_header": False,
            "field_mappings": {
                "atc_code": (0, 7),
                "description": (7, None),
            },
        },
        "drug_atc": {
            "file_format": "fixed",
            "delimiter": None,
            "has_header": False,
            "field_mappings": {
                "drug_code": (0, 11),
                "atc_code": (11, 18),
            },
        },
        "drug_ingredients": {
            "file_format": "fixed",
            "delimiter": None,
            "has_header": False,
            "field_mappings": {
                "drug_code": (0, 11),
                "ingredient_code": (11, 21),
            },
        },
    }

    def __init__(
        self,
        dictionary_version: str,
        encoding: str = "utf-8",
        strict_referential_validation: bool = False,
        custom_configs: Dict[str, Any] = None,
    ):
        """Initializes the WHODrugParser.

        Args:
            dictionary_version: The version tag of the WHODrug dictionary (e.g. "2024-03").
            encoding: The file encoding to use when reading files (default "utf-8").
            strict_referential_validation: If True, validates referential consistency
                where relationships map to defined drugs, ingredients, or ATC codes.
            custom_configs: Optional dictionary to override default layouts or mappings.

        Raises:
            ValueError: If dictionary_version is empty or None.
        """
        if not dictionary_version:
            raise ValueError("dictionary_version must be a non-empty string.")
        self.dictionary_version = dictionary_version
        self.encoding = encoding
        self.strict_referential_validation = strict_referential_validation

        # Sets to track seen codes for referential consistency checks
        self.known_drug_codes = set()
        self.known_ingredient_codes = set()
        self.known_atc_codes = set()

        # Build configurations by applying overrides to defaults
        self.configs = {}
        for file_key, default_cfg in self.DEFAULT_CONFIGS.items():
            cfg = {
                "file_format": default_cfg["file_format"],
                "delimiter": default_cfg["delimiter"],
                "has_header": default_cfg["has_header"],
                "field_mappings": dict(default_cfg["field_mappings"]),
            }
            if custom_configs and file_key in custom_configs:
                custom_cfg = custom_configs[file_key]
                if "file_format" in custom_cfg:
                    cfg["file_format"] = custom_cfg["file_format"]
                if "delimiter" in custom_cfg:
                    cfg["delimiter"] = custom_cfg["delimiter"]
                if "has_header" in custom_cfg:
                    cfg["has_header"] = custom_cfg["has_header"]
                if "field_mappings" in custom_cfg:
                    cfg["field_mappings"].update(custom_cfg["field_mappings"])
            self.configs[file_key] = cfg

    def detect_file_type(self, file_name: str) -> str:
        """Detects the WHODrug file type from a file name.

        Args:
            file_name: The file name (e.g. "DD.txt" or "DI.asc").

        Returns:
            str: One of "drugs", "ingredients", "atc", "drug_atc", "drug_ingredients".

        Raises:
            ValueError: If the file type cannot be auto-detected.
        """
        import re

        name = os.path.basename(file_name).lower()
        base_name, _ = os.path.splitext(name)
        tokens = set(re.split(r"[^a-zA-Z0-9]", base_name))

        if (
            "dada" in base_name
            or "dda" in base_name
            or "drugatc" in base_name
            or "drug_at_c" in base_name
            or "drug_atc" in base_name
        ):
            return "drug_atc"
        elif "atc" in base_name:
            return "atc"
        elif (
            "drug_ing" in base_name
            or "drug_ingredient" in base_name
            or "druging" in base_name
            or "di" in tokens
            or base_name == "di"
            or base_name.startswith("di_")
            or base_name.endswith("_di")
            or base_name.endswith("di")
        ):
            return "drug_ingredients"
        elif (
            "drug" in base_name
            or "drugs" in base_name
            or "dd" in tokens
            or base_name == "dd"
            or base_name.startswith("dd_")
            or base_name.endswith("_dd")
            or base_name.endswith("dd")
        ):
            return "drugs"
        elif (
            "ing" in tokens
            or "ingredients" in base_name
            or "active" in base_name
            or "substance" in base_name
            or "active_substance" in base_name
        ):
            return "ingredients"
        else:
            raise ValueError(
                f"Cannot auto-detect WHODrug file type from file name: {file_name}"
            )

    def parse(
        self,
        file_input: Any,
        file_type: str = None,
        file_name: str = None,
    ) -> Iterator[Dict[str, Any]]:
        """Parses a WHODrug file streamingly and yields parsed records.

        Args:
            file_input: A file path, a file-like object, or an iterable of lines.
            file_type: One of "drugs", "ingredients", "atc", "drug_atc", "drug_ingredients".
                If None, it will try to auto-detect from file_name or file_input path.
            file_name: Optional file name for error context.

        Yields:
            Dict[str, Any]: A record dict with "type" and "data" keys.

        Raises:
            ValueError: If file_type is missing and cannot be auto-detected.
            WHODrugParseError: If a line fails to pass validation rules.
        """
        parsed_file_name = file_name or ""
        if isinstance(file_input, (str, bytes, os.PathLike)):
            if not parsed_file_name:
                parsed_file_name = os.path.basename(file_input)
            if not file_type:
                file_type = self.detect_file_type(parsed_file_name)

            with open(file_input, mode="r", encoding=self.encoding) as f:
                yield from self._parse_stream(f, file_type, parsed_file_name)
        else:
            if not parsed_file_name:
                parsed_file_name = "stream"
            if not file_type:
                file_type = self.detect_file_type(parsed_file_name)

            yield from self._parse_stream(file_input, file_type, parsed_file_name)

    def parse_in_batches(
        self,
        file_input: Any,
        file_type: str = None,
        file_name: str = None,
        batch_size: int = 1000,
    ) -> Iterator[List[Dict[str, Any]]]:
        """Parses the input file streamingly and yields records in batches.

        Args:
            file_input: A file path, a file-like object, or an iterable of lines.
            file_type: One of "drugs", "ingredients", "atc", "drug_atc", "drug_ingredients".
            file_name: Optional file name for error context.
            batch_size: The number of records per batch.

        Yields:
            List[Dict[str, Any]]: A list of parsed record dicts.

        Raises:
            ValueError: If batch_size is not a positive integer.
        """
        if batch_size <= 0:
            raise ValueError("batch_size must be a positive integer.")

        batch = []
        for record in self.parse(file_input, file_type, file_name):
            batch.append(record)
            if len(batch) == batch_size:
                yield batch
                batch = []
        if batch:
            yield batch

    def _extract_field_value(
        self,
        line: str,
        mapping: Any,
        file_format: str,
        delimiter: str | None,
        header_map: Dict[str, int] | None,
        line_fields: List[str] | None,
        file_name: str,
        line_num: int,
    ) -> str:
        """Helper to extract field value according to file format and mappings."""
        if file_format == "fixed":
            if not isinstance(mapping, (list, tuple)) or len(mapping) != 2:
                raise WHODrugParseError(
                    f"Fixed-width mapping must be a tuple or list of (start, end), got {mapping}",
                    file_name,
                    line_num,
                )
            start, end = mapping[0], mapping[1]
            if start >= len(line):
                return ""
            val = line[start:end] if end is not None else line[start:]
            return val.strip()
        else:
            if line_fields is None:
                return ""
            if isinstance(mapping, int):
                idx = mapping
            elif isinstance(mapping, str):
                if not header_map:
                    raise WHODrugParseError(
                        f"Delimiter header map is missing but string field mapping was used: {mapping}",
                        file_name,
                        line_num,
                    )
                if mapping not in header_map:
                    raise WHODrugParseError(
                        f"Field mapping '{mapping}' not found in file headers: {list(header_map.keys())}",
                        file_name,
                        line_num,
                    )
                idx = header_map[mapping]
            else:
                raise WHODrugParseError(
                    f"Delimited mapping must be an int or a string (header name), got {mapping}",
                    file_name,
                    line_num,
                )

            if idx >= len(line_fields):
                return ""
            return line_fields[idx].strip()

    def _parse_stream(
        self, stream: Iterable[str], file_type: str, file_name: str
    ) -> Iterator[Dict[str, Any]]:
        """Internal helper to parse lines from a stream."""
        file_type = file_type.lower()
        if file_type not in self.configs:
            raise ValueError(
                f"Invalid file_type: {file_type}. Must be one of {list(self.configs.keys())}"
            )

        cfg = self.configs[file_type]
        file_format = cfg["file_format"]
        delimiter = cfg["delimiter"]
        has_header = cfg["has_header"]
        mappings = cfg["field_mappings"]

        header_map = None
        is_first_line = True

        for idx, line in enumerate(stream, start=1):
            line_str = line.rstrip("\r\n")
            if not line_str:
                continue

            # Trailing delimiter support
            if (
                file_format == "delimited"
                and delimiter
                and line_str.endswith(delimiter)
            ):
                line_str = line_str[: -len(delimiter)]

            if file_format == "delimited":
                split_delim = delimiter if delimiter is not None else ","
                line_fields = [f.strip() for f in line_str.split(split_delim)]
            else:
                line_fields = None

            if file_format == "delimited" and has_header and is_first_line:
                header_map = {col.strip(): i for i, col in enumerate(line_fields)}
                is_first_line = False
                continue

            try:
                if file_type == "drugs":
                    drug_code_map = mappings.get("drug_code")
                    preferred_name_map = mappings.get("preferred_name")
                    drug_name_map = mappings.get("drug_name")

                    drug_code = self._extract_field_value(
                        line_str,
                        drug_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    preferred_name = self._extract_field_value(
                        line_str,
                        preferred_name_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    drug_name = None
                    if drug_name_map is not None:
                        drug_name = (
                            self._extract_field_value(
                                line_str,
                                drug_name_map,
                                file_format,
                                delimiter,
                                header_map,
                                line_fields,
                                file_name,
                                idx,
                            )
                            or None
                        )

                    if not drug_code:
                        raise WHODrugParseError(
                            "drug_code must not be empty.", file_name, idx
                        )
                    if len(drug_code) > 50:
                        raise WHODrugParseError(
                            "drug_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )
                    if not preferred_name:
                        raise WHODrugParseError(
                            "preferred_name must not be empty.", file_name, idx
                        )
                    if len(preferred_name) > 255:
                        raise WHODrugParseError(
                            "preferred_name exceeds maximum length of 255 characters.",
                            file_name,
                            idx,
                        )
                    if drug_name and len(drug_name) > 255:
                        raise WHODrugParseError(
                            "drug_name exceeds maximum length of 255 characters.",
                            file_name,
                            idx,
                        )

                    self.known_drug_codes.add(drug_code)

                    yield {
                        "type": "drug_record",
                        "data": {
                            "drug_code": drug_code,
                            "preferred_name": preferred_name,
                            "drug_name": drug_name,
                            "dictionary_version": self.dictionary_version,
                        },
                    }

                elif file_type == "ingredients":
                    ingredient_code_map = mappings.get("ingredient_code")
                    ingredient_name_map = mappings.get("ingredient_name")

                    ingredient_code = self._extract_field_value(
                        line_str,
                        ingredient_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    ingredient_name = self._extract_field_value(
                        line_str,
                        ingredient_name_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )

                    if not ingredient_code:
                        raise WHODrugParseError(
                            "ingredient_code must not be empty.", file_name, idx
                        )
                    if len(ingredient_code) > 50:
                        raise WHODrugParseError(
                            "ingredient_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )
                    if not ingredient_name:
                        raise WHODrugParseError(
                            "ingredient_name must not be empty.", file_name, idx
                        )
                    if len(ingredient_name) > 255:
                        raise WHODrugParseError(
                            "ingredient_name exceeds maximum length of 255 characters.",
                            file_name,
                            idx,
                        )

                    self.known_ingredient_codes.add(ingredient_code)

                    yield {
                        "type": "ingredient",
                        "data": {
                            "ingredient_code": ingredient_code,
                            "ingredient_name": ingredient_name,
                            "dictionary_version": self.dictionary_version,
                        },
                    }

                elif file_type == "atc":
                    atc_code_map = mappings.get("atc_code")
                    description_map = mappings.get("description")

                    atc_code = self._extract_field_value(
                        line_str,
                        atc_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    description = self._extract_field_value(
                        line_str,
                        description_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )

                    if not atc_code:
                        raise WHODrugParseError(
                            "atc_code must not be empty.", file_name, idx
                        )
                    if len(atc_code) > 50:
                        raise WHODrugParseError(
                            "atc_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )
                    if not description:
                        raise WHODrugParseError(
                            "description must not be empty.", file_name, idx
                        )
                    if len(description) > 255:
                        raise WHODrugParseError(
                            "description exceeds maximum length of 255 characters.",
                            file_name,
                            idx,
                        )

                    self.known_atc_codes.add(atc_code)

                    yield {
                        "type": "atc",
                        "data": {
                            "atc_code": atc_code,
                            "description": description,
                            "dictionary_version": self.dictionary_version,
                        },
                    }

                elif file_type == "drug_atc":
                    drug_code_map = mappings.get("drug_code")
                    atc_code_map = mappings.get("atc_code")

                    drug_code = self._extract_field_value(
                        line_str,
                        drug_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    atc_code = self._extract_field_value(
                        line_str,
                        atc_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )

                    if not drug_code:
                        raise WHODrugParseError(
                            "drug_code must not be empty.", file_name, idx
                        )
                    if len(drug_code) > 50:
                        raise WHODrugParseError(
                            "drug_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )
                    if not atc_code:
                        raise WHODrugParseError(
                            "atc_code must not be empty.", file_name, idx
                        )
                    if len(atc_code) > 50:
                        raise WHODrugParseError(
                            "atc_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )

                    if self.strict_referential_validation:
                        if drug_code not in self.known_drug_codes:
                            raise WHODrugParseError(
                                f"Referential integrity violation: drug_code '{drug_code}' linked to ATC code '{atc_code}', but drug code is not defined in any parsed drug records.",
                                file_name,
                                idx,
                            )
                        if atc_code not in self.known_atc_codes:
                            raise WHODrugParseError(
                                f"Referential integrity violation: ATC code '{atc_code}' linked to drug_code '{drug_code}', but ATC code is not defined in any parsed ATC records.",
                                file_name,
                                idx,
                            )

                    yield {
                        "type": "drug_atc",
                        "data": {
                            "drug_code": drug_code,
                            "atc_code": atc_code,
                            "dictionary_version": self.dictionary_version,
                        },
                    }

                elif file_type == "drug_ingredients":
                    drug_code_map = mappings.get("drug_code")
                    ingredient_code_map = mappings.get("ingredient_code")

                    drug_code = self._extract_field_value(
                        line_str,
                        drug_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )
                    ingredient_code = self._extract_field_value(
                        line_str,
                        ingredient_code_map,
                        file_format,
                        delimiter,
                        header_map,
                        line_fields,
                        file_name,
                        idx,
                    )

                    if not drug_code:
                        raise WHODrugParseError(
                            "drug_code must not be empty.", file_name, idx
                        )
                    if len(drug_code) > 50:
                        raise WHODrugParseError(
                            "drug_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )
                    if not ingredient_code:
                        raise WHODrugParseError(
                            "ingredient_code must not be empty.", file_name, idx
                        )
                    if len(ingredient_code) > 50:
                        raise WHODrugParseError(
                            "ingredient_code exceeds maximum length of 50 characters.",
                            file_name,
                            idx,
                        )

                    if self.strict_referential_validation:
                        if drug_code not in self.known_drug_codes:
                            raise WHODrugParseError(
                                f"Referential integrity violation: drug_code '{drug_code}' linked to ingredient_code '{ingredient_code}', but drug code is not defined in any parsed drug records.",
                                file_name,
                                idx,
                            )
                        if ingredient_code not in self.known_ingredient_codes:
                            raise WHODrugParseError(
                                f"Referential integrity violation: ingredient_code '{ingredient_code}' linked to drug_code '{drug_code}', but ingredient code is not defined in any parsed ingredient records.",
                                file_name,
                                idx,
                            )

                    yield {
                        "type": "drug_ingredient",
                        "data": {
                            "drug_code": drug_code,
                            "ingredient_code": ingredient_code,
                            "dictionary_version": self.dictionary_version,
                        },
                    }
            except WHODrugParseError:
                raise
            except Exception as e:
                raise WHODrugParseError(
                    f"Unexpected error: {str(e)}", file_name, idx
                ) from e


def parse_whodrug_file(
    file_input: Any,
    dictionary_version: str,
    file_type: str = None,
    file_name: str = None,
    encoding: str = "utf-8",
    strict_referential_validation: bool = False,
    custom_configs: Dict[str, Any] = None,
    parser: WHODrugParser = None,
) -> Iterator[Dict[str, Any]]:
    """Stable public entry point to parse a WHODrug ASCII or delimited file streamingly.

    Args:
        file_input: A file path, a file-like object, or an iterable of lines.
        dictionary_version: The version tag of the WHODrug dictionary (e.g. "2024-03").
        file_type: One of "drugs", "ingredients", "atc", "drug_atc", "drug_ingredients".
            If None, it will try to auto-detect from file_name or file_input path.
        file_name: Optional file name for error context.
        encoding: The file encoding to use when reading files (default "utf-8").
        strict_referential_validation: If True, validates referential consistency.
        custom_configs: Optional custom layouts or overrides.
        parser: Optional pre-existing WHODrugParser instance to reuse state and cache.

    Yields:
        Dict[str, Any]: Parsed WHODrug record dictionaries.
    """
    if parser is None:
        parser = WHODrugParser(
            dictionary_version=dictionary_version,
            encoding=encoding,
            strict_referential_validation=strict_referential_validation,
            custom_configs=custom_configs,
        )
    yield from parser.parse(file_input, file_type=file_type, file_name=file_name)
