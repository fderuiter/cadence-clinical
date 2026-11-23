<template>
  <section
    id="record-forms"
    class="forms-panel"
    aria-labelledby="record-forms-title"
    :aria-busy="loading"
  >
    <div class="forms-head">
      <h3 id="record-forms-title">{{ matrixActive ? "Casebook" : "Forms" }}</h3>
      <div class="forms-head-actions">
        <router-link
          v-if="enrollmentId && newFormLocation"
          id="record-open-form"
          class="btn btn-secondary forms-open-btn"
          :to="newFormLocation"
        >
          Open new form
        </router-link>
        <router-link
          v-if="enrollmentId && handoffLocation"
          id="record-handoff-standalone"
          data-testid="record-ecrf-handoff"
          class="btn btn-secondary forms-open-btn"
          :to="handoffLocation"
        >
          Open standalone form view
        </router-link>
      </div>
    </div>

    <p v-if="loading || enrollmentLoading" class="status-line" role="status">
      Loading {{ subjectCode }}'s forms…
    </p>
    <p
      v-else-if="!enrollmentId"
      id="record-forms-no-enrollment"
      class="status-line"
    >
      There is no enrollment for {{ subjectCode }} visible to your account, so
      forms can't be listed or opened here.
    </p>
    <div
      v-else-if="loadError"
      id="record-forms-error"
      class="status-line"
      role="alert"
    >
      <p class="status-line">
        {{ subjectCode }}'s forms couldn't be loaded.
        <button
          id="btn-retry-forms"
          type="button"
          class="btn btn-secondary"
          @click="loadFormsAndSchedule"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="loadError" />
    </div>

    <template v-else-if="matrixActive && activeSchedule">
      <div
        id="casebook-summary"
        class="casebook-summary"
        data-testid="casebook-summary"
      >
        <span class="casebook-protocol-title">
          {{ activeSchedule.title || activeSchedule.versionRef }}
        </span>
        <div class="casebook-rollups">
          <span class="cb-badge cb-badge--complete">
            {{ scheduledCompleteCells }}/{{ totalScheduledCells }} scheduled
            complete
          </span>
          <span
            v-if="incompleteFormsCount > 0"
            class="cb-badge cb-badge--incomplete"
          >
            {{ incompleteFormsCount }} incomplete
          </span>
          <span
            v-if="totalOpenQueries > 0"
            class="cb-badge cb-badge--query-open"
          >
            {{ totalOpenQueries }} open
            {{ totalOpenQueries === 1 ? "query" : "queries" }}
          </span>
          <span
            v-if="verifiedFormsCount > 0"
            class="cb-badge cb-badge--verified"
          >
            {{ verifiedFormsCount }} SDV verified
          </span>
          <span v-if="signedFormsCount > 0" class="cb-badge cb-badge--signed">
            {{ signedFormsCount }} signed
          </span>
          <span v-if="lockedFormsCount > 0" class="cb-badge cb-badge--locked">
            {{ lockedFormsCount }} locked/frozen
          </span>
        </div>
      </div>

      <div
        v-if="
          activeSchedule.columns.length > 0 &&
          activeSchedule.scheduledRows.length > 0
        "
        class="casebook-matrix-scroll"
        role="region"
        :aria-label="`${subjectCode} scheduled casebook matrix`"
        tabindex="0"
      >
        <table
          id="casebook-matrix"
          class="casebook-matrix"
          data-testid="casebook-matrix"
        >
          <caption class="sr-only">
            Scheduled visits and case report forms for
            {{
              subjectCode
            }}
          </caption>
          <thead>
            <tr v-if="activeSchedule.epochs.length > 0">
              <th scope="col" rowspan="2" class="matrix-corner">Form</th>
              <th
                v-for="epoch in activeSchedule.epochs"
                :key="epoch.epochId"
                :colspan="epoch.columnCount"
                scope="colgroup"
                class="matrix-epoch-header"
                :data-epoch="epoch.epochId"
              >
                {{ epoch.name }}
              </th>
            </tr>
            <tr>
              <th
                v-if="activeSchedule.epochs.length === 0"
                scope="col"
                class="matrix-corner"
              >
                Form
              </th>
              <th
                v-for="col in activeSchedule.columns"
                :key="col.activityId"
                scope="col"
                class="matrix-visit-header"
                :data-visit="col.activityId"
              >
                <span class="visit-name">{{ col.name }}</span>
                <span v-if="col.studyDay !== null" class="visit-day">
                  Day {{ col.studyDay
                  }}<template v-if="col.windowBefore || col.windowAfter">
                    (-{{ col.windowBefore }}/+{{ col.windowAfter }}d)</template
                  >
                </span>
              </th>
            </tr>
          </thead>
          <tbody data-testid="casebook-scheduled-rows">
            <tr
              v-for="row in activeSchedule.scheduledRows"
              :key="row.collectionSpecId"
              :data-spec="row.collectionSpecId"
            >
              <th scope="row" class="matrix-row-header">
                <span class="row-spec-title">{{ row.name }}</span>
                <span class="row-spec-code">
                  {{ row.collectionSpecId
                  }}<template v-if="row.itemCount > 0">
                    · {{ row.itemCount }}
                    {{ row.itemCount === 1 ? "item" : "items" }}</template
                  >
                </span>
              </th>
              <td
                v-for="(col, colIdx) in activeSchedule.columns"
                :key="col.activityId"
                :data-cell="`${row.collectionSpecId}:${col.activityId}`"
                :class="cellClasses(row, colIdx, col.activityId)"
              >
                <template
                  v-if="
                    !isCellScheduled(row, colIdx) &&
                    cellForms(row.collectionSpecId, col.activityId).length === 0
                  "
                >
                  <span class="matrix-na" aria-label="Not scheduled">—</span>
                </template>

                <div
                  v-else-if="
                    cellForms(row.collectionSpecId, col.activityId).length === 0
                  "
                  class="matrix-cell-empty"
                >
                  <span
                    class="cb-badge cb-badge--not-started"
                    data-testid="badge-not-started"
                  >
                    Not started
                  </span>
                  <router-link
                    v-if="cellNewLocation(row.collectionSpecId, col.activityId)"
                    :id="`matrix-open-${row.collectionSpecId}-${col.activityId}`"
                    :data-testid="`matrix-open-${row.collectionSpecId}-${col.activityId}`"
                    class="matrix-open-link"
                    :to="cellNewLocation(row.collectionSpecId, col.activityId)!"
                    :aria-label="`Open form: ${row.name} for ${col.name}`"
                  >
                    Open form
                  </router-link>
                </div>

                <div v-else class="matrix-instances">
                  <div
                    v-for="(inst, idx) in cellForms(
                      row.collectionSpecId,
                      col.activityId
                    )"
                    :id="`record-form-${inst.form_id}`"
                    :key="inst.form_id"
                    class="matrix-instance"
                    :data-form-id="inst.form_id"
                  >
                    <div class="matrix-instance-top">
                      <router-link
                        :id="`record-form-link-${inst.form_id}`"
                        class="forms-link matrix-instance-link"
                        :to="formLocation(inst.form_id) ?? '#'"
                        :aria-label="
                          instanceAriaLabel(
                            inst,
                            row.name,
                            col.name,
                            idx,
                            cellForms(row.collectionSpecId, col.activityId)
                              .length
                          )
                        "
                      >
                        <template
                          v-if="
                            cellForms(row.collectionSpecId, col.activityId)
                              .length > 1
                          "
                        >
                          #{{ idx + 1 }} · v{{ inst.version }}
                        </template>
                        <template v-else>
                          {{ row.name }}
                        </template>
                      </router-link>
                    </div>
                    <div class="cb-badges">
                      <span
                        class="cb-badge"
                        :class="
                          inst.complete
                            ? 'cb-badge--complete'
                            : 'cb-badge--incomplete'
                        "
                        :data-complete="inst.complete ? 'true' : 'false'"
                      >
                        {{ completenessLabel(inst) }}
                      </span>
                      <span
                        v-for="badge in lifecycleBadges(inst)"
                        :key="badge.key"
                        class="cb-badge"
                        :class="`cb-badge--${badge.tone}`"
                        :data-testid="badge.testId"
                      >
                        {{ badge.label }}
                      </span>
                    </div>
                  </div>
                  <router-link
                    v-if="
                      cellNewLocation(row.collectionSpecId, col.activityId) &&
                      canAddRepeat(
                        cellForms(row.collectionSpecId, col.activityId)
                      )
                    "
                    :id="`matrix-repeat-${row.collectionSpecId}-${col.activityId}`"
                    :data-testid="`matrix-repeat-${row.collectionSpecId}-${col.activityId}`"
                    class="matrix-repeat-link"
                    :to="cellNewLocation(row.collectionSpecId, col.activityId)!"
                    :aria-label="`+ Add repeat for ${row.name} at ${col.name}`"
                  >
                    + Add repeat
                  </router-link>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <section
        v-if="
          activeSchedule.unscheduledRows.length > 0 ||
          unscheduledForms.length > 0
        "
        id="casebook-unscheduled"
        class="casebook-unscheduled"
        data-testid="casebook-unscheduled"
        aria-labelledby="casebook-unscheduled-title"
      >
        <div class="unscheduled-head">
          <h4 id="casebook-unscheduled-title">Unscheduled &amp; log forms</h4>
          <div
            v-if="activeSchedule.unscheduledRows.length > 0"
            class="unscheduled-triggers"
          >
            <router-link
              v-for="spec in activeSchedule.unscheduledRows"
              :key="spec.collectionSpecId"
              :id="`unscheduled-open-${spec.collectionSpecId}`"
              :data-testid="`unscheduled-open-${spec.collectionSpecId}`"
              class="btn btn-secondary forms-open-btn"
              :to="cellNewLocation(spec.collectionSpecId, null)!"
            >
              + {{ spec.name }}
            </router-link>
          </div>
        </div>

        <p v-if="unscheduledForms.length === 0" class="status-line">
          No unscheduled forms opened for {{ subjectCode }} yet.
        </p>
        <ul
          v-else
          class="forms-list"
          data-testid="casebook-unscheduled-instances"
        >
          <li
            v-for="entry in numberedUnscheduledForms"
            :id="`record-form-${entry.summary.form_id}`"
            :key="entry.summary.form_id"
            class="forms-item"
          >
            <div class="forms-item-main">
              <router-link
                :id="`record-form-link-${entry.summary.form_id}`"
                class="forms-link"
                :to="formLocation(entry.summary.form_id) ?? '#'"
              >
                {{
                  entry.summary.collection_title ||
                  entry.summary.collection_spec_id
                }}<template v-if="entry.totalForSpec > 1">
                  #{{ entry.ordinal }}</template
                >
                · Unscheduled
              </router-link>
              <span
                class="cb-badge"
                :class="
                  entry.summary.complete
                    ? 'cb-badge--complete'
                    : 'cb-badge--incomplete'
                "
                :data-complete="entry.summary.complete ? 'true' : 'false'"
              >
                {{ completenessLabel(entry.summary) }}
              </span>
              <span
                v-for="badge in lifecycleBadges(entry.summary)"
                :key="badge.key"
                class="cb-badge"
                :class="`cb-badge--${badge.tone}`"
                :data-testid="badge.testId"
              >
                {{ badge.label }}
              </span>
            </div>
            <span class="forms-meta">
              Version {{ entry.summary.version }} · Collected
              {{ entry.summary.collected_at }}
            </span>
          </li>
        </ul>
      </section>

      <section
        v-if="unmappedScheduledForms.length > 0"
        id="casebook-other-forms"
        class="casebook-unscheduled"
        aria-labelledby="casebook-other-forms-title"
      >
        <h4 id="casebook-other-forms-title">Other opened forms</h4>
        <ul class="forms-list">
          <li
            v-for="summary in unmappedScheduledForms"
            :id="`record-form-${summary.form_id}`"
            :key="summary.form_id"
            class="forms-item"
          >
            <div class="forms-item-main">
              <router-link
                :id="`record-form-link-${summary.form_id}`"
                class="forms-link"
                :to="formLocation(summary.form_id) ?? '#'"
              >
                {{ summary.collection_title || summary.collection_spec_id
                }}<template v-if="summary.planned_activity_id">
                  · {{ summary.planned_activity_id }}</template
                >
              </router-link>
              <span
                class="cb-badge"
                :class="
                  summary.complete
                    ? 'cb-badge--complete'
                    : 'cb-badge--incomplete'
                "
                :data-complete="summary.complete ? 'true' : 'false'"
              >
                {{ completenessLabel(summary) }}
              </span>
              <span
                v-for="badge in lifecycleBadges(summary)"
                :key="badge.key"
                class="cb-badge"
                :class="`cb-badge--${badge.tone}`"
                :data-testid="badge.testId"
              >
                {{ badge.label }}
              </span>
            </div>
            <span class="forms-meta">
              Version {{ summary.version }} · Collected
              {{ summary.collected_at }}
            </span>
          </li>
        </ul>
      </section>
    </template>

    <p
      v-else-if="forms.length === 0"
      id="record-forms-empty"
      class="status-line"
    >
      No forms opened for {{ subjectCode }} yet.
    </p>
    <ul
      v-else
      id="record-forms-list"
      class="forms-list"
      data-testid="participant-forms-list"
    >
      <li
        v-for="summary in forms"
        :id="`record-form-${summary.form_id}`"
        :key="summary.form_id"
        class="forms-item"
      >
        <div class="forms-item-main">
          <component
            :is="routerLinkComponent"
            v-if="routerLinkComponent"
            :id="`record-form-link-${summary.form_id}`"
            class="forms-link"
            :to="formLocation(summary.form_id)!"
          >
            {{ summary.collection_title || summary.collection_spec_id
            }}<template v-if="summary.planned_activity_id">
              · {{ summary.planned_activity_id }}</template
            >
          </component>
          <a
            v-else
            :id="`record-form-link-${summary.form_id}`"
            class="forms-link"
            :href="fallbackHref(formLocation(summary.form_id))"
          >
            {{ summary.collection_title || summary.collection_spec_id
            }}<template v-if="summary.planned_activity_id">
              · {{ summary.planned_activity_id }}</template
            >
          </a>
          <span
            class="forms-status"
            :data-complete="summary.complete ? 'true' : 'false'"
          >
            {{ completenessLabel(summary) }}
          </span>
          <span
            v-for="badge in lifecycleBadges(summary)"
            :key="badge.key"
            class="cb-badge"
            :class="`cb-badge--${badge.tone}`"
            :data-testid="badge.testId"
          >
            {{ badge.label }}
          </span>
        </div>
        <span class="forms-meta">
          Version {{ summary.version }} · Collected {{ summary.collected_at }}
        </span>
      </li>
    </ul>
  </section>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  ecrfFormsApi,
  toFormSummaryCount,
  type EcrfFormSummary,
} from "../../api/ecrfFormsApi";
import {
  type StudyScheduleOptions,
  type StudyScheduleRow,
} from "../../canvas/kernelStudy";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import SupportDetails from "./SupportDetails.vue";

