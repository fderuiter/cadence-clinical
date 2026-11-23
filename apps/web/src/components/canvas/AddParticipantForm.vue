<template>
  <form
    id="add-participant-form"
    class="add-participant"
    aria-labelledby="add-participant-title"
    @submit.prevent="submit"
  >
    <h3 id="add-participant-title">Add a participant</h3>
    <p class="hint">
      The participant starts in screening. No name, birth date or other identity
      details are recorded here.
    </p>
    <label for="add-participant-code">Participant code</label>
    <input
      id="add-participant-code"
      v-model="subjectCode"
      type="text"
      maxlength="255"
      autocomplete="off"
      required
    />
    <div class="site-label-row">
      <label for="add-participant-site">
        {{
          restrictToOptions && availableSites.length > 0
            ? "Site"
            : "Site (optional)"
        }}
      </label>
      <div class="site-shortcuts">
        <button
          v-if="!restrictToOptions && availableSites.length > 0"
          id="btn-add-participant-toggle-site-mode"
          type="button"
          class="link-button"
          @click="manualSiteMode = !manualSiteMode"
        >
          {{ manualSiteMode ? "Pick linked site" : "Enter custom code" }}
        </button>
        <button
          v-if="canQuickCreateSite"
          id="btn-add-participant-new-site"
          type="button"
          class="link-button"
          @click="toggleInlineSiteForm"
        >
          {{
            showInlineSiteForm ? "Cancel new site" : "+ Register & link site"
          }}
        </button>
      </div>
    </div>

    <select
      v-if="
        (restrictToOptions && availableSites.length > 0) ||
        (availableSites.length > 0 && !manualSiteMode)
      "
      id="add-participant-site"
      v-model="siteId"
      :required="restrictToOptions && availableSites.length > 0"
    >
      <option v-if="!restrictToOptions" value="">Not assigned</option>
      <option
        v-for="site in availableSites"
        :key="site.code"
        :value="site.code"
      >
        {{ site.label }}
      </option>
      <option
        v-if="
          !restrictToOptions &&
          siteId &&
          !availableSites.some((s) => s.code === siteId)
        "
        :value="siteId"
      >
        {{ siteId }}
      </option>
    </select>
    <template v-else>
      <input
        id="add-participant-site"
        v-model="siteId"
        type="text"
        maxlength="255"
        autocomplete="off"
        :list="
          availableSites.length > 0 ? 'add-participant-site-list' : undefined
        "
      />
      <datalist v-if="availableSites.length > 0" id="add-participant-site-list">
        <option
          v-for="option in availableSites"
          :key="option.code"
          :value="option.code"
        >
          {{ option.label }}
        </option>
      </datalist>
    </template>

    <fieldset
      v-if="showInlineSiteForm"
      id="add-participant-inline-site-panel"
      class="inline-site-panel"
      :disabled="creatingSite"
    >
      <legend>Register &amp; link site to {{ canvas.studyCode }}</legend>
      <p v-if="inlineSiteError" class="inline-site-error" role="alert">
        {{ inlineSiteError }}
      </p>
      <label for="inline-site-code">Site code</label>
      <input
        id="inline-site-code"
        v-model="inlineSiteCode"
        type="text"
        maxlength="64"
        autocomplete="off"
        placeholder="e.g. SITE-01"
      />
      <label for="inline-site-name">Site name</label>
      <input
        id="inline-site-name"
        v-model="inlineSiteName"
        type="text"
        maxlength="200"
        autocomplete="off"
        placeholder="e.g. Metro Cardiology Research Center"
      />
      <label for="inline-site-country">Country (2-letter ISO)</label>
      <input
        id="inline-site-country"
        v-model="inlineSiteCountry"
        type="text"
        maxlength="2"
        autocomplete="off"
        placeholder="US"
      />
      <label for="inline-site-institution">Site institution</label>
      <input
        id="inline-site-institution"
        v-model="inlineInstitutionName"
        type="text"
        maxlength="200"
        autocomplete="off"
        placeholder="e.g. Metro University Hospital"
      />
      <label for="inline-site-reason">Reason for site registration</label>
      <input
        id="inline-site-reason"
        v-model="inlineSiteReason"
        type="text"
        maxlength="500"
        autocomplete="off"
        placeholder="e.g. Register clinical site for screening"
      />
      <div class="buttons">
        <button
          id="btn-confirm-inline-site"
          type="button"
          class="btn btn-secondary"
          :disabled="!canSubmitInlineSite"
          @click="submitInlineSite"
        >
          {{ creatingSite ? "Registering…" : "Save & select site" }}
        </button>
      </div>
    </fieldset>
    <label for="add-participant-reason">Reason</label>
    <textarea
      id="add-participant-reason"
      v-model="reason"
      rows="2"
      placeholder="Why this participant is being added (recorded in the audit trail)"
      required
    />
    <div class="buttons">
      <button
        id="btn-confirm-add-participant"
        type="submit"
        class="btn btn-primary"
        :disabled="busy || !subjectCode.trim() || !reason.trim()"
      >
        Add participant
      </button>
      <button type="button" class="btn btn-secondary" @click="emit('close')">
        Cancel
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import { describeKernelError, kernelApi } from "../../api/kernelApi";
import {
  findStudySites,
  invalidateStudySitesCache,
  type StudySiteOption,
} from "../../canvas/kernelStudy";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";

