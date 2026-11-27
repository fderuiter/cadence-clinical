import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../../src/api/client";
import { interopService } from "../../src/api/interop";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const WEB_DIR = path.resolve(__dirname, "../..");
const REPO_ROOT = path.resolve(WEB_DIR, "../..");
const API_DIR = path.join(WEB_DIR, "src/api");
const TYPES_PATH = path.join(API_DIR, "types.ts");
const CLIENT_PATH = path.join(API_DIR, "client.ts");
const LEGACY_CLIENT_PATH = path.join(API_DIR, "legacyClient.ts");
const ESOURCE_SLICE_PATH = path.join(API_DIR, "interop", "esource.ts");
const ESLINT_CONFIG_PATH = path.join(REPO_ROOT, "eslint.config.mjs");
const GITATTRIBUTES_PATH = path.join(REPO_ROOT, ".gitattributes");

function buildDotNetCandidate(
  overrides: Record<string, unknown> = {}
): Record<string, unknown> {
  return {
    id: "cand-001",
    studyId: "STUDY-101",
    siteId: "SITE-01",
    subjectId: "SUBJ-001",
    subjectPseudonym: "PS-001",
    ehrSystemId: "EPIC-PROD-01",
    fhirResourceType: "Observation",
    fhirResourceId: "obs-bp-001",
    sourceTimestamp: "2026-11-24T10:15:00Z",
    targetDomain: "VS",
    targetVariable: "VSORRES_SYSBP",
    cdashTestcd: "SYSBP",
    cdashTest: "Systolic Blood Pressure",
    candidateValue: 122,
    candidateUnit: "mmHg",
    currentEcrfValue: "",
    currentEcrfUpdatedAt: null,
    discrepancyType: "NEW_FIELD",
    confidenceScore: "0.97",
    mappingProvenance: "LOINC 8480-6 -> VS.SYSBP",
    rawFhirResource: { resourceType: "Observation", id: "obs-bp-001" },
    status: "PENDING_REVIEW",
    resolutionAction: null,
    resolutionReason: null,
    resolvedBy: null,
    resolvedAt: null,
    electronicSignature: "",
    clinicalQueryId: null,
    createdAt: "2026-11-24T10:16:00Z",
    versionIndex: "1",
    ...overrides,
  };
}

