<template>
  <section
    id="record-forms"
    class="forms-panel"
    aria-labelledby="record-forms-title"
    :aria-busy="loading"
  >
    <div class="forms-head">
      <h3 id="record-forms-title" tabindex="-1">
        {{ matrixActive ? "Casebook" : "Forms" }}
      </h3>
      <div class="forms-head-actions">
        <component
          :is="routerLinkComponent || 'a'"
          v-if="enrollmentId && newFormLocation && !noEffectiveVersionActive"
          id="record-open-form"
          class="btn btn-secondary forms-open-btn"
          v-bind="linkAttrs(newFormLocation)"
          @click="onFallbackLinkClick($event, newFormLocation)"
        >
          Open new form
        </component>
        <component
          :is="routerLinkComponent || 'a'"
          v-if="isDemoBuild() && enrollmentId && handoffLocation"
          id="record-handoff-standalone"
          data-testid="record-ecrf-handoff"
          class="btn btn-secondary forms-open-btn"
          v-bind="linkAttrs(handoffLocation)"
          @click="onFallbackLinkClick($event, handoffLocation)"
        >
          Open standalone form view
        </component>
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

    <div
      v-else-if="noEffectiveVersionActive"
      id="casebook-governance-guidance"
      data-testid="casebook-governance-guidance"
      class="casebook-governance-banner"
      role="status"
    >
      <strong class="casebook-governance-heading">
        No governing protocol version is effective at this site yet.
      </strong>
      <p class="casebook-governance-body">
        {{
          activeSchedule?.guidance ||
          "Complete protocol review, electronic approval, and site activation before opening participant casebook forms."
        }}
      </p>
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
          <span
            class="clinical-badge clinical-badge--verified cb-badge cb-badge--complete"
          >
            {{ scheduledCompleteCells }}/{{ totalScheduledCells }} scheduled
            complete
          </span>
          <span
            v-if="incompleteFormsCount > 0"
            class="clinical-badge clinical-badge--warning cb-badge cb-badge--incomplete"
          >
            {{ incompleteFormsCount }} incomplete
          </span>
          <span
            v-if="totalOpenQueries > 0"
            class="clinical-badge clinical-badge--queried cb-badge cb-badge--query-open"
          >
            {{ totalOpenQueries }} open
            {{ totalOpenQueries === 1 ? "query" : "queries" }}
          </span>
          <span
            v-if="totalErrorFindings > 0"
            class="clinical-badge clinical-badge--error cb-badge cb-badge--error-finding"
            data-testid="casebook-error-findings"
          >
            {{ totalErrorFindings }} error
            {{ totalErrorFindings === 1 ? "finding" : "findings" }}
          </span>
          <span
            v-if="verifiedFormsCount > 0"
            class="clinical-badge clinical-badge--verified cb-badge cb-badge--verified"
          >
            {{ verifiedFormsCount }} SDV verified
          </span>
          <span
            v-if="signedFormsCount > 0"
            class="clinical-badge clinical-badge--signed cb-badge cb-badge--signed"
          >
            {{ signedFormsCount }} signed
          </span>
          <span
            v-if="lockedFormsCount > 0"
            class="clinical-badge clinical-badge--locked cb-badge cb-badge--locked"
          >
            {{ lockedFormsCount }} locked/frozen
          </span>
          <span
            v-if="windowDeviationFormsCount > 0"
            class="clinical-badge clinical-badge--warning cb-badge cb-badge--window-deviation"
            data-testid="casebook-window-deviations"
          >
            {{ windowDeviationFormsCount }} visit window
            {{ windowDeviationFormsCount === 1 ? "deviation" : "deviations" }}
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
                <span
                  v-if="col.targetDate"
                  class="visit-target-date"
                  :data-testid="`visit-target-${col.activityId}`"
                >
                  {{ col.targetDate }}
                </span>
                <div
                  v-if="col.windowStatus || col.repeating"
                  class="visit-header-badges"
                >
                  <span
                    v-if="col.windowStatus"
                    class="clinical-badge cb-badge visit-window-badge"
                    :class="visitWindowBadgeClass(col.windowStatus)"
                    :data-testid="`visit-window-${col.activityId}`"
                    :data-window-status="col.windowStatus"
                  >
                    {{ col.windowStatus }}
                  </span>
                  <span
                    v-if="col.repeating"
                    class="clinical-badge clinical-badge--draft cb-badge"
                    :data-testid="`visit-repeating-${col.activityId}`"
                  >
                    Repeating
                  </span>
                </div>
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
                  ><template v-if="row.repeating"> · Repeating</template>
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
                    class="clinical-badge clinical-badge--draft cb-badge cb-badge--not-started"
                    data-testid="badge-not-started"
                  >
                    Not started
                  </span>
                  <component
                    :is="routerLinkComponent || 'a'"
                    v-if="cellNewLocation(row.collectionSpecId, col.activityId)"
                    :id="`matrix-open-${row.collectionSpecId}-${col.activityId}`"
                    :data-testid="`matrix-open-${row.collectionSpecId}-${col.activityId}`"
                    class="matrix-open-link"
                    v-bind="
                      linkAttrs(
                        cellNewLocation(row.collectionSpecId, col.activityId)
                      )
                    "
                    :aria-label="`Open form: ${row.name} for ${col.name}`"
                    @click="
                      onCellLinkClick(
                        $event,
                        row.collectionSpecId,
                        col.activityId
                      )
                    "
                  >
                    Open form
                  </component>
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
                      <component
                        :is="routerLinkComponent || 'a'"
                        :id="`record-form-link-${inst.form_id}`"
                        class="forms-link matrix-instance-link"
                        v-bind="linkAttrs(formLocation(inst.form_id))"
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
                        @click="onFormLinkClick($event, inst.form_id)"
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
                          {{ row.name
                          }}<span
                            v-if="inst.planned_activity_id"
                            class="sr-only"
                          >
                            · {{ inst.planned_activity_id }}</span
                          >
                        </template>
                      </component>
                    </div>
                    <div class="cb-badges">
                      <span
                        v-for="badge in instanceStatusBadges(inst)"
                        :key="badge.key"
                        :class="badge.classes"
                        :data-complete="badge.dataComplete"
                        :data-testid="badge.testId"
                      >
                        {{ badge.label }}
                      </span>
                    </div>
                  </div>
                  <component
                    :is="routerLinkComponent || 'a'"
                    v-if="
                      cellNewLocation(row.collectionSpecId, col.activityId) &&
                      canAddRepeat(
                        row,
                        col,
                        cellForms(row.collectionSpecId, col.activityId)
                      )
                    "
                    :id="`matrix-repeat-${row.collectionSpecId}-${col.activityId}`"
                    :data-testid="`matrix-repeat-${row.collectionSpecId}-${col.activityId}`"
                    class="matrix-repeat-link"
                    v-bind="
                      linkAttrs(
                        cellNewLocation(row.collectionSpecId, col.activityId)
                      )
                    "
                    :aria-label="`+ Add repeat for ${row.name} at ${col.name}`"
                    @click="
                      onCellLinkClick(
                        $event,
                        row.collectionSpecId,
                        col.activityId
                      )
                    "
                  >
                    + Add repeat
                  </component>
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
            <component
              :is="routerLinkComponent || 'a'"
              v-for="spec in activeSchedule.unscheduledRows"
              :key="spec.collectionSpecId"
              :id="`unscheduled-open-${spec.collectionSpecId}`"
              :data-testid="`unscheduled-open-${spec.collectionSpecId}`"
              class="btn btn-secondary forms-open-btn"
              v-bind="linkAttrs(cellNewLocation(spec.collectionSpecId, null))"
              @click="onCellLinkClick($event, spec.collectionSpecId, null)"
            >
              + {{ spec.name }}
            </component>
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
          <ParticipantFormListItem
            v-for="entry in numberedUnscheduledForms"
            :key="entry.summary.form_id"
            :summary="entry.summary"
            :title="unscheduledFormTitle(entry)"
            :link-component="routerLinkComponent || 'a'"
            :link-bindings="linkAttrs(formLocation(entry.summary.form_id))"
            :badges="instanceStatusBadges(entry.summary)"
            @select="onFormLinkClick($event, entry.summary.form_id)"
          />
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
          <ParticipantFormListItem
            v-for="summary in unmappedScheduledForms"
            :key="summary.form_id"
            :summary="summary"
            :title="formDisplayTitle(summary)"
            :link-component="routerLinkComponent || 'a'"
            :link-bindings="linkAttrs(formLocation(summary.form_id))"
            :badges="instanceStatusBadges(summary)"
            @select="onFormLinkClick($event, summary.form_id)"
          />
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
      <ParticipantFormListItem
        v-for="summary in forms"
        :key="summary.form_id"
        :summary="summary"
        :title="formDisplayTitle(summary)"
        :link-component="routerLinkComponent || 'a'"
        :link-bindings="linkAttrs(formLocation(summary.form_id))"
        :badges="instanceStatusBadges(summary, true)"
        @select="onFormLinkClick($event, summary.form_id)"
      />
    </ul>
  </section>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import * as VueRouter from "vue-router";
