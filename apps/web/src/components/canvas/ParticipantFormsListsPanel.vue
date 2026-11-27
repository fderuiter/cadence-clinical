<template>
  <template v-if="matrixMode">
    <section
      v-if="unscheduledRows.length > 0 || unscheduledForms.length > 0"
      id="casebook-unscheduled"
      class="casebook-unscheduled"
      data-testid="casebook-unscheduled"
      aria-labelledby="casebook-unscheduled-title"
    >
      <div class="unscheduled-head">
        <h4 id="casebook-unscheduled-title">Unscheduled &amp; log forms</h4>
        <div v-if="unscheduledRows.length > 0" class="unscheduled-triggers">
          <component
            :is="routerLinkComponent || 'a'"
            v-for="spec in unscheduledRows"
            :key="spec.collectionSpecId"
            :id="`unscheduled-open-${spec.collectionSpecId}`"
            :data-testid="`unscheduled-open-${spec.collectionSpecId}`"
            class="btn btn-secondary forms-open-btn"
            v-bind="linkAttrs(cellNewLocation(spec.collectionSpecId, null))"
            @click="emit('cellLinkClick', $event, spec.collectionSpecId, null)"
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
        <li
          v-for="entry in numberedUnscheduledForms"
          :id="`record-form-${entry.summary.form_id}`"
          :key="entry.summary.form_id"
          class="forms-item"
        >
          <div class="forms-item-main">
            <component
              :is="routerLinkComponent || 'a'"
              :id="`record-form-link-${entry.summary.form_id}`"
              class="forms-link"
              v-bind="linkAttrs(formLocation(entry.summary.form_id))"
              @click="emit('formLinkClick', $event, entry.summary.form_id)"
            >
              {{
                entry.summary.collection_title ||
                entry.summary.collection_spec_id
              }}<template v-if="entry.totalForSpec > 1">
                #{{ entry.ordinal }}</template
              >
              · Unscheduled
            </component>
            <span
              class="clinical-badge cb-badge"
              :class="
                entry.summary.complete
                  ? 'clinical-badge--verified cb-badge--complete'
                  : 'clinical-badge--warning cb-badge--incomplete'
              "
              :data-complete="entry.summary.complete ? 'true' : 'false'"
            >
              {{ completenessLabel(entry.summary) }}
            </span>
            <span
              v-for="badge in lifecycleBadges(entry.summary)"
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
            <component
              :is="routerLinkComponent || 'a'"
              :id="`record-form-link-${summary.form_id}`"
              class="forms-link"
              v-bind="linkAttrs(formLocation(summary.form_id))"
              @click="emit('formLinkClick', $event, summary.form_id)"
            >
              {{ summary.collection_title || summary.collection_spec_id
              }}<template v-if="summary.planned_activity_id">
                · {{ summary.planned_activity_id }}</template
              >
            </component>
            <span
              class="clinical-badge cb-badge"
              :class="
                summary.complete
                  ? 'clinical-badge--verified cb-badge--complete'
                  : 'clinical-badge--warning cb-badge--incomplete'
              "
              :data-complete="summary.complete ? 'true' : 'false'"
            >
              {{ completenessLabel(summary) }}
            </span>
            <span
              v-for="badge in lifecycleBadges(summary)"
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
          <span class="forms-meta">
            Version {{ summary.version }} · Collected
            {{ summary.collected_at }}
          </span>
        </li>
      </ul>
    </section>
  </template>

  <p v-else-if="forms.length === 0" id="record-forms-empty" class="status-line">
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
          :to="formLocation(summary.form_id) ?? '#'"
          @click="emit('selectForm', summary.form_id)"
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
          @click="emit('formLinkClick', $event, summary.form_id)"
        >
          {{ summary.collection_title || summary.collection_spec_id
          }}<template v-if="summary.planned_activity_id">
            · {{ summary.planned_activity_id }}</template
          >
        </a>
        <span
          class="clinical-badge forms-status"
          :class="
            summary.complete
              ? 'clinical-badge--verified'
              : 'clinical-badge--warning'
          "
          :data-complete="summary.complete ? 'true' : 'false'"
        >
          {{ completenessLabel(summary) }}
        </span>
        <span
          v-for="badge in lifecycleBadges(summary)"
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
      <span class="forms-meta">
        Version {{ summary.version }} · Collected {{ summary.collected_at }}
      </span>
    </li>
  </ul>
</template>

<script setup lang="ts">
import type { Component } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfFormSummary } from "../../api/ecrfFormsApi";
import type { StudyScheduleOptions } from "../../canvas/kernelStudy";
import {
  clinicalBadgeModifier,
  completenessLabel,
  fallbackHref,
  lifecycleBadges,
} from "./casebookBadges";
import type { NumberedUnscheduledForm } from "./useParticipantCasebook";

const props = defineProps<{
  matrixMode: boolean;
  subjectCode: string;
  routerLinkComponent: Component | string | null;
  unscheduledRows: StudyScheduleOptions["unscheduledRows"];
  unscheduledForms: EcrfFormSummary[];
  numberedUnscheduledForms: NumberedUnscheduledForm[];
  unmappedScheduledForms: EcrfFormSummary[];
  forms: EcrfFormSummary[];
  formLocation: (
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw | null;
  cellNewLocation: (
    specId: string,
    visitId: string | null
  ) => RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "selectForm", formId: string): void;
  (e: "formLinkClick", event: MouseEvent, formId: string): void;
  (
    e: "cellLinkClick",
    event: MouseEvent,
    specId: string,
    visitId: string | null
  ): void;
}>();

function linkAttrs(
  loc: RouteLocationRaw | null | undefined
): Record<string, unknown> {
  return props.routerLinkComponent ? { to: loc } : { href: fallbackHref(loc) };
}
</script>

<style scoped>
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

.cb-badge,
.forms-meta {
  font-variant-numeric: tabular-nums;
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

.forms-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.forms-item-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.forms-link {
  position: relative;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}

.forms-meta {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}
</style>
