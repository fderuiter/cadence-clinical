#!/usr/bin/env python3
import os
import re
import sys

# Designated breakpoints
ALLOWED_BREAKPOINTS = {"576px", "768px", "900px", "1024px", "1200px"}

# Color checking regexes
HEX_COLOR_RE = re.compile(r"#[0-9a-fA-F]{3,8}")
COLOR_FUNC_RE = re.compile(r"\b(rgba?|hsla?)\(", re.IGNORECASE)
LITERAL_COLOR_RE = re.compile(
    r"\b(black|white|red|green|blue|yellow|purple|orange|pink|brown|gold|silver|navy|aqua|teal|lime|maroon|fuchsia|olive|cyan|magenta)\b",
    re.IGNORECASE,
)

# Spacing checking regexes (absolute hardcoded spacing units, excluding zero values)
HARDCODED_SPACING_RE = re.compile(
    r"\b(?!0+(?:px|rem|em|%|vw|vh)?\b)[0-9.]+(px|rem|em|pt|pc|in|cm|mm|ch|ex)\b",
    re.IGNORECASE,
)

# Interactive selector matching
INTERACTIVE_SELECTOR_RE = re.compile(
    r"\b(button|input|select|textarea|a)\b|\.(btn|button|interactive|touch-target)\b|\[(interactive|touch-target)\]",
    re.IGNORECASE,
)

# Standard allowed keywords/resets for spacing
ALLOWED_SPACING_KEYWORDS = {
    "auto",
    "inherit",
    "initial",
    "unset",
    "none",
    "currentColor",
    "transparent",
}


class CSSNode:
    def __init__(self, kind, header, line):
        self.kind = kind  # "root", "media", "rule", "keyframes", etc.
        self.header = header
        self.line = line
        self.children = []
        self.parent = None


class Declaration:
    def __init__(self, property_name, value, line):
        self.property_name = property_name.strip()
        self.value = value.strip()
        self.line = line


def parse_declarations_from_text(decl_text, start_line):
    if ":" not in decl_text:
        return []
    parts = decl_text.split(":", 1)
    prop = parts[0].strip()
    val = parts[1].strip()
    return [Declaration(prop, val, start_line)]


def parse_css_to_tree(css_clean):
    root = CSSNode("root", "", 1)
    current_node = root

    current_text = []
    current_text_start_line = 1

    line_num = 1
    i = 0
    n = len(css_clean)

    in_single_quote = False
    in_double_quote = False
    paren_depth = 0

    while i < n:
        c = css_clean[i]

        # Track quotes and parentheses
        if c == "'" and not in_double_quote:
            in_single_quote = not in_single_quote
        elif c == '"' and not in_single_quote:
            in_double_quote = not in_double_quote
        elif not in_single_quote and not in_double_quote:
            if c == "(":
                paren_depth += 1
            elif c == ")":
                paren_depth = max(0, paren_depth - 1)

        is_special = not in_single_quote and not in_double_quote and paren_depth == 0

        if c == "\n":
            line_num += 1

        if is_special and c == "{":
            header = "".join(current_text).strip()
            kind = "rule"
            if header.startswith("@media"):
                kind = "media"
            elif header.startswith("@keyframes") or header.startswith(
                "@-webkit-keyframes"
            ):
                kind = "keyframes"
            elif header.startswith("@font-face"):
                kind = "font-face"
            elif header.startswith("@supports"):
                kind = "supports"

            node = CSSNode(kind, header, current_text_start_line)
            node.parent = current_node
            current_node.children.append(node)
            current_node = node

            current_text = []
            current_text_start_line = line_num

        elif is_special and c == "}":
            last_text = "".join(current_text).strip()
            if last_text:
                declarations = parse_declarations_from_text(
                    last_text, current_text_start_line
                )
                current_node.children.extend(declarations)

            if current_node.parent is not None:
                current_node = current_node.parent

            current_text = []
            current_text_start_line = line_num

        elif is_special and c == ";":
            decl_text = "".join(current_text).strip()
            if decl_text and current_node.kind not in (
                "media",
                "supports",
                "keyframes",
                "root",
            ):
                declarations = parse_declarations_from_text(
                    decl_text, current_text_start_line
                )
                current_node.children.extend(declarations)

            current_text = []
            current_text_start_line = line_num

        else:
            if not current_text:
                current_text_start_line = line_num
            current_text.append(c)

        i += 1

    return root


