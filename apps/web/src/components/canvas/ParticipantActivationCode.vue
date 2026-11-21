<template>
  <section
    id="inspector-activation-code"
    class="activation-code"
    aria-labelledby="inspector-activation-code-title"
  >
    <h3 id="inspector-activation-code-title">ePRO activation code</h3>
    <p class="note">
      Issues a one-time 30-day BYOD activation code for Subject Portal sign-in.
      Only a keyed HMAC digest is stored.
    </p>
    <label for="activation-code-reason">Reason</label>
    <textarea
      id="activation-code-reason"
      v-model="reason"
      rows="2"
      placeholder="Why an activation code is issued (recorded in the audit trail)"
    />
    <button
      id="btn-issue-activation-code"
      type="button"
      class="btn btn-secondary"
      :disabled="busy || !reason.trim()"
      @click="submit"
    >
      {{ busy ? "Issuing…" : "Issue 30-day code" }}
    </button>
    <p v-if="error" id="activation-code-error" class="note" role="alert">
      {{ error }}
    </p>
    <div
      v-if="issued"
      id="activation-code-issued"
      class="issued-card"
      role="status"
    >
      <div id="activation-code-value" class="issued-code">
        {{ issued.activation_code }}
      </div>
      <p class="note">
        Valid 30 days until {{ formattedExpiry }}. Shown once for
        {{ issued.subject_id }}.
      </p>
      <button
        id="btn-copy-activation-code"
        type="button"
        class="btn btn-secondary"
        @click="copyCode"
      >
        {{ copied ? "Copied" : "Copy code" }}
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  executionApi,
  type SubjectActivationCodeIssue,
  type SubjectSummary,
} from "../../api/executionApi";

const props = defineProps<{ participant: SubjectSummary }>();

const reason = ref("");
const busy = ref(false);
const error = ref("");
const issued = ref<SubjectActivationCodeIssue | null>(null);
const copied = ref(false);

const formattedExpiry = computed(() => {
  if (!issued.value?.expires_at) return "";
  const parsed = new Date(issued.value.expires_at);
  return Number.isNaN(parsed.getTime())
    ? issued.value.expires_at
    : parsed.toISOString().slice(0, 10);
});

watch(
  () => props.participant.id,
  () => {
    reason.value = "";
    error.value = "";
    issued.value = null;
    copied.value = false;
  }
);

async function submit() {
  const why = reason.value.trim();
  if (!why || busy.value) return;
  busy.value = true;
  error.value = "";
  copied.value = false;
  try {
    issued.value = await executionApi.issueSubjectActivationCode(
      props.participant.subject_id || props.participant.id,
      why
    );
    reason.value = "";
  } catch (err) {
    error.value =
      err instanceof Error
        ? err.message
        : "Failed to issue 30-day activation code.";
  } finally {
    busy.value = false;
  }
}

async function copyCode() {
  if (!issued.value?.activation_code) return;
  try {
    if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(issued.value.activation_code);
      copied.value = true;
    }
  } catch {
    copied.value = false;
  }
}
</script>

<style scoped>
.activation-code {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
}

.activation-code h3 {
  margin: 0;
  font-size: 0.95rem;
}

.activation-code textarea {
  padding: 6px 8px;
  border: 1px solid var(--border-control);
  border-radius: 4px;
  background-color: var(--bg-card);
  color: var(--text-main);
}

.issued-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px;
  border: 1px solid var(--accent);
  border-radius: 6px;
  background: var(--accent-bg);
}

.issued-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.95rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  overflow-wrap: anywhere;
}

.note {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}
</style>