interface LifecycleBadge {
  key: string;
  label: string;
  tone:
    | "query-open"
    | "query-answered"
    | "verified"
    | "partial-sdv"
    | "unverified"
    | "locked"
    | "frozen"
    | "signed"
    | "sig-superseded";
  testId: string;
}

/**
 * Renders a participant's casebook inside Clinical Canvas: when the study has a governing Schedule of Activities,
 * displays an interactive epoch/visit/form matrix with live completeness, query, source verification, lock/freeze,
 * signature, repeating-instance, and unscheduled-form status; otherwise lists opened form instances directly.
 */
const props = defineProps<{
  studyCode?: string;
  subjectCode: string;
  enrollmentId: string | null;
  enrollmentLoading?: boolean;
  schedule?: StudyScheduleOptions | null;
  formLocation: (
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw | null;
  newFormLocation?: RouteLocationRaw | null;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  handoffLocation?: RouteLocationRaw | null;
}>();

const instance = getCurrentInstance();
const routerLinkComponent =
  instance?.appContext.components.RouterLink ||
  instance?.appContext.components["router-link"] ||
  null;

function fallbackHref(loc: RouteLocationRaw | null | undefined): string {
  if (!loc) return "#";
  if (typeof loc === "string") return loc;
  if (typeof loc === "object" && "path" in loc && loc.path) {
    const params = new URLSearchParams();
    if ("query" in loc && loc.query) {
      for (const [k, v] of Object.entries(loc.query)) {
        if (v !== undefined && v !== null) params.set(k, String(v));
      }
    }
    const qs = params.toString();
    return qs ? `${loc.path}?${qs}` : loc.path;
  }
  return "#";
}

const emit = defineEmits<{
  (e: "forms-loaded", forms: EcrfFormSummary[]): void;
}>();

const canvas = useStudyCanvasStore();
const forms = ref<EcrfFormSummary[]>([]);
const loading = ref(false);
const loadError = ref("");
let requestSeq = 0;

function formDisplayTitle(summary: EcrfFormSummary): string {
  const base = summary.collection_title || summary.collection_spec_id;
  return summary.planned_activity_id
    ? `${base} · ${summary.planned_activity_id}`
    : base;
}

const activeSchedule = computed<StudyScheduleOptions | null>(() => {
  if (props.schedule !== undefined) return props.schedule;
  const code = props.studyCode?.trim() ?? "";
  if (!code) return null;
  return canvas.scheduleOptionsByStudy[code] ?? null;
});

const matrixActive = computed(() => {
  const sched = activeSchedule.value;
  return Boolean(
    props.enrollmentId &&
    props.newFormCellLocation &&
    sched &&
    Array.isArray(sched.columns) &&
    Array.isArray(sched.scheduledRows) &&
    Array.isArray(sched.unscheduledRows) &&
    Array.isArray(sched.epochs) &&
    (sched.columns.length > 0 || sched.unscheduledRows.length > 0)
  );
});

const formsByCell = computed(() => {
  const map = new Map<string, EcrfFormSummary[]>();
  for (const summary of forms.value) {
    if (!summary.planned_activity_id) continue;
    const key = `${summary.collection_spec_id}::${summary.planned_activity_id}`;
    const bucket = map.get(key);
    if (bucket) bucket.push(summary);
    else map.set(key, [summary]);
  }
  return map;
});

function cellForms(specId: string, activityId: string): EcrfFormSummary[] {
  return formsByCell.value.get(`${specId}::${activityId}`) ?? [];
}

function isCellScheduled(row: StudyScheduleRow, colIdx: number): boolean {
  return row.columnIndices.includes(colIdx);
}

function cellNewLocation(
  specId: string,
  visitId: string | null
): RouteLocationRaw | null {
  if (props.newFormCellLocation) {
    return props.newFormCellLocation(specId, visitId);
  }
  return props.newFormLocation ?? null;
}

function canAddRepeat(instances: EcrfFormSummary[]): boolean {
  return !instances.some((inst) => Boolean(inst.locked || inst.frozen));
}

function cellClasses(row: StudyScheduleRow, colIdx: number, visitId: string) {
  const instances = cellForms(row.collectionSpecId, visitId);
  const scheduled = isCellScheduled(row, colIdx);
  if (!scheduled && instances.length === 0) {
    return ["matrix-cell", "matrix-cell--na"];
  }
  if (instances.length === 0) {
    return ["matrix-cell", "matrix-cell--not-started"];
  }
  const hasOpenQ = instances.some(
    (f) => toFormSummaryCount(f.open_query_count) > 0
  );
  const allComplete = instances.every((f) => f.complete);
  return [
    "matrix-cell",
    allComplete ? "matrix-cell--complete" : "matrix-cell--incomplete",
    hasOpenQ ? "matrix-cell--queried" : "",
  ];
}

function instanceAriaLabel(
  inst: EcrfFormSummary,
  specName: string,
  visitName: string,
  idx: number,
  total: number
): string {
  const status = inst.complete ? "complete" : "incomplete";
  if (total > 1) {
    return `#${idx + 1} · v${inst.version} — ${specName} at ${visitName}, ${status}`;
  }
  return `${specName} at ${visitName}, ${status}`;
}

function completenessLabel(summary: EcrfFormSummary): string {
  const recorded = toFormSummaryCount(summary.recorded_count);
  const total = toFormSummaryCount(summary.item_count);
  const missingReq = toFormSummaryCount(summary.missing_required_count);
  if (summary.complete) {
    return `Complete (${recorded}/${total})`;
  }
  if (missingReq > 0) {
    return `Incomplete (${recorded}/${total} recorded, ${missingReq} req missing)`;
  }
  return `Incomplete (${recorded}/${total} recorded)`;
}

function lifecycleBadges(summary: EcrfFormSummary): LifecycleBadge[] {
  const badges: LifecycleBadge[] = [];
  const openQueries = toFormSummaryCount(summary.open_query_count);
  const answeredQueries = toFormSummaryCount(summary.answered_query_count);
  const verified = toFormSummaryCount(summary.verified_count);
  const unverified = toFormSummaryCount(summary.unverified_count);

  if (openQueries > 0) {
    badges.push({
      key: "open-q",
      label: `${openQueries} open ${openQueries === 1 ? "query" : "queries"}`,
      tone: "query-open",
      testId: "badge-open-queries",
    });
  }
  if (answeredQueries > 0) {
    badges.push({
      key: "answered-q",
      label: `${answeredQueries} answered`,
      tone: "query-answered",
      testId: "badge-answered-queries",
    });
  }

  if (verified > 0 && unverified === 0) {
    badges.push({
      key: "sdv-full",
      label: `SDV verified (${verified})`,
      tone: "verified",
      testId: "badge-sdv-verified",
    });
  } else if (verified > 0 && unverified > 0) {
    badges.push({
      key: "sdv-partial",
      label: `SDV ${verified}/${verified + unverified}`,
      tone: "partial-sdv",
      testId: "badge-sdv-partial",
    });
  } else if (
    unverified > 0 &&
    (summary.verified_count !== undefined ||
      summary.unverified_count !== undefined)
  ) {
    badges.push({
      key: "sdv-none",
      label: `${unverified} unverified`,
      tone: "unverified",
      testId: "badge-sdv-unverified",
    });
  }

  if (summary.locked) {
    badges.push({
      key: "locked",
      label: "Locked",
      tone: "locked",
      testId: "badge-locked",
    });
  } else if (summary.frozen) {
    badges.push({
      key: "frozen",
      label: "Frozen",
      tone: "frozen",
      testId: "badge-frozen",
    });
  }

  if (summary.signature_superseded) {
    badges.push({
      key: "sig-superseded",
      label: "Signature superseded",
      tone: "sig-superseded",
      testId: "badge-signature-superseded",
    });
  } else if (summary.signed) {
    badges.push({
      key: "signed",
      label: "Signed",
      tone: "signed",
      testId: "badge-signed",
    });
  }

  return badges;
}

const unscheduledForms = computed(() =>
  forms.value.filter((f) => !f.planned_activity_id)
);

const numberedUnscheduledForms = computed(() => {
  const counts = new Map<string, number>();
  for (const f of unscheduledForms.value) {
    counts.set(
      f.collection_spec_id,
      (counts.get(f.collection_spec_id) ?? 0) + 1
    );
  }
  const seen = new Map<string, number>();
  return unscheduledForms.value.map((summary) => {
    const next = (seen.get(summary.collection_spec_id) ?? 0) + 1;
    seen.set(summary.collection_spec_id, next);
    return {
      summary,
      ordinal: next,
      totalForSpec: counts.get(summary.collection_spec_id) ?? 1,
    };
  });
});

const unmappedScheduledForms = computed(() => {
  const sched = activeSchedule.value;
  if (
    !sched ||
    !Array.isArray(sched.scheduledRows) ||
    !Array.isArray(sched.columns)
  ) {
    return [];
  }
  const knownSpecs = new Set(
    sched.scheduledRows.map((r) => r.collectionSpecId)
  );
  const knownCols = new Set(sched.columns.map((c) => c.activityId));
  return forms.value.filter(
    (f) =>
      Boolean(f.planned_activity_id) &&
      (!knownSpecs.has(f.collection_spec_id) ||
        !knownCols.has(f.planned_activity_id as string))
  );
});

const totalScheduledCells = computed(() => {
  const sched = activeSchedule.value;
  if (!sched || !Array.isArray(sched.scheduledRows)) return 0;
  return sched.scheduledRows.reduce(
    (sum, row) => sum + row.columnIndices.length,
    0
  );
});

const scheduledCompleteCells = computed(() => {
  const sched = activeSchedule.value;
  if (
    !sched ||
    !Array.isArray(sched.scheduledRows) ||
    !Array.isArray(sched.columns)
  ) {
    return 0;
  }
  let count = 0;
  for (const row of sched.scheduledRows) {
    for (const colIdx of row.columnIndices) {
      const col = sched.columns[colIdx];
      if (!col) continue;
      const instances = cellForms(row.collectionSpecId, col.activityId);
      if (instances.length > 0 && instances.every((f) => f.complete)) {
        count++;
      }
    }
  }
  return count;
});

const incompleteFormsCount = computed(
  () => forms.value.filter((f) => !f.complete).length
);
const totalOpenQueries = computed(() =>
  forms.value.reduce(
    (sum, f) => sum + toFormSummaryCount(f.open_query_count),
    0
  )
);
const verifiedFormsCount = computed(
  () =>
    forms.value.filter(
      (f) =>
        toFormSummaryCount(f.verified_count) > 0 &&
        toFormSummaryCount(f.unverified_count) === 0
    ).length
);
const signedFormsCount = computed(
  () => forms.value.filter((f) => Boolean(f.signed)).length
);
const lockedFormsCount = computed(
  () => forms.value.filter((f) => Boolean(f.locked || f.frozen)).length
);

async function loadFormsAndSchedule() {
  const scheduleTask =
    props.schedule === undefined && props.studyCode && props.newFormCellLocation
      ? canvas.loadScheduleOptions(props.studyCode).catch(() => null)
      : Promise.resolve(null);
  const enrollmentId = props.enrollmentId;
  if (!enrollmentId) {
    forms.value = [];
    loading.value = false;
    loadError.value = "";
    emit("forms-loaded", []);
    void scheduleTask;
    return;
  }
  if (typeof ecrfFormsApi?.listForEnrollment !== "function") {
    forms.value = [];
    emit("forms-loaded", []);
    void scheduleTask;
    return;
  }
  const seq = ++requestSeq;
  loading.value = true;
  loadError.value = "";
  const formsTask = (async () => {
    try {
      const result = await ecrfFormsApi.listForEnrollment(enrollmentId);
      if (seq === requestSeq) {
        forms.value = result ?? [];
        for (const summary of forms.value) {
          seedNodeSummary(`collection_instance:${summary.form_id}`, {
            kind: "collection_instance",
            title: formDisplayTitle(summary),
          });
        }
        emit("forms-loaded", forms.value);
      }
    } catch (err) {
      if (seq === requestSeq) {
        forms.value = [];
        loadError.value = err instanceof Error ? err.message : String(err);
        emit("forms-loaded", []);
      }
    } finally {
      if (seq === requestSeq) loading.value = false;
    }
  })();
  await Promise.all([formsTask, scheduleTask]);
}

watch(
  [() => props.enrollmentId, () => props.studyCode],
  () => {
    void loadFormsAndSchedule();
  },
  { immediate: true }
);
</script>

<style scoped>
.forms-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.forms-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.forms-head h3 {
  margin: 0;
  font-size: 1rem;
}

.forms-head-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.forms-open-btn {
  font-size: 0.82rem;
  padding: 4px 10px;
  text-decoration: none;
}

.status-line {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

.casebook-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 12px;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.82rem;
}

.casebook-protocol-title {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.casebook-rollups {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.casebook-matrix-scroll {
  overflow-x: auto;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.casebook-matrix {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.casebook-matrix th,
.casebook-matrix td {
  border-bottom: 1px solid var(--semantic-color-border);
  border-right: 1px solid var(--semantic-color-border);
  padding: 8px 10px;
  vertical-align: top;
  text-align: left;
}

.casebook-matrix th:last-child,
.casebook-matrix td:last-child {
  border-right: 0;
}

.casebook-matrix tbody tr:last-child th,
.casebook-matrix tbody tr:last-child td {
  border-bottom: 0;
}

.matrix-corner {
  background: var(--semantic-color-surface-subtle);
  font-weight: 700;
  color: var(--semantic-color-text-dark);
  min-width: 10rem;
}

.matrix-epoch-header {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  text-align: center;
}

.matrix-visit-header {
  background: var(--semantic-color-surface-subtle);
  min-width: 9rem;
}

.visit-name {
  display: block;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.visit-day {
  display: block;
  font-size: 0.74rem;
  font-weight: 500;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.matrix-row-header {
  background: var(--semantic-color-surface);
  min-width: 10rem;
}

.row-spec-title {
  display: block;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.row-spec-code {
  display: block;
  font-size: 0.74rem;
  font-weight: 400;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.matrix-cell--na {
  background: var(--semantic-color-surface-subtle);
  text-align: center;
  vertical-align: middle;
}

.matrix-na {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.matrix-cell--queried {
  background: var(--semantic-color-warning-bg-soft);
}

.matrix-cell-empty {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
}

.matrix-open-link,
.matrix-repeat-link {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}

.matrix-instances {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
}

.matrix-instance {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  width: 100%;
}

.matrix-instance + .matrix-instance {
  padding-top: 6px;
  border-top: 1px dashed var(--semantic-color-border-muted);
}

.matrix-instance-top {
  display: flex;
  align-items: center;
  gap: 6px;
}

.cb-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.cb-badge {
  display: inline-block;
  padding: 1px 7px;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 600;
  line-height: 1.4;
}

.cb-badge--not-started {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.cb-badge--complete,
.cb-badge--verified,
.cb-badge--signed {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.cb-badge--incomplete,
.cb-badge--query-open,
.cb-badge--partial-sdv,
.cb-badge--sig-superseded {
  background: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
}

.cb-badge--query-answered {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
}

.cb-badge--unverified {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.cb-badge--locked {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}

.cb-badge--frozen {
  background: var(--semantic-color-info-bg-soft);
  color: var(--semantic-color-primary-dark);
}

.casebook-unscheduled {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 4px;
}

.unscheduled-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.unscheduled-head h4 {
  margin: 0;
  font-size: 0.92rem;
  color: var(--semantic-color-text-dark);
}

.unscheduled-triggers {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.forms-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.forms-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
}

.forms-item-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.forms-link {
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}

.forms-status {
  padding: 0 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
}

.forms-status[data-complete="true"] {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.forms-meta {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}
</style>