describe("interopService eSource wire serialization and normalization", () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("listEhrCandidates sends default skip=0 and limit=50 and normalizes .NET camelCase candidates", async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          candidates: [buildDotNetCandidate()],
          totalCount: "1",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } }
      )
    );

    const response = await interopService.listEhrCandidates({
      studyId: "STUDY-101",
      subjectId: "SUBJ-001",
      siteId: "SITE-01",
      domain: "VS",
      status: "PENDING_REVIEW",
    });

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const [request] = fetchSpy.mock.calls[0] as [Request];
    const url = new URL(request.url);
    expect(url.pathname).toBe("/api/v1/interop/esource/candidates");
    expect(url.searchParams.get("study_id")).toBe("STUDY-101");
    expect(url.searchParams.get("subject_id")).toBe("SUBJ-001");
    expect(url.searchParams.get("site_id")).toBe("SITE-01");
    expect(url.searchParams.get("domain")).toBe("VS");
    expect(url.searchParams.get("status")).toBe("PENDING_REVIEW");
    expect(url.searchParams.get("skip")).toBe("0");
    expect(url.searchParams.get("limit")).toBe("50");

    expect(response.total_count).toBe(1);
    expect(response.totalCount).toBe(1);
    expect(response.candidates).toHaveLength(1);
    const [item] = response.candidates;
    expect(item.id).toBe("cand-001");
    expect(item.study_id).toBe("STUDY-101");
    expect(item.site_id).toBe("SITE-01");
    expect(item.subject_id).toBe("SUBJ-001");
    expect(item.subject_pseudonym).toBe("PS-001");
    expect(item.ehr_system_id).toBe("EPIC-PROD-01");
    expect(item.target_domain).toBe("VS");
    expect(item.target_variable).toBe("VSORRES_SYSBP");
    expect(item.cdash_testcd).toBe("SYSBP");
    expect(item.cdash_test).toBe("Systolic Blood Pressure");
    expect(item.candidate_value).toBe(122);
    expect(item.candidate_unit).toBe("mmHg");
    expect(item.current_ecrf_value).toBeNull();
    expect(item.current_ecrf_updated_at).toBeNull();
    expect(item.confidence_score).toBe(0.97);
    expect(item.electronic_signature).toBeNull();
    expect(item.version_index).toBe(1);
  });

  it("listEhrCandidates supports snake_case query inputs, falsy 0/false values, and MSW total fallback", async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          candidates: [
            buildDotNetCandidate({
              id: "cand-zero",
              candidateValue: 0,
              currentEcrfValue: false,
              currentEcrfUpdatedAt: "2026-11-24T09:00:00Z",
            }),
          ],
          total: 5,
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const response = await interopService.listEhrCandidates({
      study_id: "STUDY-202",
      subject_id: "SUBJ-202",
      site_id: "SITE-02",
      skip: 20,
      limit: 10,
    });

    const [request] = fetchSpy.mock.calls[0] as [Request];
    const url = new URL(request.url);
    expect(url.searchParams.get("study_id")).toBe("STUDY-202");
    expect(url.searchParams.get("subject_id")).toBe("SUBJ-202");
    expect(url.searchParams.get("site_id")).toBe("SITE-02");
    expect(url.searchParams.get("skip")).toBe("20");
    expect(url.searchParams.get("limit")).toBe("10");
    expect(response.total_count).toBe(5);
    expect(response.totalCount).toBe(5);
    expect(response.candidates[0].candidate_value).toBe(0);
    expect(response.candidates[0].current_ecrf_value).toBe(false);
  });

  it("acceptEhrCandidate maps snake_case and camelCase inputs to .NET camelCase DTOs and forwards step-up headers", async () => {
    fetchSpy.mockImplementation(
      async () =>
        new Response(
          JSON.stringify({
            success: true,
            candidate: buildDotNetCandidate({
              status: "ACCEPTED",
              resolutionAction: "ACCEPTED_AND_COMMITTED",
              resolutionReason: "CRC accepted candidate data from hospital EHR",
              resolvedBy: "crc.user",
              resolvedAt: "2026-11-24T11:00:00Z",
              currentEcrfValue: 122,
              currentEcrfUpdatedAt: "2026-11-24T11:00:00Z",
              electronicSignature: {
                signed_by: "crc.user",
                signed_at: "2026-11-24T11:00:00Z",
              },
              versionIndex: 2,
            }),
            clinicalQueryId: null,
            message: "Accepted",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
    );

    const snakeResult = await interopService.acceptEhrCandidate(
      {
        candidate_id: "cand-001",
        commit_reason: "CRC accepted candidate data from hospital EHR",
        change_reason: "CRC eSource Reconciliation Accept & Commit",
      },
      {
        changeReason: "CRC eSource Reconciliation Accept & Commit",
        headers: { "X-Electronic-Signature-Token": "sig-tok-123" },
      }
    );

    const [snakeReq] = fetchSpy.mock.calls[0] as [Request];
    expect(new URL(snakeReq.url).pathname).toBe(
      "/api/v1/interop/esource/reconcile/accept"
    );
    expect(snakeReq.headers.get("X-Change-Reason")).toBe(
      "CRC eSource Reconciliation Accept & Commit"
    );
    expect(snakeReq.headers.get("X-Electronic-Signature-Token")).toBe(
      "sig-tok-123"
    );
    expect(await snakeReq.clone().json()).toEqual({
      candidateId: "cand-001",
      commitReason: "CRC accepted candidate data from hospital EHR",
      changeReason: "CRC eSource Reconciliation Accept & Commit",
    });
    expect(snakeResult.success).toBe(true);
    expect(snakeResult.candidate.status).toBe("ACCEPTED");
    expect(snakeResult.candidate.resolution_action).toBe(
      "ACCEPTED_AND_COMMITTED"
    );
    expect(snakeResult.candidate.current_ecrf_value).toBe(122);
    expect(snakeResult.candidate.electronic_signature).toEqual({
      signed_by: "crc.user",
      signed_at: "2026-11-24T11:00:00Z",
    });
    expect(snakeResult.clinical_query_id).toBeNull();

    await interopService.acceptEhrCandidate({
      candidateId: "cand-002",
      commitReason: "Camel commit",
      changeReason: "Camel reason",
    });
    const [camelReq] = fetchSpy.mock.calls[1] as [Request];
    expect(await camelReq.clone().json()).toEqual({
      candidateId: "cand-002",
      commitReason: "Camel commit",
      changeReason: "Camel reason",
    });
  });

  it("batchAcceptEhrCandidates and batchRejectEhrCandidates serialize camelCase batch DTOs and normalize action_count", async () => {
    fetchSpy
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            success: true,
            actionCount: "2",
            candidates: [
              buildDotNetCandidate({ id: "cand-1", status: "ACCEPTED" }),
              buildDotNetCandidate({ id: "cand-2", status: "ACCEPTED" }),
            ],
            message: "Batch accepted",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            success: true,
            actionCount: 2,
            candidates: [
              buildDotNetCandidate({
                id: "cand-3",
                status: "REJECTED",
                resolutionReason: "Artifact noise",
              }),
              buildDotNetCandidate({
                id: "cand-4",
                status: "REJECTED",
                resolutionReason: "Artifact noise",
              }),
            ],
            message: "Batch rejected",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const acceptResp = await interopService.batchAcceptEhrCandidates({
      candidate_ids: ["cand-1", "cand-2"],
      commit_reason: "Batch accept",
      change_reason: "CRC Batch Accept",
    });
    const [acceptReq] = fetchSpy.mock.calls[0] as [Request];
    expect(new URL(acceptReq.url).pathname).toBe(
      "/api/v1/interop/esource/reconcile/batch-accept"
    );
    expect(await acceptReq.clone().json()).toEqual({
      candidateIds: ["cand-1", "cand-2"],
      commitReason: "Batch accept",
      changeReason: "CRC Batch Accept",
    });
    expect(acceptResp.action_count).toBe(2);
    expect(acceptResp.actionCount).toBe(2);
    expect(acceptResp.candidates.map((c) => c.id)).toEqual([
      "cand-1",
      "cand-2",
    ]);

    const rejectResp = await interopService.batchRejectEhrCandidates({
      candidate_ids: ["cand-3", "cand-4"],
      rejection_reason: "Artifact noise",
      change_reason: "CRC Batch Reject",
    });
    const [rejectReq] = fetchSpy.mock.calls[1] as [Request];
    expect(new URL(rejectReq.url).pathname).toBe(
      "/api/v1/interop/esource/reconcile/batch-reject"
    );
    expect(await rejectReq.clone().json()).toEqual({
      candidateIds: ["cand-3", "cand-4"],
      rejectionReason: "Artifact noise",
      changeReason: "CRC Batch Reject",
    });
    expect(rejectResp.action_count).toBe(2);
    expect(rejectResp.candidates[0].resolution_reason).toBe("Artifact noise");
  });

  it("rejectEhrCandidate and raiseEhrClinicalQuery serialize camelCase DTOs, normalize clinical_query_id, and surface ApiError on failure", async () => {
    fetchSpy
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            success: true,
            candidate: buildDotNetCandidate({
              status: "REJECTED",
              resolutionAction: "REJECTED",
              resolutionReason: "Duplicate observation",
            }),
            clinicalQueryId: null,
            message: "Rejected",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            success: true,
            candidate: buildDotNetCandidate({
              status: "QUERIED",
              clinicalQueryId: "qry-98765432",
            }),
            clinicalQueryId: "qry-98765432",
            message: "Query raised",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ detail: "Candidate already resolved" }), {
          status: 409,
          statusText: "Conflict",
          headers: { "Content-Type": "application/json" },
        })
      );

    const rejectResp = await interopService.rejectEhrCandidate({
      candidate_id: "cand-001",
      rejection_reason: "Duplicate observation",
      change_reason: "CRC Reject",
    });
    const [rejectReq] = fetchSpy.mock.calls[0] as [Request];
    expect(new URL(rejectReq.url).pathname).toBe(
      "/api/v1/interop/esource/reconcile/reject"
    );
    expect(await rejectReq.clone().json()).toEqual({
      candidateId: "cand-001",
      rejectionReason: "Duplicate observation",
      changeReason: "CRC Reject",
    });
    expect(rejectResp.candidate.status).toBe("REJECTED");

    const queryResp = await interopService.raiseEhrClinicalQuery({
      candidate_id: "cand-001",
      query_explanation: "EHR systolic BP differs from paper source",
      discrepancy_type: "VALUE_MISMATCH",
      change_reason: "CRC Raised eSource Discrepancy Query",
    });
    const [queryReq] = fetchSpy.mock.calls[1] as [Request];
    expect(new URL(queryReq.url).pathname).toBe(
      "/api/v1/interop/esource/reconcile/query"
    );
    expect(await queryReq.clone().json()).toEqual({
      candidateId: "cand-001",
      queryExplanation: "EHR systolic BP differs from paper source",
      discrepancyType: "VALUE_MISMATCH",
      changeReason: "CRC Raised eSource Discrepancy Query",
    });
    expect(queryResp.clinical_query_id).toBe("qry-98765432");
    expect(queryResp.clinicalQueryId).toBe("qry-98765432");
    expect(queryResp.candidate.clinical_query_id).toBe("qry-98765432");

    await expect(
      interopService.acceptEhrCandidate({
        candidate_id: "cand-001",
        commit_reason: "Retry",
      })
    ).rejects.toBeInstanceOf(ApiError);
  });
});

