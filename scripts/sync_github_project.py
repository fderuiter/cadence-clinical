#!/usr/bin/env python3
"""
Cadence Clinical — Automated GitHub Project & Issue Sync Tool

Automates GitHub Project 17 ('Cadence-Clinical') synchronization using native GitHub
GraphQL relationships (blockedBy, blocking, parent, subIssues), body formatting with native
tasklists, dynamic label synchronization, priority classification, and developer readiness mapping.

Usage:
    python3 scripts/sync_github_project.py
    pnpm project:sync
"""

import json
import re
import subprocess
import sys
import time

PROJECT_NUMBER = 17
OWNER = "fderuiter"
PROJECT_ID = "PVT_kwHOB5yjmM4Beuvn"

# Field IDs
STATUS_FIELD_ID = "PVTSSF_lAHOB5yjmM4BeuvnzhZGxXA"
PRIORITY_FIELD_ID = "PVTSSF_lAHOB5yjmM4BeuvnzhZGxaM"
SIZE_FIELD_ID = "PVTSSF_lAHOB5yjmM4BeuvnzhZGxaQ"

# Option IDs
STATUS_OPTIONS = {
    "Backlog": "f75ad846",
    "Ready": "e18bf179",
    "In progress": "47fc9ee4",
    "In review": "aba860b9",
    "Done": "98236657",
}

PRIORITY_OPTIONS = {"P0": "79628723", "P1": "0a877460", "P2": "da944a9c"}

SIZE_OPTIONS = {
    "XS": "911790be",
    "S": "b277fb01",
    "M": "86db8eb3",
    "L": "853c8207",
    "XL": "2d0801e2",
}


def run_cmd(args):
    res = subprocess.run(args, capture_output=True, text=True)
    if res.returncode != 0:
        print(
            f"Command failed: {' '.join(args)}\nError: {res.stderr.strip()}",
            file=sys.stderr,
        )
        return None
    return res.stdout


def run_gql(query, variables=None):
    cmd = ["gh", "api", "graphql", "-f", f"query={query}"]
    if variables:
        for k, v in variables.items():
            cmd.extend(["-F", f"{k}={v}"])
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"GraphQL Query failed: {res.stderr.strip()}", file=sys.stderr)
        return None
    try:
        return json.loads(res.stdout)
    except Exception as e:
        print(f"Failed to parse GraphQL output: {e}", file=sys.stderr)
        return None


def fetch_all_issues_gql():
    cursor = None
    all_nodes = []
    while True:
        c_str = f', after: "{cursor}"' if cursor else ""
        query = f"""
query {{
  repository(owner: "{OWNER}", name: "cadence-clinical") {{
    issues(first: 100{c_str}) {{
      nodes {{
        id
        databaseId
        number
        title
        state
        url
        body
        labels(first: 50) {{ nodes {{ name }} }}
        milestone {{ number title description }}
        parent {{ id number title state }}
        subIssues(first: 100) {{ nodes {{ id number title state }} }}
        blockedBy(first: 100) {{ nodes {{ id number title state }} }}
        blocking(first: 100) {{ nodes {{ id number title state }} }}
      }}
      pageInfo {{ hasNextPage endCursor }}
    }}
  }}
}}
"""
        data = run_gql(query)
        if not data or "data" not in data:
            break
        res = data["data"]["repository"]["issues"]
        all_nodes.extend(res["nodes"])
        if not res["pageInfo"]["hasNextPage"]:
            break
        cursor = res["pageInfo"]["endCursor"]
    return {n["number"]: n for n in all_nodes}


def add_blocked_by(issue_id, blocking_issue_id):
    mutation = """
mutation($issueId: ID!, $blockingIssueId: ID!) {
  addBlockedBy(input: {issueId: $issueId, blockingIssueId: $blockingIssueId}) {
    issue { id }
  }
}
"""
    return run_gql(
        mutation, {"issueId": issue_id, "blockingIssueId": blocking_issue_id}
    )


def remove_blocked_by(issue_id, blocking_issue_id):
    mutation = """
mutation($issueId: ID!, $blockingIssueId: ID!) {
  removeBlockedBy(input: {issueId: $issueId, blockingIssueId: $blockingIssueId}) {
    issue { id }
  }
}
"""
    return run_gql(
        mutation, {"issueId": issue_id, "blockingIssueId": blocking_issue_id}
    )


