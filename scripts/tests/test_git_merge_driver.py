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
    """Verify identification of logical code paths.

    @req:PRD-SYS-001
    """
    assert is_logical_code("apps/execution/main.py") is True
    assert is_logical_code("apps/designer/main.js") is True
    assert is_logical_code("packages/security/gating.ts") is True
    assert is_logical_code("docs/SDLC/03_API_Integration_Specification.md") is False
    assert is_logical_code("docs/adr/0001-test.md") is False
    assert is_logical_code("some_json_config.json") is False


def test_merge_secrets_baseline():
    """Verify clean merging of non-overlapping secret baselines.

    @req:PRD-SYS-001
    """
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
    """Verify generic JSON merging of non-overlapping keys and lists.

    @req:PRD-SYS-001
    """
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
    """Verify markdown text merging.

    @req:PRD-SYS-001
    """
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


def test_merge_secrets_baseline_value_collision_fail_fast():
    """Verify value collision in secret metadata returns False for fail-fast behavior.

    @req:PRD-SYS-001
    """
    curr_baseline = {
        "version": "1.5.0",
        "results": {
            "file_a.py": [
                {
                    "filename": "file_a.py",
                    "hashed_secret": "common_hash",  # pragma: allowlist secret
                    "is_secret": True,
                }
            ]
        },
    }

    oth_baseline = {
        "version": "1.5.0",
        "results": {
            "file_a.py": [
                {
                    "filename": "file_a.py",
                    "hashed_secret": "common_hash",  # pragma: allowlist secret
                    "is_secret": False,
                }
            ]
        },
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
        assert success is False


def test_merge_secrets_baseline_top_level_scalar_collision():
    """Verify top-level scalar value collision returns False.

    @req:PRD-SYS-001
    """
    curr_baseline = {"version": "1.5.0"}
    oth_baseline = {"version": "2.0.0"}

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
        assert success is False


def test_merge_secrets_baseline_structural_mismatch_fail_fast():
    """Verify structural type mismatch returns False.

    @req:PRD-SYS-001
    """
    curr_baseline = {"version": "1.5.0"}
    oth_baseline = {"version": ["1", "5", "0"]}

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
        assert success is False


def test_merge_generic_json_value_collision_fail_fast():
    """Verify scalar value collision in generic JSON returns False.

    @req:PRD-SYS-001
    """
    curr_json = {"key_a": "value_1"}
    oth_json = {"key_a": "value_2"}

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
        assert success is False


def test_git_merge_driver_cli_conflict_and_markers():
    """Verify CLI exit code 1 and standard Git conflict markers on collisions.

    @req:PRD-SYS-001
    """
    import subprocess
    import sys

    curr_baseline = {"version": "1.5.0"}
    oth_baseline = {"version": "2.0.0"}
    anc_baseline = {"version": "1.0.0"}

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.secrets.baseline")
        oth_file = os.path.join(tmpdir, "oth.secrets.baseline")
        anc_file = os.path.join(tmpdir, "anc.secrets.baseline")

        with open(curr_file, "w", encoding="utf-8") as f:
            json.dump(curr_baseline, f, indent=2)
        with open(oth_file, "w", encoding="utf-8") as f:
            json.dump(oth_baseline, f, indent=2)
        with open(anc_file, "w", encoding="utf-8") as f:
            json.dump(anc_baseline, f, indent=2)

        res = subprocess.run(
            [
                sys.executable,
                "scripts/git_merge_driver.py",
                anc_file,
                curr_file,
                oth_file,
                ".secrets.baseline",
            ],
            capture_output=True,
            text=True,
        )

        assert res.returncode == 1
        with open(curr_file, encoding="utf-8") as f:
            content = f.read()
        assert "<<<<<<<" in content
        assert "=======" in content
        assert ">>>>>>>" in content


def test_git_merge_driver_cli_non_overlapping_success():
    """Verify CLI exit code 0 on non-overlapping baseline merges.

    @req:PRD-SYS-001
    """
    import subprocess
    import sys

    curr_baseline = {
        "filters_used": [{"path": "filter_a"}],
        "plugins_used": [],
        "results": {},
    }
    oth_baseline = {
        "filters_used": [{"path": "filter_b"}],
        "plugins_used": [],
        "results": {},
    }
    anc_baseline = {"filters_used": [], "plugins_used": [], "results": {}}

    with tempfile.TemporaryDirectory() as tmpdir:
        curr_file = os.path.join(tmpdir, "curr.secrets.baseline")
        oth_file = os.path.join(tmpdir, "oth.secrets.baseline")
        anc_file = os.path.join(tmpdir, "anc.secrets.baseline")

        with open(curr_file, "w", encoding="utf-8") as f:
            json.dump(curr_baseline, f)
        with open(oth_file, "w", encoding="utf-8") as f:
            json.dump(oth_baseline, f)
        with open(anc_file, "w", encoding="utf-8") as f:
            json.dump(anc_baseline, f)

        res = subprocess.run(
            [
                sys.executable,
                "scripts/git_merge_driver.py",
                anc_file,
                curr_file,
                oth_file,
                ".secrets.baseline",
            ],
            capture_output=True,
            text=True,
        )

        assert res.returncode == 0
        with open(curr_file, encoding="utf-8") as f:
            merged = json.load(f)

        assert len(merged["filters_used"]) == 2


def test_gitattributes_configuration_mapping():
    """Verify .gitattributes restricts custom driver to security baseline files only.

    @req:PRD-SYS-001
    """
    gitattrs_path = os.path.join(os.getcwd(), ".gitattributes")
    assert os.path.exists(gitattrs_path)
    with open(gitattrs_path, encoding="utf-8") as f:
        content = f.read()

    assert ".secrets.baseline merge=custom-metadata-driver" in content
    assert "*.secrets.baseline merge=custom-metadata-driver" in content
    assert "*.md merge=custom-metadata-driver" in content


def test_merge_markdown_text_overlapping():
    """Verify markdown text merging fails-fast on overlapping changes.

    @req:PRD-SYS-001
    """
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
