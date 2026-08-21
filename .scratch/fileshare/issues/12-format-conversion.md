# 12: Auto Format Conversion (Word / Excel / PowerPoint → PDF)

**What to build:** When a DOCX, XLSX, or PPTX file is uploaded and passes virus scanning, `fileshare` automatically generates a PDF rendition in the background using LibreOffice headless. The original file and the PDF rendition are both stored; previews and protected-view always use the PDF rendition. This ensures consistent in-browser previewing without requiring the viewer to have Office installed, and supports the watermarking that the `MediaPreviewModal` applies to PDFs.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold. Can run in parallel with 04, 05, 06, and 11.

**Status:** ready-for-agent

- [ ] `FileRecord` gains fields: `pdf_rendition_key` (nullable str — object store key of the converted PDF), `conversion_status` (enum: `NOT_REQUIRED` / `PENDING` / `READY` / `FAILED`), `conversion_error` (nullable str).
- [ ] Alembic migration adds the new columns.
- [ ] LibreOffice headless container added to `docker/docker-compose.yml` (or run inside the `fileshare-transcoder` container from T-08 to consolidate workers).
- [ ] `ConversionPort` protocol defined: `async def convert_to_pdf(source_key: str) -> str` (returns the object store key of the generated PDF).
- [ ] `LibreOfficeConversionAdapter` implements `ConversionPort`: downloads source from object store, runs `libreoffice --headless --convert-to pdf`, uploads the output PDF, returns its key.
- [ ] After a successful virus scan (`scan_status == CLEAN`), if `mime_type` is `application/vnd.openxmlformats-officedocument.*` or `application/msword` / `application/vnd.ms-*`, set `conversion_status = PENDING` and enqueue a conversion task.
- [ ] On completion: `pdf_rendition_key` populated, `conversion_status = READY`.
- [ ] On failure: `conversion_status = FAILED`, `conversion_error` set. Notification to uploader. File still accessible via original format download (if caller has `download` permission).
- [ ] Download and preview endpoints: if `pdf_rendition_key` is set and `conversion_status == READY`, the presigned URL returned points to the PDF rendition. If not yet ready, returns the original object key with a `X-Rendition: original` header.
- [ ] `MediaPreviewModal.vue` checks for a `pdf_rendition_key` in the file metadata and uses it automatically — no frontend logic changes needed beyond consuming the URL.
- [ ] Integration test converts a minimal DOCX fixture to PDF and verifies the rendition key is set.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
