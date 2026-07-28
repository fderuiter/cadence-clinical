import os
import re
import subprocess
import sys

# Import shared compliance utility
try:
    import compliance_utility
except ImportError:
    try:
        from scripts import compliance_utility
    except ImportError:
        sys.path.append(os.path.dirname(os.path.abspath(__file__)))
        import compliance_utility

ADR_DIR = "docs/adr"
INDEX_FILE = os.path.join(ADR_DIR, "index.md")
IGNORE_FILES = {"TEMPLATE.md", "index.md"}

# Regex patterns
FILENAME_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}-.+\.md$")
DATE_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}")
TITLE_PATTERN_OLD = re.compile(r"^# .*\d{4}-\d{2}-\d{2}.*$")
TITLE_PATTERN_NEW = re.compile(r"^# ADR-(?:\d+|\[NUMBER\]): .*$")

REQUIRED_SECTIONS_OLD = [
    "## Status",
    "## Context",
    "## Decision",
    "## Alternatives Considered",
    "## Trade-offs",
]

REQUIRED_SECTIONS_NEW = [
    "## 1. Context & Problem Statement",
    "## 2. Decision Drivers & Constraints",
    "## 3. Options Considered",
    "## 4. Decision Outcome",
    "## 5. Consequences & Trade-offs",
    "## 6. Implementation & Verification",
]


def run_git_command(args: list[str]) -> tuple[str, str]:
    """Helper to run a system git command safely."""
    try:
        res = subprocess.run(
            args,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=True,
            timeout=10,
        )
        return res.stdout.strip(), res.stderr.strip()
    except Exception as e:
        return "", str(e)


def is_architectural_file(filepath: str) -> bool:
    """
    Checks if a file path is considered an architectural change according to:
    - Dependencies configuration (pyproject.toml, package.json)
    - API Gateway modifications (apps/gateway/)
    - Security and UI shared packages (packages/security/, packages/ui/)
    - Database/storage model changes/migrations (apps/execution/database/, 'migrations', 'models')

    Ignores tests, docs, scripts, github workflows, markdown/txt files, etc.
    """
    # Exclude directories we don't treat as architectural logic
    if (
        filepath.startswith("tests/")
        or filepath.startswith("docs/")
        or filepath.startswith("scripts/")
        or filepath.startswith(".github/")
        or filepath.endswith(".md")
        or filepath.endswith(".txt")
        or filepath.endswith(".gitignore")
    ):
        return False

    # 1. Dependencies configuration
    if filepath in ("pyproject.toml", "package.json"):
        return True

    # 2. API Gateway and router modifications
    if filepath.startswith("apps/gateway/"):
        return True

    # 3. Active shared folders
    if filepath.startswith("packages/security/") or filepath.startswith("packages/ui/"):
        return True

    # 4. Storage model changes or migrations under execution
    if (
        filepath.startswith("apps/execution/database/")
        or "migrations" in filepath
        or "models" in filepath
    ):
        if filepath.startswith("apps/execution/"):
            return True

    return False


def get_closest_local_branch_point() -> str:
    """
    Dynamically calculates the closest local merge base across all local branches
    to find the exact branch point without network dependencies or hardcoded branch names.
    """
    # Get current branch name
    current_branch, _ = run_git_command(["git", "rev-parse", "--abbrev-ref", "HEAD"])
    current_branch = current_branch.strip()

    # Get HEAD commit hash
    head_sha, _ = run_git_command(["git", "rev-parse", "HEAD"])
    head_sha = head_sha.strip()

    # Get all local and remote branches available offline
    stdout, _ = run_git_command(["git", "branch", "-a", "--format=%(refname:short)"])
    local_branches = []
    if stdout:
        for line in stdout.splitlines():
            branch = line.strip()
            # Clean up potential "remotes/" prefix
            if branch.startswith("remotes/"):
                branch = branch[len("remotes/") :]

            # Skip empty, HEAD, origin placeholder, or remote HEAD reference
            if (
                not branch
                or branch == "HEAD"
                or branch == "origin"
                or branch == "origin/HEAD"
            ):
                continue

            # Ignore current branch or remote tracking of current branch
            if branch == current_branch or branch == f"origin/{current_branch}":
                continue

            if branch not in local_branches:
                local_branches.append(branch)

    closest_mb = None
    min_distance = None

    for branch in local_branches:
        mb, _ = run_git_command(["git", "merge-base", branch, "HEAD"])
        mb = mb.strip()
        if not mb:
            continue

        # If the merge base is HEAD itself, then HEAD is an ancestor of that branch,
        # which is not a parent branching point for the current commits.
        if mb == head_sha:
            continue

        # Get topological distance from merge base to HEAD
        dist_str, _ = run_git_command(["git", "rev-list", "--count", f"{mb}..HEAD"])
        try:
            distance = int(dist_str.strip())
            # We want the closest branching point, which corresponds to the minimum distance
            if min_distance is None or distance < min_distance:
                min_distance = distance
                closest_mb = mb
        except ValueError:
            continue

    if closest_mb:
        return closest_mb

    # Fallback if no other local branches or no valid merge base found:
    # use the root commit of the current repository
    root_commit, _ = run_git_command(["git", "rev-list", "--max-parents=0", "HEAD"])
    root_commit = root_commit.strip()
    if root_commit:
        return root_commit

    return "HEAD"


