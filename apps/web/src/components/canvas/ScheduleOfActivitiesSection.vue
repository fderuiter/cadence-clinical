<template>
  <section
    id="schedule-of-activities-section"
    class="soa-section"
    aria-labelledby="schedule-of-activities-title"
    :aria-busy="loading || soaLoading"
  >
    <div class="soa-header">
      <div class="soa-header-copy">
        <h2 id="schedule-of-activities-title" tabindex="-1">
          Schedule of activities
        </h2>
        <div
          v-if="error || soaError"
          id="soa-status"
          class="soa-status"
          role="alert"
        >
          <p class="soa-status-text">
            Schedule of activities couldn't be loaded right now.
          </p>
          <SupportDetails :detail="error || soaError" />
        </div>
        <p v-else id="soa-status" class="soa-status" role="status">
          {{ statusLine }}
        </p>
      </div>

      <div v-if="visibleVersions.length" class="soa-toolbar">
        <label for="soa-version-select" class="soa-toolbar-label">
          Protocol version
        </label>
        <select
          id="soa-version-select"
          class="soa-version-select"
          :value="activeVersionRef"
          :disabled="soaLoading"
          @change="onVersionChange"
        >
          <option
            v-for="ver in visibleVersions"
            :key="ver.ref!"
            :value="ver.ref!"
          >
            Version {{ ver.number }} ({{ ver.status }})
          </option>
        </select>

        <span v-if="activeVersion" class="soa-status-pill">
          {{ activeVersion.status }}
        </span>

        <span
          v-if="parentVersionNodeRef && parentVersionLabel"
          class="soa-parent-chip"
        >
          <span class="soa-meta-label">Amends:</span>
          <NodeRefChip
            :node-ref="parentVersionNodeRef"
            :fallback-title="parentVersionLabel"
          />
        </span>
      </div>
    </div>

    <div
      v-if="soa && (soa.content_hash || soa.hash)"
      id="soa-hashes"
      class="soa-hash-bar"
    >
      <span v-if="soa.content_hash" class="soa-hash-item">
        Content digest: <code>{{ soa.content_hash }}</code>
      </span>
      <span v-if="soa.hash" class="soa-hash-item">
        Matrix digest: <code>{{ soa.hash }}</code>
      </span>
    </div>

    <template v-if="soa && !soaError">
      <p
        v-if="!soa.columns.length && !soa.rows.length"
        id="soa-empty"
        class="soa-empty"
        role="status"
      >
        No planned visits or collection specifications are recorded for this
        protocol version yet.
      </p>

      <template v-else>
        <div
          id="soa-matrix-wrapper"
          class="soa-matrix-wrapper scrollable-table-wrapper"
        >
          <table id="soa-matrix-table" class="soa-table">
            <caption class="sr-only">
              Schedule of activities for
              {{
                studyCode
              }}
              {{
                activeVersion ? `Version ${activeVersion.number}` : ""
              }}
            </caption>
            <thead>
              <tr class="soa-epoch-row">
                <th
                  scope="col"
                  rowspan="2"
                  class="soa-sticky-col soa-corner-th"
                >
                  Collection specification &amp; concepts
                </th>
                <th
                  v-for="epoch in epochSpans"
                  :key="epoch.key"
                  :colspan="epoch.span"
                  scope="colgroup"
                  class="soa-epoch-th"
                >
                  <div class="soa-epoch-inner">
                    <span>{{ epoch.name }}</span>
                    <NodeRefChip
                      v-if="epoch.protocolElement"
                      :node-ref="epoch.protocolElement"
                      :fallback-title="epoch.name"
                    />
                  </div>
                </th>
              </tr>
              <tr class="soa-visit-row">
                <th
                  v-for="col in soa.columns"
                  :key="col.activity_id"
                  scope="col"
                  class="soa-visit-th"
                >
                  <button
                    :id="`soa-col-${col.activity_id}`"
                    type="button"
                    class="soa-visit-btn"
                    @click="onColumnClick(col)"
                  >
                    <span class="soa-visit-name">
                      {{ col.name }}
                      <sup
                        v-if="col.footnotes?.length"
                        class="soa-footnote-ref numeric"
                      >
                        [{{ col.footnotes.join(",") }}]
                      </sup>
                    </span>
                    <span class="soa-visit-window numeric">
                      <CalendarClock :size="12" aria-hidden="true" />
                      {{ formatVisitTiming(col) }}
                    </span>
                    <span
                      v-if="col.locations?.length"
                      class="soa-visit-locations"
                    >
                      <MapPin :size="12" aria-hidden="true" />
                      {{ col.locations.join(", ") }}
                    </span>
                    <span v-if="col.repeating" class="soa-badge-repeating">
                      <Repeat :size="11" aria-hidden="true" />
                      Repeating
                    </span>
                  </button>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in soa.rows"
                :id="`soa-row-${row.collection_spec_id}`"
                :key="row.collection_spec_id"
                class="soa-row"
              >
                <th scope="row" class="soa-sticky-col soa-row-th">
                  <div class="soa-row-header">
                    <button
                      :id="`soa-row-btn-${row.collection_spec_id}`"
                      type="button"
                      class="soa-row-btn"
                      @click="onRowClick(row)"
                    >
                      <span class="soa-row-title">
                        {{ row.name }}
                        <sup
                          v-if="row.footnotes?.length"
                          class="soa-footnote-ref numeric"
                        >
                          [{{ row.footnotes.join(",") }}]
                        </sup>
                      </span>
                      <span class="soa-row-meta">
                        <code class="soa-code">{{
                          row.collection_spec_id
                        }}</code>
                        <span class="numeric">
                          {{ formatRowFrequency(row) }}
                        </span>
                        <span v-if="row.repeating" class="soa-badge-repeating">
                          <Repeat :size="11" aria-hidden="true" />
                          Repeating
                        </span>
                      </span>
                    </button>

                    <div class="soa-row-chips">
                      <NodeRefChip
                        v-if="
                          row.protocol_element &&
                          parseNodeRef(row.protocol_element)
                        "
                        :node-ref="row.protocol_element"
                        :fallback-title="row.name"
                      />
                      <NodeRefChip
                        v-if="
                          row.form_version_snapshot &&
                          parseNodeRef(row.form_version_snapshot)
                        "
                        :node-ref="row.form_version_snapshot"
                        :fallback-title="`${row.name} form`"
                      />
                      <template
                        v-for="concept in row.concepts"
                        :key="`${row.collection_spec_id}-${concept}`"
                      >
                        <NodeRefChip
                          v-if="normalizeLookupNodeRef(concept)"
                          :node-ref="normalizeLookupNodeRef(concept)!"
                        />
                        <code v-else class="soa-concept-code">{{
                          concept
                        }}</code>
                      </template>
                    </div>
                  </div>
                </th>

                <td
                  v-for="(col, colIdx) in soa.columns"
                  :key="`${row.collection_spec_id}-${col.activity_id}`"
                  class="soa-cell"
                  :class="{
                    'soa-cell--scheduled': isScheduled(row, colIdx),
                  }"
                >
                  <button
                    :id="`soa-cell-${row.collection_spec_id}-${col.activity_id}`"
                    type="button"
                    class="soa-cell-btn"
                    :class="{ 'is-scheduled': isScheduled(row, colIdx) }"
                    :aria-label="
                      cellAriaLabel(row, col, isScheduled(row, colIdx))
                    "
                    @click="onCellClick(row, col)"
                  >
                    <Check
                      v-if="isScheduled(row, colIdx)"
                      :size="16"
                      aria-hidden="true"
                    />
                    <Minus
                      v-else
                      :size="14"
                      class="soa-cell-dash"
                      aria-hidden="true"
                    />
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <ScheduleOfActivitiesMobileCards
          :soa="soa"
          @column-click="onColumnClick"
          @row-click="onRowClick"
          @cell-click="onCellClick"
        />
      </template>

      <footer
        v-if="soa.footnotes?.length"
        id="soa-footnotes"
        class="soa-footnotes"
        aria-labelledby="soa-footnotes-heading"
      >
        <h3 id="soa-footnotes-heading" class="soa-footnotes-title">
          Protocol footnotes
        </h3>
        <ol id="soa-footnotes-list" class="soa-footnotes-list">
          <li
            v-for="fn in soa.footnotes"
            :key="fn.number"
            class="soa-footnote-item"
          >
            <span class="soa-footnote-num numeric">[{{ fn.number }}]</span>
            <span v-if="fn.kind" class="soa-footnote-kind">{{ fn.kind }}</span>
            <span>{{ fn.text }}</span>
          </li>
        </ol>
      </footer>
    </template>
  </section>
