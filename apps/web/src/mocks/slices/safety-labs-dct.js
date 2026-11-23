import { readJsonBody } from "./shared.js";

export function createSafetyLabsDctSliceStore(ctx) {
  const { state } = ctx;

  function reset() {
    state.labRanges = [
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
    ];
    state.labCertifications = [
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
    ];
    state.labBatches = [
      {
        batch_id: "BATCH-DEMO-01",
        study_id: "DEMO-01",
        status: "COMPLETED",
        total_records: 24,
        ingested_count: 24,
        staged_aes_count: 0,
        created_at: "2026-07-15T09:00:00Z", // deid-ignore
      },
    ];
  }

  return {
    reset,
    methods: {},
  };
}

export function createSafetyLabsDctSliceHandlers(
  store,
  { http, HttpResponse }
) {
  return [
    http.get("*/api/v1/execution/lab-ranges", () =>
      HttpResponse.json(store.state.labRanges)
    ),
    http.get("*/api/v1/execution/labs/toxicity/review-queue", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/labs/certifications", () =>
      HttpResponse.json(store.state.labCertifications)
    ),
    http.get("*/api/v1/execution/labs/batch-status", () =>
      HttpResponse.json(store.state.labBatches)
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

    // Sensor Telemetry & Decentralized Clinical Trials (DCT)
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
  ];
}
