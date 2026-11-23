import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it, expect } from "vitest";
import {
  CANVAS_GENERATED_DIR,
  generateCanvasContracts,
  parsePermissionMatrix,
  parseRoleNormalization,
  renderCanvasContractFiles,
} from "../../scripts/generate-canvas-contracts.js";
import * as authority from "../../src/canvas/authority";
import {
  BACKEND_ROLE_ALIASES,
  WEB_PERSONA_ROLE_ALIASES,
  canActivateSite,
  canAnswerQueries,
  canAssignTrainingCurriculum,
  canCertifyGreenlight,
  canCompleteMonitoringVisits,
  canCorrectObservations,
  canExportEtmf,
  canGovernProtocols,
  canManageDoa,
  canManageKernelStudySites,
  canManageMilestones,
  canManageMonitoringVisits,
  canManageParticipants,
  canManageQueries,
  canManageRandomizationSchemes,
  canManageRulePacks,
  canManageSiteMilestones,
  canReadEtmf,
  canReadRandomizationSchemes,
  canReconcileIpKits,
  canRecordChainOfCustody,
  canReviewEtmfQc,
  canScheduleMonitoringVisits,
  canSeedIpKits,
  canSignForms,
  canSignOffDoa,
  canSignOffMonitoringVisit,
  canSignOffMonitoringVisits,
  canSignTrainingDelegations,
  canStageEtmfQc,
  canTagEtmf,
  canUnblindParticipant,
  canVerifySource,
  normalizeBackendRole,
} from "../../src/canvas/authority";
import {
  PARTICIPANT_STATES,
  PARTICIPANT_TRANSITIONS,
  STAGES,
  canRandomizeParticipant,
  participantStateChanges,
  findSection,
  findStage,
} from "../../src/canvas/lifecycle";
import {
  NODE_KINDS,
  formatNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "../../src/canvas/nodeRef";
import {
  PERSONA_PRESETS,
  ROLE_ALIASES as AUTH_ROLE_ALIASES,
  normalizeRole as normalizeWebRole,
} from "../../src/stores/auth.js";

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

  it("allows exactly the state changes Execution's transition graph allows", () => {
    const source = readFileSync(
      join(
        repoRoot,
        "apps/execution/src/Domain/Subjects/SubjectTransitionGraph.cs"
      ),
      "utf8"
    );
    const codec = readFileSync(
      join(repoRoot, "apps/execution/src/Domain/Subjects/SubjectStateCodec.cs"),
      "utf8"
    );
    const codeOf = Object.fromEntries(
      [...codec.matchAll(/\["([A-Z_]+)"\] = SubjectState\.(\w+)/g)].map(
        ([, code, member]) => [member, code]
      )
    );
    const graph = Object.fromEntries(
      [
        ...source.matchAll(
          /\[SubjectState\.(\w+)\] = new HashSet<SubjectState>\s*(?:\{([^}]*)\}|\(\))/g
        ),
      ].map(([, from, targets]) => [
        codeOf[from],
        [...(targets ?? "").matchAll(/SubjectState\.(\w+)/g)]
          .map(([, member]) => codeOf[member])
          .sort(),
      ])
    );

    expect(Object.keys(graph)).toHaveLength(PARTICIPANT_STATES.length);
    const mirrored = Object.fromEntries(
      Object.entries(PARTICIPANT_TRANSITIONS).map(([from, to]) => [
        from,
        [...to].sort(),
      ])
    );
    expect(mirrored).toEqual(graph);
  });

  it("offers every allowed change except unblinding, in lifecycle order", () => {
    expect(participantStateChanges("ACTIVE")).toEqual({
      targets: ["RECONSENT_REQUIRED", "COMPLETED", "WITHDRAWN"],
      unblindingWithheld: true,
    });
    expect(participantStateChanges("SCREENING")).toEqual({
      targets: ["SCREEN_FAILED", "ENROLLED", "RECONSENT_REQUIRED", "WITHDRAWN"],
      unblindingWithheld: false,
    });
    expect(participantStateChanges("ENROLLED")).toEqual({
      targets: ["RECONSENT_REQUIRED", "WITHDRAWN"],
      unblindingWithheld: false,
    });
    expect(canRandomizeParticipant("ENROLLED")).toBe(true);
    expect(canRandomizeParticipant("SCREENING")).toBe(false);
    expect(canRandomizeParticipant("RANDOMIZED")).toBe(false);
    expect(participantStateChanges("COMPLETED").targets).toEqual([]);
    expect(participantStateChanges("NOT_A_STATE").targets).toEqual([]);
  });
});

