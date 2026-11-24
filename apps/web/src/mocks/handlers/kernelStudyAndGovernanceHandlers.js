import { http, HttpResponse } from "msw";
import { demoNowIso } from "../../utils/demoMode.js";
import { resolveDemoCaller } from "../demoPolicy.js";
import { parseDemoProtocolImportRequest } from "../demoStudyImport.js";
import {
  DEFAULT_STUDY_REF,
  decodeReasonHeader,
  readIdempotencyKey,
  readJsonBody,
  readRequiredReasonHeader,
} from "./shared.js";

async function sha256Hex(bytes) {
  const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function resolveExportCutWindow(url) {
  const generatedAt = demoNowIso();
  const cutAt =
    url.searchParams.get("known_at") ||
    url.searchParams.get("as_of") ||
    generatedAt;
  const validAt = url.searchParams.get("valid_at") || cutAt;
  return { generatedAt, cutAt, validAt };
}

async function buildSignedTextHeaders(
  bodyText,
  contentType,
  generatedAt,
  extra = {}
) {
  const bytes = new TextEncoder().encode(bodyText);
  return {
    "Content-Type": contentType,
    "X-Content-Sha256": await sha256Hex(bytes),
    "X-Generated-At": generatedAt,
    ...extra,
  };
}

export function createKernelStudyAndGovernanceHandlers(store) {
  return [
    http.get("*/api/v1/kernel/studies", () =>
      HttpResponse.json(store.listStudies())
    ),
    http.post("*/api/v1/kernel/studies", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey = readIdempotencyKey(request);
      const result = store.createStudy(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/organizations", ({ request }) => {
      const url = new URL(request.url);
      const kind = url.searchParams.get("kind") || undefined;
      return HttpResponse.json(store.listOrganizations(kind));
    }),
    http.post("*/api/v1/kernel/organizations", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey = readIdempotencyKey(request);
      const result = store.createOrganization(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/sites", () =>
      HttpResponse.json(store.listSites())
    ),
    http.post("*/api/v1/kernel/sites", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey = readIdempotencyKey(request);
      const result = store.createSite(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/studies/:studyRef/sites", ({ params }) => {
      const result = store.listStudySites(
        decodeURIComponent(String(params.studyRef))
      );
      if (Array.isArray(result)) {
        return HttpResponse.json(result);
      }
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data);
    }),
    http.post(
      "*/api/v1/kernel/studies/:studyRef/sites",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const reason = readRequiredReasonHeader(request);
        const idempotencyKey = readIdempotencyKey(request);
        const result = store.linkSiteToStudy(
          decodeURIComponent(String(params.studyRef)),
          body,
          { reason, idempotencyKey }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data, {
          status: result.replayed ? 200 : 201,
        });
      }
    ),
    http.get("*/api/v1/kernel/protocol-templates", () =>
      HttpResponse.json(store.listProtocolTemplates())
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/protocol-versions",
      ({ params }) =>
        HttpResponse.json(
          store.listProtocolVersions(
            decodeURIComponent(String(params.studyRef))
          )
        )
    ),
    http.post(
      "*/api/v1/kernel/studies/:studyRef/protocol-imports",
      async ({ params, request }) => {
        const reason = readRequiredReasonHeader(request);
        const idempotencyKey = readIdempotencyKey(request);
        const parsedRequest = await parseDemoProtocolImportRequest(request);
        const result = store.importProtocolDraft(
          decodeURIComponent(String(params.studyRef)),
          parsedRequest,
          { reason, idempotencyKey }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/design-summary",
      ({ params }) =>
        HttpResponse.json(
          store.getProtocolDesignSummary(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/schedule-of-activities",
      ({ params }) =>
        HttpResponse.json(
          store.getScheduleOfActivities(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/impact",
      ({ params }) =>
        HttpResponse.json(
          store.getAmendmentImpact(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get("*/api/v1/kernel/protocol-versions/:versionRef", ({ params }) =>
      HttpResponse.json(
        store.getProtocolVersion(decodeURIComponent(String(params.versionRef)))
      )
    ),
    http.get("*/api/v1/kernel/studies/:studyRef/proposals", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listProposals({
          status: url.searchParams.get("status") || undefined,
          limit: url.searchParams.get("limit") || undefined,
        })
      );
    }),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks",
      ({ params, request }) => {
        const url = new URL(request.url);
        const activeOnly = url.searchParams.get("active_only") === "true";
        const now = new Date().toISOString();
        const studyRef = decodeURIComponent(String(params.studyRef));
        return HttpResponse.json({
          study_ref: studyRef,
          study_code: "CAD-ONC-2026",
          valid_at: now,
          known_at: now,
          locks: store.listKernelLocks(activeOnly),
          hidden: { more_exist: false, reasons: [] },
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks/readiness",
      ({ params, request }) => {
        const url = new URL(request.url);
        const studyRef = decodeURIComponent(String(params.studyRef));
        const scopeRef = url.searchParams.get("scope_ref") || undefined;
        return HttpResponse.json(store.getLockReadiness(studyRef, scopeRef));
      }
    ),
    http.post("*/api/v1/kernel/locks", async ({ request }) => {
      const body = await readJsonBody(request);
      const result = store.commitKernelLock(body, decodeReasonHeader(request));
      if (result.conflict) {
        return HttpResponse.json(result.problem, { status: 409 });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.post(
      "*/api/v1/kernel/locks/:lockRef/release",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const lockRef = decodeURIComponent(String(params.lockRef));
        const reason = decodeReasonHeader(request) || "Released in demo";
        return HttpResponse.json(
          store.releaseKernelLock(lockRef, body, reason)
        );
      }
    ),
    http.get("*/api/v1/kernel/work-queues", () =>
      HttpResponse.json({
        queues: [],
      })
    ),
    http.post("*/api/v1/kernel/work-queues", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000900",
          code: body?.code || "custom_queue",
          version: 1,
          title: body?.title || "Custom Queue",
          capability: body?.capability || "observation.verify",
          verb_class: body?.verb_class || "Execute",
          source: body?.source || "observation.awaiting_verification",
        },
        { status: 201 }
      );
    }),
    http.get("*/api/v1/kernel/studies/:studyRef/validation/findings", () =>
      HttpResponse.json({
        total: 0,
        findings: [],
      })
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/exports",
      async ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "csv").toLowerCase();
        const studyRef = decodeURIComponent(String(params.studyRef));
        const { generatedAt, cutAt, validAt } = resolveExportCutWindow(url);
        const studyExportHeaders = (bodyText, contentType, extra = {}) =>
          buildSignedTextHeaders(bodyText, contentType, generatedAt, {
            "X-Hidden-More-Exist": "false",
            ...extra,
          });
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          const xml = `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="DEMO.${studyRef}" CreationDateTime="${cutAt}" ODMVersion="1.3.2"><Study OID="${studyRef}"/></ODM>`;
          return new HttpResponse(xml, {
            headers: await studyExportHeaders(xml, "application/xml"),
          });
        }
        if (
          format === "dataset_json" ||
          format === "dataset-json" ||
          format === "json"
        ) {
          const jsonText = JSON.stringify({
            datasetJSONCreationDateTime: cutAt,
            study_ref: studyRef,
            valid_at: validAt,
            known_at: cutAt,
            hidden: { more_exist: false, reasons: [] },
            itemGroupData: {},
          });
          return new HttpResponse(jsonText, {
            headers: await studyExportHeaders(jsonText, "application/json"),
          });
        }
        if (
          format === "sas_bundle" ||
          format === "r_bundle" ||
          format === "study_archive"
        ) {
          // Valid empty-archive PK\x05\x06 End of Central Directory (22 bytes) for browser/unit download checks
          const eocd = new Uint8Array([
            0x50, 0x4b, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
          ]);
          return new HttpResponse(eocd, {
            headers: {
              "Content-Type": "application/zip",
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${format}.zip"`,
              "X-Content-Sha256": await sha256Hex(eocd),
              "X-Generated-At": generatedAt,
              "X-Hidden-More-Exist": "false",
            },
          });
        }
        const csvText = "STUDYID,USUBJID\nCAD-ONC-2026,SUBJ-1001\n";
        return new HttpResponse(csvText, {
          headers: await studyExportHeaders(csvText, "text/csv"),
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/enrollments/:enrollmentRef/casebook",
      async ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "json").toLowerCase();
        const enrollmentRef = decodeURIComponent(String(params.enrollmentRef));
        const { generatedAt, cutAt, validAt } = resolveExportCutWindow(url);
        const casebookHeaders = (bodyText, contentType, extra = {}) =>
          buildSignedTextHeaders(bodyText, contentType, generatedAt, extra);
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          const xml = `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="CASEBOOK.${enrollmentRef}" CreationDateTime="${cutAt}" ODMVersion="1.3.2"><Study OID="CAD-ONC-2026"><GlobalVariables><StudyName>CAD-ONC-2026</StudyName><StudyDescription>CAD-ONC-2026</StudyDescription><ProtocolName>CAD-ONC-2026</ProtocolName></GlobalVariables><MetaDataVersion OID="MDV.1" Name="Protocol v1"/></Study><ClinicalData StudyOID="CAD-ONC-2026" MetaDataVersionOID="MDV.1"><SubjectData SubjectKey="${enrollmentRef}"/></ClinicalData></ODM>`;
          return new HttpResponse(xml, {
            headers: await casebookHeaders(xml, "application/xml"),
          });
        }
        if (format === "html") {
          const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Casebook ${enrollmentRef}</title></head><body><h1>Participant Casebook</h1><p>${enrollmentRef}</p><p>Known at ${cutAt}</p></body></html>`;
          return new HttpResponse(html, {
            headers: await casebookHeaders(html, "text/html; charset=utf-8"),
          });
        }
        if (format === "pdf") {
          // deid-ignore: PDF-1.7 10-digit xref byte offsets are structural PDF syntax, not telephone numbers
          const pdfText = `%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\nxref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \ntrailer << /Size 3 /Root 1 0 R >>\nstartxref\n115\n%%EOF\n`; // deid-ignore
          return new HttpResponse(pdfText, {
            headers: await casebookHeaders(pdfText, "application/pdf", {
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${enrollmentRef.replace(/[^a-zA-Z0-9_-]/g, "_")}.casebook.pdf"`,
            }),
          });
        }
        const jsonText = JSON.stringify({
          enrollment_ref: enrollmentRef,
          valid_at: validAt,
          known_at: cutAt,
          generated_at: cutAt,
          observations: [],
        });
        return new HttpResponse(jsonText, {
          headers: await casebookHeaders(jsonText, "application/json"),
        });
      }
    ),
    http.get("*/api/v1/kernel/proposals/:proposalRef", ({ params }) =>
      HttpResponse.json(
        store.getProposal(decodeURIComponent(String(params.proposalRef)))
      )
    ),
    http.post(
      "*/api/v1/kernel/proposals/:proposalRef/rejection",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const caller = resolveDemoCaller(request);
        const reason =
          readRequiredReasonHeader(request) ||
          (typeof body?.reason === "string" ? body.reason.trim() : "") ||
          decodeReasonHeader(request);
        return HttpResponse.json(
          store.rejectProposal(
            decodeURIComponent(String(params.proposalRef)),
            reason,
            caller
          )
        );
      }
    ),
    http.get("*/api/v1/kernel/activity", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.getActivityPage({ limit: url.searchParams.get("limit") || 25 })
      );
    }),
    http.post("*/api/v1/kernel/simulation/simulate", async ({ request }) => {
      const body = await readJsonBody(request);
      const now = new Date().toISOString();
      const targetRef =
        body?.stored_version_ref || body?.base_version_ref || DEFAULT_STUDY_REF;
      return HttpResponse.json({
        scope: DEFAULT_STUDY_REF,
        as_of: { valid_at: now, known_at: now },
        candidate: {
          kind: "ProtocolVersion",
          version: body?.stored_version_ref || null,
          base_version: body?.base_version_ref || null,
          content_hash: "sha256:demo-sim-cand",
          base_content_hash: body?.base_version_ref
            ? "sha256:demo-sim-base"
            : null,
        },
        engine_versions: [
          { name: "protocol.expected_schedule", version: "1" },
          { name: "protocol.matrix_gap", version: "1" },
          { name: "protocol.structural_diff", version: "1" },
        ],
        input_hash: "sha256-demo-sim-in",
        result_hash: "sha256-demo-sim-out",
        structural_diff: [],
        schedule_changes: [],
        masked: [],
        dependents: store.getDependents(targetRef),
        matrix_gaps: [],
        is_empty: true,
      });
    }),
    http.post("*/api/v1/kernel/protocols/matrix-gap-check", () =>
      HttpResponse.json([])
    ),
    http.post("*/api/v1/ai/intent/route", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        query: body?.query || "",
        recommendations: [
          {
            id: "rec-study-canvas",
            name: "Open Clinical Study Workspace",
            description:
              "Inspect DEMO-01 participants, SoA, and site readiness.",
            category: "Navigation",
            icon: "compass",
            target_route: "/studies/DEMO-01",
            action: null,
            confidence: 0.96,
            roles: ["sponsor_admin", "crc", "cra", "data_manager"],
          },
        ],
      });
    }),
  ];
}