import type { RouteLocationRaw } from "vue-router";
import {
  ecrfFormsApi,
  toFormSummaryCount,
  toSignedFormDayOffset,
  type EcrfFormSummary,
} from "../../api/ecrfFormsApi";
import {
  type StudyScheduleColumn,
  type StudyScheduleOptions,
  type StudyScheduleRow,
} from "../../canvas/kernelStudy";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import { isDemoBuild } from "../../utils/demoMode";
import ParticipantFormListItem, {
  type FormStatusBadgeView,
} from "./ParticipantFormListItem.vue";
import SupportDetails from "./SupportDetails.vue";

interface LifecycleBadge {
  key: string;
  label: string;
  tone:
    | "query-open"
    | "query-answered"
    | "error-finding"
    | "verified"
    | "partial-sdv"
    | "unverified"
    | "locked"
    | "frozen"
    | "signed"
    | "sig-superseded"
    | "timing-in-window"
    | "timing-out-of-window";
  testId: string;
}

/**
 * Renders a participant's casebook inside Clinical Canvas: when the study has a governing Schedule of Activities,
 * displays an interactive epoch/visit/form matrix with live completeness, query, source verification, lock/freeze,
 * signature, repeating-instance, and unscheduled-form status; otherwise lists opened form instances directly.
 */
