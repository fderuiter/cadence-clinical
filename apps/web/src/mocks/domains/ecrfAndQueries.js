import { hasStudyWideScope, isSitePermitted } from "../demoPolicy.js";
import {
  DEFAULT_FORM_UUID,
  DEFAULT_QUERY_UUID,
  ENROLLMENT_1001_UUID,
  ENROLLMENT_1002_UUID,
  OBS_1_UUID,
  OBS_2_UUID,
  OBS_3_UUID,
  OBS_4_UUID,
  SITE_101_UUID,
  SITE_102_UUID,
  STUDY_UUID,
  VERSION_V2_UUID,
  cloneJson,
  computeContentHash,
  makeUuidFromIndex,
  resolveCollectionTitle,
  resolveEnrollmentByIdOrCode,
  resolveSiteUuid,
} from "./shared.js";

function createInitialForm() {
  const items = [
    {
      item_id: "item-sysbp",
      concept: "SYSBP",
      unit: "mm[Hg]",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "128",
        unit: "mm[Hg]",
        comparator: null,
      },
      observation_id: OBS_1_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:15:00Z",
    },
    {
      item_id: "item-diabp",
      concept: "DIABP",
      unit: "mm[Hg]",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "82",
        unit: "mm[Hg]",
        comparator: null,
      },
      observation_id: OBS_2_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:16:00Z",
    },
    {
      item_id: "item-pulse",
      concept: "PULSE",
      unit: "/min",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "72",
        unit: "/min",
        comparator: null,
      },
      observation_id: OBS_3_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:17:00Z",
    },
    {
      item_id: "item-notes",
      concept: "VSCOMM",
      unit: null,
      value_kind: "text",
      required: false,
      value: { type: "text", value: "Seated rest 5 minutes prior to cuff." },
      observation_id: OBS_4_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:18:00Z",
    },
  ];

  return {
    form_id: DEFAULT_FORM_UUID,
    enrollment_id: ENROLLMENT_1001_UUID,
    participant_id: "00000000-0000-0000-0000-000000005001", // deid-ignore
    study_id: STUDY_UUID,
    study_site_id: SITE_101_UUID,
    protocol_version_id: VERSION_V2_UUID,
    collection_spec_id: "form-vital-signs",
    collection_title: "Vital Signs (CDASH VS)",
    planned_activity_id: "visit-baseline",
    collected_at: "2026-08-01T09:15:00Z",
    opened_by: "usr-crc-01",
    opened_at: "2026-08-01T09:10:00Z",
    version: 1,
    complete: true,
    missing_required: [],
    items,
    locks: [],
    assertions: [],
    signatures: [],
    content_hash: computeContentHash(items),
  };
}