describe("legacy web contract isolation and reachability invariants", () => {
  it("keeps client.ts and interop/esource.ts free of types.ts and legacyClient.ts imports", () => {
    const clientSource = fs.readFileSync(CLIENT_PATH, "utf8");
    expect(clientSource).not.toMatch(/from\s+["']\.\/types(?:\.ts)?["']/);
    expect(clientSource).not.toMatch(
      /from\s+["']\.\/legacyClient(?:\.ts)?["']/
    );
    expect(clientSource).not.toMatch(/\bopenApiClient\b/);

    const esourceSource = fs.readFileSync(ESOURCE_SLICE_PATH, "utf8");
    expect(esourceSource).not.toMatch(/from\s+["']\.\.\/types(?:\.ts)?["']/);
    expect(esourceSource).not.toMatch(
      /from\s+["']\.\.\/legacyClient(?:\.ts)?["']/
    );
    expect(esourceSource).not.toMatch(/\bopenApiClient\b/);

    expect(fs.existsSync(LEGACY_CLIENT_PATH)).toBe(true);
    expect(
      fs.existsSync(path.join(REPO_ROOT, "docs", "openapi", "legacy"))
    ).toBe(false);
  });

  it("ignores apps/web/src/api/types.ts in eslint.config.mjs and marks it linguist-generated in .gitattributes", () => {
    const eslintSource = fs.readFileSync(ESLINT_CONFIG_PATH, "utf8");
    expect(eslintSource).toContain('"apps/web/src/api/types.ts"');

    const gitattributesSource = fs.readFileSync(GITATTRIBUTES_PATH, "utf8");
    expect(gitattributesSource).toContain(
      "apps/web/src/api/types.ts linguist-generated=true"
    );
    expect(gitattributesSource).not.toContain("docs/openapi/legacy");
  });

  it("retains zero uncalled operations and zero unreachable schemas in apps/web/src/api/types.ts", () => {
    const typesSource = fs.readFileSync(TYPES_PATH, "utf8");

    const extractInterfaceBody = (
      source: string,
      interfaceName: string
    ): string => {
      const declRegex = new RegExp(
        `export\\s+interface\\s+${interfaceName}\\s*\\{`
      );
      const match = declRegex.exec(source);
      if (!match) {
        throw new Error(`Missing export interface ${interfaceName}`);
      }
      const openBraceIdx = match.index + match[0].length - 1;
      let depth = 0;
      let inString: '"' | "'" | null = null;
      let inLineComment = false;
      let inBlockComment = false;

      for (let i = openBraceIdx; i < source.length; i++) {
        const ch = source[i];
        const next = source[i + 1];
        if (inLineComment) {
          if (ch === "\n") inLineComment = false;
          continue;
        }
        if (inBlockComment) {
          if (ch === "*" && next === "/") {
            inBlockComment = false;
            i++;
          }
          continue;
        }
        if (inString) {
          if (ch === "\\") {
            i++;
          } else if (ch === inString) {
            inString = null;
          }
          continue;
        }
        if (ch === "/" && next === "/") {
          inLineComment = true;
          i++;
          continue;
        }
        if (ch === "/" && next === "*") {
          inBlockComment = true;
          i++;
          continue;
        }
        if (ch === '"' || ch === "'") {
          inString = ch;
          continue;
        }
        if (ch === "{") {
          depth++;
        } else if (ch === "}") {
          depth--;
          if (depth === 0) {
            return source.slice(openBraceIdx + 1, i);
          }
        }
      }
      throw new Error(`Unbalanced braces in export interface ${interfaceName}`);
    };

    const parseTopLevelMembers = (
      body: string
    ): Array<{ name: string; value: string }> => {
      const members: Array<{ name: string; value: string }> = [];
      let depth = 0;
      let inString: '"' | "'" | null = null;
      let inLineComment = false;
      let inBlockComment = false;
      let tokenStart = 0;
      let colonIdx = -1;

      const stripLeadingComments = (segment: string): string => {
        let idx = 0;
        while (idx < segment.length) {
          while (idx < segment.length && /\s/.test(segment[idx])) idx++;
          if (segment[idx] === "/" && segment[idx + 1] === "*") {
            const end = segment.indexOf("*/", idx + 2);
            if (end === -1) break;
            idx = end + 2;
          } else if (segment[idx] === "/" && segment[idx + 1] === "/") {
            const end = segment.indexOf("\n", idx + 2);
            if (end === -1) return "";
            idx = end + 1;
          } else {
            break;
          }
        }
        return segment.slice(idx).trim();
      };

      const unquoteName = (rawKey: string): string => {
        const cleaned = rawKey.replace(/\?$/, "").trim();
        if (
          (cleaned.startsWith('"') && cleaned.endsWith('"')) ||
          (cleaned.startsWith("'") && cleaned.endsWith("'"))
        ) {
          return cleaned.slice(1, -1);
        }
        return cleaned;
      };

      for (let i = 0; i < body.length; i++) {
        const ch = body[i];
        const next = body[i + 1];
        if (inLineComment) {
          if (ch === "\n") inLineComment = false;
          continue;
        }
        if (inBlockComment) {
          if (ch === "*" && next === "/") {
            inBlockComment = false;
            i++;
          }
          continue;
        }
        if (inString) {
          if (ch === "\\") {
            i++;
          } else if (ch === inString) {
            inString = null;
          }
          continue;
        }
        if (ch === "/" && next === "/") {
          inLineComment = true;
          i++;
          continue;
        }
        if (ch === "/" && next === "*") {
          inBlockComment = true;
          i++;
          continue;
        }
        if (ch === '"' || ch === "'") {
          inString = ch;
          continue;
        }
        if (ch === "{" || ch === "(" || ch === "[") {
          depth++;
        } else if (ch === "}" || ch === ")" || ch === "]") {
          depth--;
        } else if (depth === 0 && ch === ":" && colonIdx === -1) {
          colonIdx = i;
        } else if (depth === 0 && ch === ";") {
          if (colonIdx !== -1) {
            const rawKey = stripLeadingComments(
              body.slice(tokenStart, colonIdx)
            );
            const rawValue = body.slice(colonIdx + 1, i).trim();
            if (rawKey) {
              members.push({
                name: unquoteName(rawKey),
                value: rawValue,
              });
            }
          }
          tokenStart = i + 1;
          colonIdx = -1;
        }
      }
      return members;
    };

    const unwrapBraces = (literal: string): string => {
      const trimmed = literal.trim();
      if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
        return trimmed.slice(1, -1);
      }
      return trimmed;
    };

    const walkSources = (dir: string, acc: string[] = []): string[] => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          if (entry.name === "generated") continue;
          walkSources(full, acc);
        } else if (/\.(ts|js|vue)$/.test(entry.name) && full !== TYPES_PATH) {
          acc.push(full);
        }
      }
      return acc;
    };

    const calledOps = new Set<string>();
    for (const file of walkSources(path.join(WEB_DIR, "src"))) {
      const content = fs.readFileSync(file, "utf8");
      const re =
        /\b(?:openApiClient|legacyClient)\s*(?:\bas\s+any\s*\))?\s*\.\s*(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s*(?:<[^>]*>)?\s*\(\s*["'`]([^"'`]+)["'`]/g;
      let match: RegExpExecArray | null;
      while ((match = re.exec(content)) !== null) {
        calledOps.add(`${match[1].toLowerCase()} ${match[2]}`);
      }
    }

    const declaredPaths: string[] = [];
    const declaredOps: Array<{ key: string; opId: string }> = [];
    const operationsMap = new Map<string, string>();
    const schemasMap = new Map<string, string>();

    for (const pathMember of parseTopLevelMembers(
      extractInterfaceBody(typesSource, "paths")
    )) {
      const pathKey = pathMember.name;
      declaredPaths.push(pathKey);
      let methodCount = 0;
      for (const sub of parseTopLevelMembers(unwrapBraces(pathMember.value))) {
        const methodName = sub.name;
        if (
          [
            "get",
            "put",
            "post",
            "delete",
            "options",
            "head",
            "patch",
            "trace",
          ].includes(methodName)
        ) {
          const typeText = sub.value.trim();
          if (typeText !== "never") {
            methodCount++;
            const opMatch = typeText.match(
              /^operations\["([^"]+)"\]$|^operations\.([A-Za-z0-9_]+)$/
            );
            declaredOps.push({
              key: `${methodName} ${pathKey}`,
              opId: opMatch ? opMatch[1] || opMatch[2] : typeText,
            });
          }
        }
      }
      expect(
        methodCount,
        `path ${pathKey} must have >=1 called method`
      ).toBeGreaterThan(0);
    }

    for (const opMember of parseTopLevelMembers(
      extractInterfaceBody(typesSource, "operations")
    )) {
      operationsMap.set(opMember.name, opMember.value);
    }

    const schemasEntry = parseTopLevelMembers(
      extractInterfaceBody(typesSource, "components")
    ).find((m) => m.name === "schemas");
    expect(schemasEntry).toBeDefined();
    for (const schemaMember of parseTopLevelMembers(
      unwrapBraces(schemasEntry!.value)
    )) {
      schemasMap.set(schemaMember.name, schemaMember.value);
    }

    expect(declaredPaths).toHaveLength(61);
    expect(declaredOps).toHaveLength(67);
    expect(operationsMap.size).toBe(67);
    expect(schemasMap.size).toBe(148);

    // Zero migrated eSource, designer amendment, or GET knowledge routes remain in legacy types.ts
    expect(
      declaredPaths.filter((p) => p.startsWith("/api/v1/interop/esource/"))
    ).toEqual([]);
    const legacyDesignerAmendmentPrefix = [
      "/api/v1/designer",
      "amendments/",
    ].join("/");
    expect(
      declaredPaths.filter((p) => p.startsWith(legacyDesignerAmendmentPrefix))
    ).toEqual([]);
    expect(
      declaredOps
        .map((o) => o.key)
        .filter((k) => k.startsWith("get /api/v1/knowledge/"))
    ).toEqual([]);

    // Every declared operation in types.ts is actively called via openApiClient
    const uncalledOps = declaredOps
      .map((o) => o.key)
      .filter((key) => !calledOps.has(key));
    expect(uncalledOps).toEqual([]);

    // Every operation in export interface operations is referenced by a retained path
    const referencedOpIds = new Set(declaredOps.map((o) => o.opId));
    const orphanedOperationDefs = [...operationsMap.keys()].filter(
      (opId) => !referencedOpIds.has(opId)
    );
    expect(orphanedOperationDefs).toEqual([]);

    // Every schema in components["schemas"] is transitively reachable from a retained operation
    const reachableSchemas = new Set<string>();
    const queue: string[] = [];
    for (const opId of referencedOpIds) {
      const opText = operationsMap.get(opId) ?? "";
      for (const m of opText.matchAll(
        /components\["schemas"\]\["([^"]+)"\]/g
      )) {
        if (!reachableSchemas.has(m[1])) {
          reachableSchemas.add(m[1]);
          queue.push(m[1]);
        }
      }
    }
    while (queue.length > 0) {
      const current = queue.shift()!;
      const schemaText = schemasMap.get(current) ?? "";
      for (const m of schemaText.matchAll(
        /components\["schemas"\]\["([^"]+)"\]/g
      )) {
        if (!reachableSchemas.has(m[1])) {
          reachableSchemas.add(m[1]);
          queue.push(m[1]);
        }
      }
    }

    const unreachableSchemas = [...schemasMap.keys()].filter(
      (name) => !reachableSchemas.has(name)
    );
    expect(unreachableSchemas).toEqual([]);
  });
});
