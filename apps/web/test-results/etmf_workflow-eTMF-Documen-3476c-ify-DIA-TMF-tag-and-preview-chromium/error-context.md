# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: etmf_workflow.spec.ts >> eTMF Document Management Workflow >> should successfully upload document and verify DIA TMF tag and preview
- Location: tests/e2e/etmf_workflow.spec.ts:57:3

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: locator('.secure-preview-panel')
Expected: visible
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" with timeout 5000ms
  - waiting for locator('.secure-preview-panel')

```

```yaml
- banner:
  - heading "Cadence Clinical" [level=1]
  - paragraph: Interactive Web Demo & Regulatory Validation Sandbox
  - text: 21 CFR Part 11 GAMP 5 IEC 62304 admin@cadence.clinical sponsor_admin, sponsor_designer, cra, data_manager, site_investigator, auditor Demo Mode
- complementary:
  - text: Showcase Modules
  - list:
    - listitem:
      - button "📋 MDR Protocol Designer"
    - listitem:
      - button "🩺 eCRF Form Engine"
    - listitem:
      - button "📊 CTMS Dashboard"
    - listitem:
      - button "⚙️ Rules Designer"
    - listitem:
      - button "🔒 Cryptographic Ledger"
    - listitem:
      - button "🔔 Notifications"
  - strong: "Offline Engine:"
  - text: All operations run securely inside your browser's VM using in-memory mock handlers and cryptographic APIs.
- main:
  - heading "Regulatory Auditor & Inspection Portal" [level=2]
  - paragraph: Unified compliance dashboard. Inspect immutable eTMF audit logs, verify real-time cryptographic execution ledger integrity, download watermarked evidence, and export validated regulatory binders.
  - text: "⚠️ Error: Failed to preview secure content: Failed to fetch GxP Execution Ledger Chain Verification"
  - button "Verify Now"
  - paragraph: Recomputes and validates the sequential Merkle-tree seals and blockchain-style chaining on clinical trial execution tables.
  - text: Ledger integrity status unknown. Click
  - strong: Verify Now
  - text: to execute GxP block verification. Regulatory Binder ZIP Export
  - paragraph: Compile and export an inspection-ready clinical archive containing all eTMF documents structurally organized by DIA TMF Zones and Sections.
  - text: Study Reference ID
  - textbox "e.g. study_001": study_001
  - checkbox "Include complete document version histories (audit files)"
  - text: Include complete document version histories (audit files)
  - button "Export Regulatory Binder (ZIP)"
  - text: Ingest New TMF Document
  - paragraph: Upload a document and index it with DIA TMF taxonomy tags into the secure study repository.
  - text: Select File
  - button "Choose File"
  - text: TMF Zone
  - combobox:
    - option "01. Trial Management" [selected]
    - option "02. Central Trial Documents"
    - option "05. Site Management"
  - text: TMF Section
  - textbox "e.g. 01.01 Trial Steering Committee": 01.01 Trial Steering Committee
  - button "Upload & Ingest Document" [disabled]
  - text: eTMF Document Directory & Viewer
  - button "Refresh List"
  - paragraph: Review indexed documents within the eTMF. Preview documents inline with browser watermarks or download fully audited, watermarked PDF/TXT copies.
  - table:
    - rowgroup:
      - row "ID Filename TMF Zone/Sec Artifact Type Status Ver. Actions":
        - columnheader "ID"
        - columnheader "Filename"
        - columnheader "TMF Zone/Sec"
        - columnheader "Artifact Type"
        - columnheader "Status"
        - columnheader "Ver."
        - columnheader "Actions"
    - rowgroup:
      - row "doc_d5ucl4bjw sample_protocol.pdf Zone 01. Trial Management / 01.01 Trial Steering Committee Informed Consent Form DRAFT v1 Preview Sign Download (Watermarked)":
        - cell "doc_d5ucl4bjw"
        - cell "sample_protocol.pdf"
        - cell "Zone 01. Trial Management / 01.01 Trial Steering Committee"
        - cell "Informed Consent Form"
        - cell "DRAFT"
        - cell "v1"
        - cell "Preview Sign Download (Watermarked)":
          - button "Preview"
          - button "Sign"
          - button "Download (Watermarked)"
  - text: Immutable eTMF Audit Ledger Trail
  - button "Refresh Logs"
  - paragraph: Complete, chronologically ordered Part 11 system log. View read actions (VIEW, LIST), ingestion audits, QC status transitions, and binder exports.
  - text: Actor ID
  - textbox "Filter by Actor"
  - text: Action Type
  - combobox:
    - option "All Actions" [selected]
    - option "INGEST (Ingest)"
    - option "VIEW (View Metadata)"
    - option "DOWNLOAD (Standard Download)"
    - option "WATERMARKED_DOWNLOAD (Auditor Download)"
    - option "LIST (Directory List)"
    - option "AUDIT_VIEW (Audit Trail Read)"
    - option "QC_TRANSITION (QC Lifecycle)"
    - option "BINDER_EXPORT (Binder Zip)"
    - option "COMPLETENESS (EDL Metrics)"
  - text: Document ID
  - textbox "Filter by Document ID"
  - button "Clear Filters"
  - button "Apply Filters"
  - table:
    - rowgroup:
      - row "UTC Timestamp Actor ID Actor Role Action Type Operation Details":
        - columnheader "UTC Timestamp"
        - columnheader "Actor ID"
        - columnheader "Actor Role"
        - columnheader "Action Type"
        - columnheader "Operation Details"
    - rowgroup:
      - 'row "2026-07-31 01:22:43 UTC demo_auditor Sponsor Admin INGEST Ingested document: sample_protocol.pdf under Zone: 01. Trial Management, Section: 01.01 Trial Steering Committee."':
        - cell "2026-07-31 01:22:43 UTC"
        - cell "demo_auditor"
        - cell "Sponsor Admin"
        - cell "INGEST"
        - 'cell "Ingested document: sample_protocol.pdf under Zone: 01. Trial Management, Section: 01.01 Trial Steering Committee."'
  - text: Showing records 1 to 1 of 1
  - button "Previous" [disabled]
  - button "Next" [disabled]
  - text: eTMF Completeness Tracking & Verification
  - button "Re-Verify"
  - paragraph: Perform live gap-analysis against the Expected Document List (EDL) to verify regulatory compliance of mandatory TMF artifacts for trial milestones.
  - text: Study ID
  - textbox "e.g. study_001": study_001
  - text: Milestone
  - combobox:
    - option "INITIATION (Study Start)" [selected]
    - option "CONDUCT (Data Collection)"
    - option "CLOSEOUT (Study Closed/Lock)"
  - text: Site ID (Optional)
  - textbox "e.g. site_001"
  - button "Run Completeness Analysis"
  - text: "⚠️ Error: Failed to fetch"