</template>

<script setup lang="ts">
import { CalendarClock, Check, MapPin, Minus, Repeat } from "lucide-vue-next";
import { parseNodeRef } from "../../canvas/nodeRef";
import { normalizeLookupNodeRef } from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";
import ScheduleOfActivitiesMobileCards from "./ScheduleOfActivitiesMobileCards.vue";
import SupportDetails from "./SupportDetails.vue";
import { useScheduleOfActivities } from "./useScheduleOfActivities";

const props = defineProps<{
  studyCode: string;
  selectedRef: string;
}>();

const emit = defineEmits<{
  selectNode: [nodeRef: string];
}>();

const {
  loading,
  error,
  soaLoading,
  soaError,
  soa,
  visibleVersions,
  activeVersionRef,
  activeVersion,
  parentVersionNodeRef,
  parentVersionLabel,
  epochSpans,
  statusLine,
  helpers,
} = useScheduleOfActivities(props, (nodeRef) => emit("selectNode", nodeRef));

const {
  isScheduled,
  formatVisitTiming,
  formatRowFrequency,
  cellAriaLabel,
  onVersionChange,
  onColumnClick,
  onRowClick,
  onCellClick,
} = helpers;
</script>

<style scoped src="./scheduleOfActivitiesSection.css"></style>