const props = defineProps<{
  studyCode?: string;
  siteCode?: string | null;
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
const useRouterFn =
  "useRouter" in VueRouter && typeof VueRouter.useRouter === "function"
    ? VueRouter.useRouter
    : null;
const fallbackRouter = useRouterFn ? useRouterFn() : null;

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

function linkAttrs(
  loc: RouteLocationRaw | null | undefined
): Record<string, unknown> {
  return routerLinkComponent ? { to: loc ?? "#" } : { href: fallbackHref(loc) };
}

const emit = defineEmits<{
  (e: "forms-loaded", forms: EcrfFormSummary[]): void;
  (e: "select-form", formId: string): void;
  (
    e: "select-new-cell",
    payload: { collectionSpecId: string; plannedActivityId: string | null }
  ): void;
}>();

function onFallbackLinkClick(
  event: MouseEvent,
  loc: RouteLocationRaw | null | undefined
) {
  if (routerLinkComponent || !loc) return;
  event.preventDefault();
  if (typeof fallbackRouter?.push === "function") {
    void fallbackRouter.push(loc);
  } else if (typeof fallbackRouter?.replace === "function") {
    void fallbackRouter.replace(loc);
  }
}

function onFormLinkClick(event: MouseEvent, formId: string) {
  emit("select-form", formId);
  onFallbackLinkClick(event, props.formLocation(formId));
}

function onCellLinkClick(
  event: MouseEvent,
  collectionSpecId: string,
  plannedActivityId: string | null
) {
  emit("select-new-cell", { collectionSpecId, plannedActivityId });
  onFallbackLinkClick(
    event,
    cellNewLocation(collectionSpecId, plannedActivityId)
  );
}

const canvas = useStudyCanvasStore();
const forms = ref<EcrfFormSummary[]>([]);
const enrollmentSchedule = ref<StudyScheduleOptions | null>(null);
const loading = ref(false);
const loadError = ref("");
let requestSeq = 0;

function formDisplayTitle(summary: EcrfFormSummary): string {
  const base = summary.collection_title || summary.collection_spec_id;
  return summary.planned_activity_id
    ? `${base} · ${summary.planned_activity_id}`
    : base;
}

function unscheduledFormTitle(entry: {
  summary: EcrfFormSummary;
  ordinal: number;
  totalForSpec: number;
}): string {
  const base =
    entry.summary.collection_title || entry.summary.collection_spec_id;
  const ord = entry.totalForSpec > 1 ? ` #${entry.ordinal}` : "";
  return `${base}${ord} · Unscheduled`;
}

function instanceStatusBadges(
  summary: EcrfFormSummary,
  flatStatusClass = false
): FormStatusBadgeView[] {
  const completeClasses = flatStatusClass
    ? `clinical-badge forms-status ${summary.complete ? "clinical-badge--verified" : "clinical-badge--warning"}`
    : `clinical-badge cb-badge ${summary.complete ? "clinical-badge--verified cb-badge--complete" : "clinical-badge--warning cb-badge--incomplete"}`;
  return [
    {
      key: "completeness",
      classes: completeClasses,
      label: completenessLabel(summary),
      dataComplete: summary.complete ? "true" : "false",
    },
    ...lifecycleBadges(summary).map((badge) => ({
      key: badge.key,
      classes: `clinical-badge cb-badge ${clinicalBadgeModifier(badge.tone)} cb-badge--${badge.tone}`,
      label: badge.label,
      testId: badge.testId,
    })),
  ];
}

const activeSchedule = computed<StudyScheduleOptions | null>(() => {
  if (enrollmentSchedule.value) return enrollmentSchedule.value;
  if (props.schedule !== undefined) return props.schedule;
  const code = props.studyCode?.trim() ?? "";
  if (!code) return null;
  return canvas.getScheduleFor(code, props.siteCode);
});

const noEffectiveVersionActive = computed(() =>
  Boolean(
    props.enrollmentId &&
    activeSchedule.value?.governanceStatus === "NoEffectiveVersion"
  )
);

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

function canAddRepeat(
  row: StudyScheduleRow,
  col: StudyScheduleColumn,
  instances: EcrfFormSummary[]
): boolean {
  if (!row.repeating && !col.repeating) {
    return false;
  }
  return !instances.some((inst) => Boolean(inst.locked || inst.frozen));
}

function visitWindowBadgeClass(windowStatus: string): string {
  switch (windowStatus) {
    case "Open":
      return "clinical-badge--verified";
    case "Closed":
      return "clinical-badge--warning";
    case "NotYetOpen":
    default:
      return "clinical-badge--draft";
  }
}

function isFormSummaryComplete(summary: EcrfFormSummary): boolean {
  return (
    Boolean(summary.complete) &&
    toFormSummaryCount(summary.missing_required_count) === 0 &&
    toFormSummaryCount(summary.open_query_count) === 0 &&
    toFormSummaryCount(summary.answered_query_count) === 0 &&
    toFormSummaryCount(summary.open_error_finding_count) === 0
  );
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
    (f) =>
      toFormSummaryCount(f.open_query_count) > 0 ||
      toFormSummaryCount(f.open_error_finding_count) > 0
  );
  const allComplete = instances.every((f) => isFormSummaryComplete(f));
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
  const status = isFormSummaryComplete(inst) ? "complete" : "incomplete";
  if (total > 1) {
    return `#${idx + 1} · v${inst.version} — ${specName} at ${visitName}, ${status}`;
  }
  return `${specName} at ${visitName}, ${status}`;
}

