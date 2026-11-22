import fs from "node:fs";
import path from "node:path";
import { setupServer } from "msw/node";
import {
  afterAll,
  afterEach,
  beforeAll,
  beforeEach,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import { DEMO_STUDY } from "demo-fixtures";
import type { components, paths } from "@/api/generated/kernel";
import { ecrfFormsApi } from "@/api/ecrfFormsApi";
import { kernelApi } from "@/api/kernelApi";
import { createWebDemoStore } from "@/mocks/demoStore";
import { buildWebGatewayHandlers } from "@/mocks/handlers";
import { resetDemoRandom } from "@/utils/demoMode";

type StudyRulePacksResponse =
  paths["/api/v1/kernel/studies/{studyRef}/rule-packs"]["get"]["responses"][200]["content"]["application/json"];
type RulePackPreviewResponse =
  paths["/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview"]["post"]["responses"][200]["content"]["application/json"];
type ApproveLibraryItemResponse =
  paths["/api/v1/kernel/library-items/{itemRef}/approve"]["post"]["responses"][200]["content"]["application/json"];
type DeclareStudyStandardsResponse =
  paths["/api/v1/kernel/studies/{studyRef}/standards"]["put"]["responses"][200]["content"]["application/json"];
type ValidationFindingPageResponse =
  paths["/api/v1/kernel/studies/{studyRef}/validation/findings"]["get"]["responses"][200]["content"]["application/json"];
type ValidationFindingDetailResponse =
  paths["/api/v1/kernel/validation/findings/{findingRef}"]["get"]["responses"][200]["content"]["application/json"];
type FormPreviewResponse =
  paths["/api/v1/execution/forms/{formId}/preview"]["post"]["responses"][200]["content"]["application/json"];

const openApiPath = path.resolve(
  __dirname,
  "../../../docs/openapi/kernel_openapi.json"
);
const kernelOpenApi = JSON.parse(fs.readFileSync(openApiPath, "utf8")) as {
  paths: Record<string, Record<string, any>>;
  components: { schemas: Record<string, any> };
};

const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function resolveSchemaRef(schema: any): any {
  if (!schema || typeof schema !== "object") return schema;
  if (typeof schema.$ref === "string") {
    const prefix = "#/components/schemas/";
    if (!schema.$ref.startsWith(prefix)) {
      throw new Error(`Unsupported OpenAPI $ref: ${schema.$ref}`);
    }
    const name = schema.$ref.slice(prefix.length);
    const resolved = kernelOpenApi.components.schemas[name];
    if (!resolved) {
      throw new Error(`Missing OpenAPI schema component: ${name}`);
    }
    return resolveSchemaRef(resolved);
  }
  return schema;
}

function assertMatchesOpenApiSchema(
  value: unknown,
  rawSchema: any,
  pointer = "$"
): void {
  const schema = resolveSchemaRef(rawSchema);
  if (!schema || Object.keys(schema).length === 0) {
    return;
  }

  if (Array.isArray(schema.oneOf)) {
    const errors: string[] = [];
    for (const branch of schema.oneOf) {
      try {
        assertMatchesOpenApiSchema(value, branch, pointer);
        return;
      } catch (err: any) {
        errors.push(err?.message || String(err));
      }
    }
    throw new Error(
      `${pointer} failed all oneOf branches: ${errors.join(" | ")}`
    );
  }

  const allowedTypes = Array.isArray(schema.type)
    ? schema.type
    : schema.type
      ? [schema.type]
      : [];

  if (value === null) {
    if (allowedTypes.includes("null") || schema.nullable === true) {
      return;
    }
    throw new Error(`${pointer} is null, expected ${allowedTypes.join("|")}`);
  }

  if (allowedTypes.length > 0) {
    const jsType = Array.isArray(value) ? "array" : typeof value;
    const typeMatches = allowedTypes.some((expectedType: string) => {
      if (expectedType === "integer") {
        return typeof value === "number" && Number.isInteger(value);
      }
      if (expectedType === "number") {
        return typeof value === "number" && Number.isFinite(value);
      }
      if (expectedType === "array") {
        return Array.isArray(value);
      }
      return jsType === expectedType;
    });
    if (!typeMatches) {
      throw new Error(
        `${pointer} has runtime type '${jsType}' (${JSON.stringify(value)}), expected ${allowedTypes.join("|")}`
      );
    }
  }

  if (typeof value === "string") {
    if (schema.pattern) {
      const re = new RegExp(schema.pattern);
      if (!re.test(value)) {
        throw new Error(
          `${pointer} ('${value}') failed pattern ${schema.pattern}`
        );
      }
    }
    if (schema.format === "uuid" && !UUID_REGEX.test(value)) {
      throw new Error(`${pointer} ('${value}') is not a valid UUID`);
    }
  }

  if (Array.isArray(value) && schema.items) {
    value.forEach((item, idx) => {
      assertMatchesOpenApiSchema(item, schema.items, `${pointer}[${idx}]`);
    });
  }

  if (value && typeof value === "object" && !Array.isArray(value)) {
    const record = value as Record<string, unknown>;
    for (const reqKey of schema.required || []) {
      if (!(reqKey in record) || record[reqKey] === undefined) {
        throw new Error(`${pointer} is missing required property '${reqKey}'`);
      }
    }
    if (schema.properties) {
      for (const [propName, propSchema] of Object.entries(schema.properties)) {
        if (propName in record && record[propName] !== undefined) {
          assertMatchesOpenApiSchema(
            record[propName],
            propSchema,
            `${pointer}.${propName}`
          );
        }
      }
    }
  }
}

function getResponseSchema(
  routeTemplate: string,
  method: string,
  status = "200"
) {
  const pathEntry = kernelOpenApi.paths[routeTemplate];
  expect(pathEntry, `Missing OpenAPI path ${routeTemplate}`).toBeDefined();
  const op = pathEntry[method.toLowerCase()];
  expect(op, `Missing ${method} on ${routeTemplate}`).toBeDefined();
  const schema = op.responses?.[status]?.content?.["application/json"]?.schema;
  expect(
    schema,
    `Missing ${status} JSON schema on ${method} ${routeTemplate}`
  ).toBeDefined();
  return schema;
}

describe("Demo MSW OpenAPI Contracts & Stateful Governance Workflows", () => {
  const store = createWebDemoStore();
  const server = setupServer();

  beforeAll(() => {
    server.listen({ onUnhandledRequest: "error" });
  });

  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    resetDemoRandom();
    store.reset();
    server.resetHandlers(
      ...buildWebGatewayHandlers({ forceDemo: true, store })
    );
  });

  afterEach(() => {
    server.resetHandlers();
    vi.unstubAllEnvs();
    resetDemoRandom();
  });

  afterAll(() => {
    server.close();
  });

  it("validates study rule packs, preview, Part 11 approval, pinning, and findings against generated OpenAPI schemas", async () => {
    const studyRef = DEMO_STUDY.ref.split("@")[0];

    const initialPacks: StudyRulePacksResponse =
      await kernelApi.listStudyRulePacks(studyRef);
    assertMatchesOpenApiSchema(
      initialPacks,
      getResponseSchema("/api/v1/kernel/studies/{studyRef}/rule-packs", "get"),
      "listStudyRulePacks"
    );
    expect(initialPacks.study_ref).toBe(studyRef);
    expect(initialPacks.rule_packs.length).toBeGreaterThanOrEqual(2);

    const approvedPack = initialPacks.rule_packs.find(
      (p) => p.status === "Approved" && p.pinned
    );
    const draftPack = initialPacks.rule_packs.find((p) => p.status === "Draft");
    expect(approvedPack).toBeDefined();
    expect(draftPack).toBeDefined();

    // Preview approved rule pack on study without mutating state
    const previewRes: RulePackPreviewResponse =
      await kernelApi.previewStudyRulePack(studyRef, approvedPack!.ref);
    assertMatchesOpenApiSchema(
      previewRes,
      getResponseSchema(
        "/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview",
        "post"
      ),
      "previewStudyRulePack"
    );
    expect(previewRes.forms_evaluated).toBeGreaterThanOrEqual(2);
    expect(previewRes.violation_count).toBeGreaterThanOrEqual(1);

    // Pinning an unapproved Draft pack must fail with 422 K.Standards.NotApproved
    await expect(
      kernelApi.declareStudyRulePacks({
        studyRef,
        rulePackRefs: [approvedPack!.ref, draftPack!.ref],
        reason: "Attempt to pin unapproved draft pack",
        idempotencyKey: "00000000-0000-4000-8000-000000009001",
      })
    ).rejects.toMatchObject({ status: 422 });

    // Obtain step-up signature token and approve the draft rule pack
    const stepUpRes = await fetch("/api/v1/auth/signature-verification", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        action: `/api/v1/kernel/library-items/${encodeURIComponent(draftPack!.ref.split("@")[0])}/approve`,
        semantic_action: "kernel.library_item.approve",
      }),
    });
    expect(stepUpRes.status).toBe(200);
    const { sig_token: sigToken } = await stepUpRes.json();

    const approvedResult: ApproveLibraryItemResponse =
      await kernelApi.approveLibraryItem({
        itemRef: draftPack!.ref,
        meaning: "Approved",
        sigToken,
        reason: "Rule pack governance approval",
        idempotencyKey: "00000000-0000-4000-8000-000000009002",
      });
    assertMatchesOpenApiSchema(
      approvedResult,
      getResponseSchema(
        "/api/v1/kernel/library-items/{itemRef}/approve",
        "post"
      ),
      "approveLibraryItem"
    );
    expect(approvedResult.status).toBe("Approved");

    // Pin both approved rule packs on CADENCE-101
    const pinnedResult: DeclareStudyStandardsResponse =
      await kernelApi.declareStudyRulePacks({
        studyRef,
        rulePackRefs: [approvedPack!.ref, draftPack!.ref],
        reason: "Pin approved conformance pack on CADENCE-101",
        idempotencyKey: "00000000-0000-4000-8000-000000009003",
      });
    assertMatchesOpenApiSchema(
      pinnedResult,
      getResponseSchema("/api/v1/kernel/studies/{studyRef}/standards", "put"),
      "declareStudyRulePacks"
    );
    expect(pinnedResult.pinned_rule_pack_refs).toHaveLength(2);
    expect(pinnedResult.rule_packs.every((p) => p.pinned)).toBe(true);

    // List validation findings and filter by status
    const findingsPage: ValidationFindingPageResponse =
      await kernelApi.listStudyValidationFindings(studyRef);
    assertMatchesOpenApiSchema(
      findingsPage,
      getResponseSchema(
        "/api/v1/kernel/studies/{studyRef}/validation/findings",
        "get"
      ),
      "listStudyValidationFindings"
    );
    expect(findingsPage.total).toBeGreaterThanOrEqual(2);
    const openFinding = findingsPage.findings.find((f) => f.status === "Open");
    const resolvedFinding = findingsPage.findings.find(
      (f) => f.status === "Resolved"
    );
    expect(openFinding).toBeDefined();
    expect(resolvedFinding).toBeDefined();

    const openFiltered = await kernelApi.listStudyValidationFindings(studyRef, {
      status: "Open",
    });
    expect(openFiltered.findings.every((f) => f.status === "Open")).toBe(true);

    // Inspect finding detail and structured provenance explanation
    const detail: ValidationFindingDetailResponse =
      await kernelApi.getValidationFinding(openFinding!.ref);
    assertMatchesOpenApiSchema(
      detail,
      getResponseSchema(
        "/api/v1/kernel/validation/findings/{findingRef}",
        "get"
      ),
      "getValidationFinding"
    );
    expect(detail.finding.ref).toBe(openFinding!.ref);
    expect(detail.explanation.steps.length).toBeGreaterThan(0);
  });

  it("evaluates pre-commit eCRF form previews against OpenAPI schema without mutating saved form values", async () => {
    const formId = "00000000-0000-0000-0000-000000002001"; // deid-ignore
    const beforeForm = await ecrfFormsApi.getForm(formId);
    const originalSysBp = beforeForm.items.find(
      (i) => i.item_id === "item-sysbp"
    );
    expect(originalSysBp?.value).toEqual({
      type: "quantity",
      value: "128",
      unit: "mm[Hg]",
      comparator: null,
    });

    // Normal draft value produces 0 warnings
    const cleanPreview: FormPreviewResponse = await ecrfFormsApi.previewValues(
      formId,
      [
        {
          item_id: "item-sysbp",
          value: {
            type: "quantity",
            value: "122",
            unit: "mm[Hg]",
            comparator: null,
          },
          expected_revision: 1,
          observation_id: null,
          observed_at: null,
        },
      ]
    );
    assertMatchesOpenApiSchema(
      cleanPreview,
      getResponseSchema("/api/v1/execution/forms/{formId}/preview", "post"),
      "previewValues(clean)"
    );
    expect(cleanPreview.form_id).toBe(formId);
    expect(cleanPreview.warnings).toHaveLength(0);

    // Out-of-range systolic BP + transposed diastolic BP produces inline rule warnings
    const breachPreview: FormPreviewResponse = await ecrfFormsApi.previewValues(
      formId,
      [
        {
          item_id: "item-sysbp",
          value: {
            type: "quantity",
            value: "215",
            unit: "mm[Hg]",
            comparator: null,
          },
          expected_revision: 1,
          observation_id: null,
          observed_at: null,
        },
        {
          item_id: "item-diabp",
          value: {
            type: "quantity",
            value: "220",
            unit: "mm[Hg]",
            comparator: null,
          },
          expected_revision: 1,
          observation_id: null,
          observed_at: null,
        },
      ]
    );
    assertMatchesOpenApiSchema(
      breachPreview,
      getResponseSchema("/api/v1/execution/forms/{formId}/preview", "post"),
      "previewValues(breach)"
    );
    const typedWarnings: components["schemas"]["Kernel_FormPreviewWarningResponse"][] =
      breachPreview.warnings;
    expect(typedWarnings.map((w) => w.rule_id)).toEqual(
      expect.arrayContaining([
        "VS-SYSBP-RANGE",
        "VS-DIABP-RANGE",
        "VS-BP-ORDER",
      ])
    );

    // Saved form data MUST remain completely unmutated after preview
    const afterForm = await ecrfFormsApi.getForm(formId);
    expect(afterForm).toEqual(beforeForm);
  });

  it("supports stateful lay-summary receipts/publishing, RBQM RACT matrix/dossier, and Aggregate Safety workflows", async () => {
    // 1. Lay Summaries
    const summariesRes = await fetch("/api/v1/payments/lay-summaries");
    expect(summariesRes.status).toBe(200);
    const summaries = await summariesRes.json();
    expect(summaries[0].study_id).toBe("CADENCE-101");

    const receiptsBeforeRes = await fetch(
      `/api/v1/payments/lay-summaries/${summaries[0].id}/receipts`
    );
    expect(receiptsBeforeRes.status).toBe(200);
    const receiptsBefore = await receiptsBeforeRes.json();

    const confirmRes = await fetch(
      `/api/v1/payments/lay-summaries/${summaries[0].id}/confirm-receipt`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          subject_id: "SUBJ-101-002",
          study_id: "CADENCE-101",
          comprehension_score: 5,
          comprehension_feedback: "Clear outcome charts.",
        }),
      }
    );
    expect(confirmRes.status).toBe(201);
    const createdReceipt = await confirmRes.json();
    expect(createdReceipt.subject_id).toBe("SUBJ-101-002");

    const receiptsAfterRes = await fetch(
      `/api/v1/payments/lay-summaries/${summaries[0].id}/receipts`
    );
    const receiptsAfter = await receiptsAfterRes.json();
    expect(receiptsAfter).toHaveLength(receiptsBefore.length + 1);

    const publishRes = await fetch("/api/v1/payments/lay-summaries", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        study_id: "CADENCE-101",
        protocol_id: "CADENCE-101-V3",
        title: "Updated Plain-Language Amendment Summary",
        plain_language_sections: { objective: "Updated cohort follow-up." },
      }),
    });
    expect(publishRes.status).toBe(201);
    const summariesAfter = await (
      await fetch("/api/v1/payments/lay-summaries")
    ).json();
    expect(summariesAfter).toHaveLength(summaries.length + 1);

    // 2. RBQM RACT Matrix, KRIs, Site Risk, and CSM Dossier
    const seedRes = await fetch(
      "/api/v1/quality/rbqm/ract/seed-default?study_id=CADENCE-101",
      { method: "POST" }
    );
    expect(seedRes.status).toBe(201);

    const createFactorRes = await fetch("/api/v1/quality/rbqm/ract/factors", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        study_id: "CADENCE-101",
        category: "IP_MANAGEMENT",
        risk_topic: "Cold-Chain Excursion During Transit",
        risk_description: "Depot shipment logger excursion risk.",
        probability_score: 3,
        impact_score: 3,
        detectability_score: 2,
        mitigation_strategy: "Continuous digital logger gating.",
        linked_kri_code: "KRI_PROTOCOL_DEVIATION",
        reason_for_change: "Add transit cold-chain risk",
      }),
    });
    expect(createFactorRes.status).toBe(201);
    const factor = await createFactorRes.json();
    expect(factor.risk_tier).toBe("HIGH");

    const updateFactorRes = await fetch(
      `/api/v1/quality/rbqm/ract/factors/${factor.id}`,
      {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          probability_score: 1,
          impact_score: 2,
          detectability_score: 2,
          reason_for_change: "Mitigation lowered probability",
        }),
      }
    );
    expect(updateFactorRes.status).toBe(200);
    expect((await updateFactorRes.json()).risk_tier).toBe("LOW");

    const krisRes = await fetch("/api/v1/quality/rbqm/kris");
    expect(krisRes.status).toBe(200);
    expect((await krisRes.json()).length).toBeGreaterThanOrEqual(3);

    const computeProfilesRes = await fetch(
      "/api/v1/quality/rbqm/site-risk-profiles/compute?study_id=CADENCE-101",
      { method: "POST" }
    );
    expect(computeProfilesRes.status).toBe(200);
    expect((await computeProfilesRes.json()).length).toBeGreaterThanOrEqual(2);

    const dossierRes = await fetch(
      "/api/v1/quality/rbqm/csm/dossiers/compile",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          study_id: "CADENCE-101",
          signer_name: "designer.lead",
          signer_role: "Head of Clinical Quality Assurance",
        }),
      }
    );
    expect(dossierRes.status).toBe(201);
    expect((await dossierRes.json()).status).toBe("SEALED_SIGNED");

    const deleteFactorRes = await fetch(
      `/api/v1/quality/rbqm/ract/factors/${factor.id}`,
      { method: "DELETE" }
    );
    expect(deleteFactorRes.status).toBe(200);

    // 3. Aggregate Safety (Exposure, DSUR compile, QPPV sign, Rule triage)
    const expRes = await fetch("/api/v1/safety/exposure/calculate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ study_id: "CADENCE-101" }),
    });
    expect(expRes.status).toBe(200);
    expect(
      (await expRes.json()).metrics.total_cumulative_subjects
    ).toBeGreaterThan(0);

    const compileRes = await fetch("/api/v1/safety/periodic-reports/compile", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        report_type: "DSUR",
        study_id: "CADENCE-101",
        compound_name: "CAD-101",
      }),
    });
    expect(compileRes.status).toBe(201);
    const report = await compileRes.json();
    expect(report.status).toBe("IN_REVIEW");
    expect(report.snapshot_hash).toMatch(/^sha256-demo-/);

    const syntheticQppvAssertion = "demo-qppv-assertion";
    const qppvRes = await fetch(
      `/api/v1/safety/periodic-reports/${report.id}/qppv-sign`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          user_id: "safety.officer",
          role: "Qualified Person for Pharmacovigilance (QPPV)",
          meaning_of_signature:
            "Executive QPPV Approval of Periodic Safety Report",
          signature_secret: syntheticQppvAssertion,
        }),
      }
    );
    expect(qppvRes.status).toBe(200);
    const signedReport = await qppvRes.json();
    expect(signedReport.status).toBe("APPROVED");
    expect(signedReport.qppv_manifest.verification_status).toBe("VALID");

    const triageRes = await fetch("/api/v1/safety/rules/evaluate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        case_id: "ICSR-CAD-01",
        study_id: "CADENCE-101",
        seriousness_death: true,
        causality: "SUSPECTED",
        expectedness: "UNEXPECTED",
      }),
    });
    expect(triageRes.status).toBe(200);
    const triage = await triageRes.json();
    expect(triage.is_susar).toBe(true);
    expect(triage.shortest_timeframe_days).toBe(7);

    // 4. Fail-closed boundary check remains intact
    const unmocked = await fetch("/api/v1/unmocked-contract-check");
    expect(unmocked.status).toBe(501);
    expect((await unmocked.json()).error).toBe("DEMO_UNMOCKED_ENDPOINT");
  });
});
