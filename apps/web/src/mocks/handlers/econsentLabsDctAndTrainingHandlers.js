import { http, HttpResponse } from "msw";
import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import { readJsonBody } from "./shared.js";

function buildDemoCurriculum(id = "curr-demo-01") {
  return {
    id,
    study_id: "DEMO-01",
    protocol_version: "2.0",
    protocol_version_number: 2,
    qualification_code: "protocol_training",
    passing_score: 0.8,
    title: "DEMO-01 Protocol v2.0 Core Qualification",
    description:
      "Protocol Amendment v2.0 procedural updates, eligibility criteria, and endpoint capture guidelines.",
    is_active: true,
    created_at: "2026-07-01T10:00:00Z", // deid-ignore
    modules: [
      {
        id: "mod-01",
        title: "Eligibility & Screening Updates",
        section_ref: "Section 4.1",
        summary:
          "Updated baseline eGFR inclusion threshold (>= 45 mL/min/1.73m2) and seated blood pressure stabilization window.",
        key_changes: [
          "Minimum 5-minute seated rest required prior to triplicate cuff readings.",
          "Updated central laboratory eGFR eligibility cut-off documented prior to randomization.",
        ],
        estimated_minutes: 10,
      },
      {
        id: "mod-02",
        title: "Endpoint Capture & Safety Escalation",
        section_ref: "Section 7.3",
        summary:
          "Mandatory Grade 3 CTCAE hepatotoxicity escalation workflow and CDASH vital signs recording tolerances.",
        key_changes: [
          "Report ALT > 3x ULN with concurrent bilirubin elevation within 24 hours.",
          "Verify Part 11 qualification before recording or correcting visit eCRF observations.",
        ],
        estimated_minutes: 12,
      },
    ],
    quiz_questions: [
      {
        id: "q-01",
        prompt:
          "What seated rest duration is required before recording baseline systolic blood pressure?",
        options: [
          { id: "opt-a", label: "At least 5 minutes of seated rest" },
          { id: "opt-b", label: "Immediate standing measurement" },
          { id: "opt-c", label: "Supine measurement after ambulation" },
        ],
        correct_option_id: "opt-a",
      },
      {
        id: "q-02",
        prompt:
          "Within what timeframe must suspected Hy's Law laboratory elevations be escalated?",
        options: [
          { id: "opt-a", label: "Within 24 hours of laboratory notification" },
          {
            id: "opt-b",
            label: "At the next routine interim monitoring visit",
          },
          { id: "opt-c", label: "At end-of-study database lock" },
        ],
        correct_option_id: "opt-a",
      },
    ],
  };
}

function createInitialTrainingState() {
  return {
    curricula: [buildDemoCurriculum("curr-demo-01")],
    assignments: [
      {
        id: "asn-demo-pending-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        user_id: "usr-crc-01",
        user_role: "CRC",
        curriculum_id: "curr-demo-01",
        status: "PENDING",
        assigned_at: "2026-07-02T09:00:00Z", // deid-ignore
        completed_at: null,
        quiz_score: null,
        etmf_artifact_id: null,
        etmf_document_id: null,
        kernel_artifact_version_ref: null,
        kernel_qualification_ref: null,
      },
      {
        id: "asn-demo-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        user_id: "usr-pi-01",
        user_role: "Principal Investigator",
        curriculum_id: "curr-demo-01",
        status: "COMPLETED",
        assigned_at: "2026-07-01T11:00:00Z", // deid-ignore
        completed_at: "2026-07-02T12:00:00Z", // deid-ignore
        quiz_score: 100,
        etmf_artifact_id: "05.02.04",
        etmf_document_id: "00000000-0000-0000-0000-000000009401", // deid-ignore
        kernel_artifact_version_ref:
          "artifact_version:00000000-0000-0000-0000-000000009501", // deid-ignore
        kernel_qualification_ref:
          "qualification:00000000-0000-0000-0000-000000009601", // deid-ignore
      },
    ],
    studyStaff: [
      {
        user_id: "usr-crc-01",
        user_name: "Site Coordinator 01",
        site_id: "SITE-101",
        role: "CRC",
        grant_id: "00000000-0000-0000-0000-000000008101", // deid-ignore
      },
      {
        user_id: "usr-subi-01",
        user_name: "Sub-Investigator 01, MD",
        site_id: "SITE-101",
        role: "Sub-Investigator",
        grant_id: "00000000-0000-0000-0000-000000008102", // deid-ignore
      },
      {
        user_id: "usr-pi-01",
        user_name: "Investigator Lead 01, MD",
        site_id: "SITE-101",
        role: "Principal Investigator",
        grant_id: "00000000-0000-0000-0000-000000008103", // deid-ignore
      },
      {
        user_id: "usr-crc-02",
        user_name: "Site Coordinator 02",
        site_id: "SITE-102",
        role: "CRC",
        grant_id: "00000000-0000-0000-0000-000000008104", // deid-ignore
      },
    ],
  };
}

