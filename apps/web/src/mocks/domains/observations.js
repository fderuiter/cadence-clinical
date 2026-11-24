import { DEMO_PARTICIPANTS } from "demo-fixtures";
import {
  ENROLLMENT_1001_UUID,
  OBS_1_UUID,
  OBS_2_UUID,
  OBS_3_UUID,
  OBS_4_UUID,
  cloneJson,
  computeContentHash,
  makeUuidFromIndex,
} from "./shared.js";

function createInitialObservations() {
  const obs1Ref = `observation:${OBS_1_UUID}`;
  const obs2Ref = `observation:${OBS_2_UUID}`;
  const obs3Ref = `observation:${OBS_3_UUID}`;
  const canonicalEnrollmentUuid =
    DEMO_PARTICIPANTS[0]?.enrollmentUuid || ENROLLMENT_1001_UUID;
  const enrollmentRef = `enrollment:${canonicalEnrollmentUuid}`;

  const makeObs = (ref, concept, value, recordedAt) => ({
    ref,
    enrollment_ref: enrollmentRef,
    concept,
    value,
    observed_at: recordedAt,
    version: 1,
    locks: [],
    revisions: [
      {
        number: 1,
        value: cloneJson(value),
        reason_for_change: "Initial baseline assessment entry",
        recorded_by: "usr-crc-01",
        recorded_at: recordedAt,
        recorded_until: null,
      },
    ],
    assertions: [],
    audit: [
      {
        sequence: 1,
        recorded_at: recordedAt,
        actor_id: "usr-crc-01",
        action: "observation.record",
        subject: ref,
        version: 1,
        reason_for_change: "Initial baseline assessment entry",
      },
    ],
  });

  return new Map([
    [
      obs1Ref,
      makeObs(
        obs1Ref,
        "SYSBP",
        { type: "quantity", value: "128", unit: "mm[Hg]", comparator: null },
        "2026-08-01T09:15:00Z"
      ),
    ],
    [
      obs2Ref,
      makeObs(
        obs2Ref,
        "DIABP",
        { type: "quantity", value: "82", unit: "mm[Hg]", comparator: null },
        "2026-08-01T09:16:00Z"
      ),
    ],
    [
      obs3Ref,
      makeObs(
        obs3Ref,
        "PULSE",
        { type: "quantity", value: "72", unit: "/min", comparator: null },
        "2026-08-01T09:17:00Z"
      ),
    ],
  ]);
}

