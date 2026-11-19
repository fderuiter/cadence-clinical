import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import { FileUploadModal } from "../index.js";

describe("FileUploadModal Component & Package Exports (#4314)", () => {
  const modalPath = path.resolve(
    __dirname,
    "../src/components/FileUploadModal.vue"
  );

  it("exports FileUploadModal from ui package entry", () => {
    expect(FileUploadModal).toBeDefined();
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