function completenessLabel(summary: EcrfFormSummary): string {
  const recorded = toFormSummaryCount(summary.recorded_count);
  const total = toFormSummaryCount(summary.item_count);
  const missingReq = toFormSummaryCount(summary.missing_required_count);
  const errorFindings = toFormSummaryCount(summary.open_error_finding_count);
  const openQueries = toFormSummaryCount(summary.open_query_count);
  const answeredQueries = toFormSummaryCount(summary.answered_query_count);
  if (isFormSummaryComplete(summary)) {
    return `Complete (${recorded}/${total})`;
  }
  if (missingReq > 0) {
    return `Incomplete (${recorded}/${total} recorded, ${missingReq} req missing)`;
  }
  if (errorFindings > 0) {
    return `Incomplete (${recorded}/${total} recorded, ${errorFindings} ${errorFindings === 1 ? "error finding" : "error findings"})`;
  }
  if (openQueries + answeredQueries > 0) {
    return `Incomplete (${recorded}/${total} recorded, active queries)`;
  }
  return `Incomplete (${recorded}/${total} recorded)`;
}

function lifecycleBadges(summary: EcrfFormSummary): LifecycleBadge[] {
  const badges: LifecycleBadge[] = [];
  const openQueries = toFormSummaryCount(summary.open_query_count);
  const answeredQueries = toFormSummaryCount(summary.answered_query_count);
  const errorFindings = toFormSummaryCount(summary.open_error_finding_count);
  const verified = toFormSummaryCount(summary.verified_count);
  const unverified = toFormSummaryCount(summary.unverified_count);

  if (summary.visit_timing) {
    const daysOut = toSignedFormDayOffset(summary.days_outside_window);
    if (summary.visit_timing === "InWindow") {
      badges.push({
        key: "visit-timing",
        label: "InWindow",
        tone: "timing-in-window",
        testId: "badge-visit-timing",
      });
    } else if (summary.visit_timing === "Early") {
      badges.push({
        key: "visit-timing",
        label: daysOut && daysOut > 0 ? `Early (-${daysOut}d)` : "Early",
        tone: "timing-out-of-window",
        testId: "badge-visit-timing",
      });
    } else if (summary.visit_timing === "Late") {
      badges.push({
        key: "visit-timing",
        label: daysOut && daysOut > 0 ? `Late (+${daysOut}d)` : "Late",
        tone: "timing-out-of-window",
        testId: "badge-visit-timing",
      });
    }
  }

  if (errorFindings > 0) {
    badges.push({
      key: "error-findings",
      label: `${errorFindings} ${errorFindings === 1 ? "error finding" : "error findings"}`,
      tone: "error-finding",
      testId: "badge-error-findings",
    });
  }
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

function clinicalBadgeModifier(tone: LifecycleBadge["tone"]): string {
  switch (tone) {
    case "verified":
    case "timing-in-window":
      return "clinical-badge--verified";
    case "signed":
      return "clinical-badge--signed";
    case "locked":
      return "clinical-badge--locked";
    case "frozen":
    case "query-answered":
      return "clinical-badge--frozen";
    case "query-open":
      return "clinical-badge--queried";
    case "error-finding":
      return "clinical-badge--error";
    case "partial-sdv":
    case "sig-superseded":
    case "timing-out-of-window":
      return "clinical-badge--warning";
    case "unverified":
    default:
      return "clinical-badge--draft";
  }
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
      if (
        instances.length > 0 &&
        instances.every((f) => isFormSummaryComplete(f))
      ) {
        count++;
      }
    }
  }
  return count;
});