def clean_css_comments(css_content):
    cleaned_css = []
    in_multiline_comment = False
    in_singleline_comment = False

    i = 0
    n = len(css_content)
    while i < n:
        if in_multiline_comment:
            if css_content[i : i + 2] == "*/":
                cleaned_css.append("  ")
                in_multiline_comment = False
                i += 2
            else:
                if css_content[i] == "\n":
                    cleaned_css.append("\n")
                else:
                    cleaned_css.append(" ")
                i += 1
        elif in_singleline_comment:
            if css_content[i] == "\n":
                cleaned_css.append("\n")
                in_singleline_comment = False
                i += 1
            else:
                cleaned_css.append(" ")
                i += 1
        else:
            if css_content[i : i + 2] == "/*":
                cleaned_css.append("  ")
                in_multiline_comment = True
                i += 2
            elif css_content[i : i + 2] == "//":
                cleaned_css.append("  ")
                in_singleline_comment = True
                i += 2
            else:
                cleaned_css.append(css_content[i])
                i += 1
    return "".join(cleaned_css)


def extract_style_blocks_from_vue(file_content):
    style_blocks = []
    in_style = False
    current_block_lines = []
    start_line = 0

    lines = file_content.split("\n")
    for i, line in enumerate(lines, 1):
        if not in_style:
            if "<style" in line:
                in_style = True
                start_line = i
                parts = line.split("<style", 1)
                tag_rest = parts[1]
                if ">" in tag_rest:
                    content_after_tag = tag_rest.split(">", 1)[1]
                    if content_after_tag.strip():
                        current_block_lines.append(content_after_tag)
        else:
            if "</style>" in line:
                parts = line.split("</style>", 1)
                content_before_tag = parts[0]
                if content_before_tag.strip():
                    current_block_lines.append(content_before_tag)
                style_blocks.append(("\n".join(current_block_lines), start_line))
                current_block_lines = []
                in_style = False
            else:
                current_block_lines.append(line)

    return style_blocks


def extract_vars_and_remain(value):
    vars_found = []
    remaining_chars = []
    i = 0
    n = len(value)
    while i < n:
        if value[i : i + 4] == "var(":
            depth = 1
            start = i
            i += 4
            while i < n and depth > 0:
                if value[i] == "(":
                    depth += 1
                elif value[i] == ")":
                    depth -= 1
                i += 1
            vars_found.append(value[start:i])
        else:
            remaining_chars.append(value[i])
            i += 1
    return vars_found, "".join(remaining_chars)


def check_color_var(var_str):
    if not var_str.startswith("var(") or not var_str.endswith(")"):
        return False, "Invalid var() format"
    inner = var_str[4:-1].strip()

    # Standard CSS var syntax can be var(--var-name, fallback)
    # The first part is the variable name. We only need to check if the variable name is a valid color token.
    parts = inner.split(",", 1)
    var_name = parts[0].strip()

    if not var_name.startswith("--color-"):
        return False, f"Variable '{var_name}' must be a color token (prefix '--color-')"
    return True, ""


def check_spacing_var(var_str):
    if not var_str.startswith("var(") or not var_str.endswith(")"):
        return False, "Invalid var() format"
    inner = var_str[4:-1].strip()

    parts = inner.split(",", 1)
    var_name = parts[0].strip()

    if not var_name.startswith("--spacing-") and not var_name.startswith(
        "--touch-target-"
    ):
        return (
            False,
            f"Variable '{var_name}' must be a spacing token (prefix '--spacing-')",
        )
    return True, ""


def check_touch_target_height(val_str):
    match = re.match(r"^\s*([0-9.]+)\s*(px|rem|em)\s*$", val_str, re.IGNORECASE)
    if match:
        num = float(match.group(1))
        unit = match.group(2).lower()
        px_val = num
        if unit in ("rem", "em"):
            px_val = num * 16.0
        if px_val < 44.0:
            return (
                False,
                f"Interactive touch target height '{val_str}' is below the 44px minimum",
            )
    return True, ""


def is_color_property(prop):
    prop_lower = prop.lower()
    return (
        prop_lower
        in (
            "color",
            "background-color",
            "border-color",
            "outline-color",
            "text-decoration-color",
            "caret-color",
            "column-rule-color",
            "fill",
            "stroke",
        )
        or prop_lower.endswith("-color")
        or prop_lower.startswith("border")
        or prop_lower in ("background", "outline")
    )