export function createEcrfAndQueriesDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.forms = new Map();
  state.queries = new Map();
  state.queryDigests = [];

  function seed() {
    const initialForm = createInitialForm();
    state.forms = new Map([[initialForm.form_id, initialForm]]);

    state.queries = new Map([
      [
        DEFAULT_QUERY_UUID,
        {
          query_id: DEFAULT_QUERY_UUID,
          form_id: DEFAULT_FORM_UUID,
          enrollment_id: ENROLLMENT_1001_UUID,
          item_id: "item-sysbp",
          observation_id: OBS_1_UUID,
          revision: 1,
          current_revision: 1,
          value_changed: false,
          status: "open",
          origin: "manual",
          owner_role: "site",
          assignee: "usr-crc-01",
          priority: "high",
          opened_at: "2026-08-02T11:00:00Z",
          status_since: "2026-08-02T11:00:00Z",
          due_at: "2026-08-16T11:00:00Z",
          escalated_at: "2026-08-17T00:05:00Z",
          last_answered_by: null,
          text: "Confirm seated rest duration prior to baseline systolic BP.",
          messages: [
            {
              kind: "raised",
              text: "Confirm seated rest duration prior to baseline systolic BP.",
              by: "usr-cra-01",
              at: "2026-08-02T11:00:00Z",
              reason_for_change: "Source chart audit discrepancy",
            },
            {
              kind: "escalated",
              text: "14-day SLA reached; escalated to High priority.",
              by: "svc:execution-edit-checks",
              at: "2026-08-17T00:05:00Z",
              reason_for_change: "14-day SLA escalation",
            },
          ],
          raised_by: "usr-cra-01",
          raised_at: "2026-08-02T11:00:00Z",
          version: 2,
          study_id: STUDY_UUID,
          study_site_id: SITE_101_UUID,
          site_code: "SITE-101",
          subject_code: "SUBJ-1001",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
      ],
      [
        "00000000-0000-0000-0000-000000003002", // deid-ignore
        {
          query_id: "00000000-0000-0000-0000-000000003002", // deid-ignore
          form_id: "00000000-0000-0000-0000-000000002002", // deid-ignore
          enrollment_id: ENROLLMENT_1002_UUID,
          item_id: "item-diabp",
          observation_id: OBS_2_UUID,
          revision: 1,
          current_revision: 2,
          value_changed: true,
          status: "answered",
          origin: "manual",
          owner_role: "monitor",
          assignee: null,
          priority: "normal",
          opened_at: "2026-08-04T09:15:00Z",
          status_since: "2026-08-05T14:20:00Z",
          due_at: null,
          escalated_at: null,
          last_answered_by: "usr-crc-01",
          text: "Verify baseline diastolic blood pressure against bedside strip.",
          messages: [
            {
              kind: "raised",
              text: "Verify baseline diastolic blood pressure against bedside strip.",
              by: "usr-cra-01",
              at: "2026-08-04T09:15:00Z",
              reason_for_change: "Routine SDV review",
            },
            {
              kind: "answered",
              text: "Observation updated to revision 2.",
              by: "usr-crc-01",
              at: "2026-08-05T14:20:00Z",
              reason_for_change: "Corrected from signed vitals flowsheet",
            },
          ],
          raised_by: "usr-cra-01",
          raised_at: "2026-08-04T09:15:00Z",
          version: 2,
          study_id: STUDY_UUID,
          study_site_id: SITE_102_UUID,
          site_code: "SITE-102",
          subject_code: "SUBJ-1002",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
      ],
      [
        "00000000-0000-0000-0000-000000003003", // deid-ignore
        {
          query_id: "00000000-0000-0000-0000-000000003003", // deid-ignore
          form_id: "00000000-0000-0000-0000-000000002002", // deid-ignore
          enrollment_id: ENROLLMENT_1002_UUID,
          item_id: "item-hr",
          observation_id: OBS_3_UUID,
          revision: 1,
          current_revision: 1,
          value_changed: false,
          status: "open",
          origin: "system",
          owner_role: "site",
          assignee: null,
          priority: "normal",
          opened_at: "2026-08-10T08:00:00Z",
          status_since: "2026-08-10T08:00:00Z",
          due_at: "2026-08-24T08:00:00Z",
          escalated_at: null,
          last_answered_by: null,
          text: "[RULE-HR-RANGE] Resting heart rate outside expected 50-100 bpm window.",
          messages: [
            {
              kind: "raised",
              text: "[RULE-HR-RANGE] Resting heart rate outside expected 50-100 bpm window.",
              by: "svc:execution-edit-checks",
              at: "2026-08-10T08:00:00Z",
              reason_for_change: "Post-commit RulePack evaluation",
            },
          ],
          raised_by: "svc:execution-edit-checks",
          raised_at: "2026-08-10T08:00:00Z",
          version: 1,
          study_id: STUDY_UUID,
          study_site_id: SITE_102_UUID,
          site_code: "SITE-102",
          subject_code: "SUBJ-1002",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
      ],
    ]);
    state.queryDigests = [];
  }

  const methods = {
    openForm(body) {
      const enrollmentId = body?.enrollment_id || ENROLLMENT_1001_UUID;
      const collectionSpecId = body?.collection_spec_id || "form-vital-signs";
      const plannedActivityId =
        body &&
        Object.prototype.hasOwnProperty.call(body, "planned_activity_id")
          ? (body.planned_activity_id ?? null)
          : "visit-baseline";

      for (const existing of state.forms.values()) {
        if (
          (body?.form_id && existing.form_id === body.form_id) ||
          (existing.enrollment_id === enrollmentId &&
            existing.collection_spec_id === collectionSpecId &&
            existing.planned_activity_id === plannedActivityId &&
            (!body?.collected_at ||
              existing.collected_at === body.collected_at))
        ) {
          return {
            form_id: existing.form_id,
            enrollment_id: existing.enrollment_id,
            protocol_version_id: existing.protocol_version_id,
            collection_spec_id: existing.collection_spec_id,
            collection_title:
              existing.collection_title ??
              resolveCollectionTitle(existing.collection_spec_id),
            planned_activity_id: existing.planned_activity_id,
            collected_at: existing.collected_at,
            version: existing.version,
          };
        }
      }

      const idx = nextCounter();
      const formId = body?.form_id || makeUuidFromIndex("00002000", idx);
      const obsSysId = makeUuidFromIndex("00006001", idx);
      const obsDiaId = makeUuidFromIndex("00006002", idx);
      const now = body?.collected_at || new Date().toISOString();

      const items = [
        {
          item_id: "item-sysbp",
          concept: "SYSBP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: null,
          observation_id: obsSysId,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
        {
          item_id: "item-diabp",
          concept: "DIABP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: null,
          observation_id: obsDiaId,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
      ];

      const subjMatch = resolveEnrollmentByIdOrCode(
        state.subjects,
        enrollmentId
      );
      const siteUuid = resolveSiteUuid(subjMatch?.site_id || "SITE-101");
      const collectionTitle = resolveCollectionTitle(collectionSpecId);

      const created = {
        form_id: formId,
        enrollment_id: enrollmentId,
        participant_id: subjMatch?.id || makeUuidFromIndex("00005000", idx),
        study_id: STUDY_UUID,
        study_site_id: siteUuid,
        protocol_version_id: VERSION_V2_UUID,
        collection_spec_id: collectionSpecId,
        collection_title: collectionTitle,
        planned_activity_id: plannedActivityId,
        collected_at: now,
        opened_by: "usr-crc-01",
        opened_at: now,
        version: 1,
        complete: false,
        missing_required: ["item-sysbp", "item-diabp"],
        items,
        locks: [],
        assertions: [],
        signatures: [],
        content_hash: computeContentHash(items),
      };
      state.forms.set(formId, created);

      return {
        form_id: created.form_id,
        enrollment_id: created.enrollment_id,
        protocol_version_id: created.protocol_version_id,
        collection_spec_id: created.collection_spec_id,
        collection_title: created.collection_title,
        planned_activity_id: created.planned_activity_id,
        collected_at: created.collected_at,
        version: created.version,
      };
    },

    listEnrollmentForms(enrollmentId) {
      const targetId = String(enrollmentId || "").trim();
      if (!targetId) return [];
      const matches = [...state.forms.values()]
        .filter((f) => f.enrollment_id === targetId)
        .sort((a, b) =>
          String(b.collected_at || "").localeCompare(
            String(a.collected_at || "")
          )
        );
      return matches.map((f) => {
        const itemsList = Array.isArray(f.items) ? f.items : [];
        const recordedItems = itemsList.filter(
          (it) =>
            it.value !== null &&
            it.value !== undefined &&
            !(it.value.type === "missing" && it.value.reason === "not_asked")
        );
        const recordedCount = recordedItems.length;
        const missingRequiredCount = Array.isArray(f.missing_required)
          ? f.missing_required.length
          : itemsList.filter(
              (it) =>
                it.required &&
                (!it.value ||
                  (it.value.type === "missing" &&
                    it.value.reason === "not_asked"))
            ).length;

        const formQueries = [...state.queries.values()].filter(
          (q) => q.form_id === f.form_id
        );
        const openQueryCount = formQueries.filter(
          (q) => String(q.status).toLowerCase() === "open"
        ).length;
        const answeredQueryCount = formQueries.filter(
          (q) => String(q.status).toLowerCase() === "answered"
        ).length;

        const formAssertions = Array.isArray(f.assertions) ? f.assertions : [];
        const activeFormVerified = formAssertions.some(
          (a) =>
            !a.retracted_at &&
            String(a.kind).toLowerCase() === "verified" &&
            !String(a.target || "").startsWith("observation:")
        );
        let verifiedCount = 0;
        for (const it of recordedItems) {
          if (activeFormVerified) {
            verifiedCount += 1;
            continue;
          }
          const obsRef = `observation:${it.observation_id}`;
          const obs = state.observations.get(obsRef);
          const obsAssertions = Array.isArray(obs?.assertions)
            ? obs.assertions
            : [];
          const isObsVerified =
            obsAssertions.some(
              (a) =>
                !a.retracted_at &&
                String(a.kind).toLowerCase() === "verified" &&
                (a.revision === undefined ||
                  Number(a.revision) === Number(it.revision))
            ) ||
            formAssertions.some(
              (a) =>
                !a.retracted_at &&
                String(a.kind).toLowerCase() === "verified" &&
                a.target === obsRef &&
                (a.revision === undefined ||
                  Number(a.revision) === Number(it.revision))
            );
          if (isObsVerified) {
            verifiedCount += 1;
          }
        }
        const unverifiedCount = Math.max(0, recordedCount - verifiedCount);

        const formLocks = Array.isArray(f.locks) ? f.locks : [];
        const studyLocked = state.locks.some(
          (lk) => String(lk.status || "").toUpperCase() === "LOCKED"
        );
        const hasHardLock =
          studyLocked ||
          formLocks.some(
            (lk) =>
              !lk.released_at &&
              String(lk.level || lk.kind || "locked").toLowerCase() !== "frozen"
          );
        const hasFreeze =
          formLocks.some(
            (lk) =>
              !lk.released_at &&
              String(lk.level || lk.kind || "").toLowerCase() === "frozen"
          ) ||
          formAssertions.some(
            (a) => !a.retracted_at && String(a.kind).toLowerCase() === "frozen"
          );
        const activeLockCount =
          formLocks.filter((lk) => !lk.released_at).length +
          (studyLocked ? 1 : 0);

        const sigs = Array.isArray(f.signatures) ? f.signatures : [];
        const signatureCount = sigs.length;
        const signed =
          signatureCount > 0 &&
          sigs.some((s) => !s.superseded && s.content_hash === f.content_hash);
        const signatureSuperseded = signatureCount > 0 && !signed;

        return {
          form_id: f.form_id,
          enrollment_id: f.enrollment_id,
          participant_id: f.participant_id,
          study_id: f.study_id,
          study_site_id: f.study_site_id,
          protocol_version_id: f.protocol_version_id,
          collection_spec_id: f.collection_spec_id,
          planned_activity_id: f.planned_activity_id,
          collected_at: f.collected_at,
          opened_by: f.opened_by,
          opened_at: f.opened_at,
          version: f.version,
          complete: Boolean(f.complete),
          item_count: itemsList.length,
          recorded_count: recordedCount,
          missing_required_count: missingRequiredCount,
          open_query_count: openQueryCount,
          answered_query_count: answeredQueryCount,
          verified_count: verifiedCount,
          unverified_count: unverifiedCount,
          locked: hasHardLock,
          frozen: !hasHardLock && hasFreeze,
          lock_count: activeLockCount,
          signed,
          signature_superseded: signatureSuperseded,
          signature_count: signatureCount,
          collection_title:
            f.collection_title ?? resolveCollectionTitle(f.collection_spec_id),
        };
      });
    },

    getForm(formId) {
      let form = state.forms.get(formId);
      if (!form) {
        form = state.forms.get(DEFAULT_FORM_UUID);
      }
      return cloneJson(form);
    },

    saveFormValues(formId, body, reason = "Saved eCRF form values") {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      const incoming = Array.isArray(body?.values) ? body.values : [];
      const now = new Date().toISOString();
      const savedValues = [];
      let anyChanged = false;

      for (const entry of incoming) {
        let item = form.items.find((it) => it.item_id === entry.item_id);
        if (!item) {
          const idx = nextCounter();
          item = {
            item_id: entry.item_id,
            concept: entry.item_id.toUpperCase(),
            unit: entry.value?.unit ?? null,
            value_kind: entry.value?.type ?? "text",
            required: false,
            value: null,
            observation_id:
              entry.observation_id || makeUuidFromIndex("00006100", idx),
            revision: 0,
            recorded_by: null,
            recorded_at: null,
          };
          form.items.push(item);
        }

        const expectedRev = Number(entry.expected_revision ?? item.revision);
        if (
          entry.expected_revision !== undefined &&
          expectedRev !== Number(item.revision)
        ) {
          return {
            conflict: true,
            problem: {
              title: "Stale item revision",
              status: 409,
              code: "K.Collection.RevisionStale",
              detail: `Item ${item.item_id} is at revision ${item.revision}, expected ${expectedRev}.`,
            },
          };
        }

        const prevJson = JSON.stringify(item.value);
        const nextJson = JSON.stringify(entry.value);
        const changed = prevJson !== nextJson;
        if (changed) {
          anyChanged = true;
          item.revision = Number(item.revision) + 1;
          item.value = cloneJson(entry.value);
          item.recorded_by = "usr-crc-01";
          item.recorded_at = entry.observed_at || now;

          const obsId =
            item.observation_id ||
            entry.observation_id ||
            makeUuidFromIndex("00006200", nextCounter());
          item.observation_id = obsId;
          const obsRef = `observation:${obsId}`;
          const existingObs = state.observations.get(obsRef);
          if (existingObs) {
            const lastRev =
              existingObs.revisions[existingObs.revisions.length - 1];
            if (lastRev && !lastRev.recorded_until) {
              lastRev.recorded_until = now;
            }
            existingObs.value = cloneJson(entry.value);
            existingObs.version = Number(existingObs.version) + 1;
            existingObs.revisions.push({
              number: item.revision,
              value: cloneJson(entry.value),
              reason_for_change: reason,
              recorded_by: "usr-crc-01",
              recorded_at: now,
              recorded_until: null,
            });
            existingObs.audit.push({
              sequence: existingObs.audit.length + 1,
              recorded_at: now,
              actor_id: "usr-crc-01",
              action: "observation.correct",
              subject: obsRef,
              version: existingObs.version,
              reason_for_change: reason,
            });
          } else {
            state.observations.set(obsRef, {
              ref: obsRef,
              enrollment_ref: `enrollment:${form.enrollment_id}`,
              concept: item.concept || item.item_id,
              value: cloneJson(entry.value),
              observed_at: now,
              version: 1,
              locks: [],
              revisions: [
                {
                  number: item.revision,
                  value: cloneJson(entry.value),
                  reason_for_change: reason,
                  recorded_by: "usr-crc-01",
                  recorded_at: now,
                  recorded_until: null,
                },
              ],
              assertions: [],
              audit: [
                {
                  sequence: 1,
                  recorded_at: now,
                  actor_id: "usr-crc-01",
                  action: "observation.record",
                  subject: obsRef,
                  version: 1,
                  reason_for_change: reason,
                },
              ],
            });
          }

          for (const q of state.queries.values()) {
            if (q.observation_id === obsId) {
              q.current_revision = item.revision;
              q.value_changed = item.revision > Number(q.revision);
            }
          }
        }

        savedValues.push({
          item_id: item.item_id,
          observation_id: item.observation_id,
          value: cloneJson(item.value),
          revision: item.revision,
          changed,
        });
      }

      const missingRequired = form.items
        .filter(
          (it) =>
            it.required &&
            (!it.value ||
              (it.value.type === "missing" && it.value.reason === "not_asked"))
        )
        .map((it) => it.item_id);
      form.missing_required = missingRequired;
      form.complete = missingRequired.length === 0;
      if (anyChanged) {
        form.version = Number(form.version) + 1;
        form.content_hash = computeContentHash(form.items);
        for (const sig of form.signatures) {
          sig.superseded = true;
        }
        ctx.recordActivity(
          "execution.form.saved",
          "collection",
          "form",
          `form:${form.form_id}`,
          "usr-crc-01"
        );
      }

      return {
        conflict: false,
        data: {
          form_id: form.form_id,
          version: form.version,
          values: savedValues,
          form: cloneJson(form),
        },
      };
    },

    signForm(formId, body, reason = "Investigator eCRF sign-off") {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      if (body?.content_hash && body.content_hash !== form.content_hash) {
        return {
          conflict: true,
          problem: {
            title: "Form content changed since read",
            status: 409,
            code: "K.Trust.TargetContentChanged",
            detail: "Reload the form and review updated values before signing.",
          },
        };
      }
      const idx = nextCounter();
      const now = new Date().toISOString();
      const signature = {
        attestation_id:
          body?.signature_id || makeUuidFromIndex("00007000", idx),
        meaning: body?.meaning || "approved",
        signed_by: "usr-pi-01",
        signed_at: now,
        reason,
        content_hash: form.content_hash,
        superseded: false,
        signed_values: form.items
          .filter((it) => it.value)
          .map((it) => ({
            item_id: it.item_id,
            observation_id: it.observation_id,
            revision: it.revision,
            value: cloneJson(it.value),
          })),
      };
      form.signatures.push(signature);
      ctx.recordActivity(
        "execution.form.signed",
        "trust",
        "form",
        `form:${form.form_id}`,
        "usr-pi-01"
      );
      return { conflict: false, data: cloneJson(signature) };
    },

    listFormQueries(formId, caller = null) {
      let results = [...state.queries.values()].filter(
        (q) => q.form_id === formId
      );
      if (caller) {
        results = results.filter(
          (q) => !q.site_code || isSitePermitted(caller, q.site_code)
        );
      }
      return cloneJson(results);
    },

    listEnrollmentQueries(enrollmentIdOrOptions, caller = null) {
      const opts =
        enrollmentIdOrOptions && typeof enrollmentIdOrOptions === "object"
          ? enrollmentIdOrOptions
          : { enrollmentId: enrollmentIdOrOptions };
      let results = [...state.queries.values()];
      if (caller) {
        results = results.filter(
          (q) => !q.site_code || isSitePermitted(caller, q.site_code)
        );
      }
      if (opts.enrollmentId) {
        const matches = results.filter(
          (q) => q.enrollment_id === opts.enrollmentId
        );
        if (matches.length > 0) {
          results = matches;
        }
      } else if (opts.studyId) {
        const matches = results.filter(
          (q) => !q.study_id || q.study_id === opts.studyId
        );
        results = matches;
      }
      if (opts.status) {
        const wanted = String(opts.status).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.status).toLowerCase() === wanted
        );
      }
      if (opts.owner) {
        const wanted = String(opts.owner).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.owner_role || "site").toLowerCase() === wanted
        );
      }
      if (opts.origin) {
        const wanted = String(opts.origin).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.origin || "manual").toLowerCase() === wanted
        );
      }
      if (opts.priority) {
        const wanted = String(opts.priority).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.priority || "normal").toLowerCase() === wanted
        );
      }
      if (
        opts.escalated !== undefined &&
        opts.escalated !== null &&
        opts.escalated !== ""
      ) {
        const wantedEsc =
          opts.escalated === true || String(opts.escalated) === "true";
        results = results.filter((q) => Boolean(q.escalated_at) === wantedEsc);
      }
      if (opts.assignee) {
        const wantedAssignee = String(opts.assignee).trim().toLowerCase();
        const normalizedWanted = wantedAssignee.startsWith("u:")
          ? wantedAssignee.slice(2)
          : wantedAssignee;
        results = results.filter((q) => {
          const current = String(q.assignee || "")
            .trim()
            .toLowerCase();
          if (!current) return false;
          const normalizedCurrent = current.startsWith("u:")
            ? current.slice(2)
            : current;
          return normalizedCurrent === normalizedWanted;
        });
      }
      if (opts.site) {
        const site = String(opts.site).trim().toLowerCase();
        results = results.filter(
          (q) =>
            String(q.site_code || "").toLowerCase() === site ||
            String(q.study_site_id || "").toLowerCase() === site
        );
      }
      if (opts.participant) {
        const participant = String(opts.participant).trim().toLowerCase();
        results = results.filter(
          (q) =>
            String(q.subject_code || "")
              .toLowerCase()
              .includes(participant) ||
            String(q.enrollment_id || "").toLowerCase() === participant
        );
      }
      if (
        opts.valueChanged !== undefined &&
        opts.valueChanged !== null &&
        opts.valueChanged !== ""
      ) {
        const wantedBool =
          opts.valueChanged === true || String(opts.valueChanged) === "true";
        results = results.filter(
          (q) => Boolean(q.value_changed) === wantedBool
        );
      }
      return cloneJson(results);
    },

    raiseQuery(formId, body, reasonHeader) {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      const item =
        (body?.observation_id &&
          form.items.find((it) => it.observation_id === body.observation_id)) ||
        (body?.item_id &&
          form.items.find((it) => it.item_id === body.item_id)) ||
        form.items[0];
      const idx = nextCounter();
      const now = new Date().toISOString();
      const dueAt = new Date(Date.now() + 14 * 86400 * 1000).toISOString();
      if (!body?.observation_id && body?.item_id && item && !item.value) {
        const unaskedObsId =
          item.observation_id || makeUuidFromIndex("00006300", idx);
        item.observation_id = unaskedObsId;
        item.revision = Math.max(1, Number(item.revision || 0));
        item.value = { type: "missing", reason: "not_asked" };
        item.recorded_by = "usr-cra-01";
        item.recorded_at = now;
      }
      const queryId = body?.query_id || makeUuidFromIndex("00003000", idx);
      const text = String(
        body?.text || "Verify recorded value against source."
      );
      const rev = Number(body?.revision ?? item?.revision ?? 1);
      const subjectMatch = resolveEnrollmentByIdOrCode(
        state.subjects,
        form.enrollment_id
      );

      const created = {
        query_id: queryId,
        form_id: form.form_id,
        enrollment_id: form.enrollment_id,
        item_id: body?.item_id || item?.item_id || "item-sysbp",
        observation_id:
          body?.observation_id || item?.observation_id || OBS_1_UUID,
        revision: rev,
        current_revision: item?.revision ?? rev,
        value_changed: false,
        status: "open",
        origin: "manual",
        owner_role: body?.owner_role || "site",
        assignee: body?.assignee || null,
        priority: body?.priority || "normal",
        opened_at: now,
        status_since: now,
        due_at: dueAt,
        escalated_at: null,
        last_answered_by: null,
        text,
        messages: [
          {
            kind: "raised",
            text,
            by: "usr-cra-01",
            at: now,
            reason_for_change: reasonHeader || text,
          },
        ],
        raised_by: "usr-cra-01",
        raised_at: now,
        version: 1,
        study_id: form.study_id || STUDY_UUID,
        study_site_id: form.study_site_id || SITE_101_UUID,
        site_code: subjectMatch?.site_id || "SITE-101",
        subject_code: subjectMatch?.subject_id || "SUBJ-1001",
        collection_spec_id: form.collection_spec_id || "form-vital-signs",
        planned_activity_id: form.planned_activity_id || "visit-baseline",
      };
      state.queries.set(queryId, created);
      ctx.recordActivity(
        "execution.query.raised",
        "execution",
        "query",
        `query:${queryId}`,
        "usr-cra-01"
      );
      return cloneJson(created);
    },

    transitionQuery(formId, queryId, action, body, reason) {
      const existing = state.queries.get(queryId);
      if (!existing) {
        return {
          notFound: true,
          problem: {
            title: "Query not found",
            status: 404,
            detail: `Query ${queryId} does not exist on form ${formId}.`,
          },
        };
      }
      if (action === "reassign") {
        if (existing.status !== "open" && existing.status !== "answered") {
          return {
            invalid: true,
            problem: {
              title: "Invalid query transition",
              status: 409,
              code: "M.Execution.Query.InvalidTransition",
              detail: "Only open or answered queries may be reassigned.",
            },
          };
        }
        const now = new Date().toISOString();
        const nextAssignee =
          typeof body?.assignee === "string" && body.assignee.trim()
            ? body.assignee.trim()
            : null;
        const nextRole =
          typeof body?.owner_role === "string" && body.owner_role.trim()
            ? body.owner_role.trim()
            : existing.owner_role || "site";
        existing.assignee = nextAssignee;
        existing.owner_role = nextRole;
        existing.version = Number(existing.version) + 1;
        const summary = nextAssignee
          ? `Assigned to ${nextAssignee} (${nextRole}).`
          : `Unassigned (${nextRole}).`;
        const noteText =
          typeof body?.text === "string" && body.text.trim()
            ? `${summary} ${body.text.trim()}`
            : summary;
        existing.messages.push({
          kind: "reassigned",
          text: noteText,
          by: "usr-dm-01",
          at: now,
          reason_for_change: reason || noteText,
        });
        ctx.recordActivity(
          "execution.query.reassigned",
          "execution",
          "query",
          `query:${queryId}`,
          "usr-dm-01"
        );
        return { data: cloneJson(existing) };
      }

      const statusMap = {
        answer: "answered",
        reopen: "open",
        close: "closed",
        cancel: "cancelled",
      };
      const kindMap = {
        answer: "answered",
        reopen: "reopened",
        close: "closed",
        cancel: "cancelled",
      };
      const nextStatus = statusMap[action];
      if (!nextStatus) {
        return {
          invalid: true,
          problem: {
            title: "Invalid query transition",
            status: 409,
            code: "M.Execution.Query.InvalidTransition",
            detail: `Unsupported transition ${action}.`,
          },
        };
      }
      const now = new Date().toISOString();
      if (
        action === "close" &&
        existing.status === "open" &&
        (existing.origin || "manual") !== "system"
      ) {
        return {
          invalid: true,
          problem: {
            title: "Invalid query transition",
            status: 409,
            code: "M.Execution.Query.InvalidTransition",
            detail: "Manual queries must be answered before closure.",
          },
        };
      }
      const actor = action === "answer" ? "usr-crc-01" : "usr-dm-01";
      const messageText = String(
        body?.text || reason || `Query ${kindMap[action]}.`
      );
      existing.status = nextStatus;
      existing.status_since = now;
      if (action === "answer") {
        const prevRole = existing.owner_role || "site";
        existing.owner_role = "monitor";
        existing.assignee =
          typeof body?.assignee === "string" && body.assignee.trim()
            ? body.assignee.trim()
            : prevRole !== "monitor"
              ? null
              : existing.assignee;
        existing.due_at = null;
        existing.escalated_at = null;
        existing.last_answered_by = actor;
      } else if (action === "reopen") {
        const prevRole = existing.owner_role || "monitor";
        existing.owner_role = "site";
        existing.assignee =
          typeof body?.assignee === "string" && body.assignee.trim()
            ? body.assignee.trim()
            : prevRole !== "site"
              ? null
              : existing.assignee;
        existing.due_at = new Date(
          Date.now() + 14 * 86400 * 1000
        ).toISOString();
        existing.escalated_at = null;
      } else {
        existing.due_at = null;
        existing.escalated_at = null;
      }
      existing.version = Number(existing.version) + 1;
      existing.messages.push({
        kind: kindMap[action],
        text: messageText,
        by: actor,
        at: now,
        reason_for_change: reason || messageText,
      });
      ctx.recordActivity(
        `execution.query.${kindMap[action]}`,
        "execution",
        "query",
        `query:${queryId}`,
        action === "answer" ? "usr-crc-01" : "usr-dm-01"
      );
      return { data: cloneJson(existing) };
    },

    listQueryDigests({ studyId, site, recipientRole } = {}, caller = null) {
      if (state.queryDigests.length === 0) {
        methods.escalateStudyQueries(
          studyId || STUDY_UUID,
          "Initial SLA digest"
        );
      }
      let results = [...state.queryDigests];
      if (caller) {
        results = results.filter((d) =>
          d.study_site_id || d.site_code
            ? isSitePermitted(caller, d.site_code)
            : hasStudyWideScope(caller)
        );
      }
      if (studyId) {
        results = results.filter((d) => !d.study_id || d.study_id === studyId);
      }
      if (site) {
        const wanted = String(site).trim().toLowerCase();
        results = results.filter(
          (d) =>
            String(d.site_code || "").toLowerCase() === wanted ||
            String(d.study_site_id || "").toLowerCase() === wanted
        );
      }
      if (recipientRole) {
        const wantedRole = String(recipientRole).trim().toLowerCase();
        results = results.filter(
          (d) => String(d.recipient_role || "").toLowerCase() === wantedRole
        );
      }
      return cloneJson(results);
    },

    escalateStudyQueries(studyId, reason = "14-day SLA check", caller = null) {
      const now = new Date();
      const nowIso = now.toISOString();
      const digestDate = nowIso.slice(0, 10);
      let escalatedQueries = 0;
      const overdue = [];
      for (const q of state.queries.values()) {
        if (studyId && q.study_id && q.study_id !== studyId) continue;
        if (q.status !== "open") continue;
        const dueMs = q.due_at ? Date.parse(q.due_at) : NaN;
        if (!Number.isNaN(dueMs) && dueMs <= now.getTime()) {
          if (!q.escalated_at) {
            q.escalated_at = nowIso;
            q.priority = "high";
            q.version = Number(q.version) + 1;
            q.messages.push({
              kind: "escalated",
              text: "Query exceeded 14-day response SLA and was escalated to high priority.",
              by: "svc:execution-query-sla",
              at: nowIso,
              reason_for_change: reason,
            });
            escalatedQueries++;
          }
        }
        if (q.escalated_at) {
          const openedMs = q.opened_at
            ? Date.parse(q.opened_at)
            : now.getTime();
          const ageDays = Math.max(
            14,
            Math.floor((now.getTime() - openedMs) / 86400000)
          );
          overdue.push({
            query_id: q.query_id,
            form_id: q.form_id,
            enrollment_id: q.enrollment_id,
            study_site_id: q.study_site_id || SITE_101_UUID,
            site_code: q.site_code || "SITE-101",
            subject_code: q.subject_code || "SUBJ-1001",
            collection_spec_id: q.collection_spec_id || "form-vital-signs",
            item_id: q.item_id,
            owner_role: q.owner_role || "site",
            priority: q.priority || "high",
            opened_at: q.opened_at || nowIso,
            due_at: q.due_at || nowIso,
            age_days: ageDays,
          });
        }
      }
      const targetStudyId = studyId || STUDY_UUID;
      const bySite = new Map();
      for (const entry of overdue) {
        const key = entry.site_code || "SITE-101";
        if (!bySite.has(key)) bySite.set(key, []);
        bySite.get(key).push(entry);
      }
      const digests = [
        {
          digest_id: "00000000-0000-0000-0000-000000009501", // deid-ignore
          study_id: targetStudyId,
          study_site_id: null,
          scope_key: "study",
          recipient_role: "study_lead",
          digest_date: digestDate,
          generated_at: nowIso,
          study_code: "CAD-ONC-301",
          site_code: null,
          escalated_count: overdue.length,
          entries: overdue,
        },
      ];
      let idx = 2;
      for (const [siteCode, entries] of bySite.entries()) {
        digests.push({
          digest_id: `00000000-0000-0000-0000-00000000950${idx++}`, // deid-ignore
          study_id: targetStudyId,
          study_site_id: entries[0]?.study_site_id || SITE_101_UUID,
          scope_key: `site:${entries[0]?.study_site_id || SITE_101_UUID}`,
          recipient_role: "site_investigator",
          digest_date: digestDate,
          generated_at: nowIso,
          study_code: "CAD-ONC-301",
          site_code: siteCode,
          escalated_count: entries.length,
          entries,
        });
      }
      state.queryDigests = digests;
      const visibleDigests = caller
        ? digests.filter((d) =>
            d.study_site_id || d.site_code
              ? isSitePermitted(caller, d.site_code)
              : hasStudyWideScope(caller)
          )
        : digests;
      return cloneJson({
        escalated_queries: escalatedQueries,
        digests: visibleDigests,
      });
    },
  };

  return { seed, methods };
}
