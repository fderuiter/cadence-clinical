import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import { MediaPlayer, FileUploadModal } from "../src/index.js";

describe("MediaPlayer Component & Package Exports (#4314)", () => {
  const componentPath = path.resolve(__dirname, "../src/components/MediaPlayer.vue");
  const modalPath = path.resolve(__dirname, "../src/components/FileUploadModal.vue");

  it("exports MediaPlayer and FileUploadModal from ui package entry", () => {
    expect(MediaPlayer).toBeDefined();
    expect(FileUploadModal).toBeDefined();
  });

  it("MediaPlayer component file exists and defines all required clinical media modes", () => {
    expect(fs.existsSync(componentPath)).toBe(true);
    const code = fs.readFileSync(componentPath, "utf-8");

    // 1. PDF Document Viewer requirements
    expect(code).toContain("pdf-viewer-container");
    expect(code).toContain("pdf-frame");
    expect(code).toContain("pdf-canvas");
    expect(code).toContain("pdfTotalPages");
    expect(code).toContain("prevPdfPage");
    expect(code).toContain("nextPdfPage");

    // 2. Image Viewer Pan & Zoom requirements
    expect(code).toContain("image-viewer-container");
    expect(code).toContain("pannable-image");
    expect(code).toContain("zoomLevel");
    expect(code).toContain("zoomIn");
    expect(code).toContain("zoomOut");
    expect(code).toContain("rotateClockwise");
    expect(code).toContain("resetTransform");
    expect(code).toContain("handleMouseDown");
    expect(code).toContain("handleMouseMove");

    // 3. Audio Waveform Preview requirements
    expect(code).toContain("audio-viewer-container");
    expect(code).toContain("waveform-bars");
    expect(code).toContain("waveform-bar");
    expect(code).toContain("audio-controls-bar");
    expect(code).toContain("timeline-slider");
    expect(code).toContain("formatTime");

    // 4. Direct Video Playback (MP4/WebM) requirements
    expect(code).toContain("video-viewer-container");
    expect(code).toContain("video-element");
    expect(code).toContain("speed-selector");
    expect(code).toContain("setPlaybackRate");

    // 5. GxP Watermarking Overlay requirements (21 CFR Part 11)
    expect(code).toContain("watermark-overlay");
    expect(code).toContain("watermarkText");
    expect(code).toContain("isWatermarked");
    expect(code).toContain("CONFIDENTIAL - CLINICAL TRIAL REVIEW ONLY");
  });

  it("MediaPlayer defines accessible ARIA semantics and clinical design tokens", () => {
    const code = fs.readFileSync(componentPath, "utf-8");
    expect(code).toContain('role="region"');
    expect(code).toContain(":aria-label=\"title\"");
    expect(code).toContain('aria-label="Zoom Out"');
    expect(code).toContain('aria-label="Zoom In"');
    expect(code).toContain('aria-label="Rotate 90 Degrees"');
    expect(code).toContain('aria-label="Audio playback seek slider"');
    expect(code).toContain('aria-label="Video playback seek slider"');
    expect(code).toContain("var(--color-primary");
    expect(code).toContain("var(--color-surface");
    expect(code).toContain("var(--color-border");
  });

  it("FileUploadModal component file exists and satisfies 21 CFR Part 11 checksum requirements", () => {
    expect(fs.existsSync(modalPath)).toBe(true);
    const code = fs.readFileSync(modalPath, "utf-8");

    // Form inputs and drag-drop dropzone
    expect(code).toContain("dropzone");
    expect(code).toContain("onDragOver");
    expect(code).toContain("onDrop");
    expect(code).toContain("handleSelectedFile");

    // SHA-256 calculation via Web Crypto API
    expect(code).toContain('crypto.subtle.digest("SHA-256"');
    expect(code).toContain("calculatedSha256");

    // Reason for Change validation (GxP mandate)
    expect(code).toContain("Reason for Change (21 CFR Part 11 Mandate)");
    expect(code).toContain("reasonForChange.value.trim().length < 5");

    // Multipart vs Singlepart threshold
    expect(code).toContain("multipartThresholdBytes");
    expect(code).toContain("is_multipart");
    expect(code).toContain("parts_count");
    expect(code).toContain("startUpload");
  });
});
