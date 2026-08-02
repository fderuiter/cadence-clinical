import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";

describe("CSS Assets and Package Exports", () => {
  const uiDir = path.resolve(__dirname, "..");

  it("should have correct exports in package.json", () => {
    const pkgPath = path.join(uiDir, "package.json");
    const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf-8"));

    expect(pkg.exports).toBeDefined();
    expect(pkg.exports["."]).toBe("./index.js");
    expect(pkg.exports["./tokens.css"]).toBe("./tokens.css");
    expect(pkg.exports["./responsive.css"]).toBe("./responsive.css");
    expect(pkg.exports["./package.json"]).toBe("./package.json");
  });

  it("should have tokens.css with correct root custom properties", () => {
    const tokensPath = path.join(uiDir, "tokens.css");
    expect(fs.existsSync(tokensPath)).toBe(true);

    const content = fs.readFileSync(tokensPath, "utf-8");
    expect(content).toContain(":root");
    expect(content).toContain("--color-primary");
    expect(content).toContain("--color-accent");
    expect(content).toContain("--color-success");
    expect(content).toContain("--color-warning");
    expect(content).toContain("--color-error");
    expect(content).toContain("--color-surface");
    expect(content).toContain("--color-text");
    expect(content).toContain("--spacing-md");
    expect(content).toContain("--font-size-base");
    expect(content).toContain("--breakpoint-tablet");
    expect(content).toContain("--touch-target-min");
  });

  it("should have responsive.css with all required utility classes", () => {
    const responsivePath = path.join(uiDir, "responsive.css");
    expect(fs.existsSync(responsivePath)).toBe(true);

    const content = fs.readFileSync(responsivePath, "utf-8");
    expect(content).toContain(".responsive-grid");
    expect(content).toContain(".grid-layout-responsive");
    expect(content).toContain(".grid-2-responsive");
    expect(content).toContain(".container");
    expect(content).toContain(".touch-target");
    expect(content).toContain(".scrollable-table-wrapper");
  });
});