def add_sub_issue(parent_issue_id, sub_issue_id):
    mutation = """
mutation($issueId: ID!, $subIssueId: ID!) {
  addSubIssue(input: {issueId: $issueId, subIssueId: $subIssueId}) {
    issue { id }
  }
}
"""
    return run_gql(mutation, {"issueId": parent_issue_id, "subIssueId": sub_issue_id})


def assign_work_stream(i):
    title = i["title"].lower()
    body = (i.get("body") or "").lower()
    labels = [lbl["name"].lower() for lbl in i.get("labels", {}).get("nodes", [])]

    if any(
        k in title or k in body for k in ["etmf", "isf", "document", "archiving", "tmf"]
    ):
        return (
            "Stream 1: eTMF & Regulated Document Management",
            "Document Management & Archiving",
        )
    elif any(
        k in title or k in body
        for k in ["rtsm", "irt", "dispensation", "randomization", "supply", "blinded"]
    ):
        return "Stream 2: RTSM & IP Supply Chain", "RTSM / IRT v1.0"
    elif any(
        k in title or k in body
        for k in ["ecrf", "soa", "mdr", "protocol", "designer", "form", "crf"]
    ):
        return "Stream 3: Study Designer, eCRF & SoA", "Ultimate CRF Builder"
    elif any(
        k in title or k in body
        for k in ["ecoa", "epro", "subject portal", "patient", "econsent"]
    ):
        return (
            "Stream 4: eCOA, ePRO & Subject Portal",
            "Native eCOA/ePRO Subject Portal v1.6.0",
        )
    elif "scope: frontend" in labels or any(
        k in title or k in body for k in ["spa", "vue", "ui", "widget", "component"]
    ):
        return "Stream 5: Frontend Vue 3 SPA", "Frontend SPA Completion"
    elif any(
        k in title or k in body
        for k in [
            "rbac",
            "security",
            "keycloak",
            "audit",
            "aes",
            "crypto",
            "auth",
            "ticket",
        ]
    ):
        return (
            "Stream 6: Platform Security, RBAC & Audit Ledger",
            "Compliance & Security Controls",
        )
    elif any(
        k in title or k in body
        for k in ["sdtm", "adam", "dataset", "export", "biostat", "cdisc"]
    ):
        return (
            "Stream 7: Biostatistics & Dataset Exports",
            "Biostatistical Export Pipeline v1.7.0 Completion",
        )
    elif any(
        k in title or k in body
        for k in ["sdv", "lab", "monitoring", "site", "query", "sae", "reconciliation"]
    ):
        return "Stream 8: Clinical Operations, SDV & Lab Ranges", "SDV/TSDV Hardening"
    else:
        return (
            "Stream 6: Platform Security, RBAC & Audit Ledger",
            "Compliance & Security Controls",
        )


