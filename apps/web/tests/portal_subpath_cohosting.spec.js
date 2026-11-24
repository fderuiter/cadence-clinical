import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { describe, expect, it } from "vitest";
import {
  assertThemeLinksExist,
  isCohostedSubpathLink,
} from "../../../docs/.vitepress/repoLinks.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "../../..");
const docsDir = path.join(repoRoot, "docs");

describe("GitHub Pages co-hosted subpath portal configuration (ADR-2550)", () => {
  it("recognizes declared co-hosted demo subpaths while rejecting missing docs routes", () => {
    expect(isCohostedSubpathLink("/demo/")).toBe(true);
    expect(isCohostedSubpathLink("/demo")).toBe(true);
    expect(isCohostedSubpathLink("/subject-portal/")).toBe(true);
    expect(isCohostedSubpathLink("/subject-portal/#/wallet")).toBe(true);
    expect(isCohostedSubpathLink("/DOCUMENTATION_INDEX")).toBe(false);

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            { text: "Home", link: "/" },
            { text: "Staff Demo", link: "/demo/", target: "_self" },
            {
              text: "Participant Demo",
              link: "/subject-portal/",
              target: "_self",
            },
            { text: "Doc Index", link: "/DOCUMENTATION_INDEX" },
          ],
          sidebar: [],
        },
        docsDir
      )
    ).not.toThrow();

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [{ text: "Broken", link: "/does-not-exist-anywhere" }],
          sidebar: [],
        },
        docsDir
      )
    ).toThrow(/does-not-exist-anywhere/);
  });

  it("authors a guided VitePress home page with Staff Demo, Participant Demo, Docs, and staged README actions", () => {
    const landingContent = fs.readFileSync(
      path.join(docsDir, "index.md"),
      "utf8"
    );
    expect(landingContent).toContain("layout: home");
    expect(landingContent).toContain('link: "/demo/"');
    expect(landingContent).toContain('link: "/subject-portal/"');
    expect(landingContent).toContain('link: "/DOCUMENTATION_INDEX"');
    expect(landingContent).toContain('link: "/README"');
    expect(landingContent).toContain('target: "_self"');
    expect(landingContent).toContain("CADENCE-101");
    expect(landingContent).not.toContain("DEMO-01");

    const adr2550 = fs.readFileSync(
      path.join(
        docsDir,
        "adr",
        "2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md"
      ),
      "utf8"
    );
    expect(adr2550).toContain("/studies/CADENCE-101");
    expect(adr2550).not.toContain("DEMO-01");
  });

  it("stages subpath demos in deploy-docs.yml before SHA256 checksums and SLSA provenance", () => {
    const workflow = fs.readFileSync(
      path.join(repoRoot, ".github", "workflows", "deploy-docs.yml"),
      "utf8"
    );
    const docsBuildIdx = workflow.indexOf("pnpm docs:build");
    const stageDemosIdx = workflow.indexOf("pnpm docs:stage-demos");
    const shaIdx = workflow.indexOf(
      "Generate Documentation Artifact SHA256 Checksums"
    );
    const slsaIdx = workflow.indexOf(
      "Generate SLSA Build Provenance for Documentation"
    );

    expect(docsBuildIdx).toBeGreaterThan(-1);
    expect(stageDemosIdx).toBeGreaterThan(docsBuildIdx);
    expect(shaIdx).toBeGreaterThan(stageDemosIdx);
    expect(slsaIdx).toBeGreaterThan(shaIdx);
  });
});
