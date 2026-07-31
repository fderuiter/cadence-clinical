from unittest.mock import patch

import pytest

from scripts.detect_duplication import main, normalize_line, scan_file_for_lines

# Language Coverage Tests


def test_normalize_line_python():
    # Standard inline comment stripping
    assert normalize_line("x = 10  # inline comment") == "x = 10"

    # Python comments starting the line
    assert normalize_line("# This is a full-line comment") == ""

    # URL in Python comment (should still strip, but first masks URL)
    assert normalize_line("# visit https://example.com for info") == ""

    # URL in Python string
    assert (
        normalize_line("url = 'https://health.gov/api'")
        == 'url = "http-url-placeholder"'
    )

    # Python import/from ignored
    assert normalize_line("import os") == ""
    assert normalize_line("from math import ceil") == ""


def test_normalize_line_javascript():
    # Clean URLs with quotes and semicolons should be preserved
    assert (
        normalize_line('const api = "https://example.com/endpoint";')
        == 'const api = "http-url-placeholder";'
    )
    assert (
        normalize_line("const api = 'http://api.foo.com';")
        == 'const api = "http-url-placeholder";'
    )

    # URL with template literal/backticks
    assert (
        normalize_line("const api = `https://test.net/path`;")
        == 'const api = "http-url-placeholder";'
    )

    # URL and inline comment on the same line
    assert (
        normalize_line('const api = "https://example.com"; // API url')
        == 'const api = "http-url-placeholder";'
    )

    # Non-URL inline comments stripped
    assert normalize_line("const x = 12; // inline comment") == "const x = 12;"
    assert normalize_line("// full line comment") == ""

    # JS block comment single line
    assert normalize_line("/* block comment */ const y = 20;") == "const y = 20;"

    # Block comment delimiters starting line
    assert normalize_line("/* start comment") == ""
    assert normalize_line(" * middle comment") == ""
    assert normalize_line(" */ end comment") == ""

    # JS imports/exports/braces ignored
    assert normalize_line("import { something } from 'lib';") == ""
    assert normalize_line("export default function() {") == ""
    assert normalize_line("const { a, b } = obj;") == ""
    assert normalize_line("}") == ""
    assert normalize_line("{") == ""


def test_normalize_line_vue():
    # Vue template syntax and mixed lines
    assert normalize_line("<template>") == "<template>"
    assert normalize_line("  <div class='container'>") == '<div class="container">'
    assert (
        normalize_line("const axios = require('axios'); // loading lib")
        == 'const axios = require("axios");'
    )


def test_normalize_line_css():
    # CSS block comments
    assert normalize_line("/* css block comment */ .button {") == ".button {"

    # URLs inside CSS asset background
    assert (
        normalize_line("background-image: url('https://images.com/bg.png');")
        == 'background-image: url("http-url-placeholder");'
    )
    assert normalize_line(".button {") == ".button {"


# File System Mocking / Isolation Tests


def test_scan_file_for_lines(tmp_path):
    # Use tmp_path to create a temporary test file
    test_file = tmp_path / "sample.py"
    test_file.write_text(
        "import os\n"
        "x = 42  # standard comment\n"
        "url = 'https://example.com/endpoint'\n"
        "/* css block comment */\n",
        encoding="utf-8",
    )

    # Scan the file
    lines = scan_file_for_lines(str(test_file))

    # Verify scanned lines (it should ignore empty and boilerplate import/comment lines)
    # import os -> skipped
    # /* css block comment */ -> becomes empty -> skipped
    # Only x = 42 and url = 'https://example.com/endpoint' are valid
    assert len(lines) == 2

    # First line metadata
    assert lines[0][0] == "x = 42"
    assert lines[0][1] == 2
    assert lines[0][2] == "x = 42  # standard comment"

    # Second line metadata
    assert lines[1][0] == 'url = "http-url-placeholder"'
    assert lines[1][1] == 3
    assert lines[1][2] == "url = 'https://example.com/endpoint'"