describe("Generated canvas contracts freshness & drift gate", () => {
  it("matches every checked-in file in src/canvas/generated/", async () => {
    const rendered = await renderCanvasContractFiles();
    expect(rendered.size).toBeGreaterThan(0);
    for (const [fileName, expected] of rendered.entries()) {
      const checkedIn = readFileSync(
        join(CANVAS_GENERATED_DIR, fileName),
        "utf8"
      );
      expect(checkedIn, fileName).toBe(expected);
    }
    const stale = await generateCanvasContracts({ check: true });
    expect(stale).toEqual([]);
  });

  it("exits with code 1 on --check when any file under src/canvas/generated/ drifts", async () => {
    const tempDir = mkdtempSync(join(tmpdir(), "cadence-canvas-contracts-"));
    try {
      await generateCanvasContracts({ check: false, outputDir: tempDir });
      expect(
        await generateCanvasContracts({
          check: true,
          outputDir: tempDir,
          baseDir: tempDir,
        })
      ).toEqual([]);

      // Drift an existing generated file and verify check detects it and CLI exits 1
      writeFileSync(
        join(tempDir, "nodeKinds.ts"),
        "// drifted\nexport const NODE_KINDS = [] as const;\n"
      );
      const stale = await generateCanvasContracts({
        check: true,
        outputDir: tempDir,
        baseDir: tempDir,
      });
      expect(stale).toContain("nodeKinds.ts");

      let exitCode = 0;
      try {
        execFileSync(
          process.execPath,
          [
            "--input-type=module",
            "-e",
            `import { generateCanvasContracts } from "./scripts/generate-canvas-contracts.js";
             const stale = await generateCanvasContracts({ check: true, outputDir: ${JSON.stringify(tempDir)} });
             if (stale.length > 0) process.exit(1);`,
          ],
          { cwd: join(repoRoot, "apps/web"), stdio: "pipe" }
        );
      } catch (err: any) {
        exitCode = err.status ?? 1;
      }
      expect(exitCode).toBe(1);
    } finally {
      rmSync(tempDir, { recursive: true, force: true });
    }
  });
});

