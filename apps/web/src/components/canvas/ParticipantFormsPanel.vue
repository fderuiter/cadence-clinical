<template>
  <section
    id="record-forms"
    class="forms-panel"
    aria-labelledby="record-forms-title"
    :aria-busy="loading"
  >
    <div class="forms-head">
      <h3 id="record-forms-title">Forms</h3>
      <router-link
        v-if="enrollmentId && newFormLocation"
        id="record-open-form"
        class="btn btn-secondary forms-open-btn"
        :to="newFormLocation"
      >
        Open new form
      </router-link>
    </div>

    <p v-if="!enrollmentId" id="record-forms-no-enrollment" class="status-line">
      There is no enrollment for {{ subjectCode }} visible to your account, so
      forms can't be listed or opened here.
    </p>
    <p v-else-if="loading" class="status-line" role="status">
      Loading {{ subjectCode }}'s forms…
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
          @click="loadForms"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="loadError" />
    </div>
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
          <router-link
            :id="`record-form-link-${summary.form_id}`"
            class="forms-link"
            :to="formLocation(summary.form_id)"
          >
            {{ summary.collection_title || summary.collection_spec_id
            }}<template v-if="summary.planned_activity_id">
              · {{ summary.planned_activity_id }}</template
            >
          </router-link>
          <span
            class="forms-status"
            :data-complete="summary.complete ? 'true' : 'false'"
          >
            {{
              summary.complete
                ? `Complete (${summary.recorded_count}/${summary.item_count})`
                : `Incomplete (${summary.recorded_count}/${summary.item_count} recorded)`
            }}
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
import { ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { ecrfFormsApi, type EcrfFormSummary } from "../../api/ecrfFormsApi";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import SupportDetails from "./SupportDetails.vue";

/**
 * Lists the case report forms opened for a participant's enrollment and links each form directly inside Clinical Canvas.
 */
const props = defineProps<{
  subjectCode: string;
  enrollmentId: string | null;
  formLocation: (formId: string) => RouteLocationRaw;
  newFormLocation?: RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "forms-loaded", forms: EcrfFormSummary[]): void;
}>();

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

async function loadForms() {
  const enrollmentId = props.enrollmentId;
  if (!enrollmentId) {
    forms.value = [];
    loading.value = false;
    loadError.value = "";
    emit("forms-loaded", []);
    return;
  }
  if (typeof ecrfFormsApi?.listForEnrollment !== "function") {
    forms.value = [];
    emit("forms-loaded", []);
    return;
  }
  const seq = ++requestSeq;
  loading.value = true;
  loadError.value = "";
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
    }
  } finally {
    if (seq === requestSeq) loading.value = false;
  }
}

watch(
  () => props.enrollmentId,
  () => {
    void loadForms();
  },
  { immediate: true }
);
</script>

<style scoped>
.forms-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
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