@patch("sys.argv", ["scripts/detect_duplication.py"])
@patch("os.path.exists")
@patch("os.walk")
@patch("scripts.detect_duplication.scan_file_for_lines")
@patch("sys.exit")
def test_main_no_duplicates_scanned(mock_exit, mock_scan, mock_walk, mock_exists):
    mock_exit.side_effect = SystemExit

    # Mock file discovery
    mock_exists.side_effect = lambda path: path in ["/app/apps", "/app/packages"]
    mock_walk.side_effect = [
        [("/app/apps/serviceA", [], ["file1.js"])],
        [("/app/packages/libB", [], ["file2.js"])],
    ]

    # Mock no duplicate line blocks (less than 15 lines)
    mock_scan.side_effect = [[("lineA", 1, "lineA")] * 10, [("lineB", 1, "lineB")] * 10]

    # Execute main
    with pytest.raises(SystemExit):
        main()

    # Should exit with 0 (no duplicate found)
    mock_exit.assert_called_once_with(0)


@patch("sys.argv", ["scripts/detect_duplication.py"])
@patch("os.path.exists")
@patch("os.walk")
@patch("scripts.detect_duplication.scan_file_for_lines")
@patch("sys.exit")
def test_main_with_duplicates_detected(mock_exit, mock_scan, mock_walk, mock_exists):
    mock_exit.side_effect = SystemExit

    # Mock file discovery
    mock_exists.side_effect = lambda path: path in ["/app/apps", "/app/packages"]
    mock_walk.side_effect = [
        [("/app/apps/serviceA", [], ["file1.js"])],
        [("/app/packages/libB", [], ["file2.js"])],
    ]

    # Mock duplicate line blocks (15 lines of identical content)
    # To avoid matching ignored file pairs, we use distinct random paths
    lines_file1 = [
        ("identical_logic_line", i, f"identical_logic_line {i}") for i in range(1, 20)
    ]
    lines_file2 = [
        ("identical_logic_line", i, f"identical_logic_line {i}") for i in range(1, 20)
    ]

    mock_scan.side_effect = [lines_file1, lines_file2]

    # Execute main
    with pytest.raises(SystemExit):
        main()

    # Should exit with 1 because duplication is detected
    mock_exit.assert_called_once_with(1)


@patch("sys.argv", ["scripts/detect_duplication.py"])
@patch("os.path.exists")
@patch("os.walk")
@patch("scripts.detect_duplication.scan_file_for_lines")
@patch("sys.exit")
def test_main_url_logic_preservation(mock_exit, mock_scan, mock_walk, mock_exists):
    mock_exit.side_effect = SystemExit

    # This test verifies that different code logic following a URL is NOT truncated
    # into identical lines, which would cause false-positive duplication blocks.
    mock_exists.side_effect = lambda path: path in ["/app/apps", "/app/packages"]
    mock_walk.side_effect = [
        [("/app/apps/serviceA", [], ["file1.js"])],
        [("/app/packages/libB", [], ["file2.js"])],
    ]

    # File 1 has logic fetching URL and calling actionA
    lines_file1 = []
    for i in range(1, 20):
        lines_file1.append(
            (
                f'const r = "http-url-placeholder"; actionA({i});',
                i,
                f"const r = 'https://api1.com'; actionA({i});",
            )
        )

    # File 2 has logic fetching URL but calling actionB
    lines_file2 = []
    for i in range(1, 20):
        lines_file2.append(
            (
                f'const r = "http-url-placeholder"; actionB({i});',
                i,
                f"const r = 'https://api2.com'; actionB({i});",
            )
        )

    mock_scan.side_effect = [lines_file1, lines_file2]

    # Execute main
    with pytest.raises(SystemExit):
        main()

    # Since actionA and actionB are different, the normalization preserves them
    # and they should NOT be detected as duplicates.
    mock_exit.assert_called_once_with(0)
