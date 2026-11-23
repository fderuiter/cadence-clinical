import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  ClinicalIcon,
  CLINICAL_ICON_MAP,
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  normalizeClinicalIconSize,
  renderClinicalIconSvg,
} from "../index.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../../..");

function walkSourceFiles(dir, exts, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (["node_modules", "dist", "coverage", ".git"].includes(entry.name)) {
      continue;
    }
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walkSourceFiles(fullPath, exts, out);
    } else if (exts.includes(path.extname(entry.name))) {
      out.push(fullPath);
    }
  }
  return out;
}

describe("ClinicalIcon & renderClinicalIconSvg Primitives", () => {
  it("locks ClinicalIcon sizes to 16px or 20px and stroke width to 1.75px", () => {
    expect(CLINICAL_ICON_SIZES).toEqual([16, 20]);
    expect(CLINICAL_ICON_STROKE_WIDTH).toBe(1.75);
    expect(normalizeClinicalIconSize(16)).toBe(16);
    expect(normalizeClinicalIconSize("16")).toBe(16);
    expect(normalizeClinicalIconSize(12)).toBe(16);
    expect(normalizeClinicalIconSize(20)).toBe(20);
    expect(normalizeClinicalIconSize("20")).toBe(20);
    expect(normalizeClinicalIconSize(24)).toBe(20);
    expect(normalizeClinicalIconSize(undefined)).toBe(16);
  });

  it("exposes curated semantic icon names mapped to Lucide components", () => {
    expect(CLINICAL_ICON_NAMES.length).toBeGreaterThanOrEqual(80);
    for (const key of [
      "alert-triangle",
      "check-circle",
      "clipboard-list",
      "file-lock",
      "hospital",
      "lock",
      "pen-tool",
      "refresh-cw",
      "search",
      "shield-check",
      "siren",
      "sparkles",
      "stethoscope",
      "timer",
      "x",
    ]) {
      expect(CLINICAL_ICON_MAP[key]).toBeDefined();
    }
  });

  it("renders decorative ClinicalIcon SVGs with aria-hidden=true by default and role=img when labeled", async () => {
    const decorativeHtml = await renderToString(
      createSSRApp({
        render: () => h(ClinicalIcon, { name: "shield-check", size: 20 }),
      })
    );
    expect(decorativeHtml).toContain("<svg");
    expect(decorativeHtml).toContain('width="20"');
    expect(decorativeHtml).toContain('height="20"');
    expect(decorativeHtml).toContain('stroke-width="1.75"');
    expect(decorativeHtml).toContain('aria-hidden="true"');
    expect(decorativeHtml).not.toContain('role="img"');

    const labeledHtml = await renderToString(
      createSSRApp({
        render: () =>
          h(ClinicalIcon, {
            name: "siren",
            size: 14,
            label: "SLA breach alert",
          }),
      })
    );
    expect(labeledHtml).toContain('width="16"');
    expect(labeledHtml).toContain('height="16"');
    expect(labeledHtml).toContain('role="img"');
    expect(labeledHtml).toContain('aria-label="SLA breach alert"');
    expect(labeledHtml).not.toContain('aria-hidden="true"');

    const fallbackHtml = await renderToString(
      createSSRApp({
        render: () => h(ClinicalIcon, { name: "unknown-nonexistent-icon" }),
      })
    );
    expect(fallbackHtml).toContain("<svg");
    expect(fallbackHtml).toContain('aria-hidden="true"');
  });

  it("renders pure HTML vector SVG strings via renderClinicalIconSvg with escaped attributes", () => {
    const svg = renderClinicalIconSvg("check-circle", {
      size: 20,
      className: "status-svg",
    });
    expect(svg).toContain("<svg");
    expect(svg).toContain('width="20"');
    expect(svg).toContain('height="20"');
    expect(svg).toContain('stroke-width="1.75"');
    expect(svg).toContain('aria-hidden="true"');
    expect(svg).toContain("clinical-icon");
    expect(svg).toContain("status-svg");

    const labeledSvg = renderClinicalIconSvg("unknown-icon", {
      size: 16,
      label: 'Alert <"critical">&',
    });
    expect(labeledSvg).toContain('role="img"');
    expect(labeledSvg).toContain(
      'aria-label="Alert &lt;&quot;critical&quot;&gt;&amp;"'
    );
    expect(labeledSvg).not.toContain('aria-hidden="true"');
  });

  it("maps every semantic icon literal used across packages/ui, apps/web, and apps/subject-portal in CLINICAL_ICON_MAP", () => {
    const sourceFiles = [
      ...walkSourceFiles(path.join(repoRoot, "packages/ui/src"), [
        ".vue",
        ".js",
        ".ts",
      ]),
      ...walkSourceFiles(path.join(repoRoot, "apps/web/src"), [
        ".vue",
        ".js",
        ".ts",
      ]),
      ...walkSourceFiles(path.join(repoRoot, "apps/subject-portal/src"), [
        ".vue",
        ".js",
        ".ts",
      ]),
    ];

    const missing = [];
    for (const filePath of sourceFiles) {
      const content = fs.readFileSync(filePath, "utf-8");
      for (const match of content.matchAll(
        /<ClinicalIcon\b[^>]*?\sname="([^"]+)"/g
      )) {
        const iconName = match[1].trim().toLowerCase();
        if (!(iconName in CLINICAL_ICON_MAP)) {
          missing.push(`${path.relative(repoRoot, filePath)}: ${iconName}`);
        }
      }
      for (const match of content.matchAll(
        /renderClinicalIconSvg\(\s*["']([^"']+)["']/g
      )) {
        const iconName = match[1].trim().toLowerCase();
        if (!(iconName in CLINICAL_ICON_MAP)) {
          missing.push(`${path.relative(repoRoot, filePath)}: ${iconName}`);
        }
      }
    }

    expect(missing).toEqual([]);
  });
});
