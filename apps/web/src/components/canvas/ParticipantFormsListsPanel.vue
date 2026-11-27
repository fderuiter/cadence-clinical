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
        <ParticipantFormListItem
          v-for="entry in numberedUnscheduledForms"
          :key="entry.summary.form_id"
          :summary="entry.summary"
          :title="unscheduledFormTitle(entry)"
          :link-component="routerLinkComponent || 'a'"
          :link-bindings="linkAttrs(formLocation(entry.summary.form_id))"
          :badges="instanceStatusBadges(entry.summary)"
          @select="emit('formLinkClick', $event, entry.summary.form_id)"
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
          :title="scheduledFormTitle(summary)"
          :link-component="routerLinkComponent || 'a'"
          :link-bindings="linkAttrs(formLocation(summary.form_id))"
          :badges="instanceStatusBadges(summary)"
          @select="emit('formLinkClick', $event, summary.form_id)"
        />
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
    <ParticipantFormListItem
      v-for="summary in forms"
      :key="summary.form_id"
      :summary="summary"
      :title="scheduledFormTitle(summary)"
      :link-component="routerLinkComponent || 'a'"
      :link-bindings="linkAttrs(formLocation(summary.form_id), true)"
      :badges="instanceStatusBadges(summary, true)"
      @select="
        routerLinkComponent
          ? emit('selectForm', summary.form_id)
          : emit('formLinkClick', $event, summary.form_id)
      "
    />
  </ul>
</template>

<script setup lang="ts">
import type { Component } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfFormSummary } from "../../api/ecrfFormsApi";
import type { StudyScheduleOptions } from "../../canvas/kernelStudy";
import { fallbackHref, instanceStatusBadges } from "./casebookBadges";
import ParticipantFormListItem from "./ParticipantFormListItem.vue";
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
  loc: RouteLocationRaw | null | undefined,
  fallbackToHash = false
): Record<string, unknown> {
  if (props.routerLinkComponent) {
    return { to: fallbackToHash ? (loc ?? "#") : loc };
  }
  return { href: fallbackHref(loc) };
}

function unscheduledFormTitle(entry: NumberedUnscheduledForm): string {
  const base =
    entry.summary.collection_title || entry.summary.collection_spec_id;
  const ordinal = entry.totalForSpec > 1 ? ` #${entry.ordinal}` : "";
  return `${base}${ordinal} · Unscheduled`;
}

function scheduledFormTitle(summary: EcrfFormSummary): string {
  const base = summary.collection_title || summary.collection_spec_id;
  return summary.planned_activity_id
    ? `${base} · ${summary.planned_activity_id}`
    : base;
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
</style>