def clean_and_format_body(i, epics, issue_by_num):
    num = i["number"]
    body = (i.get("body") or "").strip()
    is_epic = num in epics or i["title"].startswith("EPIC:")

    lines = body.split("\n")
    cleaned_lines = []

    for line in lines:
        line_str = line.strip()
        if any(
            k in line_str
            for k in [
                "BLOCKED",
                "READY FOR DEV",
                "PARENT EPIC",
                "Developer Readiness",
                "Work Stream",
                "Requirements Traceability",
                "Target Modules",
                "🔴",
                "🟢",
                "🔵",
            ]
        ):
            continue
        cleaned_lines.append(line)

    body_core = "\n".join(cleaned_lines).strip()
    body_core = re.sub(r"^(---|\s+|\n)+", "", body_core).strip()

    body_core = re.sub(
        r"## 📋 Definition of Done \(DoD\) Checklist.*",
        "",
        body_core,
        flags=re.DOTALL,
    ).strip()
    body_core = re.sub(
        r"### 🔗 Prerequisites / Dependencies.*",
        "",
        body_core,
        flags=re.DOTALL,
    ).strip()
    body_core = re.sub(
        r"### 📋 Child Issues / Sub-Tasks.*",
        "",
        body_core,
        flags=re.DOTALL,
    ).strip()
    body_core = re.sub(r"\n---\n*$", "", body_core).strip()

    stream_name, default_ms = assign_work_stream(i)
    current_ms = i["milestone"]["title"] if i.get("milestone") else default_ms

    req_ids = sorted(list(set(re.findall(r"(PRD-[A-Z0-9-]+|Trace-\d+|ADR-\d+)", body))))
    req_str = (
        ", ".join(req_ids) if req_ids else "PRD-SYS-001 (Core Platform Compliance)"
    )

    file_targets = sorted(
        list(
            set(
                re.findall(
                    r"`(apps/[^`,\s]+|packages/[^`,\s]+|docs/[^`,\s]+|tests/[^`,\s]+)`",
                    body,
                )
            )
        )
    )
    files_str = ", ".join(file_targets[:4]) if file_targets else "See issue body specs"
    if len(file_targets) > 4:
        files_str += f" (+{len(file_targets) - 4} more)"

    metadata_block = f"""| **Work Stream**: `{stream_name}` | **Milestone**: `{current_ms}`

> 🔒 **Requirements Traceability**: `{req_str}` | GxP 21 CFR Part 11 Regulated
> 📁 **Target Modules / Files**: `{files_str}`"""

    tasklist_section = ""

    # Native subIssues for epics
    native_sub_issues = i.get("subIssues", {}).get("nodes", [])
    if is_epic and native_sub_issues:
        items = []
        sorted_subs = sorted(native_sub_issues, key=lambda x: x["number"])
        for c in sorted_subs:
            c_num = c["number"]
            c_title = c["title"]
            c_state = c["state"]
            box = "[x]" if c_state == "CLOSED" else "[ ]"
            items.append(f"- {box} #{c_num} — {c_title}")
        tasklist_section = "\n\n### 📋 Child Issues / Sub-Tasks\n" + "\n".join(items)

    # Native blockedBy for non-epics
    native_blocked_by = i.get("blockedBy", {}).get("nodes", [])
    if not is_epic and native_blocked_by:
        items = []
        sorted_prereqs = sorted(native_blocked_by, key=lambda x: x["number"])
        for p in sorted_prereqs:
            p_num = p["number"]
            p_title = p["title"]
            p_state = p["state"]
            box = "[x]" if p_state == "CLOSED" else "[ ]"
            items.append(f"- {box} #{p_num} — {p_title}")
        tasklist_section = "\n\n### 🔗 Prerequisites / Dependencies\n" + "\n".join(
            items
        )

    dod_block = """## 📋 Definition of Done (DoD) Checklist
- [ ] Implementation complete across target file paths.
- [ ] Unit & integration tests added/updated in `tests/` (`uv run pytest`).
- [ ] Code formatted and typed cleanly (`uv run ruff check .`).
- [ ] GxP audit fields preserved/updated (`created_by`, `reason_for_change`, versioning) if models modified.
- [ ] Traceability docs or ADR updated if architectural/contract changes introduced."""

    formatted_body = f"""{metadata_block}

---

{body_core}{tasklist_section}

---

{dod_block}"""

    was_changed = body.strip() != formatted_body.strip()
    return formatted_body, was_changed


