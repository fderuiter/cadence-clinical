import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, expect, it } from "vitest";
import * as protocolImportModule from "../src/api/kernel/protocolImport";

const repoRoot = path.resolve(__dirname, "../../..");

function countNonEmptyLines(relPath: string): number {
  const fullPath = path.join(repoRoot, relPath);
  const content = fs.readFileSync(fullPath, "utf-8");
  return content.split(/\r?\n/).filter((line) => line.trim().length > 0).length;
}

function countTotalLines(relPath: string): number {
  const fullPath = path.join(repoRoot, relPath);
  const content = fs.readFileSync(fullPath, "utf-8");
  const lines = content.split(/\r?\n/);
  return lines[lines.length - 1] === "" ? lines.length - 1 : lines.length;
}

function listVueFilesUnder(relDir: string): string[] {
  const absDir = path.join(repoRoot, relDir);
  if (!fs.existsSync(absDir)) return [];
  const results: string[] = [];
  for (const entry of fs.readdirSync(absDir, { withFileTypes: true })) {
    const childRel = path.posix.join(relDir, entry.name);
    if (entry.isDirectory()) {
      results.push(...listVueFilesUnder(childRel));
    } else if (entry.isFile() && entry.name.endsWith(".vue")) {
      results.push(childRel);
    }
  }
  return results;
}

/**
 * Simulates a Git three-way merge across a mini index snapshot of files in a directory:
 * Branch A edits `fileA`, Branch B edits `fileB`, and `git merge-file` verifies every
 * file in the directory merges cleanly with zero conflict markers.
 */
