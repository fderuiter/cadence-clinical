import { http, HttpResponse } from "msw";
import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import { readJsonBody } from "./shared.js";

function buildDemoCurriculum(id = "curr-demo-01", includeStatus = true) {
  return {
    id,
    study_id: "DEMO-01",
    title: "DEMO-01 Protocol v2.0 Core Qualification",
    version: "2.0",
    ...(includeStatus ? { status: "ACTIVE" } : {}),
    modules: [
      {
        id: "mod-01",
        title: "Protocol Synopsis & Eligibility",
        content: "Synthetic protocol qualification module.",
        quiz_questions: [],
      },
    ],
  };
}

export function createEconsentLabsDctAndTrainingHandlers() {
  return [
    http.get("*/api/v1/econsent/templates", () =>
      HttpResponse.json([
        {
          template_id: "tmpl-demo-01",
          study_id: "DEMO-01",
          template_name: "DEMO-01 Master Informed Consent",
          protocol_version: "2.0",
          status: "PUBLISHED",
          version_index: 1,
          requires_reconsent: false,
          notification_channels: ["Email"],
          clauses: ["Voluntary Participation & Clinical Study Overview"],
          workflow_steps: [],
        },
      ])
    ),
    http.get("*/api/v1/econsent/clauses", () => HttpResponse.json([])),
    http.get("*/api/v1/econsent/translations", () => HttpResponse.json([])),
    http.get("*/api/v1/fileshare/files", () => {
      const xrefEntries = [9, 52, 101, 202, 318].map(
        (offset) => `${String(offset).padStart(10, "0")} ${"0".repeat(5)} n `
      );
      const minimalPdfText = [
        "%PDF-1.4",
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj",
        "2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj",
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj",
        "4 0 obj<</Length 68>>stream",
        "BT /F1 18 Tf 72 720 Td (Cadence Clinical Protocol Synopsis DEMO-01) Tj ET",
        "endstream",
        "endobj",
        "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj",
        "xref",
        "0 6",
        `${"0".repeat(10)} 65535 f `,
        ...xrefEntries,
        "trailer<</Size 6/Root 1 0 R>>",
        "startxref",
        "386",
        "%%EOF",
      ].join("\n");
      const pdfBase64 = btoa(minimalPdfText);

      return HttpResponse.json([
        {
          id: "file-demo-01",
          filename: "DEMO-01-Protocol-Synopsis.pdf",
          content_type: "application/pdf",
          mime_type: "application/pdf",
          download_url: `data:application/pdf;base64,${pdfBase64}`,
          size_bytes: 245760,
          study_id: "DEMO-01",
          site_id: "SITE-101",
          uploaded_by: "designer_demo",
          created_at: "2026-07-01T10:00:00Z", // deid-ignore
          is_on_hold: false,
          approval_status: "APPROVED",
          version_number: 1,
        },
        {
          id: "file-demo-02",
          filename: "DEMO-01-Site-Initiation-Briefing.mp4",
          content_type: "video/mp4",
          mime_type: "video/mp4",
          transcode_status: "READY",
          size_bytes: 12582912,
          study_id: "DEMO-01",
          site_id: "SITE-101",
          uploaded_by: "cra_demo",
          created_at: "2026-07-02T14:30:00Z", // deid-ignore
          is_on_hold: false,
          approval_status: "APPROVED",
          version_number: 1,
        },
      ]);
    }),
    http.get(
      "*/api/v1/fileshare/files/:fileId/stream-url",
      ({ params, request }) => {
        const reqUrl = new URL(request.url);
        const apiIdx = reqUrl.pathname.indexOf("/api/v1/fileshare/");
        const prefix = apiIdx > 0 ? reqUrl.pathname.slice(0, apiIdx) : "";
        return HttpResponse.json({
          file_id: String(params.fileId),
          stream_url: `${prefix}/api/v1/fileshare/demo-stream/master.m3u8`,
          expires_in: 300,
          transcode_status: "READY",
          message: "Demo HLS stream ready",
        });
      }
    ),
    http.get(
      "*/api/v1/fileshare/demo-stream/master.m3u8",
      () =>
        new HttpResponse(
          [
            "#EXTM3U",
            "#EXT-X-VERSION:3",
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360",
            "prog_index.m3u8",
            "",
          ].join("\n"),
          {
            status: 200,
            headers: { "Content-Type": "application/vnd.apple.mpegurl" },
          }
        )
    ),
    http.get(
      "*/api/v1/fileshare/demo-stream/prog_index.m3u8",
      () =>
        new HttpResponse(
          [
            "#EXTM3U",
            "#EXT-X-VERSION:3",
            "#EXT-X-TARGETDURATION:2",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXTINF:1.0,",
            "seg0.ts",
            "#EXT-X-ENDLIST",
            "",
          ].join("\n"),
          {
            status: 200,
            headers: { "Content-Type": "application/vnd.apple.mpegurl" },
          }
        )
    ),
    http.get("*/api/v1/fileshare/demo-stream/seg0.ts", () => {
      // Three 188-byte MPEG-TS packets (sync byte 0x47, null PID 0x1FFF stuffing)
      const tsBytes = new Uint8Array(188 * 3);
      tsBytes.fill(0xff);
      for (let pkt = 0; pkt < 3; pkt++) {
        const offset = pkt * 188;
        tsBytes[offset] = 0x47;
        tsBytes[offset + 1] = 0x1f;
        tsBytes[offset + 2] = 0xff;
        tsBytes[offset + 3] = 0x10 | (pkt & 0x0f);
      }
      return new HttpResponse(tsBytes, {
        status: 200,
        headers: { "Content-Type": "video/mp2t" },
      });
    }),
    http.post("*/api/v1/fileshare/audit/playback", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        id: "audit-demo-playback-01",
        file_record_id: body?.file_record_id || "file-demo-02",
        event: body?.event || "PLAY_START",
        session_id: body?.session_id || "sess-demo",
        recorded_at: demoNowIso(nextDemoStep("fileshare-playback-audit")),
      });
    }),
    http.get("*/api/v1/fileshare/files/:fileId/grants", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/guest-links", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/audit-logs", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/activity", () =>
      HttpResponse.json({ items: [], total: 0 })
    ),
    http.get("*/api/v1/execution/lab-ranges", () =>
      HttpResponse.json([
        {
          id: "lr-demo-01",
          study_id: "DEMO-01",
          lab_source: "CENTRAL",
          test_code: "ALT",
          test_name: "Alanine Aminotransferase",
          sex: "ALL",
          age_low: 18,
          age_high: 85,
          ref_low: 7,
          ref_high: 40,
          unit: "U/L",
          loinc_code: "1742-6",
          is_active: true,
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/toxicity/review-queue", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/labs/certifications", () =>
      HttpResponse.json([
        {
          id: "cert-demo-01", // deid-ignore
          site_id: "SITE-101",
          lab_name: "SITE-101 Clinical Pathology Laboratory",
          cert_type: "CLIA",
          cert_number: "CLIA-DEMO-101",
          effective_date: "2026-01-01", // deid-ignore
          expiration_date: "2028-01-01", // deid-ignore
          status: "VERIFIED",
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/batch-status", () =>
      HttpResponse.json([
        {
          batch_id: "BATCH-DEMO-01",
          study_id: "DEMO-01",
          status: "COMPLETED",
          total_records: 24,
          ingested_count: 24,
          staged_aes_count: 0,
          created_at: "2026-07-15T09:00:00Z", // deid-ignore
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/loinc-catalog", () =>
      HttpResponse.json({
        items: [
          {
            loinc_code: "1742-6",
            test_code: "ALT",
            component: "Alanine aminotransferase",
            standard_unit: "U/L",
            category: "Chemistry",
          },
          {
            loinc_code: "2160-0",
            test_code: "CREAT",
            component: "Creatinine",
            standard_unit: "umol/L",
            category: "Chemistry",
          },
        ],
      })
    ),
    http.post("*/api/v1/execution/labs/convert-unit", async ({ request }) => {
      const body = await readJsonBody(request);
      const val = Number(body?.value ?? 1) || 1;
      const factor = 88.4;
      return HttpResponse.json({
        converted_value: Number((val * factor).toFixed(2)),
        to_unit: body?.to_unit || "umol/L",
        factor,
        loinc_code: "2160-0",
      });
    }),
    http.post(
      "*/api/v1/execution/labs/toxicity/evaluate",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const val = Number(body?.value ?? 25);
        const uln = Number(body?.ref_high ?? 40) || 40;
        const grade = val > uln * 5 ? 3 : val > uln * 3 ? 2 : val > uln ? 1 : 0;
        return HttpResponse.json({
          ctcae_grade: grade,
          toxicity_description:
            grade === 0
              ? "Within normal reference limits"
              : `CTCAE v5.0 Grade ${grade} elevation`,
          is_critical_ae_trigger: grade >= 3,
          is_hys_law_warning: false,
        });
      }
    ),
    http.get("*/api/v1/interop/sensors/devices", () =>
      HttpResponse.json({ data: { devices: [] } })
    ),
    http.get("*/api/v1/interop/sensors/daily-summaries", () =>
      HttpResponse.json({ data: { summaries: [] } })
    ),
    http.get("*/api/v1/interop/sensors/safety-alerts", () =>
      HttpResponse.json({ data: { alerts: [] } })
    ),
    http.get("*/api/v1/interop/sensors/metrics/:studyId", () =>
      HttpResponse.json({
        data: {
          metrics: {
            active_devices: 2,
            daily_compliance_pct: 96.5,
            open_safety_alerts: 0,
            ingested_epochs_24h: 1440,
          },
        },
      })
    ),
    http.get("*/api/v1/interop/dct/telehealth/sessions", () =>
      HttpResponse.json({ sessions: [] })
    ),
    http.get("*/api/v1/interop/dct/econsent/pediatric-assent", () =>
      HttpResponse.json({ assents: [] })
    ),
    http.get("*/api/v1/interop/dct/dtp/shipments", () =>
      HttpResponse.json({ shipments: [] })
    ),
    http.get("*/api/v1/interop/dct/home-health/visits", () =>
      HttpResponse.json({ visits: [] })
    ),
    http.get("*/api/v1/interop/dct/metrics/:studyId", () =>
      HttpResponse.json({
        total_telehealth_sessions: 1,
        active_video_rooms: 0,
        pediatric_assents_completed: 1,
        total_dtp_shipments: 2,
        active_cold_chain_shipments: 1,
        temperature_excursion_alerts: 0,
      })
    ),
    http.get("*/api/v1/training/curricula", () =>
      HttpResponse.json([buildDemoCurriculum("curr-demo-01", true)])
    ),
    http.get("*/api/v1/training/assignments", () =>
      HttpResponse.json([
        {
          id: "asn-demo-01",
          study_id: "DEMO-01",
          curriculum_id: "curr-demo-01",
          status: "COMPLETED",
          time_spent_seconds: 180,
          quiz_score: 100,
        },
      ])
    ),
    http.get(
      "*/api/v1/training/assignments/:assignmentId/certificate",
      ({ params }) =>
        HttpResponse.json({
          certificate_id: "cert-trn-01", // deid-ignore
          assignment_id: params.assignmentId,
          status: "ISSUED",
          issued_at: "2026-07-02T12:00:00Z", // deid-ignore
          sha256_hash: "sha256-demo-trn-cert",
        })
    ),
    http.post("*/api/v1/training/assignments/:assignmentId/progress", () =>
      HttpResponse.json({ status: "OK" })
    ),
    http.get("*/api/v1/training/assignments/:assignmentId", ({ params }) =>
      HttpResponse.json({
        id: params.assignmentId,
        study_id: "DEMO-01",
        status: "COMPLETED",
        time_spent_seconds: 180,
        quiz_score: 100,
        curriculum: buildDemoCurriculum("curr-demo-01", false),
      })
    ),
    http.get("*/api/v1/training/curricula/:curriculumId", ({ params }) =>
      HttpResponse.json(buildDemoCurriculum(String(params.curriculumId), true))
    ),
    http.post(
      "*/api/v1/training/curricula/batch-assign",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-batch-assign");
        return HttpResponse.json({
          batch_transaction_id: `TRN-BATCH-${step}`,
          curriculum_id: body?.curriculum_id || "curr-demo-01",
          study_id: body?.study_id || "CADENCE-101",
          total_eligible: 1,
          total_assigned: 1,
          total_skipped: 0,
          assignments: [
            {
              id: `asn-batch-${step}`,
              study_id: body?.study_id || "CADENCE-101",
              curriculum_id: body?.curriculum_id || "curr-demo-01",
              status: "ASSIGNED",
            },
          ],
          skipped_items: [],
          executed_at: demoNowIso(step),
        });
      }
    ),
    http.get("*/api/v1/training/delegations", () => HttpResponse.json([])),
    http.post(
      "*/api/v1/training/delegations/bulk-signoff",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-delegations-signoff");
        const ids = Array.isArray(body?.delegation_ids)
          ? body.delegation_ids
          : [];
        return HttpResponse.json({
          batch_transaction_id: `DLG-BATCH-${step}`,
          total_requested: ids.length,
          total_signed: ids.length,
          total_failed: 0,
          signed_delegations: ids.map((id) => ({
            id,
            status: "SIGNED",
          })),
          failed_items: [],
          sha256_seal: `sha256-demo-dlg-${step}`,
          etmf_artifact_id: `DOC-DLG-${step}`,
          executed_at: demoNowIso(step),
        });
      }
    ),
    http.get("*/api/v1/training/gating-status", () =>
      HttpResponse.json({ is_gated: false })
    ),
  ];
}