def main():
    print(
        "=== Cadence Clinical — Native GitHub Relationship & Project Sync ===",
        flush=True,
    )

    # 1. Fetch all repo issues via GraphQL
    print(
        "1. Fetching all repository issues and native relationships via GraphQL...",
        flush=True,
    )
    issue_by_num = fetch_all_issues_gql()
    if not issue_by_num:
        print("Failed to fetch repository issues.", file=sys.stderr)
        sys.exit(1)

    print(f"Fetched {len(issue_by_num)} total issues.", flush=True)

    epics = set()
    for num, i in issue_by_num.items():
        labels = [lbl["name"] for lbl in i.get("labels", {}).get("nodes", [])]
        if "Parent" in labels or i["title"].startswith("EPIC:"):
            epics.add(num)

    # 2. Smart Migration: Parse explicit body declarations and ensure native GraphQL relations exist
    print(
        "2. Auditing explicit body text declarations and migrating to native GraphQL relationships...",
        flush=True,
    )
    relations_added = 0

    for num, i in issue_by_num.items():
        if i["state"] != "OPEN":
            continue

        body = i.get("body") or ""
        i_id = i["id"]

        # Parse explicit "Blocked by:", "Depends on:", "Native blockers:"
        explicit_prereqs = set()
        for line in body.splitlines():
            l_str = line.strip()
            for pattern in [
                r"Blocked by:\s*([^\.\n]+)",
                r"Depends on:\s*([^\.\n]+)",
                r"Native blockers:\s*([^\.\n]+)",
            ]:
                m = re.search(pattern, l_str, re.IGNORECASE)
                if m:
                    for ref in re.findall(r"#(\d+)", m.group(1)):
                        ref_num = int(ref)
                        if ref_num in issue_by_num and ref_num != num:
                            explicit_prereqs.add(ref_num)

        # Sync native blockedBy
        existing_blocked_by = {
            b["number"] for b in i.get("blockedBy", {}).get("nodes", [])
        }
        for p_num in explicit_prereqs:
            if p_num not in existing_blocked_by:
                p_issue = issue_by_num[p_num]
                print(
                    f"Adding native relationship: #{num} blockedBy #{p_num}",
                    flush=True,
                )
                add_blocked_by(i_id, p_issue["id"])
                relations_added += 1

        # Parse explicit "Parent: #X" or "Epic: #X"
        parent_match = re.search(
            r"(?:Parent(?: coordination issue)?|Epic):\s*#(\d+)", body, re.IGNORECASE
        )
        if parent_match:
            parent_num = int(parent_match.group(1))
            current_parent = i.get("parent")
            if parent_num in issue_by_num and (
                not current_parent or current_parent["number"] != parent_num
            ):
                parent_issue = issue_by_num[parent_num]
                print(
                    f"Adding native parent relationship: #{parent_num} subIssue #{num}",
                    flush=True,
                )
                add_sub_issue(parent_issue["id"], i_id)
                relations_added += 1

    if relations_added > 0:
        print(
            f"Added {relations_added} native relationships. Re-fetching issues...",
            flush=True,
        )
        issue_by_num = fetch_all_issues_gql()

    # 3. Synchronize Issue Labels and Descriptions using Native Relationships
    print(
        "3. Synchronizing issue labels & descriptions using native relationship states...",
        flush=True,
    )
    open_issues = [i for i in issue_by_num.values() if i["state"] == "OPEN"]
    formatted_count = 0
    unblocked_count = 0
    blocked_count = 0
    tmp_file = "/tmp/cadence_issue_body.md"

    for i in open_issues:
        num = i["number"]
        new_body, was_changed = clean_and_format_body(i, epics, issue_by_num)

        native_blocked_by = i.get("blockedBy", {}).get("nodes", [])
        open_native_blockers = [b for b in native_blocked_by if b["state"] == "OPEN"]

        current_labels = [lbl["name"] for lbl in i.get("labels", {}).get("nodes", [])]

        labels_to_remove = [
            lbl for lbl in ["blocked", "status: blocked"] if lbl in current_labels
        ]
        labels_to_add = [
            lbl for lbl in ["blocked", "status: blocked"] if lbl not in current_labels
        ]

        if open_native_blockers and labels_to_add:
            for lbl in labels_to_add:
                run_cmd(["gh", "issue", "edit", str(num), "--add-label", lbl])
            blocked_count += 1
        elif not open_native_blockers and labels_to_remove:
            for lbl in labels_to_remove:
                run_cmd(["gh", "issue", "edit", str(num), "--remove-label", lbl])
            unblocked_count += 1

        if was_changed:
            with open(tmp_file, "w") as f:
                f.write(new_body)
            run_cmd(["gh", "issue", "edit", str(num), "--body-file", tmp_file])
            formatted_count += 1
            time.sleep(0.05)

    print(
        f"Reformatted {formatted_count} issues. Dynamically unblocked {unblocked_count} issues. Added blocked labels to {blocked_count} issues.",
        flush=True,
    )

    # 4. Fetch Project 17 Items
    print("4. Fetching items from GitHub Project 17...", flush=True)
    raw_project = run_cmd(
        [
            "gh",
            "project",
            "item-list",
            str(PROJECT_NUMBER),
            "--owner",
            OWNER,
            "--format",
            "json",
            "--limit",
            "1000",
        ]
    )
    if not raw_project:
        print("Warning: Could not fetch GitHub Project items.", file=sys.stderr)
        return

    project_data = json.loads(raw_project)
    items = project_data.get("items", [])

    item_by_issue_num = {}
    for item in items:
        if item.get("content", {}).get("type") == "Issue":
            num = item["content"].get("number")
            if num:
                item_by_issue_num[num] = item

    # 5. Add missing issues to Project Board
    missing_issues = [i for i in open_issues if i["number"] not in item_by_issue_num]
    if missing_issues:
        print(
            f"Adding {len(missing_issues)} missing issues to Project Board...",
            flush=True,
        )
        for i in missing_issues:
            run_cmd(
                [
                    "gh",
                    "project",
                    "item-add",
                    str(PROJECT_NUMBER),
                    "--owner",
                    OWNER,
                    "--url",
                    i["url"],
                ]
            )
            time.sleep(0.05)
        # Refresh items
        raw_project = run_cmd(
            [
                "gh",
                "project",
                "item-list",
                str(PROJECT_NUMBER),
                "--owner",
                OWNER,
                "--format",
                "json",
                "--limit",
                "1000",
            ]
        )
        project_data = json.loads(raw_project)
        items = project_data.get("items", [])
        for item in items:
            if item.get("content", {}).get("type") == "Issue":
                num = item["content"].get("number")
                if num:
                    item_by_issue_num[num] = item

    # 6. Sync Project Board fields
    print(
        f"5. Synchronizing fields for {len(item_by_issue_num)} project items...",
        flush=True,
    )
    for idx, (num, item) in enumerate(item_by_issue_num.items(), 1):
        item_id = item["id"]
        issue = issue_by_num.get(num)
        if not issue:
            continue

        labels = [
            lbl["name"].lower() for lbl in issue.get("labels", {}).get("nodes", [])
        ]
        body = issue.get("body") or ""
        state = issue["state"]

        native_blocked_by = issue.get("blockedBy", {}).get("nodes", [])
        open_native_blockers = [b for b in native_blocked_by if b["state"] == "OPEN"]

        # Status logic
        if state == "CLOSED":
            target_status = "Done"
        elif "jules" in labels:
            target_status = "In progress"
        elif open_native_blockers or num in epics:
            target_status = "Backlog"
        else:
            target_status = "Ready"

        # Priority logic
        if "priority: high" in labels or "p0" in labels or "critical" in labels:
            target_priority = "P0"
        elif "priority: medium" in labels or "p1" in labels:
            target_priority = "P1"
        else:
            target_priority = "P2"

        # Size logic
        if num in epics:
            target_size = "XL"
        elif (
            len(re.findall(r"`(apps/[^`]+|packages/[^`]+)`", body)) >= 5
            or "architecture" in labels
        ):
            target_size = "L"
        elif len(re.findall(r"`(apps/[^`]+|packages/[^`]+)`", body)) >= 2:
            target_size = "M"
        elif "scope: frontend" in labels or "type: bug" in labels:
            target_size = "S"
        else:
            target_size = "M"

        if item.get("status") != target_status:
            run_cmd(
                [
                    "gh",
                    "project",
                    "item-edit",
                    "--id",
                    item_id,
                    "--project-id",
                    PROJECT_ID,
                    "--field-id",
                    STATUS_FIELD_ID,
                    "--single-select-option-id",
                    STATUS_OPTIONS[target_status],
                ]
            )
        if item.get("priority") != target_priority:
            run_cmd(
                [
                    "gh",
                    "project",
                    "item-edit",
                    "--id",
                    item_id,
                    "--project-id",
                    PROJECT_ID,
                    "--field-id",
                    PRIORITY_FIELD_ID,
                    "--single-select-option-id",
                    PRIORITY_OPTIONS[target_priority],
                ]
            )
        if item.get("size") != target_size:
            run_cmd(
                [
                    "gh",
                    "project",
                    "item-edit",
                    "--id",
                    item_id,
                    "--project-id",
                    PROJECT_ID,
                    "--field-id",
                    SIZE_FIELD_ID,
                    "--single-select-option-id",
                    SIZE_OPTIONS[target_size],
                ]
            )

        time.sleep(0.02)

    print(
        "✅ Standardized Project Board & Native GitHub Relationship Sync Complete!",
        flush=True,
    )


if __name__ == "__main__":
    main()
