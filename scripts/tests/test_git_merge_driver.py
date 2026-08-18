import json
import os
import tempfile

from scripts.git_merge_driver import (
    is_logical_code,
    merge_generic_json,
    merge_markdown_text,
    merge_secrets_baseline,
)


def test_is_logical_code():
    assert is_logical_code("apps/execution/main.py") is True
    assert is_logical_code("apps/designer/main.js") is True
    assert is_logical_code("packages/security/gating.ts") is True
    assert is_logical_code("docs/SDLC/03_API_Integration_Specification.md") is False
    assert is_logical_code("docs/adr/0001-test.md") is False
    assert is_logical_code("some_json_config.json") is False


def test_merge_secrets_baseline():
    curr_baseline = {
        "filters_used": [{"path": "filter_a"}],
        "plugins_used": [{"name": "plugin_a"}],
        "results": {
            "file_a.py": [
                {
                    "filename": "file_a.py",
                    "hashed_secret": "abc123curr",  # pragma: allowlist secret
                    "line_number": 10,
                },
                {
                    "filename": "file_a.py",
                    "hashed_secret": "common_hash",  # pragma: allowlist secret
                    "line_number": 12,
                },
            ]
        },
        "generated_at": "2026-08-01T00:00:00Z",
    }

    oth_baseline = {
        "filters_used": [{"path": "filter_b"}],
        "plugins_used": [{"name": "plugin_b"}],
        "results": {
            "file_a.py": [
                {
                    "filename": "file_a.py",
                    "hashed_secret": "xyz789oth",  # pragma: allowlist secret
                    "line_number": 20,
                },
                {
                    "filename": "file_a.py",
                    "hashed_secret": "common_hash",  # pragma: allowlist secret
                    "line_number": 15,
                },
            ]
        },
        "generated_at": "2026-08-02T00:00:00Z",
    }

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.baseline")
        oth_file = os.path.join(tmpdir, "oth.baseline")
        anc_file = os.path.join(tmpdir, "anc.baseline")

        with open(curr_file, "w", encoding="utf-8") as f:
            json.dump(curr_baseline, f)
        with open(oth_file, "w", encoding="utf-8") as f:
            json.dump(oth_baseline, f)
        with open(anc_file, "w", encoding="utf-8") as f:
            json.dump({}, f)

        success = merge_secrets_baseline(anc_file, curr_file, oth_file)
        assert success is True

        with open(curr_file, encoding="utf-8") as f:
            merged = json.load(f)

        assert "generated_at" not in merged
        assert len(merged["filters_used"]) == 2
        assert len(merged["plugins_used"]) == 2

        file_a_results = merged["results"]["file_a.py"]
        assert len(file_a_results) == 3  # abc123curr, common_hash, xyz789oth
        for res in file_a_results:
            assert "line_number" not in res


def test_merge_generic_json():
    curr_json = {
        "setting_a": True,
        "list_items": [1, 2, 3],
        "nested": {"key_a": "val_a"},
    }
    oth_json = {
        "setting_b": False,
        "list_items": [3, 4, 5],
        "nested": {"key_b": "val_b"},
    }

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.json")
        oth_file = os.path.join(tmpdir, "oth.json")
        anc_file = os.path.join(tmpdir, "anc.json")

        with open(curr_file, "w", encoding="utf-8") as f:
            json.dump(curr_json, f)
        with open(oth_file, "w", encoding="utf-8") as f:
            json.dump(oth_json, f)
        with open(anc_file, "w", encoding="utf-8") as f:
            json.dump({}, f)

        success = merge_generic_json(anc_file, curr_file, oth_file)
        assert success is True

        with open(curr_file, encoding="utf-8") as f:
            merged = json.load(f)

        assert merged["setting_a"] is True
        assert merged["setting_b"] is False
        assert sorted(merged["list_items"]) == [1, 2, 3, 4, 5]
        assert merged["nested"] == {"key_a": "val_a", "key_b": "val_b"}


def test_merge_markdown_text():
    anc_content = "# Header\n\nSection 1\n\nSection 2\n"
    curr_content = "# Header\n\nSection 1 updated by HEAD\n\nSection 2\n"
    oth_content = "# Header\n\nSection 1\n\nSection 2 updated by OTHER\n"

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.md")
        oth_file = os.path.join(tmpdir, "oth.md")
        anc_file = os.path.join(tmpdir, "anc.md")

        with open(curr_file, "w", encoding="utf-8") as f:
            f.write(curr_content)
        with open(oth_file, "w", encoding="utf-8") as f:
            f.write(oth_content)
        with open(anc_file, "w", encoding="utf-8") as f:
            f.write(anc_content)

        success = merge_markdown_text(anc_file, curr_file, oth_file)
        assert success is True

        with open(curr_file, encoding="utf-8") as f:
            merged = f.read()

        assert "<<<<<<<" not in merged
        assert "=======" not in merged
        assert ">>>>>>>" not in merged
        assert "Section 1 updated by HEAD" in merged
        assert "Section 2 updated by OTHER" in merged


def test_merge_markdown_text_overlapping():
    anc_content = "# Header\n\nOriginal line\n"
    curr_content = "# Header\n\nLine edited by HEAD\n"
    oth_content = "# Header\n\nLine edited by OTHER\n"

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.md")
        oth_file = os.path.join(tmpdir, "oth.md")
        anc_file = os.path.join(tmpdir, "anc.md")

        with open(curr_file, "w", encoding="utf-8") as f:
            f.write(curr_content)
        with open(oth_file, "w", encoding="utf-8") as f:
            f.write(oth_content)
        with open(anc_file, "w", encoding="utf-8") as f:
            f.write(anc_content)

        success = merge_markdown_text(anc_file, curr_file, oth_file)
        assert success is False

        with open(curr_file, encoding="utf-8") as f:
            merged = f.read()

        assert "<<<<<<<" in merged
        assert "=======" in merged
        assert ">>>>>>>" in merged
        assert "Line edited by HEAD" in merged
        assert "Line edited by OTHER" in merged
