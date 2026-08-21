# 10: Documents Hub UI

**What to build:** A new top-level workspace, `DocumentsHubView.vue`, giving every persona a unified, cross-study view of all documents they have access to — either by role or by share grant. It mirrors the SharePoint experience: a left-panel folder tree (studies → sites → zones → folders), a main document grid with sorting, filtering, and search, a share button on every row, and a slide-out share management drawer to view and revoke grants. A study-contextual variant (embedded as a tab inside a study detail page) reuses the same components with a pre-filtered scope.

**Blocked by:** 04 — Internal Share Grants, 05 — External Guest Links, 06 — Multipart Presigned Upload, 09 — HLS Adaptive Streaming.

**Status:** ready-for-agent

- [ ] `apps/web/src/views/DocumentsHubView.vue` created. Registered in the router at `/documents` (requires authentication).
- [ ] Left panel: folder tree component showing Studies → Sites → TMF Zones / eISF Sections → Folders. Clicking a node filters the main grid. Lazy-loads children on expand. Reuses the `TmfBinderTree` pattern but extended to cover cross-study scope.
- [ ] Main grid: columns — Name, Type (icon), Study, Site, Uploaded By, Last Modified, Status, Shared (badge showing active grant count). Sortable by any column. Supports multi-select for bulk share operations.
- [ ] Search bar: filters by filename, study name, and uploader. Calls `GET /api/v1/fileshare/files?q=&study_id=&site_id=&page=` with server-side pagination.
- [ ] Share button (per row and in bulk): opens `ShareDrawer.vue` — a slide-in panel with:
  - Internal share: user search autocomplete, permission level selector (view / comment / download / upload-revision / reshare / approve / expire-revoke), optional expiry toggle.
  - External link: generates guest link (calls T-05 endpoint), displays the URL with a "Copy Link" button and expiry date.
  - Active grants list: table of who has access, at what level, expiring when, with a "Revoke" action per row.
- [ ] `MediaPreviewModal.vue` (T-07 / T-09) used for inline previews on row click.
- [ ] Persona switcher in the top bar (already in the platform) correctly restricts which studies/sites appear in the folder tree based on the active role.
- [ ] Study-contextual variant: `StudyDocumentsTab.vue` wraps `DocumentsHubView` with `studyId` prop pre-set and the folder tree collapsed to that study's scope. Embedded in the existing study detail page.
- [ ] Navigation: "Documents" entry added to the main sidebar nav (icon: folder). Route guard requires any authenticated role.
- [ ] All styling uses semantic CSS variables — no Tailwind classes. Full-width layout for the hub workspace.
- [ ] Vitest / Vue Test Utils tests cover the share drawer open/close, grant creation, and revocation flows with mocked API calls.