export function createObservationsDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.observations = createInitialObservations();

  function seed() {
    state.observations = createInitialObservations();
    state.observations.set(`observation:${OBS_4_UUID}`, {
      ref: `observation:${OBS_4_UUID}`,
      enrollment_ref: `enrollment:${ENROLLMENT_1001_UUID}`,
      concept: "VSCOMM",
      value: { type: "text", value: "Seated rest 5 minutes prior to cuff." },
      observed_at: "2026-08-01T09:18:00Z",
      version: 1,
      locks: [],
      revisions: [
        {
          number: 1,
          value: {
            type: "text",
            value: "Seated rest 5 minutes prior to cuff.",
          },
          reason_for_change: "Initial baseline vital signs note",
          recorded_by: "usr-crc-01",
          recorded_at: "2026-08-01T09:18:00Z",
          recorded_until: null,
        },
      ],
      assertions: [],
      audit: [
        {
          sequence: 1,
          recorded_at: "2026-08-01T09:18:00Z",
          actor_id: "usr-crc-01",
          action: "observation.record",
          subject: `observation:${OBS_4_UUID}`,
          version: 1,
          reason_for_change: "Initial baseline vital signs note",
        },
      ],
    });
  }

  function getObservation(observationRef, scopeHint = {}) {
    const normalized = String(observationRef).startsWith("observation:")
      ? String(observationRef)
      : `observation:${observationRef}`;
    const canonicalEnrollmentUuid =
      DEMO_PARTICIPANTS[0]?.enrollmentUuid || ENROLLMENT_1001_UUID;
    let obs = state.observations.get(normalized);
    if (!obs) {
      obs = {
        ref: normalized,
        enrollment_ref: `enrollment:${canonicalEnrollmentUuid}`,
        concept: "SYSBP",
        value: {
          type: "quantity",
          value: "124",
          unit: "mm[Hg]",
          comparator: null,
        },
        observed_at: "2026-08-01T09:15:00Z",
        version: 1,
        locks: [],
        revisions: [
          {
            number: 1,
            value: {
              type: "quantity",
              value: "124",
              unit: "mm[Hg]",
              comparator: null,
            },
            reason_for_change: "Initial observation capture",
            recorded_by: "usr-crc-01",
            recorded_at: "2026-08-01T09:15:00Z",
            recorded_until: null,
          },
        ],
        assertions: [],
        audit: [
          {
            sequence: 1,
            recorded_at: "2026-08-01T09:15:00Z",
            actor_id: "usr-crc-01",
            action: "observation.record",
            subject: normalized,
            version: 1,
            reason_for_change: "Initial observation capture",
          },
        ],
      };
      state.observations.set(normalized, obs);
    }
    const result = cloneJson(obs);
    const hintStudy = String(scopeHint?.studyCode || "").trim();
    const hintSite = String(scopeHint?.siteCode || "").trim();
    const hintSubject = String(scopeHint?.subjectCode || "").trim();
    if (hintStudy || hintSite || hintSubject) {
      const matchedSubject = state.subjects.find((subj) => {
        if (
          hintStudy &&
          String(subj.study_id || "").toUpperCase() !== hintStudy.toUpperCase()
        ) {
          return false;
        }
        if (
          hintSite &&
          String(subj.site_id || "").toUpperCase() !== hintSite.toUpperCase()
        ) {
          return false;
        }
        if (hintSubject && subj.subject_id !== hintSubject) {
          return false;
        }
        return true;
      });
      if (matchedSubject) {
        result.enrollment_ref = `enrollment:${matchedSubject.id}`;
      }
    }
    return result;
  }

  const methods = {
    getObservation,

    correctObservation(observationRef, body, reasonForChange) {
      const normalized = String(observationRef).startsWith("observation:")
        ? String(observationRef)
        : `observation:${observationRef}`;
      const existing = state.observations.get(normalized);
      if (!existing) {
        getObservation(normalized);
      }
      const target = state.observations.get(normalized);
      const expectedVersion = Number(body?.expected_version ?? target.version);
      if (expectedVersion !== Number(target.version)) {
        return {
          conflict: true,
          problem: {
            title: "Observation version conflict",
            status: 409,
            code: "K.Collection.RevisionStale",
            detail: `Expected version ${expectedVersion} but observation is at version ${target.version}.`,
          },
        };
      }

      const now = new Date().toISOString();
      const nextRevision = target.revisions.length + 1;
      const nextVersion = Number(target.version) + 1;
      const prevRev = target.revisions[target.revisions.length - 1];
      if (prevRev && !prevRev.recorded_until) {
        prevRev.recorded_until = now;
      }

      const nextValue = cloneJson(body.value);
      target.value = nextValue;
      target.version = nextVersion;
      target.revisions.push({
        number: nextRevision,
        value: cloneJson(nextValue),
        reason_for_change: reasonForChange || "Clinical observation correction",
        recorded_by: "usr-crc-01",
        recorded_at: now,
        recorded_until: null,
      });
      target.audit.push({
        sequence: target.audit.length + 1,
        recorded_at: now,
        actor_id: "usr-crc-01",
        action: "observation.correct",
        subject: normalized,
        version: nextVersion,
        reason_for_change: reasonForChange || "Clinical observation correction",
      });

      const rawObsUuid = normalized.replace(/^observation:/, "");
      for (const form of state.forms.values()) {
        let touched = false;
        for (const item of form.items) {
          if (item.observation_id === rawObsUuid) {
            item.value = cloneJson(nextValue);
            item.revision = nextRevision;
            item.recorded_at = now;
            item.recorded_by = "usr-crc-01";
            touched = true;
          }
        }
        if (touched) {
          form.version = Number(form.version) + 1;
          form.content_hash = computeContentHash(form.items);
          for (const sig of form.signatures) {
            sig.superseded = true;
          }
        }
      }

      for (const query of state.queries.values()) {
        if (query.observation_id === rawObsUuid) {
          query.current_revision = nextRevision;
          query.value_changed = nextRevision > Number(query.revision);
        }
      }

      ctx.recordActivity(
        "observation.corrected",
        "collection",
        "observation",
        normalized,
        "usr-crc-01"
      );

      return {
        conflict: false,
        data: {
          ref: normalized,
          enrollment_ref: body?.enrollment_ref || target.enrollment_ref,
          value: cloneJson(nextValue),
          version: nextVersion,
          revision: nextRevision,
        },
      };
    },

    verifyObservation(
      observationRef,
      body,
      reason = "Source data verified in demo session"
    ) {
      const rawObsRef = String(observationRef).split("@")[0];
      const normalized = rawObsRef.startsWith("observation:")
        ? rawObsRef
        : `observation:${rawObsRef}`;
      if (!state.observations.has(normalized)) {
        getObservation(normalized);
      }
      const target = state.observations.get(normalized);
      const currentRev =
        target.revisions[target.revisions.length - 1]?.number ?? 1;
      const requestedRev = Number(body?.revision ?? currentRev);
      if (requestedRev !== Number(currentRev)) {
        return {
          conflict: true,
          problem: {
            title: "Stale observation revision",
            status: 409,
            code: "K.Assertion.RevisionStale",
            detail: `Observation is at revision ${currentRev}, requested ${requestedRev}.`,
          },
        };
      }
      const idx = nextCounter();
      const assertionUuid =
        body?.assertion_id || makeUuidFromIndex("00008000", idx);
      const assertionRef = `assertion:${assertionUuid}`;
      const now = new Date().toISOString();
      const assertionEntry = {
        assertion_id: assertionUuid,
        kind: "Verified",
        target: normalized,
        revision: requestedRev,
        text: null,
        asserted_by: "usr-cra-01",
        asserted_at: now,
        reason,
        retracted_at: null,
      };
      target.assertions.push({
        ref: assertionRef,
        kind: "Verified",
        target: normalized,
        revision: requestedRev,
        retracts: null,
        asserted_at: now,
        asserted_by: "usr-cra-01",
        retracted_at: null,
      });
      const rawObsUuid = normalized.replace(/^observation:/, "");
      for (const form of state.forms.values()) {
        if (form.items.some((it) => it.observation_id === rawObsUuid)) {
          form.assertions.push(cloneJson(assertionEntry));
        }
      }
      ctx.recordActivity(
        "observation.verified",
        "quality",
        "assertion",
        assertionRef,
        "usr-cra-01"
      );
      return {
        conflict: false,
        data: {
          ref: assertionRef,
          kind: "Verified",
          target: normalized,
          revision: requestedRev,
          retracts: null,
          asserted_at: now,
          asserted_by: "usr-cra-01",
        },
      };
    },

    retractAssertion(
      assertionRef,
      body,
      reason = "Verification withdrawn in demo session"
    ) {
      const normalized = String(assertionRef).startsWith("assertion:")
        ? String(assertionRef)
        : `assertion:${assertionRef}`;
      const rawUuid = normalized.replace(/^assertion:/, "");
      const now = new Date().toISOString();
      let targetObsRef = `observation:${OBS_1_UUID}`;

      for (const obs of state.observations.values()) {
        for (const a of obs.assertions) {
          if (a.ref === normalized || a.assertion_id === rawUuid) {
            if (a.retracted_at) {
              return {
                conflict: true,
                problem: {
                  title: "Assertion already retracted",
                  status: 409,
                  code: "K.Assertion.AlreadyRetracted",
                  detail: `Assertion ${normalized} was already retracted.`,
                },
              };
            }
            a.retracted_at = now;
            targetObsRef = a.target || obs.ref;
          }
        }
      }

      for (const form of state.forms.values()) {
        for (const a of form.assertions) {
          if (a.assertion_id === rawUuid) {
            a.retracted_at = now;
            targetObsRef = a.target || targetObsRef;
          }
        }
      }

      const idx = nextCounter();
      const retractionId =
        body?.assertion_id || makeUuidFromIndex("00008100", idx);
      const retractionRef = `assertion:${retractionId}`;
      ctx.recordActivity(
        "assertion.retracted",
        "quality",
        "assertion",
        retractionRef,
        "usr-cra-01",
        reason
      );
      return {
        conflict: false,
        data: {
          ref: retractionRef,
          kind: "Verified",
          target: targetObsRef,
          revision: null,
          retracts: normalized,
          asserted_at: now,
          asserted_by: "usr-cra-01",
        },
      };
    },
  };

  return { seed, methods };
}
