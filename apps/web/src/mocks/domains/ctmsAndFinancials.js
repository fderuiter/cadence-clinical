export function createCtmsAndFinancialsDomain(ctx) {
  const { state } = ctx;

  state.milestones = [];
  state.monitoringVisits = [];
  state.craWorkload = [];
  state.recruitment = [];
  state.doaLogs = new Map();
  state.grants = [];
  state.financialTemplates = [];
  state.financialAdjustments = [];
  state.procedureGrids = [];
  state.grantAmendments = [];

  function seed() {
    state.milestones = [
      {
        id: "ms-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "IRB Favorable Opinion",
        status: "COMPLETED",
        planned_date: "2026-07-01", // deid-ignore
        actual_date: "2026-06-28", // deid-ignore
      },
      {
        id: "ms-102",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "Site Initiation Visit (SIV)",
        status: "IN_PROGRESS",
        planned_date: "2026-08-10", // deid-ignore
        actual_date: null,
      },
    ];

    state.monitoringVisits = [
      {
        id: "mv-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        visit_type: "INTERIM_MONITORING",
        status: "SCHEDULED",
        scheduled_date: "2026-08-25", // deid-ignore
        monitor_id: "usr-cra-01",
        sdv_percentage: 100,
      },
    ];

    state.craWorkload = [
      {
        cra_id: "usr-cra-01",
        cra_name: "Lead Clinical Monitor 01",
        assigned_sites: 2,
        pending_sdv_items: 4,
        utilization_pct: 68,
      },
    ];

    state.recruitment = [
      {
        id: "rec-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        target_enrollment: 15,
        screened_count: 6,
        enrolled_count: 4,
        screen_failure_count: 2,
      },
    ];

    state.doaLogs = new Map([
      [
        "SITE-101",
        {
          site_id: "SITE-101",
          pi_name: "Investigator Lead 01, MD",
          active_delegations: [
            {
              record_id: "doa-staff-01",
              site_id: "SITE-101",
              staff_user_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              staff_role: "CRC",
              task_codes: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              is_active: true,
              signed_off: true,
              end_date: null,
              start_date: "2026-07-15", // deid-ignore
              pi_signed_off_by: "usr-pi-01",
              pi_signed_off_at: "2026-07-15T10:00:00Z", // deid-ignore
            },
          ],
          revoked_delegations: [],
          delegated_staff: [
            {
              id: "doa-staff-01",
              staff_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              role: "CRC",
              tasks: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              signed_at: "2026-07-15T10:00:00Z",
            },
          ],
          audit_history: [
            {
              id: "doa-aud-01",
              action: "DELEGATION_APPROVED",
              actor: "Investigator Lead 01, MD",
              timestamp: "2026-07-15T10:00:00Z",
              reason: "Initial site delegation sign-off",
            },
          ],
        },
      ],
    ]);

    state.grants = [
      {
        id: "grant-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        currency: "USD",
        total_budget: 145000,
        disbursed_amount: 32500, // deid-ignore
        holdback_percentage: 10,
        overhead_percentage: 28,
        version_index: 1,
        status: "ACTIVE",
      },
    ];

    state.financialTemplates = [
      {
        id: "ftmpl-01",
        template_name: "Academic Medical Center Tier 1",
        tier_level: "TIER_1",
        currency: "USD",
        overhead_percentage: 28,
        withholding_percentage: 10,
        fixed_startup_fee: 8500,
        fixed_closeout_fee: 3500,
        irb_fee: 2500,
        patient_stipend_amount: 75,
        travel_reimbursement_rate: 50,
      },
    ];

    state.financialAdjustments = [
      {
        id: "fadj-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        description: "Unscheduled safety lab handling fee",
        amount: 450,
        currency: "USD",
        status: "APPROVED",
      },
    ];

    state.procedureGrids = [
      {
        id: "pgrid-01",
        grant_id: "grant-101",
        procedure_code: "PROC-VS",
        procedure_name: "Vital Signs & 12-Lead ECG",
        visit_name: "Baseline",
        unit_cost: 320,
        currency: "USD",
        version_index: 1,
      },
    ];

    state.grantAmendments = [
      {
        id: "gamd-01",
        grant_id: "grant-101",
        version_index: 1,
        effective_date: "2026-07-01", // deid-ignore
        summary: "Initial executed clinical trial agreement budget",
      },
    ];
  }

  return { seed, methods: {} };
}