function assertDisjointModuleThreeWayMergeClean(
  relFileA: string,
  editA: (src: string) => string,
  relFileB: string,
  editB: (src: string) => string,
  sharedIndexFile?: string
): void {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "cadence-merge-check-"));
  try {
    const candidates = new Set([relFileA, relFileB]);
    if (sharedIndexFile) candidates.add(sharedIndexFile);

    for (const rel of candidates) {
      const baseText = fs.readFileSync(path.join(repoRoot, rel), "utf-8");
      const branchAText = rel === relFileA ? editA(baseText) : baseText;
      const branchBText = rel === relFileB ? editB(baseText) : baseText;

      const basePath = path.join(tmpDir, "base.txt");
      const oursPath = path.join(tmpDir, "ours.txt");
      const theirsPath = path.join(tmpDir, "theirs.txt");
      fs.writeFileSync(basePath, baseText, "utf-8");
      fs.writeFileSync(oursPath, branchAText, "utf-8");
      fs.writeFileSync(theirsPath, branchBText, "utf-8");

      execFileSync("git", ["merge-file", oursPath, basePath, theirsPath], {
        stdio: "pipe",
      });
      const merged = fs.readFileSync(oursPath, "utf-8");
      expect(merged).not.toContain("<<<<<<<");
      expect(merged).not.toContain(">>>>>>>");
    }
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

describe("Modular ESLint rules, Kernel slice transports, and live panel decomposition", () => {
  it("keeps eslint.config.mjs, protocolImport.ts, all 10 target panels, and extracted child Vue panels below 500 non-empty lines", () => {
    expect(countNonEmptyLines("eslint.config.mjs")).toBeLessThan(500);
    expect(
      countTotalLines("apps/web/src/api/kernel/protocolImport.ts")
    ).toBeLessThan(250);
    expect(
      countNonEmptyLines("apps/web/src/api/kernel/protocolImport.ts")
    ).toBeLessThan(500);

    const parentVueTargets = [
      "apps/web/src/components/ContextualHelpDrawer.vue",
      "apps/web/src/components/AppShell.vue",
      "apps/web/src/components/clinical/ProtocolTrainingConsole.vue",
      "apps/web/src/components/clinical/BulkIpKitReconciliationDrawer.vue",
      "apps/web/src/components/canvas/CanvasInspector.vue",
      "apps/web/src/components/canvas/LocksSection.vue",
      "apps/web/src/components/canvas/ParticipantFormsPanel.vue",
      "apps/web/src/components/etmf/DocumentGrid.vue",
      "apps/subject-portal/src/views/WalletView.vue",
      "apps/web/src/views/ObservationView.vue",
    ];

    for (const rel of parentVueTargets) {
      expect(
        countNonEmptyLines(rel),
        `${rel} must be < 500 non-empty lines`
      ).toBeLessThan(500);
    }

    const extractedVueFiles = [
      ...listVueFilesUnder("apps/web/src/components/help"),
      ...listVueFilesUnder("apps/web/src/components/shell"),
      ...listVueFilesUnder("apps/web/src/components/clinical/training"),
      ...listVueFilesUnder(
        "apps/web/src/components/clinical/ip-reconciliation"
      ),
      ...listVueFilesUnder("apps/web/src/components/etmf/document-grid"),
      ...listVueFilesUnder("apps/web/src/components/observation"),
      ...listVueFilesUnder("apps/subject-portal/src/components/wallet"),
      "apps/web/src/components/canvas/CasebookSummaryBar.vue",
      "apps/web/src/components/canvas/InspectorParticipantDetails.vue",
      "apps/web/src/components/canvas/InspectorRecordDetails.vue",
      "apps/web/src/components/canvas/InspectorSimulatePanel.vue",
      "apps/web/src/components/canvas/LocksApplyForm.vue",
      "apps/web/src/components/canvas/LocksQuickReasonModal.vue",
      "apps/web/src/components/canvas/ParticipantFormsListsPanel.vue",
    ];

    expect(extractedVueFiles.length).toBeGreaterThanOrEqual(20);
    for (const rel of extractedVueFiles) {
      expect(
        countNonEmptyLines(rel),
        `extracted child ${rel} must be < 500 non-empty lines`
      ).toBeLessThan(500);
      expect(rel.endsWith("Drawer.vue")).toBe(false);
    }
  });

  it("marks generated canvas contracts as linguist-generated in .gitattributes without merge=union drivers", () => {
    const gitattributes = fs.readFileSync(
      path.join(repoRoot, ".gitattributes"),
      "utf-8"
    );
    const activeRules = gitattributes
      .split(/\r?\n/)
      .filter((line) => !line.trim().startsWith("#"))
      .join("\n");
    expect(activeRules).toContain(
      "apps/web/src/canvas/generated/*.ts linguist-generated=true"
    );
    expect(activeRules).not.toContain("merge=union");
  });

  it("co-locates lazy <slice>.transport.ts modules across all 8 Kernel slices while preserving protocolImport exports", () => {
    const slices = [
      "studies",
      "navigation",
      "observations",
      "proposals",
      "locks",
      "validation",
      "workQueues",
      "entitlements",
    ];
    for (const slice of slices) {
      const transportPath = path.join(
        repoRoot,
        `apps/web/src/api/kernel/${slice}.transport.ts`
      );
      const facadePath = path.join(
        repoRoot,
        `apps/web/src/api/kernel/${slice}.ts`
      );
      expect(fs.existsSync(transportPath)).toBe(true);
      expect(fs.readFileSync(facadePath, "utf-8")).toContain(
        `import("./${slice}.transport")`
      );
    }

    expect(typeof protocolImportModule.executeProtocolDraftImport).toBe(
      "function"
    );
    expect(typeof protocolImportModule.executeListProtocolTemplates).toBe(
      "function"
    );
    expect(typeof protocolImportModule.executeGetProtocolDesignSummary).toBe(
      "function"
    );
    expect(typeof protocolImportModule.executeAmendmentImpact).toBe("function");
    expect(typeof protocolImportModule.executeSimulate).toBe("function");
    expect(typeof protocolImportModule.executePreviewStudyRulePack).toBe(
      "function"
    );
  });

  it("passes three-way git merge-file checks for concurrent ESLint rule, allowlist shard, and Kernel slice edits", () => {
    assertDisjointModuleThreeWayMergeClean(
      "scripts/eslint/rules/no-inline-hex.mjs",
      (src) => `${src}\n// concurrent rule edit A\n`,
      "scripts/eslint/rules/require-tabular-nums.mjs",
      (src) => `${src}\n// concurrent rule edit B\n`,
      "eslint.config.mjs"
    );

    assertDisjointModuleThreeWayMergeClean(
      "scripts/eslint/allowlists/imaging.mjs",
      (src) => src.replace('"views/ImagingView.vue",\n', ""),
      "scripts/eslint/allowlists/auditor-and-quality.mjs",
      (src) => src.replace('"views/RulesView.vue",\n', ""),
      "scripts/eslint/allowlists/index.mjs"
    );

    assertDisjointModuleThreeWayMergeClean(
      "apps/web/src/api/kernel/studies.transport.ts",
      (src) => `${src}\nexport const _concurrentStudiesSentinel = true;\n`,
      "apps/web/src/api/kernel/locks.transport.ts",
      (src) => `${src}\nexport const _concurrentLocksSentinel = true;\n`,
      "apps/web/src/api/kernel/protocolImport.ts"
    );
  });
});