/**
 * Adds a participant to the open study through Execution, which starts it in screening and audits the reason.
 * Codes are sent as typed: Execution compares them exactly.
 */
const props = withDefaults(
  defineProps<{
    defaultSiteId: string;
    siteOptions?: ReadonlyArray<StudySiteOption>;
    restrictToOptions?: boolean;
  }>(),
  {
    siteOptions: () => [],
    restrictToOptions: false,
  }
);
const emit = defineEmits<{
  added: [participant: SubjectSummary | null];
  close: [];
}>();

const canvas = useStudyCanvasStore();
const subjectCode = ref("");
const siteId = ref(
  props.defaultSiteId ||
    (props.restrictToOptions && props.siteOptions.length > 0
      ? props.siteOptions[0].code
      : "")
);
const reason = ref("");
const busy = ref(false);

const authStore = useAuthStore();
const linkedSites = ref<StudySiteOption[]>([]);
const manualSiteMode = ref(false);
const showInlineSiteForm = ref(false);
const creatingSite = ref(false);
const inlineSiteError = ref("");
const inlineSiteCode = ref("");
const inlineSiteName = ref("");
const inlineSiteCountry = ref("US");
const inlineInstitutionName = ref("");
const inlineSiteReason = ref("");

const availableSites = computed<StudySiteOption[]>(() => {
  if (props.restrictToOptions) {
    const allowedCodes = new Set(props.siteOptions.map((s) => s.code));
    const byCode = new Map<string, StudySiteOption>();
    for (const opt of props.siteOptions) {
      byCode.set(opt.code, opt);
    }
    for (const linked of linkedSites.value) {
      if (allowedCodes.has(linked.code) && !byCode.has(linked.code)) {
        byCode.set(linked.code, linked);
      }
    }
    return Array.from(byCode.values());
  }
  const byCode = new Map<string, StudySiteOption>();
  for (const opt of props.siteOptions) {
    byCode.set(opt.code, opt);
  }
  for (const linked of linkedSites.value) {
    if (!byCode.has(linked.code)) {
      byCode.set(linked.code, linked);
    }
  }
  return Array.from(byCode.values()).sort((a, b) =>
    a.code.localeCompare(b.code)
  );
});

const canQuickCreateSite = computed(
  () =>
    Boolean(canvas.studyCode) &&
    typeof kernelApi.createSite === "function" &&
    typeof kernelApi.linkSiteToStudy === "function"
);

const canSubmitInlineSite = computed(
  () =>
    !creatingSite.value &&
    Boolean(inlineSiteCode.value.trim()) &&
    Boolean(inlineSiteName.value.trim()) &&
    inlineSiteCountry.value.trim().length === 2 &&
    Boolean(inlineInstitutionName.value.trim()) &&
    Boolean(inlineSiteReason.value.trim())
);

