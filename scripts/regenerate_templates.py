#!/usr/bin/env python3
"""
Developer template regeneration bootstrap command.

Constructs the protocol template (protocol_template.docx) programmatically
and writes it to the designated templates directory.
"""

import os
import sys

# Ensure correct PYTHONPATH resolution for packages/core-models
sys.path.insert(
    0,
    os.path.join(os.path.dirname(os.path.dirname(__file__)), "packages", "core-models"),
)

from apps.designer.rendering import build_docx_template


def main() -> None:
    """
    Invokes the build_docx_template builder function to write the protocol template.
    """
    print("Regenerating DOCX protocol template...", end=" ", flush=True)
    try:
        path = build_docx_template()
        print("Success!")
        print(f"Generated template written to: {path}")
        sys.exit(0)
    except Exception as e:
        print("Failed!")
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