def is_spacing_property(prop):
    prop_lower = prop.lower()
    return (
        prop_lower
        in (
            "margin",
            "padding",
            "gap",
            "row-gap",
            "column-gap",
            "top",
            "bottom",
            "left",
            "right",
        )
        or prop_lower.startswith("margin-")
        or prop_lower.startswith("padding-")
    )


def is_exempt_from_color_checks(filepath):
    # tokens.css defines the colors, so it is exempt from raw color checks
    return "tokens.css" in filepath


def scan_css_tree(root_node, filepath, errors):
    def traverse(node):
        # Validate Media Queries
        if node.kind == "media":
            # Check for max-width
            if "max-width" in node.header:
                errors.append(
                    {
                        "file": filepath,
                        "line": node.line,
                        "rule": "Media Query - Mobile-First Constraint",
                        "severity": "ERROR",
                        "message": f"Media query '{node.header}' uses max-width. Mobile-first rules must use min-width.",
                        "suggestion": "Convert to min-width rules or use a mobile-first responsive scale",
                    }
                )

            # Check hardcoded dimensions in media queries
            match = re.search(
                r"min-width\s*:\s*([0-9.]+px)", node.header, re.IGNORECASE
            )
            if match:
                val = match.group(1).lower()
                if val not in ALLOWED_BREAKPOINTS:
                    errors.append(
                        {
                            "file": filepath,
                            "line": node.line,
                            "rule": "Media Query - Designated Breakpoint Scale",
                            "severity": "ERROR",
                            "message": f"Media query '{node.header}' uses non-standard breakpoint '{val}'.",
                            "suggestion": f"Use one of the designated breakpoints: {', '.join(sorted(ALLOWED_BREAKPOINTS))}",
                        }
                    )

        # Validate Declarations in this node
        for child in node.children:
            if isinstance(child, Declaration):
                prop = child.property_name
                val = child.value
                line = child.line

                # Custom variables start with '--' - bypass strict checks on their definitions
                if prop.startswith("--"):
                    continue

                # Check Colors
                if is_color_property(prop) and not is_exempt_from_color_checks(
                    filepath
                ):
                    vars_found, remain = extract_vars_and_remain(val)
                    # Check if there are variables, they must be color variables
                    for var_str in vars_found:
                        ok, msg = check_color_var(var_str)
                        if not ok:
                            errors.append(
                                {
                                    "file": filepath,
                                    "line": line,
                                    "rule": "Design Token Compliance - Color Token",
                                    "severity": "ERROR",
                                    "message": f"Invalid color token usage on property '{prop}: {val}': {msg}",
                                    "suggestion": "Use var(--color-...) with defined color tokens",
                                }
                            )
                    # Check remaining string for hardcoded colors
                    if HEX_COLOR_RE.search(remain):
                        errors.append(
                            {
                                "file": filepath,
                                "line": line,
                                "rule": "Design Token Compliance - Hex Color",
                                "severity": "ERROR",
                                "message": f"Property '{prop}: {val}' contains hardcoded hex color.",
                                "suggestion": "Use a design token color via var(--color-...)",
                            }
                        )
                    if COLOR_FUNC_RE.search(remain):
                        errors.append(
                            {
                                "file": filepath,
                                "line": line,
                                "rule": "Design Token Compliance - Color Function",
                                "severity": "ERROR",
                                "message": f"Property '{prop}: {val}' contains hardcoded rgb/rgba/hsl/hsla function color.",
                                "suggestion": "Use a design token color via var(--color-...)",
                            }
                        )

                    clean_remain = remain.strip()
                    if clean_remain.lower() not in (
                        "transparent",
                        "currentcolor",
                        "inherit",
                        "initial",
                        "unset",
                        "none",
                    ):
                        if lit_color_re_match := LITERAL_COLOR_RE.search(remain):
                            errors.append(
                                {
                                    "file": filepath,
                                    "line": line,
                                    "rule": "Design Token Compliance - Literal Color",
                                    "severity": "ERROR",
                                    "message": f"Property '{prop}: {val}' contains literal color '{lit_color_re_match.group(0)}'.",
                                    "suggestion": "Use a design token color via var(--color-...)",
                                }
                            )

                # Check Spacing
                if is_spacing_property(prop):
                    vars_found, remain = extract_vars_and_remain(val)
                    for var_str in vars_found:
                        ok, msg = check_spacing_var(var_str)
                        if not ok:
                            errors.append(
                                {
                                    "file": filepath,
                                    "line": line,
                                    "rule": "Design Token Compliance - Spacing Token",
                                    "severity": "ERROR",
                                    "message": f"Invalid spacing token usage on property '{prop}: {val}': {msg}",
                                    "suggestion": "Use var(--spacing-...) with defined spacing tokens",
                                }
                            )
                    # Check remaining string for hardcoded spacing units
                    if clean_remain := remain.strip():
                        tokens = re.split(r"[\s,\/\(\)\+\-\*]+", clean_remain)
                        for tok in tokens:
                            tok = tok.strip()
                            if not tok or tok.lower() in ALLOWED_SPACING_KEYWORDS:
                                continue
                            if HARDCODED_SPACING_RE.search(tok):
                                errors.append(
                                    {
                                        "file": filepath,
                                        "line": line,
                                        "rule": "Design Token Compliance - Spacing Unit",
                                        "severity": "ERROR",
                                        "message": f"Property '{prop}: {val}' uses absolute hardcoded unit in '{tok}'.",
                                        "suggestion": "Use var(--spacing-...) instead of absolute hardcoded units",
                                    }
                                )

                # Check Touch Target Heights
                if prop.lower() in ("height", "min-height") and node.parent is not None:
                    # Check if the rule's selector targets interactive elements
                    if INTERACTIVE_SELECTOR_RE.search(node.header):
                        ok, msg = check_touch_target_height(val)
                        if not ok:
                            errors.append(
                                {
                                    "file": filepath,
                                    "line": line,
                                    "rule": "Accessible Interaction - Touch Target Height",
                                    "severity": "ERROR",
                                    "message": f"Rule '{node.header}' contains {msg}.",
                                    "suggestion": "Ensure interactive elements have a minimum physical height of at least 44px or specify var(--touch-target-min)",
                                }
                            )

            elif isinstance(child, CSSNode):
                traverse(child)

    traverse(root_node)


