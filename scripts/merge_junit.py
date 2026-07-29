#!/usr/bin/env python3
"""JUnit XML Merging Script.

This script parses multiple JUnit XML files and merges them into a single,
consolidated JUnit XML report file. It updates test counters (tests, failures,
errors, skipped) and sums execution time.
"""

import os
import sys
import xml.etree.ElementTree as ET


def merge_junit_xml(output_path: str, input_paths: list[str]) -> None:
    """Merge multiple JUnit XML files into a single consolidated XML report.

    Args:
        output_path: The path to the destination consolidated XML file.
        input_paths: A list of paths to the input JUnit XML files to merge.

    Raises:
        ValueError: If no input paths are provided.
    """
    if not input_paths:
        raise ValueError("No input paths provided for merging.")

    # Create root element for consolidated testsuites
    merged_root = ET.Element("testsuites")

    total_tests = 0
    total_failures = 0
    total_errors = 0
    total_skipped = 0
    total_time = 0.0

    for path in input_paths:
        if not os.path.exists(path):
            print(f"Warning: JUnit XML report file '{path}' not found. Skipping.")
            continue
        try:
            tree = ET.parse(path)  # nosec B314
            root = tree.getroot()

            # Pytest can output <testsuites> or <testsuite> at the top level
            if root.tag == "testsuites":
                for suite in root.findall("testsuite"):
                    merged_root.append(suite)
                    total_tests += int(suite.get("tests", 0))
                    total_failures += int(suite.get("failures", 0))
                    total_errors += int(suite.get("errors", 0))
                    total_skipped += int(suite.get("skipped", 0))
                    total_time += float(suite.get("time", 0.0))
            elif root.tag == "testsuite":
                merged_root.append(root)
                total_tests += int(root.get("tests", 0))
                total_failures += int(root.get("failures", 0))
                total_errors += int(root.get("errors", 0))
                total_skipped += int(root.get("skipped", 0))
                total_time += float(root.get("time", 0.0))
        except Exception as e:
            print(f"Warning: Error parsing {path}: {e}")

    merged_root.set("tests", str(total_tests))
    merged_root.set("failures", str(total_failures))
    merged_root.set("errors", str(total_errors))
    merged_root.set("skipped", str(total_skipped))
    merged_root.set("time", f"{total_time:.3f}")

    merged_tree = ET.ElementTree(merged_root)
    merged_tree.write(output_path, encoding="utf-8", xml_declaration=True)
    print(f"Successfully merged {len(input_paths)} files into {output_path}")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(
            "Usage: python merge_junit.py <output_path> <input_path1> <input_path2> ..."
        )
        sys.exit(1)
    merge_junit_xml(sys.argv[1], sys.argv[2:])
