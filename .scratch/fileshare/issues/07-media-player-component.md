# 07: Media Player Vue Component (PDF, Image, Audio, Direct Video)

**What to build:** A unified `MediaPreviewModal.vue` component that replaces the existing `PdfPreviewModal.vue` and adds support for images, audio, and direct-URL video playback (MP4/WebM). The component renders in a protected view — a watermark overlay is shown on the file, right-click is disabled, and the system keyboard shortcut for saving is intercepted. Every open/close event is logged to the audit trail. This component is used by both authenticated internal users and unauthenticated external guests (T-05's `/share/:token` route).

**Blocked by:** 06 — S3 Multipart Presigned Client-Side Upload (so that presigned download URLs are available).

**Status:** ready-for-agent

- [ ] `apps/web/src/components/fileshare/MediaPreviewModal.vue` created. Accepts props: `fileRecord` (id, filename, mimeType, downloadUrl, watermarkText), `permissions` (object: `canDownload`, `canComment`), `isGuestView` (boolean).
- [ ] PDF rendering: iframe or PDF.js canvas inside the modal. Watermark text overlaid via absolutely-positioned, pointer-events-none `<div>` styled with opacity and diagonal rotation using CSS variables from `style.css`.
- [ ] Image rendering: `<img>` tag with watermark overlay. Supports JPEG, PNG, TIFF (TIFF via Canvas API conversion if needed).
- [ ] Audio rendering: styled HTML5 `<audio>` element with custom controls (play/pause, scrub bar, time display). No waveform visualisation required for Phase 1.
- [ ] Direct video rendering: HTML5 `<video>` element with controls, responsive sizing. Plays MP4 and WebM from a presigned URL. The HLS upgrade (T-09) will replace this element's source logic; this ticket lays the skeleton.
- [ ] Protected view enforcement: `@contextmenu.prevent` on the modal container; `document.addEventListener('keydown')` intercepts Ctrl+S / Cmd+S and Ctrl+P / Cmd+P.
- [ ] Download button rendered only when `canDownload` is true; triggers a presigned download URL fetch rather than linking directly (prevents link-sharing of the raw object URL).
- [ ] On modal open: frontend calls `POST /api/v1/fileshare/audit/access` with `{file_record_id, event: 'VIEW_OPEN', source: 'internal'|'guest_link'}`. On modal close: calls with `event: 'VIEW_CLOSE'`.
- [ ] `PdfPreviewModal.vue` replaced with an import alias pointing to `MediaPreviewModal.vue`. `DocumentManagerView.vue` and any other consumers updated.
- [ ] All styling uses semantic CSS variables (`var(--surface)`, `var(--border)`, `var(--radius-md)`) — no Tailwind classes.
- [ ] Component tested with Vitest / Vue Test Utils for correct prop-driven rendering and protected-view behaviour.