def get_changed_files() -> set[str]:
    """Retrieves list of changed files from local Git using dynamic branch point detection."""
    changed_files = set()

    # 1. Read from changed_files.txt (if generated by CI / previous step)
    if os.path.exists("changed_files.txt"):
        try:
            with open("changed_files.txt", "r") as f:
                for line in f:
                    if line.strip():
                        changed_files.add(line.strip())
        except Exception:
            pass

    # 2. Dynamically calculate the closest local branch point
    branch_point = get_closest_local_branch_point()

    # 3. Collect modified files from commit history from branch_point to HEAD
    # Following first-parent lineage bypasses parents of merge commits (Requirement 4)
    stdout, _ = run_git_command(
        ["git", "rev-list", "--first-parent", f"{branch_point}..HEAD"]
    )
    if stdout:
        for commit in stdout.splitlines():
            commit = commit.strip()
            if not commit:
                continue

            # Check if this commit is a merge commit (has more than 1 parent)
            parents_out, _ = run_git_command(
                ["git", "log", "--pretty=%P", "-n", "1", commit]
            )
            parents = parents_out.strip().split()
            if len(parents) > 1:
                # Bypass merge commits to prevent pulling in unrelated branch changes
                continue

            # Get files modified in this commit
            files_out, _ = run_git_command(
                ["git", "diff-tree", "--no-commit-id", "--name-only", "-r", commit]
            )
            if files_out:
                for line in files_out.splitlines():
                    file_path = line.strip()
                    if file_path:
                        changed_files.add(file_path)

    # 4. Include all untracked and uncommitted local workspace files (Requirement 3)
    stdout, _ = run_git_command(["git", "status", "--porcelain"])
    if stdout:
        for line in stdout.splitlines():
            if len(line) > 3:
                # Format: "M  path/to/file" or "?? path/to/file" or "R  old -> new"
                filepath = line[3:].strip()
                if " -> " in filepath:
                    filepath = filepath.split(" -> ")[1].strip()
                filepath = filepath.strip('"')
                if filepath:
                    changed_files.add(filepath)

    return changed_files


def check_architectural_changes_require_adr(changed_files: set[str]) -> bool:
    """
    Checks if there are architectural changes, and if so, verifies that
    a corresponding new ADR is added inside docs/adr/.
    """
    architectural_changes = [f for f in changed_files if is_architectural_file(f)]
    if not architectural_changes:
        return True

    # Check if a new ADR file is present in changed_files
    new_adr_added = False
    for f in changed_files:
        if f.startswith("docs/adr/") and FILENAME_PATTERN.match(os.path.basename(f)):
            # Ensure it is not deleted
            if os.path.exists(f):
                new_adr_added = True
                break

    if not new_adr_added:
        print(
            "Error: Architectural changes detected, but no corresponding ADR found in docs/adr/."
        )
        print("Architectural files changed:")
        for f in architectural_changes:
            print(f"  - {f}")
        print(
            "Please add a new ADR markdown file under docs/adr/ following the template."
        )
        return False

    return True


