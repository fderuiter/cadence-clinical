# Issue tracker: GitHub Issues + Local Markdown (.scratch/)

Issues and specs for this repo live in **two places** kept in sync:

- **GitHub Issues** — the canonical, shared record. All published tickets go here.
- **`.scratch/<feature>/issues/`** — local working markdown, used for drafting, blocked work, or offline sessions. Promote to GitHub when ready.

Use the `gh` CLI for all GitHub operations.

## GitHub Operations

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

## Local Markdown Operations (.scratch/)

Local tickets live under `.scratch/<feature>/issues/` as individual `.md` files named `<NNN>-<slug>.md`.

- **Create**: write a new file. Include a YAML front matter block:
  ```yaml
  ---
  title: "Short title"
  status: open          # open | in-progress | done | wontfix
  labels: []
  blocked_by: []        # list of local filenames or GH issue numbers
  gh_issue: null        # filled in when promoted to GitHub
  ---
  ```
- **Read**: open the file directly.
- **List**: `ls .scratch/<feature>/issues/` or `grep -r "status: open" .scratch/`.
- **Update status**: edit the `status` field in the front matter.
- **Promote to GitHub**: `gh issue create` with the file's title + body, then record the returned issue number in `gh_issue:` front matter.

## Dual-tracker workflow

| Situation | Action |
|---|---|
| Drafting tickets in a session | Write to `.scratch/<feature>/issues/` first |
| Ticket is fully specified and ready | Promote: `gh issue create`, record `gh_issue:` number in the file |
| Fetching a ticket for implementation | Check `gh_issue:` field; if set, use `gh issue view <n>` for the canonical copy |
| Offline / no GitHub access | Work from `.scratch/` entirely; promote in bulk when back online |

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

## When a skill says "publish to the issue tracker"

Create a GitHub issue (`gh issue create`) **and** write the corresponding `.scratch/<feature>/issues/<NNN>-<slug>.md` file with `gh_issue:` filled in.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`. If not yet promoted, read the local `.scratch/` file.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single GitHub issue with child issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`. Also maintain a mirror at `.scratch/wayfinder/<slug>/map.md`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues are not enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub native issue dependencies. Add an edge with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker numeric database id (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`). Where dependencies are not available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body.
- **Frontier query**: list the map open children, drop any with an open blocker or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer to the map Decisions-so-far.