def find_files_to_scan():
    # Standalone Stylesheets
    standalone_files = [
        "packages/ui/tokens.css",
        "packages/ui/responsive.css",
        "apps/subject-portal/style.css",
        "apps/web/src/style.css",
    ]
    files_to_scan = [os.path.join("/app", f) for f in standalone_files]

    # Vue components directories
    vue_dirs = [
        "packages/ui/src/components/clinical",
        "apps/subject-portal",
        "apps/web",
    ]

    for vdir in vue_dirs:
        abs_vdir = os.path.join("/app", vdir)
        if not os.path.exists(abs_vdir):
            continue
        for dirpath, dirnames, filenames in os.walk(abs_vdir):
            if any(
                ignored in dirpath.split(os.sep)
                for ignored in (
                    "node_modules",
                    "dist",
                    ".nuxt",
                    "build",
                    ".git",
                    ".cache",
                    "coverage",
                    ".pytest_cache",
                )
            ):
                continue
            for filename in filenames:
                if filename.endswith(".vue"):
                    files_to_scan.append(os.path.join(dirpath, filename))

    return sorted(list(set(files_to_scan)))


def main():
    errors = []
    files_to_scan = find_files_to_scan()

    for filepath in files_to_scan:
        try:
            with open(filepath, encoding="utf-8") as f:
                content = f.read()
        except Exception as e:
            print(f"Skipping file {filepath} due to read error: {e}")
            continue

        if filepath.endswith(".css"):
            # Standalone stylesheet
            css_clean = clean_css_comments(content)
            root_node = parse_css_to_tree(css_clean)
            scan_css_tree(root_node, filepath, errors)

        elif filepath.endswith(".vue"):
            # Single File Component
            style_blocks = extract_style_blocks_from_vue(content)
            for style_content, start_line in style_blocks:
                # Prepend start_line - 1 newlines to align line numbers perfectly!
                prepended_content = ("\n" * (start_line - 1)) + style_content
                css_clean = clean_css_comments(prepended_content)
                root_node = parse_css_to_tree(css_clean)
                scan_css_tree(root_node, filepath, errors)

    # Format and display errors
    if errors:
        print(f"\n[STYLING INFRACTIONS FOUND: {len(errors)}]\n")
        for err in errors:
            # Output short location relative to /app
            rel_file = os.path.relpath(err["file"], "/app")
            print(f"File: {rel_file}:{err['line']}")
            print(f"Rule violated: {err['rule']}")
            print(f"Details: {err['message']}")
            print(f"Remediation: {err['suggestion']}")
            print("-" * 60)
        sys.exit(1)
    else:
        print(
            "\nAll stylesheets and components adhere to the design system! No styling errors found.\n"
        )
        sys.exit(0)


if __name__ == "__main__":
    main()