const incompleteFormsCount = computed(
  () => forms.value.filter((f) => !isFormSummaryComplete(f)).length
);
const totalOpenQueries = computed(() =>
  forms.value.reduce(
    (sum, f) => sum + toFormSummaryCount(f.open_query_count),
    0
  )
);
const totalErrorFindings = computed(() =>
  forms.value.reduce(
    (sum, f) => sum + toFormSummaryCount(f.open_error_finding_count),
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
const windowDeviationFormsCount = computed(
  () =>
    forms.value.filter(
      (f) => f.visit_timing === "Early" || f.visit_timing === "Late"
    ).length
);

async function loadFormsAndSchedule() {
  const scheduleTask =
    props.schedule === undefined && props.studyCode && props.newFormCellLocation
      ? canvas
          .loadScheduleOptions(
            props.studyCode,
            props.siteCode ? { siteCode: props.siteCode } : undefined
          )
          .catch(() => null)
      : Promise.resolve(null);
  const enrollmentId = props.enrollmentId;
  if (!enrollmentId) {
    forms.value = [];
    enrollmentSchedule.value = null;
    loading.value = false;
    loadError.value = "";
    emit("forms-loaded", []);
    void scheduleTask;
    return;
  }
  const seq = ++requestSeq;
  const enrollmentScheduleTask =
    props.schedule === undefined &&
    typeof canvas.loadEnrollmentSchedule === "function"
      ? canvas
          .loadEnrollmentSchedule(enrollmentId)
          .then((loaded) => {
            if (seq === requestSeq) {
              enrollmentSchedule.value = loaded;
            }
            return loaded;
          })
          .catch(() => null)
      : Promise.resolve(null);
  if (typeof ecrfFormsApi?.listForEnrollment !== "function") {
    forms.value = [];
    await enrollmentScheduleTask;
    emit("forms-loaded", []);
    void scheduleTask;
    return;
  }
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
  await Promise.all([formsTask, scheduleTask, enrollmentScheduleTask]);
}

watch(
  [() => props.enrollmentId, () => props.studyCode, () => props.siteCode],
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
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
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
  top: 0;
  left: 0;
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
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
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
  position: relative;
  width: 100%;
  max-width: 100%;
  min-width: 0;
  box-sizing: border-box;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.casebook-matrix-scroll:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 2px;
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
  font-size: var(--font-size-2xs);
  font-weight: 500;
  color: var(--semantic-color-text-secondary);
  font-variant-numeric: tabular-nums;
  margin-top: 2px;
}

.visit-target-date {
  display: block;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  margin-top: 2px;
  font-variant-numeric: tabular-nums;
}

.visit-header-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 4px;
}

.cb-badge {
  font-variant-numeric: tabular-nums;
}

.casebook-governance-banner {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 8px;
  background: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-text-dark);
  font-size: 0.85rem;
}

.casebook-governance-heading {
  font-size: 0.88rem;
}

.casebook-governance-body {
  margin: 0;
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
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  font-weight: 400;
  color: var(--semantic-color-text-secondary);
  font-variant-numeric: tabular-nums;
  margin-top: 2px;
}

.casebook-summary {
  font-variant-numeric: tabular-nums;
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

.casebook-unscheduled {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 4px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
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

.matrix-instance-link {
  position: relative;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}
</style>
