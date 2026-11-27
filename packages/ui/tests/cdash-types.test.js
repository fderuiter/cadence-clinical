import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const TYPE_FILE = path.resolve(__dirname, "../src/types/cdash.ts");
const GENERATED_VARIABLES_FILE = path.resolve(
  __dirname,
  "../src/generated/cdashVariables.ts"
);

describe("CDASH Generated TS Types", () => {
  it("keeps static CDASH interfaces file and generates the variable union file", () => {
    expect(fs.existsSync(TYPE_FILE)).toBe(true);
    expect(fs.existsSync(GENERATED_VARIABLES_FILE)).toBe(true);
  });

  it("contains static CDASHModelVariable, CDASHIGVariable, and CdashField interfaces and re-exports CdashVariableCode", () => {
    const content = fs.readFileSync(TYPE_FILE, "utf8");
    expect(content).toContain("export interface CDASHModelVariable");
    expect(content).toContain('"Version": string;');
    expect(content).toContain("export interface CDASHIGVariable");
    expect(content).toContain("export type { CdashVariableCode }");
    expect(content).toContain("export interface CdashField");
  });

  it("contains correct variable names from the CDASH Model and IG schemas in cdashVariables.ts", () => {
    const content = fs.readFileSync(GENERATED_VARIABLES_FILE, "utf8");
    expect(content).toContain("export type CdashVariableCode =");
    // Verify prominent variables are present in the generated CdashVariableCode union
    expect(content).toContain('"--YN"');
    expect(content).toContain('"--TRT"');
    expect(content).toContain('"STUDYID"');
    expect(content).toContain('"SITEID"');
    expect(content).toContain('"SUBJID"');
  });
});