def validate_existing_adrs(targets: list[str] = None) -> bool:
    """Validates structure, filenames, and index alignment of existing ADRs."""
    if not os.path.isdir(ADR_DIR):
        print(f"Error: Directory {ADR_DIR} not found.")
        return False

    valid_reqs = compliance_utility.get_valid_requirements()

    try:
        with open(INDEX_FILE, "r") as f:
            index_content = f.read()
    except Exception as e:
        print(f"Error reading {INDEX_FILE}: {e}")
        return False

    all_passed = True

    if targets is not None:
        # Check only the target files passed as arguments
        for filepath in targets:
            filename = os.path.basename(filepath)
            if not filename.endswith(".md"):
                continue
            if FILENAME_PATTERN.match(filename):
                # Ensure it resides in docs/adr
                file_dir = os.path.dirname(os.path.normpath(filepath))
                expected_dir = os.path.normpath(ADR_DIR)
                if os.path.abspath(file_dir) != os.path.abspath(expected_dir):
                    print(
                        f"Error: ADR file '{filename}' found outside the proper directory ({file_dir}). Must reside in {ADR_DIR}."
                    )
                    all_passed = False
                    continue

            if filename in IGNORE_FILES:
                continue

            file_dir = os.path.dirname(os.path.normpath(filepath))
            expected_dir = os.path.normpath(ADR_DIR)
            if os.path.abspath(file_dir) != os.path.abspath(expected_dir):
                continue

            # 1. Check filename pattern
            if not FILENAME_PATTERN.match(filename):
                print(
                    f"Error: File '{filename}' does not follow the standard chronological date pattern (YYYY-MM-DD-...)."
                )
                all_passed = False

            # 2. Check if file is in index
            if f"({filename})" not in index_content:
                print(
                    f"Error: File '{filename}' is missing from the index log ({INDEX_FILE})."
                )
                all_passed = False

            # 3. Read file and check contents
            try:
                with open(filepath, "r") as f:
                    content = f.read()
            except Exception as e:
                print(f"Error reading {filepath}: {e}")
                all_passed = False
                continue

            lines = content.split("\n")

            # Check title
            is_new_format = False
            if lines and TITLE_PATTERN_NEW.match(lines[0]):
                is_new_format = True
            elif not lines or not TITLE_PATTERN_OLD.match(lines[0]):
                print(
                    f"Error: File '{filename}' title (first line) does not contain the correct format (old or new)."
                )
                all_passed = False

            # Check required sections
            missing_sections = []
            required_sections = (
                REQUIRED_SECTIONS_NEW if is_new_format else REQUIRED_SECTIONS_OLD
            )
            for section in required_sections:
                if section not in content:
                    missing_sections.append(section)

            if missing_sections:
                print(
                    f"Error: File '{filename}' is missing required sections: {', '.join(missing_sections)}"
                )
                all_passed = False

            # 4. Check compliance requirement mapping
            comp_ok, comp_err = compliance_utility.validate_adr_compliance(
                filename, content, valid_reqs
            )
            if not comp_ok:
                print(comp_err)
                all_passed = False

    else:
        # Check for ADRs outside the proper folder
        for root, _, files in os.walk("."):
            if ".git" in root or ".venv" in root or "node_modules" in root:
                continue
            for filename in files:
                if not filename.endswith(".md"):
                    continue
                if FILENAME_PATTERN.match(filename):
                    # Ensure it resides in docs/adr
                    expected_dir = os.path.join(".", "docs", "adr")
                    if os.path.abspath(root) != os.path.abspath(expected_dir):
                        print(
                            f"Error: ADR file '{filename}' found outside the proper directory ({root}). Must reside in {ADR_DIR}."
                        )
                        all_passed = False

        for filename in os.listdir(ADR_DIR):
            if not filename.endswith(".md"):
                continue
            if filename in IGNORE_FILES:
                continue

            filepath = os.path.join(ADR_DIR, filename)

            # 1. Check filename pattern
            if not FILENAME_PATTERN.match(filename):
                print(
                    f"Error: File '{filename}' does not follow the standard chronological date pattern (YYYY-MM-DD-...)."
                )
                all_passed = False

            # 2. Check if file is in index
            if f"({filename})" not in index_content:
                print(
                    f"Error: File '{filename}' is missing from the index log ({INDEX_FILE})."
                )
                all_passed = False

            # 3. Read file and check contents
            try:
                with open(filepath, "r") as f:
                    content = f.read()
            except Exception as e:
                print(f"Error reading {filepath}: {e}")
                all_passed = False
                continue

            lines = content.split("\n")

            # Check title
            is_new_format = False
            if lines and TITLE_PATTERN_NEW.match(lines[0]):
                is_new_format = True
            elif not lines or not TITLE_PATTERN_OLD.match(lines[0]):
                print(
                    f"Error: File '{filename}' title (first line) does not contain the correct format (old or new)."
                )
                all_passed = False

            # Check required sections
            missing_sections = []
            required_sections = (
                REQUIRED_SECTIONS_NEW if is_new_format else REQUIRED_SECTIONS_OLD
            )
            for section in required_sections:
                if section not in content:
                    missing_sections.append(section)

            if missing_sections:
                print(
                    f"Error: File '{filename}' is missing required sections: {', '.join(missing_sections)}"
                )
                all_passed = False

            # 4. Check compliance requirement mapping
            comp_ok, comp_err = compliance_utility.validate_adr_compliance(
                filename, content, valid_reqs
            )
            if not comp_ok:
                print(comp_err)
                all_passed = False

    return all_passed


def main():
    if len(sys.argv) > 1:
        # File paths provided as arguments
        targets = sys.argv[1:]
        existing_valid = validate_existing_adrs(targets)

        changed_files = set(targets)
        arch_valid = check_architectural_changes_require_adr(changed_files)
    else:
        # Fallback to scanning everything
        existing_valid = validate_existing_adrs()

        changed_files = get_changed_files()
        arch_valid = check_architectural_changes_require_adr(changed_files)

    if not existing_valid or not arch_valid:
        print("ADR validation failed.")
        sys.exit(1)

    print("All ADRs passed validation.")


if __name__ == "__main__":
    main()
