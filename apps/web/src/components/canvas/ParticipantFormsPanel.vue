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
      <CasebookSummaryBar
        :protocol-title="activeSchedule.title || activeSchedule.versionRef"
        :rollups="rollups"
      />

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
                        class="clinical-badge cb-badge"
                        :class="
                          inst.complete
                            ? 'clinical-badge--verified cb-badge--complete'
                            : 'clinical-badge--warning cb-badge--incomplete'
                        "
                        :data-complete="inst.complete ? 'true' : 'false'"
                      >
                        {{ completenessLabel(inst) }}
                      </span>
                      <span
                        v-for="badge in lifecycleBadges(inst)"
                        :key="badge.key"
                        class="clinical-badge cb-badge"
                        :class="[
                          clinicalBadgeModifier(badge.tone),
                          `cb-badge--${badge.tone}`,
                        ]"
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

      <ParticipantFormsListsPanel
        matrix-mode
        :subject-code="subjectCode"
        :router-link-component="routerLinkComponent"
        :unscheduled-rows="activeSchedule.unscheduledRows"
        :unscheduled-forms="unscheduledForms"
        :numbered-unscheduled-forms="numberedUnscheduledForms"
        :unmapped-scheduled-forms="unmappedScheduledForms"
        :forms="forms"
        :form-location="formLocation"
        :cell-new-location="cellNewLocation"
        @select-form="emit('select-form', $event)"
        @form-link-click="onFormLinkClick"
        @cell-link-click="onCellLinkClick"
      />
    </template>

    <ParticipantFormsListsPanel
      v-else
      :matrix-mode="false"
      :subject-code="subjectCode"
      :router-link-component="routerLinkComponent"
      :unscheduled-rows="[]"
      :unscheduled-forms="[]"
      :numbered-unscheduled-forms="[]"
      :unmapped-scheduled-forms="[]"
      :forms="forms"
      :form-location="formLocation"
      :cell-new-location="cellNewLocation"
      @select-form="emit('select-form', $event)"
      @form-link-click="onFormLinkClick"
      @cell-link-click="onCellLinkClick"
    />
  </section>
</template>

<script setup lang="ts">
import { getCurrentInstance } from "vue";
import * as VueRouter from "vue-router";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfFormSummary } from "../../api/ecrfFormsApi";
import { isDemoBuild } from "../../utils/demoMode";
import CasebookSummaryBar from "./CasebookSummaryBar.vue";
import ParticipantFormsListsPanel from "./ParticipantFormsListsPanel.vue";
import SupportDetails from "./SupportDetails.vue";
import {
  canAddRepeat,
  clinicalBadgeModifier,
  completenessLabel,
  fallbackHref,
  instanceAriaLabel,
  lifecycleBadges,
  visitWindowBadgeClass,
} from "./casebookBadges";
import {
  useParticipantCasebook,
  type ParticipantFormsProps,
} from "./useParticipantCasebook";

const props = defineProps<ParticipantFormsProps>();

const emit = defineEmits<{
  (e: "forms-loaded", forms: EcrfFormSummary[]): void;
  (e: "select-form", formId: string): void;
  (
    e: "select-new-cell",
    payload: { collectionSpecId: string; plannedActivityId: string | null }
  ): void;
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

function linkAttrs(
  loc: RouteLocationRaw | null | undefined
): Record<string, unknown> {
  return routerLinkComponent ? { to: loc } : { href: fallbackHref(loc) };
}

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

const {
  activeSchedule,
  cellClasses,
  cellForms,
  cellNewLocation,
  forms,
  isCellScheduled,
  loadError,
  loadFormsAndSchedule,
  loading,
  matrixActive,
  noEffectiveVersionActive,
  numberedUnscheduledForms,
  rollups,
  unmappedScheduledForms,
  unscheduledForms,
} = useParticipantCasebook(props, (loaded) => emit("forms-loaded", loaded));
</script>

<style scoped src="./participantFormsPanel.css"></style>
