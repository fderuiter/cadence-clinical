import csv
import io
import re
from typing import List, Dict, Any

# W3C XML Name Regex (simplified to allow English letters, underscores, dots, hyphens, and numbers)
# A strict W3C NameStartChar without colon
XML_NAME_START_CHAR = r"([A-Za-z_]|[\xC0-\xD6\xD8-\xF6\xF8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD])"
XML_NAME_CHAR = r"(" + XML_NAME_START_CHAR + r"|[\-\.0-9\xB7\u0300-\u036F\u203F-\u2040])"

XML_NAME_PATTERN = re.compile(f"^{XML_NAME_START_CHAR}{XML_NAME_CHAR}*$")

def is_valid_xml_name(name: str) -> bool:
    if not name:
        return False
    # Requirement 3: Target names containing namespace colons must have exactly one colon
    if ":" in name:
        parts = name.split(":")
        if len(parts) != 2:
            return False
        prefix, local_name = parts
        return bool(XML_NAME_PATTERN.match(prefix)) and bool(XML_NAME_PATTERN.match(local_name))
    
    return bool(XML_NAME_PATTERN.match(name))

def validate_mapping_csv(csv_content: str) -> List[Dict[str, Any]]:
    """
    Parses and validates the CSV configuration.
    Raises ValueError if any target name fails the W3C XML Name regex.
    """
    reader = csv.DictReader(io.StringIO(csv_content))
    if not reader.fieldnames:
        raise ValueError("Invalid CSV format: Missing headers")
        
    if "to_name" not in reader.fieldnames or "to_alias" not in reader.fieldnames:
        raise ValueError("Invalid CSV format: Missing mandatory headers ('to_name', 'to_alias')")
        
    rows = []
    for line_number, row in enumerate(reader, start=2): # 1-based header, so start=2
        to_name = row.get("to_name", "").strip()
        to_alias = row.get("to_alias", "").strip()
        
        if to_name and not is_valid_xml_name(to_name):
            raise ValueError(f"Row {line_number}: Invalid XML name in 'to_name' column: '{to_name}'")
            
        if to_alias and not is_valid_xml_name(to_alias):
            raise ValueError(f"Row {line_number}: Invalid XML name in 'to_alias' column: '{to_alias}'")
            
        rows.append(row)
        
    return rows