export function createEconsentLabsDctAndTrainingHandlers(store) {
  let trainingState = createInitialTrainingState();
  store?.onReset?.(() => {
    trainingState = createInitialTrainingState();
  });
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
    http.get("*/api/v1/interop/esource/candidates", ({ request }) => {
      const url = new URL(request.url);
      const studyId = url.searchParams.get("study_id") || "CADENCE-101";
      const subjectId = url.searchParams.get("subject_id") || "SUBJ-1001";
      return HttpResponse.json({
        candidates: [
          {
            id: "cand-demo-1",
            study_id: studyId,
            site_id: "SITE-101",
            subject_id: subjectId,
            subject_pseudonym: subjectId,
            ehr_system_id: "EPIC-HEALTH",
            fhir_resource_type: "Observation",
            fhir_resource_id: "obs-sysbp-1",
            source_timestamp: "2026-08-02T10:00:00Z",
            target_domain: "VS",
            target_variable: "SYSBP",
            cdash_testcd: "SYSBP",
            cdash_test: "Systolic Blood Pressure",
            candidate_value: 128,
            candidate_unit: "mmHg",
            current_ecrf_value: 140,
            current_ecrf_updated_at: "2026-08-01T10:00:00Z",
            discrepancy_type: "VALUE_MISMATCH",
            confidence_score: 0.98,
            mapping_provenance: "LOINC 8480-6 -> VS.SYSBP",
            raw_fhir_resource: {
              resourceType: "Observation",
              valueQuantity: { value: 128, unit: "mmHg" },
            },
            status: "PENDING_REVIEW",
            version_index: 1,
            created_at: "2026-08-02T10:00:00Z",
          },
          {
            id: "cand-demo-2",
            study_id: studyId,
            site_id: "SITE-101",
            subject_id: subjectId,
            subject_pseudonym: subjectId,
            ehr_system_id: "EPIC-HEALTH",
            fhir_resource_type: "Observation",
            fhir_resource_id: "obs-gluc-2",
            source_timestamp: "2026-08-02T10:05:00Z",
            target_domain: "LB",
            target_variable: "GLUC",
            cdash_testcd: "GLUC",
            cdash_test: "Glucose",
            candidate_value: 105,
            candidate_unit: "mg/dL",
            current_ecrf_value: null,
            current_ecrf_updated_at: null,
            discrepancy_type: "NEW_FIELD",
            confidence_score: 0.95,
            mapping_provenance: "LOINC 2339-0 -> LB.GLUC",
            raw_fhir_resource: {
              resourceType: "Observation",
              valueQuantity: { value: 105, unit: "mg/dL" },
            },
            status: "PENDING_REVIEW",
            version_index: 1,
            created_at: "2026-08-02T10:05:00Z",
          },
        ],
        total: 2,
      });
    }),
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
    http.get("*/api/v1/training/curricula", ({ request }) => {
      const url = new URL(request.url);
      const protocolVersion = url.searchParams.get("protocol_version");
      const list = protocolVersion
        ? trainingState.curricula.filter(
            (c) => c.protocol_version === protocolVersion
          )
        : trainingState.curricula;
      return HttpResponse.json(list);
    }),
    http.post("*/api/v1/training/curricula", async ({ request }) => {
      const body = await readJsonBody(request);
      const step = nextDemoStep("training-curriculum-author");
      const created = {
        ...buildDemoCurriculum(`curr-demo-${step}`),
        study_id: body?.study_id || "DEMO-01",
        protocol_version: body?.protocol_version || "2.0",
        protocol_version_number: Number(body?.protocol_version_number ?? 2),
        qualification_code: body?.qualification_code || "protocol_training",
        passing_score: Number(body?.passing_score ?? 0.8),
        title: body?.title || `Protocol Curriculum ${step}`,
        description: body?.description || "",
        modules: Array.isArray(body?.modules) ? body.modules : [],
        quiz_questions: Array.isArray(body?.quiz_questions)
          ? body.quiz_questions
          : [],
        created_at: demoNowIso(step),
      };
      trainingState.curricula.unshift(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.get("*/api/v1/training/study-staff", ({ request }) => {
      const url = new URL(request.url);
      const siteId = url.searchParams.get("site_id");
      const list = siteId
        ? trainingState.studyStaff.filter((s) => s.site_id === siteId)
        : trainingState.studyStaff;
      return HttpResponse.json(list);
    }),
    http.get("*/api/v1/training/assignments", ({ request }) => {
      const url = new URL(request.url);
      const siteId = url.searchParams.get("site_id");
      const list = siteId
        ? trainingState.assignments.filter((a) => a.site_id === siteId)
        : trainingState.assignments;
      return HttpResponse.json(list);
    }),
    http.get("*/api/v1/training/assignments/:assignmentId", ({ params }) => {
      const found = trainingState.assignments.find(
        (a) => a.id === String(params.assignmentId)
      );
      const curriculum =
        trainingState.curricula.find((c) => c.id === found?.curriculum_id) ||
        trainingState.curricula[0] ||
        buildDemoCurriculum("curr-demo-01");
      return HttpResponse.json({
        ...(found || {
          id: String(params.assignmentId),
          study_id: "DEMO-01",
          status: "COMPLETED",
          quiz_score: 100,
        }),
        curriculum,
      });
    }),
    http.post(
      "*/api/v1/training/assignments/:assignmentId/complete",
      async ({ params, request }) => {
        const sigToken = request.headers.get("X-Sig-Token");
        if (!sigToken) {
          return HttpResponse.json(
            {
              title: "Step-up signature token required",
              status: 401,
              detail: "X-Sig-Token header is required for Part 11 attestation.",
            },
            { status: 401 }
          );
        }
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-assignment-complete");
        const assignmentId = String(params.assignmentId);
        const found = trainingState.assignments.find(
          (a) => a.id === assignmentId
        );
        const curriculum =
          trainingState.curricula.find((c) => c.id === found?.curriculum_id) ||
          trainingState.curricula[0] ||
          buildDemoCurriculum("curr-demo-01");
        const answers = Array.isArray(body?.quiz_answers)
          ? body.quiz_answers
          : [];
        const questions = Array.isArray(curriculum.quiz_questions)
          ? curriculum.quiz_questions
          : [];
        let correct = 0;
        for (const q of questions) {
          const match = answers.find((a) => a.question_id === q.id);
          if (match?.selected_option_id === q.correct_option_id) {
            correct += 1;
          }
        }
        const scoreRatio =
          questions.length > 0 ? correct / questions.length : 1;
        const passingRatio = Number(curriculum.passing_score ?? 0.8);
        if (scoreRatio < passingRatio) {
          return HttpResponse.json(
            {
              title: "Qualification quiz threshold not met",
              status: 422,
              detail: `Score ${Math.round(scoreRatio * 100)}% is below required ${Math.round(passingRatio * 100)}%.`,
            },
            { status: 422 }
          );
        }

        const completedAt = demoNowIso(step);
        const docId = `00000000-0000-0000-0000-0000000094${String(step).padStart(2, "0")}`; // deid-ignore
        const artifactRef = `artifact_version:00000000-0000-0000-0000-0000000095${String(step).padStart(2, "0")}`; // deid-ignore
        const qualRef = `qualification:00000000-0000-0000-0000-0000000096${String(step).padStart(2, "0")}`; // deid-ignore
        if (found) {
          found.status = "COMPLETED";
          found.completed_at = completedAt;
          found.quiz_score = scoreRatio;
          found.etmf_artifact_id = "05.02.04";
          found.etmf_document_id = docId;
          found.kernel_artifact_version_ref = artifactRef;
          found.kernel_qualification_ref = qualRef;
        }
        return HttpResponse.json({
          assignment: found || {
            id: assignmentId,
            curriculum_id: curriculum.id,
            study_id: curriculum.study_id,
            site_id: "SITE-101",
            user_id: "usr-crc-01",
            user_name: "Site Coordinator 01",
            user_role: "CRC",
            status: "COMPLETED",
            quiz_score: scoreRatio,
            completed_at: completedAt,
            etmf_artifact_id: "05.02.04",
            etmf_document_id: docId,
            kernel_artifact_version_ref: artifactRef,
            kernel_qualification_ref: qualRef,
          },
          quiz_score: scoreRatio,
          passing_score: passingRatio,
          sha256_seal: `sha256-demo-trn-${step}`,
          etmf_artifact_id: "05.02.04",
          etmf_document_id: docId,
          kernel_artifact_version_ref: artifactRef,
          kernel_qualification_ref: qualRef,
          executed_at: completedAt,
        });
      }
    ),
    http.get("*/api/v1/training/curricula/:curriculumId", ({ params }) => {
      const id = String(params.curriculumId);
      const found = trainingState.curricula.find((c) => c.id === id);
      return HttpResponse.json(found || buildDemoCurriculum(id));
    }),
    http.post(
      "*/api/v1/training/curricula/batch-assign",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-batch-assign");
        const createdAssignment = {
          id: `asn-batch-${step}`,
          study_id: body?.study_id || "DEMO-01",
          site_id: body?.site_ids?.[0] || "SITE-101",
          user_id: "usr-crc-02",
          user_role: "CRC",
          curriculum_id: body?.curriculum_id || "curr-demo-01",
          status: "PENDING",
          assigned_at: demoNowIso(step),
        };
        trainingState.assignments.push(createdAssignment);
        return HttpResponse.json(
          {
            batch_transaction_id: `TRN-BATCH-${step}`,
            curriculum_id: body?.curriculum_id || "curr-demo-01",
            study_id: body?.study_id || "DEMO-01",
            total_eligible: 1,
            total_assigned: 1,
            total_skipped: 0,
            assignments: [createdAssignment],
            skipped_items: [],
            executed_at: demoNowIso(step),
          },
          { status: 201 }
        );
      }
    ),
    http.get("*/api/v1/training/gating-status", () =>
      HttpResponse.json({ is_gated: false })
    ),
  ];
}
