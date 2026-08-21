# 09: HLS Adaptive Streaming in Media Player

**What to build:** Upgrade the `<video>` element in `MediaPreviewModal.vue` (T-07) to play HLS adaptive bitrate streams using HLS.js. If the HLS manifest is ready (`transcode_status == READY`), the player loads it via HLS.js and adapts quality to available bandwidth. If transcoding is still in progress, the player shows a "Processing video…" state with a progress indicator and polls for readiness. The playback audit events introduced in T-08 (`PLAY_START`, `PLAY_25`, etc.) are fired from the player to the `fileshare` audit endpoint.

**Blocked by:** 07 — Media Player Vue Component AND 08 — Video Transcoding Pipeline.

**Status:** ready-for-agent

- [ ] `hls.js` added as a frontend dependency (`pnpm add hls.js`).
- [ ] `MediaPreviewModal.vue` video section updated: calls `GET /api/v1/fileshare/files/{file_record_id}/stream-url` on open; if `202 Accepted` (transcoding pending), renders a "Processing video — check back soon" placeholder with a spinner; if `200 OK`, initialises `Hls` instance on the `<video>` ref.
- [ ] Quality selector overlay rendered on the video: user can manually pin to 360p / 720p / 1080p / Auto. Implemented by switching `Hls.currentLevel` on change.
- [ ] Playback audit hooks: `Hls` `MEDIA_PLAYING` event → `PLAY_START`; `timeupdate` listener tracking 25/50/75% thresholds → corresponding audit events; `ended` event → `PLAY_END`. All calls to `POST /api/v1/fileshare/audit/playback` with current `session_id` (UUID generated on modal open).
- [ ] For native HLS support (Safari), falls back to `<video src="{manifestUrl}">` directly without HLS.js (Safari parses HLS natively).
- [ ] If `mime_type` is NOT a video type, the existing PDF/image/audio branches are unchanged.
- [ ] Polling for `transcode_status`: if `202` returned, frontend polls `stream-url` every 10 seconds (max 10 attempts), then shows a "Video processing is taking longer than expected — try again later" message.
- [ ] Component tests updated to cover HLS initialisation branch and polling behaviour using a mock API.
- [ ] `pnpm run build` passes with zero errors after dependency addition.
