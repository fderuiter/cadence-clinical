import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, it, expect } from "vitest";
import {
  NODE_KINDS,
  formatNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "../../src/canvas/nodeRef";
import {
  PARTICIPANT_STATES,
  STAGES,
  findSection,
  findStage,
} from "../../src/canvas/lifecycle";

const repoRoot = join(__dirname, "..", "..", "..", "..");
const ID = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e";

describe("NodeRef mirror (ADR-2481)", () => {
  it("registers exactly the kinds NodeKinds.cs declares, in its order", () => {
    const source = readFileSync(
      join(
        repoRoot,
        "packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs"
      ),
      "utf8"
    );
    const declared = [
      ...source.matchAll(/public const string \w+ = "([a-z_]+)";/g),
    ].map(([, kind]) => kind);

    expect(declared.length).toBeGreaterThan(0);
    expect([...NODE_KINDS]).toEqual(declared);
  });

  it("parses the canonical forms with and without a version", () => {
    expect(parseNodeRef(`participant:${ID}`)).toEqual({
      kind: "participant",
      id: ID,
    });
    expect(parseNodeRef(`study_site:${ID}@3`)).toEqual({
      kind: "study_site",
      id: ID,
      version: 3,
    });
  });

  it.each([
    ["an unregistered kind", `subject:${ID}`],
    ["no kind", `:${ID}`],
    ["no separator", ID],
    ["an upper-case id", `participant:${ID.toUpperCase()}`],
    ["a braced id", `participant:{${ID}}`],
    ["an unhyphenated id", `participant:${ID.replaceAll("-", "")}`],
    ["the empty id", "participant:00000000-0000-0000-0000-000000000000"],
    ["version zero", `participant:${ID}@0`],
    ["a leading zero", `participant:${ID}@07`],
    ["a negative version", `participant:${ID}@-1`],
    ["an empty version", `participant:${ID}@`],
    ["an unsafe version", `participant:${ID}@99999999999999999999`],
    ["a non-string", 42],
  ])("rejects %s", (_, text) => {
    expect(parseNodeRef(text)).toBeNull();
  });

  it("formats to the string it parses from, and refuses a non-canonical reference", () => {
    const text = `participant:${ID}@12`;
    expect(formatNodeRef(parseNodeRef(text)!)).toBe(text);
    expect(() =>
      formatNodeRef({ kind: "participant", id: ID.toUpperCase() })
    ).toThrow(/canonical/);
  });

  it("builds a reference from an API id only when it is a canonical UUID", () => {
    expect(tryNodeRef("participant", ID)).toEqual({
      kind: "participant",
      id: ID,
    });
    expect(tryNodeRef("participant", "SUBJ-001")).toBeNull();
    expect(tryNodeRef("participant", undefined)).toBeNull();
  });
});

describe("Canvas lifecycle", () => {
  it("names the six lifecycle stages in order (CLINICAL_CANVAS.md §2.2)", () => {
    expect(STAGES.map((s) => s.label)).toEqual([
      "Design",
      "Start",
      "Conduct",
      "Data",
      "Analyze",
      "Submit",
    ]);
  });

  it("finds a section's stage and ignores unknown ids", () => {
    expect(findSection("participants")?.stage.id).toBe("conduct");
    expect(findSection("site-activation")?.stage.id).toBe("start");
    expect(findSection("dashboards")).toBeUndefined();
    expect(findStage("data")?.label).toBe("Data");
    expect(findStage("edc")).toBeUndefined();
  });

  it("lists every participant state Execution reports, and no other", () => {
    const codec = readFileSync(
      join(repoRoot, "apps/execution/src/Domain/Subjects/SubjectStateCodec.cs"),
      "utf8"
    );
    const codes = [...codec.matchAll(/\["([A-Z_]+)"\] = SubjectState\./g)].map(
      ([, code]) => code
    );

    expect(codes.length).toBeGreaterThan(0);
    expect(PARTICIPANT_STATES.map((s) => s.code).sort()).toEqual(codes.sort());
  });
});