describe("Canvas authority mirror & all 33 can* helpers", () => {
  const { roleAliases: csharpRoleAliases } = parseRoleNormalization();
  const { permissionRoles: csharpPermissionRoles } = parsePermissionMatrix();

  const CANONICAL_TEST_ROLES = [
    "sysadmin",
    "sponsor_designer",
    "protocol_reviewer",
    "sponsor_dm",
    "sponsor_mm",
    "sponsor_statistician",
    "unblinded_statistician",
    "idmc",
    "pharmacist",
    "emergency_unblinder",
    "investigator",
    "principal_investigator",
    "authorized_er_physician",
    "lead_investigator",
    "crc",
    "cra",
    "monitor",
    "external_monitor",
    "subject",
    "auditor",
    "finance_officer",
    "terminology_manager",
  ] as const;

  it("mirrors RoleNormalization.cs aliases 1:1 and reconciles web-only super_admin and tmf_auditor against auth.js", () => {
    expect(BACKEND_ROLE_ALIASES).toEqual(csharpRoleAliases);
    expect("super_admin" in BACKEND_ROLE_ALIASES).toBe(false);
    expect("tmf_auditor" in BACKEND_ROLE_ALIASES).toBe(false);

    // Reconcile super_admin against auth.js: super_admin persona requires sponsor_admin and normalizes to sponsor_admin -> sponsor_dm (never sysadmin)
    const superAdminPreset = PERSONA_PRESETS.find(
      (p: { key: string }) => p.key === "super_admin"
    );
    expect(superAdminPreset?.requires).toEqual(["sponsor_admin"]);
    expect(normalizeWebRole("super_admin")).toBe("sponsor_admin");
    expect(AUTH_ROLE_ALIASES.super_admin).toEqual(
      AUTH_ROLE_ALIASES.sponsor_admin
    );
    expect(WEB_PERSONA_ROLE_ALIASES.super_admin).toBe(
      csharpRoleAliases.sponsor_admin
    );
    expect(normalizeBackendRole("super_admin")).toBe("sponsor_dm");
    expect(normalizeBackendRole("super_admin")).not.toBe("sysadmin");

    // Reconcile tmf_auditor against auth.js: tmf_auditor maps to auditor
    expect(AUTH_ROLE_ALIASES.tmf_auditor).toEqual(AUTH_ROLE_ALIASES.auditor);
    expect(WEB_PERSONA_ROLE_ALIASES.tmf_auditor).toBe(
      csharpRoleAliases.auditor
    );
    expect(normalizeBackendRole("tmf_auditor")).toBe("auditor");
    expect(normalizeBackendRole("TMF Auditor")).toBe("auditor");
  });

  it("offers participant changes to exactly the roles Execution's SubjectAuthority accepts", () => {
    const source = readFileSync(
      join(
        repoRoot,
        "apps/execution/src/Application/Subjects/SubjectAuthority.cs"
      ),
      "utf8"
    );
    const method = source.slice(
      source.indexOf("public static bool CanManageParticipants")
    );
    const body = method.slice(
      method.indexOf("return normalized is"),
      method.indexOf(";", method.indexOf("return normalized is"))
    );
    const accepted = [...body.matchAll(/"([^"]+)"/g)].map(([, role]) => role);

    expect(accepted.length).toBeGreaterThan(0);
    for (const role of accepted) {
      expect(canManageParticipants({ rawRoles: [role] }), role).toBe(true);
      expect(
        canManageParticipants({ rawRoles: [` ${role.toUpperCase()} `] }),
        role
      ).toBe(true);
    }
    for (const role of [
      "cra",
      "monitor",
      "auditor",
      "tmf_auditor",
      "sponsor_admin",
      "super_admin",
      "admin",
      "sponsor_dm",
      "data_manager",
      "data manager",
      "dm",
    ]) {
      expect(canManageParticipants({ rawRoles: [role] }), role).toBe(false);
    }
    expect(canManageParticipants({ rawRoles: null })).toBe(false);
  });

  it("exposes Start and Conduct CTMS/RTSM sections without product acronyms (UX-1)", () => {
    expect(findSection("startup-greenlight")?.stage.id).toBe("start");
    expect(findSection("site-milestones")?.stage.id).toBe("start");
    expect(findSection("delegation")?.stage.id).toBe("start");
    expect(findSection("monitoring-visits")?.stage.id).toBe("conduct");
    expect(findSection("supply")?.stage.id).toBe("conduct");

    for (const stage of STAGES) {
      for (const section of stage.sections) {
        expect(section.label).not.toMatch(/\b(EDC|CTMS|eTMF|RTSM|P21|QMS)\b/);
      }
    }
  });

  it("validates all 33 can* authority helpers against PermissionMatrix*.cs and Execution/Kernel contracts", () => {
    const exportedCanNames = Object.keys(authority)
      .filter(
        (key) =>
          /^can[A-Z]/.test(key) &&
          typeof (authority as Record<string, unknown>)[key] === "function"
      )
      .sort();

    const matrixBackedChecks: Array<{
      name: string;
      fn: (auth: authority.AuthorityAuth) => boolean;
      permission: string;
    }> = [
      {
        name: "canSignForms",
        fn: canSignForms,
        permission: "ecrf_form:sign",
      },
      {
        name: "canVerifySource",
        fn: canVerifySource,
        permission: "sdv:update",
      },
      {
        name: "canActivateSite",
        fn: canActivateSite,
        permission: "site_compliance:activate",
      },
      {
        name: "canCertifyGreenlight",
        fn: canCertifyGreenlight,
        permission: "ctms_greenlight:certify",
      },
      {
        name: "canManageMilestones",
        fn: canManageMilestones,
        permission: "ctms_site_milestone:update",
      },
      {
        name: "canManageSiteMilestones",
        fn: canManageSiteMilestones,
        permission: "ctms_site_milestone:update",
      },
      {
        name: "canScheduleMonitoringVisits",
        fn: canScheduleMonitoringVisits,
        permission: "ctms_monitoring_visit:create",
      },
      {
        name: "canManageMonitoringVisits",
        fn: canManageMonitoringVisits,
        permission: "ctms_monitoring_visit:create",
      },
      {
        name: "canCompleteMonitoringVisits",
        fn: canCompleteMonitoringVisits,
        permission: "ctms_monitoring_visit:update",
      },
      {
        name: "canSignOffMonitoringVisits",
        fn: canSignOffMonitoringVisits,
        permission: "ctms_monitoring_visit:sign_off",
      },
      {
        name: "canSignOffMonitoringVisit",
        fn: canSignOffMonitoringVisit,
        permission: "ctms_monitoring_visit:sign_off",
      },
      {
        name: "canManageDoa",
        fn: canManageDoa,
        permission: "ctms_doa:assign",
      },
      {
        name: "canSignOffDoa",
        fn: canSignOffDoa,
        permission: "ctms_doa:sign_off",
      },
      {
        name: "canReconcileIpKits",
        fn: canReconcileIpKits,
        permission: "rtsm_supply:write",
      },
      {
        name: "canSeedIpKits",
        fn: canSeedIpKits,
        permission: "ip_kit_inventory:seed",
      },
      {
        name: "canRecordChainOfCustody",
        fn: canRecordChainOfCustody,
        permission: "cgt_chain_of_custody:write",
      },
      {
        name: "canReadRandomizationSchemes",
        fn: canReadRandomizationSchemes,
        permission: "randomization_scheme:read",
      },
      {
        name: "canManageRandomizationSchemes",
        fn: canManageRandomizationSchemes,
        permission: "randomization_scheme:draft",
      },
      {
        name: "canReadEtmf",
        fn: canReadEtmf,
        permission: "etmf_document:read",
      },
      {
        name: "canStageEtmfQc",
        fn: canStageEtmfQc,
        permission: "etmf_document:create",
      },
      {
        name: "canTagEtmf",
        fn: canTagEtmf,
        permission: "etmf_document:tag",
      },
      {
        name: "canReviewEtmfQc",
        fn: canReviewEtmfQc,
        permission: "etmf_document:transition_clinical_qc",
      },
      {
        name: "canExportEtmf",
        fn: canExportEtmf,
        permission: "etmf_document:export",
      },
      {
        name: "canAssignTrainingCurriculum",
        fn: canAssignTrainingCurriculum,
        permission: "training_curriculum:assign",
      },
      {
        name: "canSignTrainingDelegations",
        fn: canSignTrainingDelegations,
        permission: "delegation_log:sign",
      },
    ];

    const serviceBackedChecks: Array<{
      name: string;
      fn: (auth: authority.AuthorityAuth) => boolean;
      allowedCanonicalRoles: readonly string[];
    }> = [
      {
        name: "canManageParticipants",
        fn: canManageParticipants,
        allowedCanonicalRoles: [
          "investigator",
          "principal_investigator",
          "lead_investigator",
          "crc",
        ],
      },
      {
        name: "canManageQueries",
        fn: canManageQueries,
        allowedCanonicalRoles: ["cra", "monitor", "sponsor_dm"],
      },
      {
        name: "canAnswerQueries",
        fn: canAnswerQueries,
        allowedCanonicalRoles: [
          "crc",
          "investigator",
          "principal_investigator",
          "lead_investigator",
          "authorized_er_physician",
        ],
      },
      {
        name: "canManageRulePacks",
        fn: canManageRulePacks,
        allowedCanonicalRoles: ["sponsor_designer", "sponsor_dm", "sysadmin"],
      },
      {
        name: "canCorrectObservations",
        fn: canCorrectObservations,
        allowedCanonicalRoles: [
          "crc",
          "investigator",
          "principal_investigator",
          "lead_investigator",
          "authorized_er_physician",
          "sponsor_dm",
        ],
      },
      {
        name: "canUnblindParticipant",
        fn: canUnblindParticipant,
        allowedCanonicalRoles: [
          "principal_investigator",
          "authorized_er_physician",
        ],
      },
      {
        name: "canGovernProtocols",
        fn: canGovernProtocols,
        allowedCanonicalRoles: [
          "sysadmin",
          "sponsor_designer",
          "sponsor_dm",
          "protocol_reviewer",
        ],
      },
      {
        name: "canManageKernelStudySites",
        fn: canManageKernelStudySites,
        allowedCanonicalRoles: ["sysadmin", "sponsor_designer", "sponsor_dm"],
      },
    ];

    const testedNames = [
      ...matrixBackedChecks.map((c) => c.name),
      ...serviceBackedChecks.map((c) => c.name),
    ].sort();
    expect(exportedCanNames).toHaveLength(33);
    expect(testedNames).toEqual(exportedCanNames);

    // Verify all 25 PermissionMatrix-backed helpers against C# PermissionMatrix*.cs across every canonical role
    for (const { name, fn, permission } of matrixBackedChecks) {
      const grantedSet = new Set(csharpPermissionRoles[permission] ?? []);
      expect(grantedSet.size, `${name} (${permission})`).toBeGreaterThan(0);
      for (const role of CANONICAL_TEST_ROLES) {
        expect(fn({ rawRoles: [role] }), `${name} with ${role}`).toBe(
          grantedSet.has(role)
        );
      }
      // Verify all RoleNormalization aliases resolve identically to their canonical target
      for (const [alias, canonical] of Object.entries(csharpRoleAliases)) {
        expect(
          fn({ rawRoles: [alias] }),
          `${name} with alias '${alias}' -> '${canonical}'`
        ).toBe(grantedSet.has(canonical));
      }
    }

    // Verify the 8 Execution/Kernel service-backed helpers across every canonical role
    for (const { name, fn, allowedCanonicalRoles } of serviceBackedChecks) {
      const allowedSet = new Set(allowedCanonicalRoles);
      for (const role of CANONICAL_TEST_ROLES) {
        expect(fn({ rawRoles: [role] }), `${name} with ${role}`).toBe(
          allowedSet.has(role)
        );
      }
    }

    // Explicitly verify super_admin (sponsor_dm, NOT sysadmin) and tmf_auditor (auditor) across critical gates
    expect(canActivateSite({ rawRoles: ["super_admin"] })).toBe(true);
    expect(canCertifyGreenlight({ rawRoles: ["super_admin"] })).toBe(true);
    expect(canSignOffDoa({ rawRoles: ["super_admin"] })).toBe(false);
    expect(canSignOffDoa({ rawRoles: ["sysadmin"] })).toBe(true);
    expect(canReadRandomizationSchemes({ rawRoles: ["tmf_auditor"] })).toBe(
      true
    );
    expect(canStageEtmfQc({ rawRoles: ["tmf_auditor"] })).toBe(false);
    expect(canReviewEtmfQc({ rawRoles: ["tmf_auditor"] })).toBe(false);

    // rtsm_supply -> pharmacist only
    expect(canReconcileIpKits({ rawRoles: ["pharmacist"] })).toBe(true);
    for (const role of [
      "sysadmin",
      "Data Manager",
      "Site Investigator",
      "crc",
      "cra",
    ]) {
      expect(canReconcileIpKits({ rawRoles: [role] }), role).toBe(false);
    }

    // randomization_scheme:draft/approve -> unblinded_statistician only
    expect(
      canManageRandomizationSchemes({ rawRoles: ["unblinded_statistician"] })
    ).toBe(true);
    for (const role of [
      "sysadmin",
      "sponsor_dm",
      "Data Manager",
      "auditor",
      "pharmacist",
      "crc",
      "Principal Investigator",
    ]) {
      expect(canManageRandomizationSchemes({ rawRoles: [role] }), role).toBe(
        false
      );
    }

    // SubjectAuthority.CanUnblind -> principal_investigator & authorized_er_physician only
    const authoritySource = readFileSync(
      join(
        repoRoot,
        "apps/execution/src/Application/Subjects/SubjectAuthority.cs"
      ),
      "utf8"
    );
    const unblindMethod = authoritySource.slice(
      authoritySource.indexOf("public static bool CanUnblind")
    );
    const unblindBody = unblindMethod.slice(
      unblindMethod.indexOf("return normalized is"),
      unblindMethod.indexOf(";", unblindMethod.indexOf("return normalized is"))
    );
    const unblindRoles = [...unblindBody.matchAll(/"([^"]+)"/g)].map(
      ([, role]) => role
    );
    expect(unblindRoles.length).toBeGreaterThan(0);
    for (const role of unblindRoles) {
      expect(canUnblindParticipant({ rawRoles: [role] }), role).toBe(true);
    }
    for (const role of [
      "Site Investigator",
      "investigator",
      "sub_investigator",
      "lead_investigator",
      "er_physician",
      "er physician",
      "crc",
      "pharmacist",
      "unblinded_statistician",
      "sysadmin",
    ]) {
      expect(canUnblindParticipant({ rawRoles: [role] }), role).toBe(false);
    }

    // eTMF permission matrix mirror (PermissionMatrix.Etmf.cs + PermissionMatrix.cs)
    for (const role of [
      "sysadmin",
      "sponsor_dm",
      "cra",
      "crc",
      "investigator",
      "monitor",
      "auditor",
    ]) {
      expect(canReadEtmf({ rawRoles: [role] }), role).toBe(true);
    }
    for (const role of ["pharmacist", "participant", "sponsor_designer"]) {
      expect(canReadEtmf({ rawRoles: [role] }), role).toBe(false);
    }

    for (const role of ["sysadmin", "sponsor_dm", "cra", "monitor", "crc"]) {
      expect(canStageEtmfQc({ rawRoles: [role] }), role).toBe(true);
      expect(canTagEtmf({ rawRoles: [role] }), role).toBe(true);
    }
    for (const role of ["investigator", "auditor", "pharmacist"]) {
      expect(canStageEtmfQc({ rawRoles: [role] }), role).toBe(false);
      expect(canTagEtmf({ rawRoles: [role] }), role).toBe(false);
    }

    for (const role of ["sysadmin", "sponsor_dm", "cra", "monitor"]) {
      expect(canReviewEtmfQc({ rawRoles: [role] }), role).toBe(true);
    }
    for (const role of ["crc", "investigator", "auditor", "pharmacist"]) {
      expect(canReviewEtmfQc({ rawRoles: [role] }), role).toBe(false);
    }

    for (const role of ["sponsor_dm", "cra", "monitor", "auditor"]) {
      expect(canExportEtmf({ rawRoles: [role] }), role).toBe(true);
    }
    for (const role of ["sysadmin", "crc", "investigator", "pharmacist"]) {
      expect(canExportEtmf({ rawRoles: [role] }), role).toBe(false);
    }
  });
});