function nextInlineKey(prefix: string): string {
  if (isDemoBuild()) {
    return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
}

async function loadSites() {
  if (!canvas.studyCode) return;
  try {
    linkedSites.value = await findStudySites(canvas.studyCode);
  } catch {
    linkedSites.value = [];
  }
}

function toggleInlineSiteForm() {
  showInlineSiteForm.value = !showInlineSiteForm.value;
  inlineSiteError.value = "";
}

async function submitInlineSite() {
  if (!canSubmitInlineSite.value || !canvas.studyCode) return;
  inlineSiteError.value = "";
  creatingSite.value = true;
  try {
    const created = await kernelApi.createSite({
      siteCode: inlineSiteCode.value.trim(),
      name: inlineSiteName.value.trim(),
      country: inlineSiteCountry.value.trim().toUpperCase(),
      institutionName: inlineInstitutionName.value.trim(),
      reason: inlineSiteReason.value.trim(),
      idempotencyKey: nextInlineKey("inline-site"),
    });
    await kernelApi.linkSiteToStudy({
      studyRef: canvas.studyCode,
      siteRef: created.ref,
      siteCode: created.code,
      reason: inlineSiteReason.value.trim(),
      idempotencyKey: nextInlineKey("inline-study-site"),
    });
    invalidateStudySitesCache(canvas.studyCode);

    if (
      Array.isArray(authStore.siteIds) &&
      authStore.siteIds.length > 0 &&
      !authStore.siteIds.includes(created.code)
    ) {
      authStore.siteIds = [...authStore.siteIds, created.code];
    }
    if (
      Array.isArray(authStore.assignedSites) &&
      !authStore.assignedSites.includes("*") &&
      authStore.assignedSites.length > 0 &&
      !authStore.assignedSites.includes(created.code)
    ) {
      authStore.assignedSites = [...authStore.assignedSites, created.code];
    }

    const optionLabel =
      created.name && created.name !== created.code
        ? `${created.code} — ${created.name}`
        : created.code;
    const merged = new Map<string, StudySiteOption>(
      linkedSites.value.map((s) => [s.code, s])
    );
    merged.set(created.code, { code: created.code, label: optionLabel });
    linkedSites.value = Array.from(merged.values()).sort((a, b) =>
      a.code.localeCompare(b.code)
    );

    siteId.value = created.code;
    manualSiteMode.value = false;
    showInlineSiteForm.value = false;
    inlineSiteCode.value = "";
    inlineSiteName.value = "";
    inlineInstitutionName.value = "";
    inlineSiteReason.value = "";
  } catch (err) {
    inlineSiteError.value = describeKernelError(err);
  } finally {
    creatingSite.value = false;
  }
}

watch(
  () =>
    [props.defaultSiteId, props.restrictToOptions, props.siteOptions] as const,
  ([nextDefault, restricted, options]) => {
    if (!siteId.value && nextDefault) {
      siteId.value = nextDefault;
    } else if (
      restricted &&
      options.length > 0 &&
      !options.some((o) => o.code === siteId.value) &&
      !linkedSites.value.some((o) => o.code === siteId.value)
    ) {
      siteId.value = nextDefault || options[0].code;
    }
  }
);
async function submit() {
  const code = subjectCode.value.trim();
  const why = reason.value.trim();
  if (!code || !why || busy.value) return;
  busy.value = true;
  try {
    const result = await canvas.addParticipant(code, siteId.value.trim(), why);
    // When adding fails nothing was created: keep the form so the user can correct it.
    if (result.added) emit("added", result.participant);
  } finally {
    busy.value = false;
  }
}

onMounted(() => {
  loadSites();
});
</script>

<style scoped>
.add-participant {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 460px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.add-participant h3 {
  margin: 0;
  font-size: 1rem;
}

.hint {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.site-label-row {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.site-shortcuts {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.link-button {
  background: none;
  border: none;
  padding: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
  cursor: pointer;
}

.add-participant input,
.add-participant select,
.add-participant textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text, inherit);
}

.inline-site-panel {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 4px 0;
  padding: 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.inline-site-panel legend {
  font-weight: 600;
  font-size: 0.85rem;
  padding: 0 4px;
}

.inline-site-error {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-error);
}

.buttons {
  display: flex;
  gap: 6px;
}
</style>
