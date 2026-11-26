import * as fs from "fs";
import * as path from "path";

export interface SuiteResult {
  suiteName: string;
  persona: string;
  passed: boolean;
  durationMs: number;
  videoFileName?: string;
  consoleErrors: string[];
  pageErrors: string[];
}

export interface VerificationReportData {
  runName: string;
  timestamp: string;
  suites: SuiteResult[];
  overallStatus: "PASSED" | "FAILED";
  totalDurationMs: number;
}

export function generateHtmlVerificationReport(
  data: VerificationReportData,
  outputDir: string
): string {
  const suitesHtml = data.suites
    .map(
      (s) => `
    <div class="suite-card ${s.passed ? "passed" : "failed"}">
      <div class="suite-header">
        <div class="suite-title">
          <span class="status-icon">${s.passed ? "PASS" : "FAIL"}</span>
          <h3>${s.suiteName}</h3>
        </div>
        <div class="suite-badge">${s.persona}</div>
      </div>
      <div class="suite-body">
        <div class="meta-row">
          <span>Duration: <strong>${(s.durationMs / 1000).toFixed(2)}s</strong></span>
          <span>Triple Sentinel: <strong>${s.passed ? "VERIFIED (0 errors)" : "FAILED"}</strong></span>
        </div>
        ${
          s.videoFileName
            ? `
        <div class="video-container">
          <video controls preload="metadata">
            <source src="./${s.videoFileName}" type="video/webm">
            Your browser does not support the video tag.
          </video>
        </div>`
            : `<p class="no-video">No video artifact recorded</p>`
        }
      </div>
    </div>
  `
    )
    .join("\n");

  const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Cadence Clinical — Triple Sentinel UI Verification Report</title>
  <style>
    :root {
      --bg: #0f172a;
      --surface: #1e293b;
      --border: #334155;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --primary: #0284c7;
      --success: #10b981;
      --error: #ef4444;
    }
    body {
      margin: 0;
      padding: 32px;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      background: var(--bg);
      color: var(--text);
    }
    .header {
      border-bottom: 1px solid var(--border);
      padding-bottom: 24px;
      margin-bottom: 32px;
    }
    .badge-bar {
      display: flex;
      gap: 12px;
      margin-top: 12px;
    }
    .status-badge {
      background: ${data.overallStatus === "PASSED" ? "var(--success)" : "var(--error)"};
      color: white;
      padding: 6px 14px;
      border-radius: 9999px;
      font-weight: bold;
      font-size: 0.85rem;
      text-transform: uppercase;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(420px, 1fr));
      gap: 24px;
    }
    .suite-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
    .suite-header {
      padding: 16px 20px;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid var(--border);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .suite-title {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .suite-title h3 {
      margin: 0;
      font-size: 1.1rem;
    }
    .suite-badge {
      font-size: 0.75rem;
      background: rgba(2, 132, 199, 0.2);
      color: #38bdf8;
      padding: 4px 10px;
      border-radius: 6px;
      font-weight: 600;
    }
    .suite-body {
      padding: 20px;
    }
    .meta-row {
      display: flex;
      justify-content: space-between;
      font-size: 0.85rem;
      color: var(--text-muted);
      margin-bottom: 16px;
    }
    .video-container {
      background: #000;
      border-radius: 8px;
      overflow: hidden;
      aspect-ratio: 16/9;
    }
    video {
      width: 100%;
      height: 100%;
      display: block;
    }
  </style>
</head>
<body>
  <div class="header">
    <h1>Cadence Clinical — Triple Sentinel UI Verification Report</h1>
    <p style="color: var(--text-muted);">Run Identifier: <code>${data.runName}</code> | Executed: ${data.timestamp}</p>
    <div class="badge-bar">
      <div class="status-badge">${data.overallStatus}</div>
      <div style="color: var(--text-muted); align-self: center;">${data.suites.length} Persona Suites Completed</div>
    </div>
  </div>
  <div class="grid">
    ${suitesHtml}
  </div>
</body>
</html>`;

  const reportPath = path.join(outputDir, "verification_report.html");
  fs.writeFileSync(reportPath, html, "utf-8");
  return reportPath;
}
