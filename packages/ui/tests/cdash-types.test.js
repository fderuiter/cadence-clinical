import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const TYPE_FILE = path.resolve(__dirname, "../src/types/cdash.ts");

describe("CDASH Generated TS Types", () => {
  it("generates the types file", () => {
    expect(fs.existsSync(TYPE_FILE)).toBe(true);
  });

  it("contains CDASHModelVariable and CDASHIGVariable interfaces", () => {
    const content = fs.readFileSync(TYPE_FILE, "utf8");
    expect(content).toContain("export interface CDASHModelVariable");
    expect(content).toContain("export interface CDASHIGVariable");
    expect(content).toContain("export type CdashVariableCode");
    expect(content).toContain("export interface CdashField");
  });

  it("contains correct variable names from the CDASH Model and IG schemas", () => {
    const content = fs.readFileSync(TYPE_FILE, "utf8");
    // Verify some prominent variables are present in the CdashVariableCode union
    expect(content).toContain('"--YN"');
    expect(content).toContain('"--TRT"');
    expect(content).toContain('"STUDYID"');
    expect(content).toContain('"SITEID"');
    expect(content).toContain('"SUBJID"');
  });
});
