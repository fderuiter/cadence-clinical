import {
  DEMO_PARTICIPANTS,
  DEMO_SITES,
  DEMO_SPONSOR,
  DEMO_STUDY,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import { demoNowIso, demoNowMs } from "../utils/demoMode.js";

function unversionedRef(refText) {
  return String(refText || "")
    .trim()
    .split("@")[0];
}

function isoDateOnly(step = 0) {
  return demoNowIso(step).slice(0, 10);
}

function addDaysIso(baseStep, days) {
  const ms = demoNowMs(baseStep) + days * 24 * 60 * 60 * 1000;
  return new Date(ms).toISOString();
}

function extractScalarText(val) {
  if (val === null || val === undefined) return null;
  if (typeof val === "number" || typeof val === "boolean") return String(val);
  if (typeof val === "string") return val;
  if (typeof val === "object") {
    if (val.type === "missing") return null;
    if (val.value !== undefined && val.value !== null) {
      return String(val.value);
    }
  }
  return null;
}

function extractNumericValue(val) {
  const raw = extractScalarText(val);
  if (raw === null || raw.trim() === "") return null;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
}

export function createDemoGovernanceDomain({
  state,
  nextCounter,
  getStudyByRefOrCode,
  makeUuidFromIndex,
  recordActivity,
  cloneJson,
  computeContentHash,
}) {
  const RULE_PACK_VS_UUID = makeUuidFromIndex("00006100", 1);
  const RULE_PACK_AE_UUID = makeUuidFromIndex("00006100", 2);
  const RULE_PACK_VS_BASE_REF = `library_item:${RULE_PACK_VS_UUID}`;
  const RULE_PACK_AE_BASE_REF = `library_item:${RULE_PACK_AE_UUID}`;
  const CADENCE_FORM_1_UUID = makeUuidFromIndex("00002100", 1);
  const CADENCE_FORM_2_UUID = makeUuidFromIndex("00002100", 2);
  const RUN_INITIAL_REF = `validation_run:${makeUuidFromIndex("00006300", 1)}`;
  const RUN_RECHECK_REF = `validation_run:${makeUuidFromIndex("00006300", 2)}`;

  function buildInitialRulePacks() {
    return new Map([
      [
        RULE_PACK_VS_BASE_REF,
        {
          base_ref: RULE_PACK_VS_BASE_REF,
          ref: `${RULE_PACK_VS_BASE_REF}@1`,
          key: "vs-vital-ranges",
          version: 1,
          title: "CDASH Vital Signs Plausibility & Range Pack",
          description:
            "Physiological bounds and systolic/diastolic ordering edit checks for CDASH VS forms.",
          status: "Approved",
          content_hash: "sha256-demo-pack-vs-v1",
          rules: [
            {
              id: "VS-SYSBP-RANGE",
              version: 1,
              stage: "Collection",
              dataset: "VS",
              severity: "Warning",
              explanation:
                "Systolic blood pressure should remain within 70 to 180 mm[Hg] after 5 minutes seated rest.",
              remediation_guidance:
                "Confirm cuff sizing and seated rest duration, or record clinical justification.",
              condition: "SYSBP < 70 || SYSBP > 180",
            },
            {
              id: "VS-DIABP-RANGE",
              version: 1,
              stage: "Collection",
              dataset: "VS",
              severity: "Warning",
              explanation:
                "Diastolic blood pressure should remain within 40 to 110 mm[Hg] after seated rest.",
              remediation_guidance:
                "Repeat measurement and verify auscultatory or oscillometric reading.",
              condition: "DIABP < 40 || DIABP > 110",
            },
            {
              id: "VS-PULSE-RANGE",
              version: 1,
              stage: "Collection",
              dataset: "VS",
              severity: "Warning",
              explanation:
                "Resting heart rate should remain within 40 to 130 /min.",
              remediation_guidance:
                "Confirm resting pulse after 5 minutes seated and obtain 12-lead ECG if indicated.",
              condition: "PULSE < 40 || PULSE > 130",
            },
            {
              id: "VS-BP-ORDER",
              version: 1,
              stage: "Collection",
              dataset: "VS",
              severity: "Error",
              explanation:
                "Diastolic blood pressure must be strictly lower than systolic blood pressure.",
              remediation_guidance:
                "Verify that systolic and diastolic blood pressure entries were not transposed.",
              condition: "DIABP >= SYSBP",
            },
          ],
        },
      ],
      [
        RULE_PACK_AE_BASE_REF,
        {
          base_ref: RULE_PACK_AE_BASE_REF,
          ref: `${RULE_PACK_AE_BASE_REF}@1`,
          key: "sdtm-conformance-core",
          version: 1,
          title: "SDTM Tabulation & Assessment Completeness Pack",
          description:
            "Collection note completeness and SDTM DM arm-assignment conformance rules.",
          status: "Draft",
          content_hash: "sha256-demo-pack-sdtm-v1",
          rules: [
            {
              id: "VS-COMM-PRESENT",
              version: 1,
              stage: "Collection",
              dataset: "VS",
              severity: "Notice",
              explanation:
                "Baseline vital signs assessments should include a positioning comment in VSCOMM.",
              remediation_guidance:
                "Enter seated-rest duration and cuff arm in the Vital Signs comment field.",
              condition: "VSCOMM == ''",
            },
            {
              id: "DM-ARMCD-POPULATED",
              version: 1,
              stage: "Tabulation",
              dataset: "DM",
              severity: "Error",
              explanation:
                "Active randomized enrollment records must carry a non-empty planned arm code.",
              remediation_guidance:
                "Complete RTSM treatment assignment before running SDTM DM tabulation.",
              condition: "ARMCD == null",
            },
          ],
        },
      ],
    ]);
  }

  function buildCadenceStudyForms() {
    const p1 = DEMO_PARTICIPANTS[0];
    const p2 = DEMO_PARTICIPANTS[1] || DEMO_PARTICIPANTS[0];
    const site1Uuid =
      DEMO_SITES[0]?.studySiteUuid || makeUuidFromIndex("00000200", 101);
    const studyUuid =
      DEMO_STUDY.uuid || unversionedRef(DEMO_STUDY.ref).replace(/^study:/, "");
    const protocolVerUuid = makeUuidFromIndex("00000100", 2);
    const t0 = demoNowIso(1);
    const t1 = demoNowIso(2);

    const makeVsForm = ({
      formId,
      participant,
      sysbp,
      diabp,
      pulse,
      note,
      obsSeed,
      recordedAt,
    }) => {
      const items = [
        {
          item_id: "item-sysbp",
          concept: "SYSBP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: {
            type: "quantity",
            value: String(sysbp),
            unit: "mm[Hg]",
            comparator: null,
          },
          observation_id: makeUuidFromIndex("00006400", obsSeed + 1),
          revision: 1,
          recorded_by: "crc.site101",
          recorded_at: recordedAt,
        },
        {
          item_id: "item-diabp",
          concept: "DIABP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: {
            type: "quantity",
            value: String(diabp),
            unit: "mm[Hg]",
            comparator: null,
          },
          observation_id: makeUuidFromIndex("00006400", obsSeed + 2),
          revision: 1,
          recorded_by: "crc.site101",
          recorded_at: recordedAt,
        },
        {
          item_id: "item-pulse",
          concept: "PULSE",
          unit: "/min",
          value_kind: "quantity",
          required: true,
          value: {
            type: "quantity",
            value: String(pulse),
            unit: "/min",
            comparator: null,
          },
          observation_id: makeUuidFromIndex("00006400", obsSeed + 3),
          revision: 1,
          recorded_by: "crc.site101",
          recorded_at: recordedAt,
        },
        {
          item_id: "item-notes",
          concept: "VSCOMM",
          unit: null,
          value_kind: "text",
          required: false,
          value: { type: "text", value: note },
          observation_id: makeUuidFromIndex("00006400", obsSeed + 4),
          revision: 1,
          recorded_by: "crc.site101",
          recorded_at: recordedAt,
        },
      ];
      return {
        form_id: formId,
        enrollment_id: participant.enrollmentUuid,
        participant_id: participant.participantUuid,
        study_id: studyUuid,
        study_site_id: site1Uuid,
        protocol_version_id: protocolVerUuid,
        collection_spec_id: "form-vital-signs",
        collection_title: "Vital Signs (CDASH VS)",
        planned_activity_id: "visit-baseline",
        collected_at: recordedAt,
        opened_by: "crc.site101",
        opened_at: recordedAt,
        version: 1,
        complete: true,
        missing_required: [],
        items,
        locks: [],
        assertions: [],
        signatures: [],
        content_hash: computeContentHash(items),
      };
    };

    return [
      makeVsForm({
        formId: CADENCE_FORM_1_UUID,
        participant: p1,
        sysbp: "126",
        diabp: "78",
        pulse: "68",
        note: "Seated 5 minutes in left-arm cuff.",
        obsSeed: 10,
        recordedAt: t0,
      }),
      makeVsForm({
        formId: CADENCE_FORM_2_UUID,
        participant: p2,
        sysbp: "186",
        diabp: "88",
        pulse: "76",
        note: "Elevated baseline systolic reading confirmed on repeat.",
        obsSeed: 20,
        recordedAt: t1,
      }),
    ];
  }

  function evaluatePackOnItemMap(pack, itemMap) {
    const breaches = [];
    const getEntry = (itemId, concept) =>
      itemMap.get(itemId) || itemMap.get(concept) || null;

    const sysEntry = getEntry("item-sysbp", "SYSBP");
    const diaEntry = getEntry("item-diabp", "DIABP");
    const pulseEntry = getEntry("item-pulse", "PULSE");
    const notesEntry = getEntry("item-notes", "VSCOMM");

    const sysVal = sysEntry ? extractNumericValue(sysEntry.value) : null;
    const diaVal = diaEntry ? extractNumericValue(diaEntry.value) : null;
    const pulseVal = pulseEntry ? extractNumericValue(pulseEntry.value) : null;
    const notesVal = notesEntry ? extractScalarText(notesEntry.value) : null;

    for (const rule of pack.rules) {
      if (rule.stage !== "Collection") continue;
      if (
        rule.id === "VS-SYSBP-RANGE" &&
        sysVal !== null &&
        (sysVal < 70 || sysVal > 180)
      ) {
        breaches.push({
          pack_key: pack.key,
          rule_pack_ref: pack.ref,
          rule_id: rule.id,
          rule_version: Number(rule.version),
          severity: rule.severity,
          dataset: rule.dataset || "VS",
          item_id: sysEntry?.item_id || "item-sysbp",
          variable: "SYSBP",
          expected: "70 <= SYSBP <= 180 mm[Hg]",
          observed: [{ variable: "SYSBP", value: String(sysVal) }],
          message: `Systolic blood pressure (${sysVal} mm[Hg]) is outside physiological reference bounds (70-180 mm[Hg]).`,
          remediation_guidance: rule.remediation_guidance,
        });
      } else if (
        rule.id === "VS-DIABP-RANGE" &&
        diaVal !== null &&
        (diaVal < 40 || diaVal > 110)
      ) {
        breaches.push({
          pack_key: pack.key,
          rule_pack_ref: pack.ref,
          rule_id: rule.id,
          rule_version: Number(rule.version),
          severity: rule.severity,
          dataset: rule.dataset || "VS",
          item_id: diaEntry?.item_id || "item-diabp",
          variable: "DIABP",
          expected: "40 <= DIABP <= 110 mm[Hg]",
          observed: [{ variable: "DIABP", value: String(diaVal) }],
          message: `Diastolic blood pressure (${diaVal} mm[Hg]) is outside physiological reference bounds (40-110 mm[Hg]).`,
          remediation_guidance: rule.remediation_guidance,
        });
      } else if (
        rule.id === "VS-PULSE-RANGE" &&
        pulseVal !== null &&
        (pulseVal < 40 || pulseVal > 130)
      ) {
        breaches.push({
          pack_key: pack.key,
          rule_pack_ref: pack.ref,
          rule_id: rule.id,
          rule_version: Number(rule.version),
          severity: rule.severity,
          dataset: rule.dataset || "VS",
          item_id: pulseEntry?.item_id || "item-pulse",
          variable: "PULSE",
          expected: "40 <= PULSE <= 130 /min",
          observed: [{ variable: "PULSE", value: String(pulseVal) }],
          message: `Resting heart rate (${pulseVal} /min) is outside protocol bounds (40-130 /min).`,
          remediation_guidance: rule.remediation_guidance,
        });
      } else if (
        rule.id === "VS-BP-ORDER" &&
        sysVal !== null &&
        diaVal !== null &&
        diaVal >= sysVal
      ) {
        breaches.push({
          pack_key: pack.key,
          rule_pack_ref: pack.ref,
          rule_id: rule.id,
          rule_version: Number(rule.version),
          severity: rule.severity,
          dataset: rule.dataset || "VS",
          item_id: diaEntry?.item_id || "item-diabp",
          variable: "DIABP",
          expected: "DIABP < SYSBP",
          observed: [
            { variable: "DIABP", value: String(diaVal) },
            { variable: "SYSBP", value: String(sysVal) },
          ],
          message: `Diastolic blood pressure (${diaVal} mm[Hg]) must be lower than systolic blood pressure (${sysVal} mm[Hg]).`,
          remediation_guidance: rule.remediation_guidance,
        });
      } else if (
        rule.id === "VS-COMM-PRESENT" &&
        notesEntry &&
        (!notesVal || notesVal.trim() === "")
      ) {
        breaches.push({
          pack_key: pack.key,
          rule_pack_ref: pack.ref,
          rule_id: rule.id,
          rule_version: Number(rule.version),
          severity: rule.severity,
          dataset: rule.dataset || "VS",
          item_id: notesEntry.item_id || "item-notes",
          variable: "VSCOMM",
          expected: "Non-empty VSCOMM note",
          observed: [{ variable: "VSCOMM", value: "" }],
          message:
            "Baseline vital signs comment (VSCOMM) is blank; document cuff arm and seated rest.",
          remediation_guidance: rule.remediation_guidance,
        });
      }
    }
    return breaches;
  }

  function buildItemMapForForm(form, overlayValues = []) {
    const map = new Map();
    for (const item of form?.items || []) {
      const cloned = cloneJson(item);
      map.set(cloned.item_id, cloned);
      if (cloned.concept) {
        map.set(cloned.concept, cloned);
      }
    }
    for (const draft of overlayValues || []) {
      if (!draft?.item_id) continue;
      const existing = map.get(draft.item_id) || {
        item_id: draft.item_id,
        concept: String(draft.item_id)
          .replace(/^item-/, "")
          .toUpperCase(),
      };
      const updated = {
        ...existing,
        value: cloneJson(draft.value),
      };
      map.set(updated.item_id, updated);
      if (updated.concept) {
        map.set(updated.concept, updated);
      }
    }
    return map;
  }

  function resolveStudyForms(study) {
    const targetUuid = unversionedRef(study.ref).replace(/^study:/, "");
    const direct = [...state.forms.values()].filter(
      (f) => f.study_id === targetUuid
    );
    if (direct.length > 0) return direct;
    return [...state.forms.values()];
  }

  function getOrCreateStudyStandards(study) {
    const studyKey = unversionedRef(study.ref);
    if (!state.studyStandards.has(studyKey)) {
      const idx = state.studyStandards.size + 1;
      state.studyStandards.set(studyKey, {
        profile_ref: `study_standards_profile:${makeUuidFromIndex("00006000", idx)}`,
        pinned_rule_pack_refs: [RULE_PACK_VS_BASE_REF],
      });
    }
    return state.studyStandards.get(studyKey);
  }

  function formatRulePackForStudy(pack, pinnedSet) {
    return {
      ref: pack.ref,
      key: pack.key,
      version: Number(pack.version),
      title: pack.title,
      description: pack.description ?? null,
      status: pack.status,
      pinned: pinnedSet.has(pack.base_ref),
      content_hash: pack.content_hash,
      rule_count: pack.rules.length,
      rules: pack.rules.map((r) => ({
        id: r.id,
        version: Number(r.version),
        stage: r.stage,
        dataset: r.dataset ?? null,
        severity: r.severity,
        explanation: r.explanation,
        remediation_guidance: r.remediation_guidance ?? null,
        condition: r.condition ?? null,
      })),
    };
  }

  function buildFindingExplanation(finding) {
    return {
      subject: finding.ref,
      claim: `Rule ${finding.rule_id} (v${finding.rule_version}) recorded ${finding.severity.toLowerCase()} finding (${finding.status}): ${finding.message}`,
      producer: "kernel.validation.engine",
      steps: [
        {
          claim: `Evaluated pinned rule pack ${finding.pack_key} (${finding.rule_pack_ref}) on ${finding.stage.toLowerCase()} dataset ${finding.dataset}.`,
          evidence: [
            `study_ref=${finding.study_ref}`,
            `record_key=${finding.record_key}`,
            `run_ref=${finding.last_detected_run_ref}`,
          ],
        },
        {
          claim: `Compared observed item values against expected condition (${finding.expected || "governed range"}).`,
          evidence: (finding.observed || []).map(
            (obs) => `${obs.variable}=${obs.value ?? "null"}`
          ),
        },
      ],
      conditions: [
        {
          code: finding.rule_id,
          description: finding.expected || finding.message,
          met: finding.status === "Resolved",
          evidence: (finding.observed || []).map(
            (obs) => `${obs.variable}=${obs.value ?? "null"}`
          ),
        },
      ],
      next_actions:
        finding.status === "Open"
          ? [
              {
                description:
                  "Open the source eCRF item to confirm or correct the recorded value with a 21 CFR Part 11 change reason.",
                capability: "collection.record",
                target: finding.record_key,
              },
            ]
          : [],
      unmet_conditions:
        finding.status === "Open"
          ? [
              {
                code: finding.rule_id,
                description: finding.expected || finding.message,
                met: false,
                evidence: (finding.observed || []).map(
                  (obs) => `${obs.variable}=${obs.value ?? "null"}`
                ),
              },
            ]
          : [],
    };
  }

  function recomputeStudyFindings(study) {
    const studyBaseRef = unversionedRef(study.ref);
    const standards = getOrCreateStudyStandards(study);
    const pinnedPacks = standards.pinned_rule_pack_refs
      .map((r) => state.rulePacks.get(unversionedRef(r)))
      .filter(Boolean);
    const forms = resolveStudyForms(study);

    // Keep previously resolved findings for audit history and derive active open findings from pinned packs.
    const existingForStudy =
      state.validationFindingsByStudy.get(studyBaseRef) || [];
    const activeDerived = [];
    let seedIndex = 1;

    for (const form of forms) {
      const itemMap = buildItemMapForForm(form, []);
      for (const pack of pinnedPacks) {
        const breaches = evaluatePackOnItemMap(pack, itemMap);
        for (const breach of breaches) {
          const deterministicIdx =
            study.code === (DEMO_STUDY.code || "CADENCE-101")
              ? seedIndex
              : 100 + seedIndex;
          seedIndex += 1;
          const findingRef = `validation_finding:${makeUuidFromIndex("00006200", deterministicIdx)}`;
          activeDerived.push({
            ref: findingRef,
            study_ref: studyBaseRef,
            stage: "Collection",
            pack_key: breach.pack_key,
            rule_pack_ref: breach.rule_pack_ref,
            rule_id: breach.rule_id,
            rule_version: breach.rule_version,
            severity: breach.severity,
            status: "Open",
            blocks: breach.severity === "Error",
            target_kind: "Record",
            dataset: breach.dataset,
            record_key: form.form_id,
            variable: breach.item_id,
            message: breach.message,
            expected: breach.expected,
            observed: breach.observed,
            first_detected_run_ref: RUN_INITIAL_REF,
            last_detected_run_ref: RUN_RECHECK_REF,
            resolved_by_run_ref: null,
            classification: "CDASH Edit Check",
          });
        }
      }
    }

    // Preserve historical resolved baseline finding so status filter ("Resolved") is immediately demonstrable.
    const resolvedHistoricalRef = `validation_finding:${makeUuidFromIndex(
      "00006200",
      study.code === (DEMO_STUDY.code || "CADENCE-101") ? 50 : 150
    )}`;
    const historicalResolved = existingForStudy.find(
      (f) => f.ref === resolvedHistoricalRef
    ) || {
      ref: resolvedHistoricalRef,
      study_ref: studyBaseRef,
      stage: "Collection",
      pack_key: "vs-vital-ranges",
      rule_pack_ref: `${RULE_PACK_VS_BASE_REF}@1`,
      rule_id: "VS-PULSE-RANGE",
      rule_version: 1,
      severity: "Warning",
      status: "Resolved",
      blocks: false,
      target_kind: "Record",
      dataset: "VS",
      record_key: forms[0]?.form_id || CADENCE_FORM_1_UUID,
      variable: "item-pulse",
      message:
        "Resting heart rate transient elevation resolved following 5-minute seated repeat.",
      expected: "40 <= PULSE <= 130 /min",
      observed: [{ variable: "PULSE", value: "68" }],
      first_detected_run_ref: RUN_INITIAL_REF,
      last_detected_run_ref: RUN_INITIAL_REF,
      resolved_by_run_ref: RUN_RECHECK_REF,
      classification: "CDASH Edit Check",
    };

    const combined = [...activeDerived, historicalResolved];
    state.validationFindingsByStudy.set(studyBaseRef, combined);
    for (const f of combined) {
      state.validationFindingsByRef.set(unversionedRef(f.ref), f);
    }
    return combined;
  }

  function buildInitialLaySummaries() {
    const studyCode = DEMO_STUDY.code || "CADENCE-101";
    const sponsorName = DEMO_SPONSOR.name || "Cadence Clinical Research";
    const pubAt = demoNowIso(3);
    const deadlineAt = addDaysIso(0, 180);
    const baseSections = {
      objective:
        "Evaluate whether once-daily investigational cardiometabolic therapy safely lowers systolic blood pressure and glycemic burden over 12 weeks.",
      participants:
        "Adult outpatient participants across synthetic US clinical sites (SITE-101 and SITE-102) meeting protocol screening criteria.",
      treatments:
        "Participants were randomized to Arm A (active oral regimen once daily) or Arm B (matching placebo control).",
      results:
        "Participants in Arm A achieved clinically meaningful reductions in seated systolic blood pressure with high daily ePRO diary adherence.",
      side_effects:
        "Most reported events were mild and transient; no unexpected serious adverse reactions occurred during the reporting window.",
      future_plans:
        "Follow-up safety monitoring and plain-language participant result notifications remain active under EU CTR Article 37 governance.",
    };

    const summaries = [
      {
        id: "lsum-cadence-101",
        study_id: studyCode,
        protocol_id: `${studyCode}-V2`,
        title: `Plain-Language Clinical Trial Results Summary (${studyCode})`,
        trial_phase: DEMO_STUDY.phase || "Phase II",
        therapeutic_area: "Cardiometabolic Medicine",
        sponsor_name: sponsorName,
        irb_approval_number: "IRB-CADENCE-101-A37",
        irb_approval_date: isoDateOnly(1),
        language: "en",
        language_code: "en",
        reading_grade_level: 6.4,
        flesch_kincaid_score: 74.2,
        status: "PUBLISHED",
        published_at: pubAt,
        regulatory_deadline: deadlineAt,
        eu_ctr_compliant: true,
        cryptographic_hash: "sha256-demo-lsum-cadence-101",
        summary_text: `This plain-language summary shares key findings from clinical study ${studyCode} sponsored by ${sponsorName} in accordance with EU CTR 536/2014 Article 37.`,
        sections: cloneJson(baseSections),
        plain_language_sections: cloneJson(baseSections),
      },
      {
        id: "lsum-01",
        study_id: "DEMO-01",
        protocol_id: "DEMO-01-V2",
        title: "Plain-Language Summary of Study DEMO-01",
        trial_phase: "Phase II",
        therapeutic_area: "Cardiometabolic Medicine",
        sponsor_name: sponsorName,
        irb_approval_number: "IRB-DEMO-01-A37",
        irb_approval_date: isoDateOnly(1),
        language: "en",
        language_code: "en",
        reading_grade_level: 6.5,
        flesch_kincaid_score: 73.8,
        status: "PUBLISHED",
        published_at: pubAt,
        regulatory_deadline: deadlineAt,
        eu_ctr_compliant: true,
        cryptographic_hash: "sha256-demo-lsum-01",
        summary_text:
          "Plain-language clinical trial results summary prepared for DEMO-01 study participants and caregivers.",
        sections: cloneJson(baseSections),
        plain_language_sections: cloneJson(baseSections),
      },
    ];

    const primarySubject = DEMO_PARTICIPANTS[0]?.subjectCode || "SUBJ-101-001";
    const receipts = new Map([
      [
        "lsum-cadence-101",
        [
          {
            id: "rcpt-cadence-101-01",
            summary_id: "lsum-cadence-101",
            subject_id: primarySubject,
            study_id: studyCode,
            comprehension_score: 5,
            comprehension_feedback:
              "Clear explanation of study purpose, visit schedule, and blood pressure outcomes.",
            signer_identity: `${primarySubject} (eConsent Verified)`,
            confirmed_at: demoNowIso(4),
            receipt_hash: "sha256-demo-rcpt-cadence-101-01",
          },
        ],
      ],
      [
        "lsum-01",
        [
          {
            id: "rcpt-lsum-01-01",
            summary_id: "lsum-01",
            subject_id: "SUBJ-1001",
            study_id: "DEMO-01",
            comprehension_score: 5,
            comprehension_feedback:
              "Plain-language sections were clear and easy to follow.",
            signer_identity: "SUBJ-1001 (Portal Verified)",
            confirmed_at: demoNowIso(4),
            receipt_hash: "sha256-demo-rcpt-lsum-01",
          },
        ],
      ],
    ]);

    return { summaries, receipts };
  }

  function buildDefaultRactFactorsForStudy(studyId) {
    const createdAt = demoNowIso(2);
    const createdBy =
      getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead";
    return [
      {
        id: `ract-${studyId.toLowerCase()}-01`,
        study_id: studyId,
        category: "SAFETY",
        risk_topic: "Delayed Serious Adverse Event Escalation",
        risk_description:
          "Potential delay between site awareness of grade 3+ cardiovascular or hepatic events and pharmacovigilance intake.",
        probability_score: 2,
        impact_score: 3,
        detectability_score: 3,
        total_risk_score: 18,
        risk_tier: "HIGH",
        mitigation_strategy:
          "Automated eCRF edit-check triggers and 24-hour medical monitor escalation alerts.",
        linked_kri_code: "KRI_AE_RATE",
        is_active: true,
        created_at: createdAt,
        created_by: createdBy,
        version_index: 1,
        reason_for_change: "Initial TransCelerate RACT baseline risk catalog",
      },
      {
        id: `ract-${studyId.toLowerCase()}-02`,
        study_id: studyId,
        category: "CONSENT",
        risk_topic: "Protocol Amendment Re-Consent Alignment",
        risk_description:
          "Risk of subjects completing scheduled assessments prior to signing amended ICF.",
        probability_score: 2,
        impact_score: 3,
        detectability_score: 2,
        total_risk_score: 12,
        risk_tier: "MEDIUM",
        mitigation_strategy:
          "Electronic eConsent version gating prior to visit form unlock.",
        linked_kri_code: "KRI_PROTOCOL_DEVIATION",
        is_active: true,
        created_at: createdAt,
        created_by: createdBy,
        version_index: 1,
        reason_for_change: "Initial TransCelerate RACT baseline risk catalog",
      },
      {
        id: `ract-${studyId.toLowerCase()}-03`,
        study_id: studyId,
        category: "DATA_INTEGRITY",
        risk_topic: "Unresolved Vital Signs Edit-Check Queries",
        risk_description:
          "Aging eCRF data queries delaying interim lock readiness.",
        probability_score: 2,
        impact_score: 2,
        detectability_score: 1,
        total_risk_score: 4,
        risk_tier: "LOW",
        mitigation_strategy:
          "Pre-commit eCRF range previews and weekly centralized CRA query review.",
        linked_kri_code: "KRI_QUERY_AGE",
        is_active: true,
        created_at: createdAt,
        created_by: createdBy,
        version_index: 1,
        reason_for_change: "Initial TransCelerate RACT baseline risk catalog",
      },
    ];
  }

  function buildDefaultSiteRiskProfilesForStudy(studyId, scoreBump = 0) {
    const evalAt = demoNowIso(5);
    const site1Code = DEMO_SITES[0]?.code || "SITE-101";
    const site2Code = DEMO_SITES[1]?.code || "SITE-102";
    const s1Score = Number((5.4 + scoreBump).toFixed(1));
    const s2Score = Number((2.6 + scoreBump * 0.5).toFixed(1));
    return [
      {
        id: `srp-${studyId.toLowerCase()}-${site1Code.toLowerCase()}`,
        study_id: studyId,
        site_id: site1Code,
        evaluation_date: evalAt,
        composite_risk_score: s1Score,
        risk_rank: 1,
        percentile_rank: 85,
        risk_tier: s1Score >= 8 ? "HIGH" : s1Score >= 5 ? "MEDIUM" : "LOW",
        high_risk_kri_count: 1,
        active_deviations_count: 1,
      },
      {
        id: `srp-${studyId.toLowerCase()}-${site2Code.toLowerCase()}`,
        study_id: studyId,
        site_id: site2Code,
        evaluation_date: evalAt,
        composite_risk_score: s2Score,
        risk_rank: 2,
        percentile_rank: 35,
        risk_tier: s2Score >= 8 ? "HIGH" : s2Score >= 5 ? "MEDIUM" : "LOW",
        high_risk_kri_count: 0,
        active_deviations_count: 0,
      },
    ];
  }

  function buildInitialKris() {
    return [
      {
        id: "kri-def-01",
        code: "KRI_QUERY_AGE",
        name: "Unresolved Query Aging Rate",
        category: "DATA_INTEGRITY",
        description:
          "Percentage of eCRF data queries remaining open past 14 calendar days.",
        calculation_formula:
          "(count(queries > 14d) / count(total_queries)) * 100",
        green_threshold: 5.0,
        amber_threshold: 15.0,
        red_threshold: 25.0,
        weight: 2.0,
        is_active: true,
      },
      {
        id: "kri-def-02",
        code: "KRI_AE_RATE",
        name: "Adverse Event Reporting Rate Variance",
        category: "SAFETY",
        description:
          "Site AE reporting rate relative to study cohort central tendency.",
        calculation_formula: "count(ae_records) / count(active_subjects)",
        green_threshold: 3.0,
        amber_threshold: 6.0,
        red_threshold: 10.0,
        weight: 3.0,
        is_active: true,
      },
      {
        id: "kri-def-03",
        code: "KRI_PROTOCOL_DEVIATION",
        name: "Protocol Deviation Rate",
        category: "CONSENT",
        description: "Protocol deviations logged per enrolled participant.",
        calculation_formula: "count(deviations) / count(active_subjects)",
        green_threshold: 1.0,
        amber_threshold: 3.0,
        red_threshold: 5.0,
        weight: 2.5,
        is_active: true,
      },
    ];
  }

  function seedGovernanceState() {
    state.rulePacks = buildInitialRulePacks();
    state.studyStandards = new Map();
    state.validationFindingsByStudy = new Map();
    state.validationFindingsByRef = new Map();

    for (const cadenceForm of buildCadenceStudyForms()) {
      state.forms.set(cadenceForm.form_id, cadenceForm);
      for (const item of cadenceForm.items) {
        const obsRef = `observation:${item.observation_id}`;
        state.observations.set(obsRef, {
          ref: obsRef,
          enrollment_ref: `enrollment:${cadenceForm.enrollment_id}`,
          concept: item.concept,
          value: cloneJson(item.value),
          observed_at: item.recorded_at,
          version: 1,
          locks: [],
          revisions: [
            {
              number: 1,
              value: cloneJson(item.value),
              reason_for_change: "Initial baseline vital signs entry",
              recorded_by: item.recorded_by,
              recorded_at: item.recorded_at,
              recorded_until: null,
            },
          ],
          assertions: [],
          audit: [
            {
              sequence: 1,
              recorded_at: item.recorded_at,
              actor_id: item.recorded_by,
              action: "observation.record",
              subject: obsRef,
              version: 1,
              reason_for_change: "Initial baseline vital signs entry",
            },
          ],
        });
      }
    }

    for (const study of state.studies.values()) {
      getOrCreateStudyStandards(study);
      recomputeStudyFindings(study);
    }

    const { summaries, receipts } = buildInitialLaySummaries();
    state.laySummaries = summaries;
    state.laySummaryReceipts = receipts;

    const defaultStudies = [
      DEMO_STUDY.code || "CADENCE-101",
      "STUDY-ONC-2026",
      "STUDY-CARDIO-002",
      "DEMO-01",
    ];
    state.ractFactorsByStudy = new Map(
      defaultStudies.map((studyId) => [
        studyId,
        buildDefaultRactFactorsForStudy(studyId),
      ])
    );
    state.kriDefinitions = buildInitialKris();
    state.siteRiskProfilesByStudy = new Map(
      defaultStudies.map((studyId) => [
        studyId,
        buildDefaultSiteRiskProfilesForStudy(studyId, 0),
      ])
    );
    state.csmDossiers = [];

    const initialReport = {
      id: "rpt_demo_dsur_01",
      report_type: "DSUR",
      study_id: DEMO_STUDY.code || "CADENCE-101",
      compound_name: "CAD-101",
      reporting_period_start: isoDateOnly(0),
      reporting_period_end: addDaysIso(0, 90).slice(0, 10),
      status: "FROZEN_COMPILED",
      snapshot_hash: "sha256-demo-dsur-snapshot-01",
      line_listings_count: 3,
      total_subject_exposure: 150,
      compiled_at: demoNowIso(5),
      qppv_manifest: null,
    };
    state.safetyReports = new Map([[initialReport.id, initialReport]]);
  }

  return {
    seedGovernanceState,

    syncStudyValidationAfterFormChange(studyUuid) {
      for (const study of state.studies.values()) {
        const uuid = unversionedRef(study.ref).replace(/^study:/, "");
        if (!studyUuid || uuid === studyUuid) {
          recomputeStudyFindings(study);
        }
      }
    },

    listStudyRulePacks(studyRef) {
      const study = getStudyByRefOrCode(studyRef);
      const studyBaseRef = unversionedRef(study.ref);
      const standards = getOrCreateStudyStandards(study);
      const pinnedSet = new Set(
        standards.pinned_rule_pack_refs.map((r) => unversionedRef(r))
      );
      const packs = [...state.rulePacks.values()].map((pack) =>
        formatRulePackForStudy(pack, pinnedSet)
      );
      return cloneJson({
        study_ref: studyBaseRef,
        profile_ref: standards.profile_ref,
        pinned_rule_pack_refs: [...pinnedSet],
        rule_packs: packs,
      });
    },

    previewStudyRulePack(studyRef, itemRef) {
      const study = getStudyByRefOrCode(studyRef);
      const cleanItemRef = unversionedRef(itemRef);
      const pack = state.rulePacks.get(cleanItemRef);
      if (!pack) {
        return {
          error: {
            status: 404,
            problem: {
              type: "https://cadence.clinical/errors/K.Standards.LibraryItemUnknown",
              title: "Rule pack not found",
              status: 404,
              code: "K.Standards.LibraryItemUnknown",
              detail: `No rule-pack library item matches '${itemRef}'.`,
            },
          },
        };
      }
      const forms = resolveStudyForms(study);
      const violations = [];
      for (const form of forms) {
        const itemMap = buildItemMapForForm(form, []);
        const breaches = evaluatePackOnItemMap(pack, itemMap);
        for (const breach of breaches) {
          violations.push({
            form_id: form.form_id,
            pack_key: breach.pack_key,
            rule_id: breach.rule_id,
            rule_version: Number(breach.rule_version),
            severity: breach.severity,
            dataset: breach.dataset,
            item_id: breach.item_id ?? null,
            message: breach.message,
            remediation_guidance: breach.remediation_guidance ?? null,
          });
        }
      }
      return {
        data: cloneJson({
          item_ref: pack.ref,
          forms_evaluated: forms.length,
          rules_evaluated: pack.rules.length,
          violation_count: violations.length,
          violations,
        }),
      };
    },

    approveLibraryItem(
      itemRef,
      body = {},
      { reason = "", sigToken = "" } = {}
    ) {
      const cleanRef = unversionedRef(itemRef);
      const pack = state.rulePacks.get(cleanRef);
      if (!pack) {
        return {
          error: {
            status: 404,
            problem: {
              type: "https://cadence.clinical/errors/K.Standards.LibraryItemUnknown",
              title: "Library item not found",
              status: 404,
              code: "K.Standards.LibraryItemUnknown",
              detail: `No library item matches '${itemRef}'.`,
            },
          },
        };
      }
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              type: "https://cadence.clinical/errors/K.Command.ReasonForChangeRequired",
              title: "Reason for change required",
              status: 422,
              code: "K.Command.ReasonForChangeRequired",
              detail:
                "X-Change-Reason header is required to approve a rule pack.",
            },
          },
        };
      }
      if (!sigToken && !body?.attestation_id) {
        return {
          error: {
            status: 401,
            problem: {
              type: "https://cadence.clinical/errors/K.Trust.AuthenticationStale",
              title: "Step-up signature token required",
              status: 401,
              code: "K.Trust.AuthenticationStale",
              detail:
                "Approving a library item requires a step-up signature token or attestation ID.",
            },
          },
        };
      }
      pack.status = "Approved";
      recordActivity(
        "library_item.approved",
        "standards",
        "library_item",
        pack.ref,
        getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead"
      );

      let isPinnedAnywhere = false;
      for (const std of state.studyStandards.values()) {
        if (
          std.pinned_rule_pack_refs.some((r) => unversionedRef(r) === cleanRef)
        ) {
          isPinnedAnywhere = true;
          break;
        }
      }
      const pinnedSet = isPinnedAnywhere ? new Set([cleanRef]) : new Set();
      return {
        data: cloneJson(formatRulePackForStudy(pack, pinnedSet)),
      };
    },

    declareStudyRulePacks(studyRef, body = {}, { reason = "" } = {}) {
      const study = getStudyByRefOrCode(studyRef);
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              type: "https://cadence.clinical/errors/K.Command.ReasonForChangeRequired",
              title: "Reason for change required",
              status: 422,
              code: "K.Command.ReasonForChangeRequired",
              detail:
                "X-Change-Reason header is required when updating study standards.",
            },
          },
        };
      }
      const incomingRefs = Array.isArray(body?.rule_pack_refs)
        ? body.rule_pack_refs
        : null;
      if (!incomingRefs) {
        return {
          error: {
            status: 422,
            problem: {
              type: "https://cadence.clinical/errors/K.Standards.DeclarationInvalid",
              title: "Invalid rule pack declaration",
              status: 422,
              code: "K.Standards.DeclarationInvalid",
              detail: "Request body must provide a 'rule_pack_refs' array.",
            },
          },
        };
      }

      const normalized = [];
      for (const rawRef of incomingRefs) {
        const clean = unversionedRef(rawRef);
        const fullRef = clean.includes(":") ? clean : `library_item:${clean}`;
        const pack = state.rulePacks.get(fullRef);
        if (!pack) {
          return {
            error: {
              status: 404,
              problem: {
                type: "https://cadence.clinical/errors/K.Standards.LibraryItemUnknown",
                title: "Library item not found",
                status: 404,
                code: "K.Standards.LibraryItemUnknown",
                detail: `Rule pack '${rawRef}' does not exist in this tenant.`,
              },
            },
          };
        }
        if (pack.status !== "Approved") {
          return {
            error: {
              status: 422,
              problem: {
                type: "https://cadence.clinical/errors/K.Standards.NotApproved",
                title: "Rule pack is not approved",
                status: 422,
                code: "K.Standards.NotApproved",
                detail: `Rule pack '${pack.key}' must be Approved before pinning on a study.`,
              },
            },
          };
        }
        if (!normalized.includes(fullRef)) {
          normalized.push(fullRef);
        }
      }

      const standards = getOrCreateStudyStandards(study);
      standards.pinned_rule_pack_refs = normalized;
      recomputeStudyFindings(study);
      recordActivity(
        "study.standards_declared",
        "standards",
        "study",
        unversionedRef(study.ref),
        getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead"
      );

      return { data: this.listStudyRulePacks(study.ref) };
    },

    listStudyValidationFindings(studyRef, filters = {}) {
      const study = getStudyByRefOrCode(studyRef);
      const studyBaseRef = unversionedRef(study.ref);
      let findings = state.validationFindingsByStudy.get(studyBaseRef);
      if (!findings) {
        findings = recomputeStudyFindings(study);
      }
      let filtered = [...findings];
      if (filters.stage) {
        filtered = filtered.filter(
          (f) => f.stage.toLowerCase() === String(filters.stage).toLowerCase()
        );
      }
      if (filters.status) {
        filtered = filtered.filter(
          (f) => f.status.toLowerCase() === String(filters.status).toLowerCase()
        );
      }
      if (filters.severity) {
        filtered = filtered.filter(
          (f) =>
            f.severity.toLowerCase() === String(filters.severity).toLowerCase()
        );
      }
      if (filters.dataset) {
        filtered = filtered.filter(
          (f) =>
            String(f.dataset || "").toLowerCase() ===
            String(filters.dataset).toLowerCase()
        );
      }
      if (filters.pack_key) {
        filtered = filtered.filter((f) => f.pack_key === filters.pack_key);
      }
      const total = filtered.length;
      if (
        filters.limit !== undefined &&
        filters.limit !== null &&
        filters.limit !== ""
      ) {
        const parsedLimit = Number(filters.limit);
        if (Number.isInteger(parsedLimit) && parsedLimit >= 0) {
          filtered = filtered.slice(0, parsedLimit);
        }
      }
      return cloneJson({
        total,
        findings: filtered,
      });
    },

    getValidationFinding(findingRef) {
      const cleanRef = unversionedRef(findingRef);
      const fullRef = cleanRef.includes(":")
        ? cleanRef
        : `validation_finding:${cleanRef}`;
      const finding = state.validationFindingsByRef.get(fullRef);
      if (!finding) {
        return {
          error: {
            status: 404,
            problem: {
              type: "https://cadence.clinical/errors/K.Aggregate.NotFound",
              title: "Validation finding not found",
              status: 404,
              code: "K.Aggregate.NotFound",
              detail: `Validation finding '${findingRef}' was not found.`,
            },
          },
        };
      }
      return {
        data: cloneJson({
          finding,
          explanation: buildFindingExplanation(finding),
        }),
      };
    },

    previewFormValues(formId, body = {}) {
      const cleanFormId = String(formId || "").trim();
      const form =
        state.forms.get(cleanFormId) || [...state.forms.values()][0] || null;
      if (!form) {
        return {
          error: {
            status: 404,
            problem: {
              type: "https://cadence.clinical/errors/K.Aggregate.NotFound",
              title: "Form not found",
              status: 404,
              code: "K.Aggregate.NotFound",
              detail: `Form '${cleanFormId}' does not exist.`,
            },
          },
        };
      }

      let matchedStudy = null;
      for (const candidate of state.studies.values()) {
        const candidateUuid = unversionedRef(candidate.ref).replace(
          /^study:/,
          ""
        );
        if (candidateUuid === form.study_id) {
          matchedStudy = candidate;
          break;
        }
      }
      if (!matchedStudy) {
        matchedStudy = getStudyByRefOrCode(DEMO_STUDY.code || "CADENCE-101");
      }

      const standards = getOrCreateStudyStandards(matchedStudy);
      const pinnedPacks = standards.pinned_rule_pack_refs
        .map((r) => state.rulePacks.get(unversionedRef(r)))
        .filter(Boolean);

      const draftValues = Array.isArray(body?.values) ? body.values : [];
      const itemMap = buildItemMapForForm(form, draftValues);
      const warnings = [];

      for (const pack of pinnedPacks) {
        const breaches = evaluatePackOnItemMap(pack, itemMap);
        for (const breach of breaches) {
          warnings.push({
            pack_key: breach.pack_key,
            rule_id: breach.rule_id,
            rule_version: Number(breach.rule_version),
            severity: breach.severity,
            dataset: breach.dataset,
            item_id: breach.item_id,
            message: breach.message,
            remediation_guidance: breach.remediation_guidance ?? null,
          });
        }
      }

      return {
        data: cloneJson({
          form_id: cleanFormId || form.form_id,
          warnings,
        }),
      };
    },

    // Lay Summaries & Distribution Receipts
    listLaySummaries() {
      return cloneJson(state.laySummaries);
    },

    listLaySummaryReceipts(summaryId) {
      const key = String(summaryId || "").trim();
      return cloneJson(state.laySummaryReceipts.get(key) || []);
    },

    confirmLaySummaryReceipt(summaryId, body = {}) {
      const key = String(summaryId || "").trim() || "lsum-cadence-101";
      const idx = nextCounter();
      const summary =
        state.laySummaries.find((s) => s.id === key) || state.laySummaries[0];
      const subjectId =
        String(body?.subject_id || "").trim() ||
        DEMO_PARTICIPANTS[0]?.subjectCode ||
        "SUBJ-101-001";
      const studyId =
        String(body?.study_id || "").trim() ||
        summary?.study_id ||
        DEMO_STUDY.code ||
        "CADENCE-101";
      const scoreRaw = Number(body?.comprehension_score ?? 5);
      const comprehensionScore =
        Number.isFinite(scoreRaw) && scoreRaw >= 1 && scoreRaw <= 5
          ? Math.round(scoreRaw)
          : 5;

      const receipt = {
        id: `rcpt-${idx}`,
        summary_id: key,
        subject_id: subjectId,
        study_id: studyId,
        comprehension_score: comprehensionScore,
        comprehension_feedback: body?.comprehension_feedback
          ? String(body.comprehension_feedback)
          : null,
        acknowledgment_statement: body?.acknowledgment_statement || null,
        signer_identity: `${subjectId} (21 CFR Part 11 Verified)`,
        confirmed_at: demoNowIso(idx),
        receipt_hash: `sha256-demo-rcpt-${String(idx).padStart(4, "0")}`,
      };

      const existing = state.laySummaryReceipts.get(key) || [];
      existing.unshift(receipt);
      state.laySummaryReceipts.set(key, existing);
      return cloneJson(receipt);
    },

    publishLaySummary(body = {}) {
      const idx = nextCounter();
      const studyId =
        String(body?.study_id || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      const sections = cloneJson(
        body?.plain_language_sections || body?.sections || {}
      );
      const created = {
        id: `lsum-${idx}`,
        study_id: studyId,
        protocol_id: String(body?.protocol_id || `${studyId}-V2`),
        title:
          String(body?.title || "").trim() ||
          `Plain-Language Trial Results Summary (${studyId})`,
        trial_phase: body?.trial_phase || "Phase II",
        therapeutic_area: body?.therapeutic_area || "Cardiometabolic Medicine",
        sponsor_name:
          body?.sponsor_name ||
          DEMO_SPONSOR.name ||
          "Cadence Clinical Research",
        irb_approval_number:
          body?.irb_approval_number || `IRB-${studyId}-${idx}`,
        irb_approval_date: body?.irb_approval_date || isoDateOnly(idx),
        language: body?.language || "en",
        language_code: body?.language || "en",
        reading_grade_level: 6.6,
        flesch_kincaid_score: 73.5,
        status: "PUBLISHED",
        published_at: demoNowIso(idx),
        regulatory_deadline: addDaysIso(idx, 180),
        eu_ctr_compliant: true,
        cryptographic_hash: `sha256-demo-lsum-${String(idx).padStart(4, "0")}`,
        summary_text:
          body?.summary_text ||
          `Published plain-language clinical study results for ${studyId}.`,
        sections,
        plain_language_sections: sections,
      };
      state.laySummaries.unshift(created);
      if (!state.laySummaryReceipts.has(created.id)) {
        state.laySummaryReceipts.set(created.id, []);
      }
      return cloneJson(created);
    },

    // Quality & RBQM
    listRactFactors(studyId) {
      const key =
        String(studyId || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      if (!state.ractFactorsByStudy.has(key)) {
        state.ractFactorsByStudy.set(key, buildDefaultRactFactorsForStudy(key));
      }
      return cloneJson(state.ractFactorsByStudy.get(key));
    },

    seedDefaultRactFactors(studyId) {
      const key =
        String(studyId || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      const defaults = buildDefaultRactFactorsForStudy(key);
      const existing = state.ractFactorsByStudy.get(key) || [];
      const merged = [...existing];
      for (const factor of defaults) {
        if (!merged.some((f) => f.risk_topic === factor.risk_topic)) {
          merged.push(factor);
        }
      }
      state.ractFactorsByStudy.set(key, merged);
      return cloneJson(merged);
    },

    createRactFactor(body = {}, reason = "Added RACT risk factor") {
      const idx = nextCounter();
      const studyId =
        String(body?.study_id || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      const prob = Number(body?.probability_score ?? 2);
      const impact = Number(body?.impact_score ?? 2);
      const detect = Number(body?.detectability_score ?? 2);
      const total = prob * impact * detect;
      const tier = total >= 15 ? "HIGH" : total >= 7 ? "MEDIUM" : "LOW";
      const created = {
        id: `ract-${idx}`,
        study_id: studyId,
        category: String(body?.category || "SAFETY").toUpperCase(),
        risk_topic: String(body?.risk_topic || `Protocol Risk Factor ${idx}`),
        risk_description: String(
          body?.risk_description || "Controlled RBQM risk evaluation entry."
        ),
        probability_score: prob,
        impact_score: impact,
        detectability_score: detect,
        total_risk_score: total,
        risk_tier: tier,
        mitigation_strategy: String(
          body?.mitigation_strategy || "Centralized KRI monitoring."
        ),
        linked_kri_code: body?.linked_kri_code || undefined,
        is_active: true,
        created_at: demoNowIso(idx),
        created_by:
          body?.created_by ||
          getDemoPersonaForWorkspace("super_admin")?.username ||
          "designer.lead",
        version_index: 1,
        reason_for_change: body?.reason_for_change || reason,
      };
      const list = state.ractFactorsByStudy.get(studyId) || [];
      list.unshift(created);
      state.ractFactorsByStudy.set(studyId, list);
      return cloneJson(created);
    },

    updateRactFactor(factorId, body = {}, reason = "Updated RACT risk factor") {
      const targetId = String(factorId || "").trim();
      for (const [studyId, list] of state.ractFactorsByStudy.entries()) {
        const found = list.find((f) => f.id === targetId);
        if (found) {
          const prob = Number(
            body?.probability_score ?? found.probability_score
          );
          const impact = Number(body?.impact_score ?? found.impact_score);
          const detect = Number(
            body?.detectability_score ?? found.detectability_score
          );
          const total = prob * impact * detect;
          found.category = body?.category ?? found.category;
          found.risk_topic = body?.risk_topic ?? found.risk_topic;
          found.risk_description =
            body?.risk_description ?? found.risk_description;
          found.probability_score = prob;
          found.impact_score = impact;
          found.detectability_score = detect;
          found.total_risk_score = total;
          found.risk_tier =
            total >= 15 ? "HIGH" : total >= 7 ? "MEDIUM" : "LOW";
          found.mitigation_strategy =
            body?.mitigation_strategy ?? found.mitigation_strategy;
          found.linked_kri_code =
            body?.linked_kri_code !== undefined
              ? body.linked_kri_code || undefined
              : found.linked_kri_code;
          found.version_index = Number(found.version_index || 1) + 1;
          found.reason_for_change = body?.reason_for_change || reason;
          state.ractFactorsByStudy.set(studyId, list);
          return cloneJson(found);
        }
      }
      return null;
    },

    deleteRactFactor(factorId) {
      const targetId = String(factorId || "").trim();
      for (const [studyId, list] of state.ractFactorsByStudy.entries()) {
        const next = list.filter((f) => f.id !== targetId);
        if (next.length !== list.length) {
          state.ractFactorsByStudy.set(studyId, next);
          return { deleted: true, id: targetId };
        }
      }
      return { deleted: false, id: targetId };
    },

    listKriDefinitions() {
      return cloneJson(state.kriDefinitions);
    },

    listSiteRiskProfiles(studyId) {
      const key =
        String(studyId || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      if (!state.siteRiskProfilesByStudy.has(key)) {
        state.siteRiskProfilesByStudy.set(
          key,
          buildDefaultSiteRiskProfilesForStudy(key, 0)
        );
      }
      return cloneJson(state.siteRiskProfilesByStudy.get(key));
    },

    computeSiteRiskProfiles(studyId) {
      const idx = nextCounter();
      const key =
        String(studyId || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      const factors = this.listRactFactors(key);
      const highFactorBonus =
        factors.filter((f) => f.risk_tier === "HIGH").length * 0.2;
      const computed = buildDefaultSiteRiskProfilesForStudy(
        key,
        highFactorBonus
      ).map((profile) => ({
        ...profile,
        evaluation_date: demoNowIso(idx),
      }));
      state.siteRiskProfilesByStudy.set(key, computed);
      return cloneJson(computed);
    },

    compileCsmDossier(body = {}) {
      const idx = nextCounter();
      const studyId =
        String(body?.study_id || "").trim() || DEMO_STUDY.code || "CADENCE-101";
      const factors = this.listRactFactors(studyId);
      const highCount = factors.filter((f) => f.risk_tier === "HIGH").length;
      const medCount = factors.filter((f) => f.risk_tier === "MEDIUM").length;
      const lowCount = factors.filter((f) => f.risk_tier === "LOW").length;
      const linkedCount = factors.filter((f) => f.linked_kri_code).length;
      const coveragePct =
        factors.length > 0
          ? Math.round((linkedCount / factors.length) * 100)
          : 0;
      const signedAt = demoNowIso(idx);

      const dossier = {
        id: `csm-dossier-${idx}`,
        study_id: studyId,
        dossier_name:
          body?.dossier_name || `CSM-Inspection-Dossier-${studyId}-${idx}`,
        status: "SEALED_SIGNED",
        merkle_seal: `sha256-demo-csm-merkle-${String(idx).padStart(4, "0")}`,
        ract_summary: {
          total_risk_factors: factors.length,
          high_risk_count: highCount,
          medium_risk_count: medCount,
          low_risk_count: lowCount,
          linked_kri_coverage_percentage: coveragePct,
        },
        kri_surveillance_summary: {
          total_kris_defined: state.kriDefinitions.length,
          high_or_critical_evaluations_count: 1,
        },
        electronic_signature: {
          signer_name: body?.signer_name || "Lead Quality Reviewer",
          signer_role:
            body?.signer_role || "Head of Clinical Quality Assurance",
          signed_at: signedAt,
          signature_hash: `sha256-demo-csm-sig-${String(idx).padStart(4, "0")}`,
        },
      };
      state.csmDossiers.unshift(dossier);
      return cloneJson(dossier);
    },

    // Aggregate Safety & Pharmacovigilance
    calculateSafetyExposure(body = {}) {
      const idx = nextCounter();
      return cloneJson({
        study_id: body?.study_id || DEMO_STUDY.code || "CADENCE-101",
        calculated_at: demoNowIso(idx),
        metrics: {
          total_cumulative_subjects: 156,
          total_interval_subjects: 124,
          total_cumulative_patient_years: 148.4,
          total_interval_patient_years: 102.6,
          by_treatment_arm: [
            {
              group_name: "Arm A: Active 50mg QD",
              subject_count: 62,
              interval_patient_years: 50.8,
              cumulative_patient_years: 75.2,
              mean_duration_days: 284,
              median_duration_days: 302,
            },
            {
              group_name: "Arm B: Active 10mg QD",
              subject_count: 52,
              interval_patient_years: 33.9,
              cumulative_patient_years: 46.8,
              mean_duration_days: 244,
              median_duration_days: 254,
            },
            {
              group_name: "Arm C: Placebo Control",
              subject_count: 42,
              interval_patient_years: 17.9,
              cumulative_patient_years: 26.4,
              mean_duration_days: 204,
              median_duration_days: 212,
            },
          ],
          by_age_group: [
            {
              group_name: "18-64 (Adult)",
              subject_count: 122,
              interval_patient_years: 81.4,
              cumulative_patient_years: 117.6,
            },
            {
              group_name: ">= 65 (Older Adult)",
              subject_count: 34,
              interval_patient_years: 21.2,
              cumulative_patient_years: 30.8,
            },
          ],
          by_sex: [
            {
              group_name: "FEMALE",
              subject_count: 81,
              interval_patient_years: 53.4,
              cumulative_patient_years: 77.2,
            },
            {
              group_name: "MALE",
              subject_count: 75,
              interval_patient_years: 49.2,
              cumulative_patient_years: 71.2,
            },
          ],
        },
      });
    },

    compilePeriodicSafetyReport(body = {}) {
      const idx = nextCounter();
      const reportId = `rpt_dsur_${idx}`;
      const compiled = {
        id: reportId,
        report_type: body?.report_type || "DSUR",
        study_id: body?.study_id || DEMO_STUDY.code || "CADENCE-101",
        compound_name: body?.compound_name || "CAD-101",
        reporting_period_start: body?.reporting_period_start || isoDateOnly(0),
        reporting_period_end:
          body?.reporting_period_end || addDaysIso(0, 90).slice(0, 10),
        status: "IN_REVIEW",
        snapshot_hash: `sha256-demo-dsur-snap-${String(idx).padStart(4, "0")}`,
        line_listings_count: 3,
        total_subject_exposure: 156,
        compiled_at: demoNowIso(idx),
        qppv_manifest: null,
      };
      state.safetyReports.set(reportId, compiled);
      return cloneJson(compiled);
    },

    qppvSignPeriodicSafetyReport(reportId, body = {}) {
      const idx = nextCounter();
      const key = String(reportId || "").trim() || "rpt_demo_dsur_01";
      const existing = state.safetyReports.get(key) || {
        id: key,
        report_type: "DSUR",
        study_id: DEMO_STUDY.code || "CADENCE-101",
        snapshot_hash: "sha256-demo-dsur-snapshot-01",
      };
      const manifest = {
        qppv_user_id: body?.user_id || "safety.officer",
        qppv_name: body?.user_id || "safety.officer",
        qppv_role:
          body?.role || "Qualified Person for Pharmacovigilance (QPPV)",
        meaning_of_signature:
          body?.meaning_of_signature ||
          "Executive QPPV Approval of Periodic Safety Report",
        signed_at: demoNowIso(idx),
        manifest_id: `sig_qppv_${String(idx).padStart(4, "0")}`,
        snapshot_hash: existing.snapshot_hash,
        verification_status: "VALID",
      };
      existing.status = "APPROVED";
      existing.qppv_manifest = manifest;
      state.safetyReports.set(key, existing);
      return cloneJson({
        id: existing.id,
        status: "APPROVED",
        snapshot_hash: existing.snapshot_hash,
        qppv_manifest: manifest,
      });
    },

    evaluateSafetyRules(body = {}) {
      const idx = nextCounter();
      const isFatalOrLt = Boolean(
        body?.seriousness_death || body?.seriousness_life_threatening
      );
      const anySerious =
        isFatalOrLt ||
        Boolean(
          body?.seriousness_hospitalization ||
          body?.seriousness_disability ||
          body?.seriousness_congenital_anomaly ||
          body?.seriousness_other
        );
      const nonCausal = new Set(["NOT_RELATED", "UNLIKELY"]);
      const isCausal = !nonCausal.has(
        String(body?.causality || "SUSPECTED").toUpperCase()
      );
      const isUnexpected =
        String(body?.expectedness || "UNEXPECTED").toUpperCase() ===
        "UNEXPECTED";
      const isSusar = anySerious && isCausal && isUnexpected;
      const shortestDays = isSusar ? (isFatalOrLt ? 7 : 15) : null;
      const dueIso = shortestDays
        ? addDaysIso(idx, shortestDays).replace("T", " ").slice(0, 16) + " UTC"
        : null;
      const remainingHours = shortestDays ? shortestDays * 24 - 4 : 0;

      const authorities = [
        {
          authority: "US FDA (IND Safety Report)",
          country_code: "US",
          citation7: "21 CFR 312.32(c)(1)(i)",
          citation15: "21 CFR 312.32(c)(1)(i)",
        },
        {
          authority: "EMA EudraVigilance Gateway",
          country_code: "EU",
          citation7: "Regulation (EU) No 536/2014 Art. 42(2)(a)",
          citation15: "Regulation (EU) No 536/2014 Art. 42(2)(b)",
        },
        {
          authority: "PMDA Japan (MHLW)",
          country_code: "JP",
          citation7: "PMD Act Art. 80-2 (7-Day Fatal/LT)",
          citation15: "PMD Act Art. 80-2 (15-Day Serious Unexpected)",
        },
      ];

      return cloneJson({
        case_id: body?.case_id || `ICSR-${idx}`,
        study_id: body?.study_id || DEMO_STUDY.code || "CADENCE-101",
        subject_id:
          body?.subject_id ||
          DEMO_PARTICIPANTS[0]?.subjectCode ||
          "SUBJ-101-001",
        evaluated_at: demoNowIso(idx),
        is_susar: isSusar,
        shortest_timeframe_days: shortestDays,
        evaluations: authorities.map((auth) => ({
          authority: auth.authority,
          country_code: auth.country_code,
          is_expedited: isSusar,
          timeframe_days: shortestDays,
          rule_name: isSusar
            ? `${shortestDays}-Day Expedited SUSAR Mandate`
            : "Periodic Aggregate DSUR Inclusion Only",
          statutory_citation:
            shortestDays === 7 ? auth.citation7 : auth.citation15,
          countdown: isSusar
            ? {
                alert_level: shortestDays === 7 ? "WARNING" : "NORMAL",
                remaining_hours: remainingHours,
              }
            : null,
          due_date: dueIso,
        })),
      });
    },
  };
}