```

# Test source

```ts
  1   | import { test, expect } from "@playwright/test";
  2   | import { EtmfPage } from "./pages/EtmfPage";
  3   | import * as path from "path";
  4   | import * as fs from "fs";
  5   | import { fileURLToPath } from "url";
  6   |
  7   | const __filename = fileURLToPath(import.meta.url);
  8   | const __dirname = path.dirname(__filename);
  9   |
  10  | test.describe("eTMF Document Management Workflow", () => {
  11  |   test.use({ storageState: "playwright/.auth/user.json" });
  12  |
  13  |   test.beforeEach(async ({ page }) => {
  14  |     // Intercept signature verification endpoint
  15  |     await page.route("**/api/v1/auth/signature-verification", async (route) => {
  16  |       await route.fulfill({
  17  |         status: 200,
  18  |         contentType: "application/json",
  19  |         body: JSON.stringify({ sig_token: "mock-sig-token-123" }),
  20  |       });
  21  |     });
  22  |
  23  |     // Intercept document signing endpoint
  24  |     await page.route("**/api/v1/etmf/documents/**/sign-off", async (route) => {
  25  |       await route.fulfill({
  26  |         status: 200,
  27  |         contentType: "application/json",
  28  |         body: JSON.stringify({
  29  |           id: "doc_mock_signed",
  30  |           filename: "sample_protocol.pdf",
  31  |           zone: "01. Trial Management",
  32  |           section: "01.01 Trial Steering Committee",
  33  |           artifact_type: "Protocol",
  34  |           status: "SIGNED",
  35  |           version_index: 1.1,
  36  |           signature_manifestation: {
  37  |             signer_id: "admin@cadence.clinical",
  38  |             timestamp: new Date().toISOString(),
  39  |             signing_reason: "APPROVAL",
  40  |             sha256_hash: "mock-sha256-hash-xyz-789",
  41  |             signature: "mock-digital-signature-proof",
  42  |           },
  43  |         }),
  44  |       });
  45  |     });
  46  |
  47  |     // Intercept watermarked preview endpoint
  48  |     await page.route("**/watermarked**", async (route) => {
  49  |       await route.fulfill({
  50  |         status: 200,
  51  |         contentType: "text/plain",
  52  |         body: "Mock Audited PDF Content - Watermarked Preview is successfully active.",
  53  |       });
  54  |     });
  55  |   });
  56  |
  57  |   test("should successfully upload document and verify DIA TMF tag and preview", async ({ page }) => {
  58  |     const etmf = new EtmfPage(page);
  59  |     await etmf.goto();
  60  |
  61  |     // Create a temporary sample file to upload
  62  |     const tempFilePath = path.join(__dirname, "sample_protocol.pdf");
  63  |     fs.writeFileSync(tempFilePath, "Mock PDF Content");
  64  |
  65  |     try {
  66  |       await etmf.uploadDocument(tempFilePath, {
  67  |         zone: "01. Trial Management",
  68  |         section: "01.01 Trial Steering Committee",
  69  |       });
  70  |
  71  |       // Verify the document is added to the directory table
  72  |       await etmf.verifyDocumentInBinder("sample_protocol.pdf");
  73  |
  74  |       // Verify audit log event is recorded in the ledger table
  75  |       await etmf.assertAuditLogEventRecorded("INGEST", "sample_protocol.pdf");
  76  |
  77  |       // Test Scenario 1 PDF preview: Click preview button
  78  |       await page.click("button.btn-preview-doc");
  79  |       const previewPanel = page.locator(".secure-preview-panel");
> 80  |       await expect(previewPanel).toBeVisible();
      |                                  ^ Error: expect(locator).toBeVisible() failed
  81  |       await expect(previewPanel).toContainText("sample_protocol.pdf");
  82  |     } finally {
  83  |       // Clean up temp file
  84  |       if (fs.existsSync(tempFilePath)) {
  85  |         fs.unlinkSync(tempFilePath);
  86  |       }
  87  |     }
  88  |   });
  89  |
  90  |   test("should digitally sign document and verify version increment to v1.1", async ({ page }) => {
  91  |     const etmf = new EtmfPage(page);
  92  |     await etmf.goto();
  93  |
  94  |     // In demo mode, we should have the mock uploaded document or some documents. Let's upload a document first:
  95  |     const tempFilePath = path.join(__dirname, "sample_protocol.pdf");
  96  |     fs.writeFileSync(tempFilePath, "Mock PDF Content");
  97  |
  98  |     try {
  99  |       await etmf.uploadDocument(tempFilePath, {
  100 |         zone: "01. Trial Management",
  101 |         section: "01.01 Trial Steering Committee",
  102 |       });
  103 |
  104 |       // Find the Sign button for this document and click it
  105 |       const signBtn = page.locator("button.btn-sign-doc").first();
  106 |       await expect(signBtn).toBeVisible();
  107 |       await signBtn.click();
  108 |
  109 |       // Complete SignatureCaptureModal inputs
  110 |       const modal = page.locator("#signature-capture-modal");
  111 |       await expect(modal).toBeVisible();
  112 |
  113 |       await page.fill("#sig-username", "admin@cadence.clinical");
  114 |       await page.fill("#sig-password", "admin_password");
  115 |       await page.selectOption("#sig-reason", "APPROVAL");
  116 |
  117 |       // Click sign button
  118 |       await page.click("#btn-confirm-sig");
  119 |
  120 |       // Verify version incremented to v1.1 and status is SIGNED
  121 |       const versionCell = page.locator(".clinical-table tbody tr").first().locator("td").nth(5);
  122 |       await expect(versionCell).toContainText("v1.1");
  123 |
  124 |       const statusCell = page.locator(".clinical-table tbody tr").first().locator("td").nth(4);
  125 |       await expect(statusCell).toContainText("SIGNED");
  126 |     } finally {
  127 |       if (fs.existsSync(tempFilePath)) {
  128 |         fs.unlinkSync(tempFilePath);
  129 |       }
  130 |     }
  131 |   });
  132 | });
  133 |
```