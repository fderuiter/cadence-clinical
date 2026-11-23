<template>
  <component
    :is="inAppShell ? 'div' : 'main'"
    id="canvas-workspace"
    class="open-study"
    :role="inAppShell ? 'region' : undefined"
    :aria-label="inAppShell ? 'Open a study' : undefined"
    tabindex="-1"
  >
    <h1 id="canvas-study-title" tabindex="-1">Open a study</h1>
    <form
      id="open-study-form"
      class="open-study-form"
      @submit.prevent="openTypedStudy"
    >
      <label for="open-study-code">Study code</label>
      <input
        id="open-study-code"
        v-model="typedStudy"
        type="text"
        autocomplete="off"
        maxlength="255"
        placeholder="e.g. CADENCE-101"
      />
      <button
        id="btn-open-study"
        type="submit"
        class="btn btn-primary"
        :disabled="!typedStudy.trim()"
      >
        Open
      </button>
    </form>
    <p v-if="!hasStudyDirectoryService" class="open-study-note">
      Enter a study code such as CADENCE-101 to open its workspace.
    </p>

    <section
      v-if="hasStudyDirectoryService"
      id="visible-studies-section"
      class="directory-section"
      aria-labelledby="visible-studies-heading"
    >
      <div class="directory-header">
        <h2 id="visible-studies-heading">Visible studies in your tenant</h2>
        <span
          id="visible-studies-status"
          class="directory-status"
          role="status"
        >
          {{ studyDirectoryStatus }}
        </span>
      </div>

      <div
        v-if="studyDirectoryError"
        id="visible-studies-error"
        class="inline-banner inline-banner-error"
        role="alert"
      >
        {{ studyDirectoryError }}
      </div>

      <ul
        v-if="visibleStudies.length > 0"
        id="visible-studies-list"
        class="visible-studies-list"
      >
        <li
          v-for="item in visibleStudies"
          :id="`study-item-${item.code}`"
          :key="item.ref"
          class="study-directory-card"
        >
          <div class="study-card-main">
            <div class="study-card-top">
              <strong class="study-card-code">{{ item.code }}</strong>
              <span class="study-phase-badge">{{ item.phase }}</span>
            </div>
            <div class="study-card-title">
              {{ item.title }}
            </div>
            <div class="study-card-sponsor">
              Sponsor: {{ item.sponsor_name }} ({{ item.sponsor_code }}) ·
              {{
                item.site_count === 1
                  ? "1 linked site"
                  : `${item.site_count ?? 0} linked sites`
              }}
            </div>
          </div>
          <div class="study-card-actions">
            <button
              :id="`btn-open-study-${item.code}`"
              type="button"
              class="btn btn-primary"
              @click="openStudyByCode(item.code)"
            >
              Open {{ item.code }}
            </button>
            <button
              :id="`btn-design-study-${item.code}`"
              type="button"
              class="btn btn-secondary"
              @click="openStudyDesign(item.code)"
            >
              Protocol drafts
            </button>
          </div>
        </li>
      </ul>
    </section>

    <section
      v-if="hasCreateStudyService"
      id="create-study-section"
      class="directory-section"
      aria-labelledby="create-study-heading"
    >
      <h2 id="create-study-heading">Create a new study</h2>
      <p class="open-study-note">
        Register a study with its unique code, title, clinical phase, sponsor,
        and signed reason for change.
      </p>

      <div
        v-if="creatingStudy"
        id="create-study-progress"
        class="inline-banner inline-banner-progress"
        role="status"
      >
        {{
          creatingStudyStep === "import"
            ? "Importing protocol draft and validating forms and items…"
            : "Registering study in your tenant…"
        }}
      </div>

      <div
        v-if="createStudySuccess"
        id="create-study-success"
        class="inline-banner inline-banner-success"
        role="status"
        tabindex="-1"
      >
        <span>{{ createStudySuccess }}</span>
        <button
          v-if="lastCreatedStudyCode"
          id="btn-open-created-study"
          type="button"
          class="btn btn-secondary"
          @click="openStudyDesign(lastCreatedStudyCode)"
        >
          Open {{ lastCreatedStudyCode }}
        </button>
      </div>

      <div
        v-if="createStudyError"
        id="create-study-error"
        class="inline-banner inline-banner-error"
        role="alert"
      >
        {{ createStudyError }}
      </div>

      <form
        id="create-study-form"
        class="create-study-form"
        @submit.prevent="submitCreateStudy"
      >
        <div class="create-study-grid">
          <div class="create-field">
            <label for="create-study-code">Study code</label>
            <input
              id="create-study-code"
              v-model="newStudyCode"
              type="text"
              autocomplete="off"
              maxlength="64"
              placeholder="e.g. CADENCE-101"
              :disabled="creatingStudy"
            />
          </div>

          <div class="create-field">
            <label for="create-study-phase">Clinical phase</label>
            <select
              id="create-study-phase"
              v-model="newStudyPhase"
              :disabled="creatingStudy"
            >
              <option value="Phase 1">Phase 1</option>
              <option value="Phase 1/2">Phase 1/2</option>
              <option value="Phase 2">Phase 2</option>
              <option value="Phase 2/3">Phase 2/3</option>
              <option value="Phase 3">Phase 3</option>
              <option value="Phase 4">Phase 4</option>
              <option value="Observational">Observational</option>
            </select>
          </div>

          <div class="create-field create-field-wide">
            <label for="create-study-title">Study title</label>
            <input
              id="create-study-title"
              v-model="newStudyTitle"
              type="text"
              autocomplete="off"
              maxlength="500"
              placeholder="e.g. Multicenter Cardiovascular Outcomes Study"
              :disabled="creatingStudy"
            />
          </div>

          <div class="create-field create-field-wide">
            <label for="create-study-sponsor">Sponsor</label>
            <select
              id="create-study-sponsor"
              v-model="selectedSponsorChoice"
              :disabled="creatingStudy"
            >
              <option
                v-for="org in sponsorOrganizations"
                :key="org.ref"
                :value="org.ref"
              >
                {{ org.name }} ({{ org.code }})
              </option>
              <option value="__new__">
                Register new sponsor organization…
              </option>
            </select>
          </div>

          <template v-if="selectedSponsorChoice === '__new__'">
            <div class="create-field">
              <label for="create-study-sponsor-name">New sponsor name</label>
              <input
                id="create-study-sponsor-name"
                v-model="newSponsorName"
                type="text"
                autocomplete="off"
                maxlength="200"
                placeholder="e.g. Cadence Pharma"
                :disabled="creatingStudy"
              />
            </div>
            <div class="create-field">
              <label for="create-study-sponsor-code">
                Sponsor code (optional)
              </label>
              <input
                id="create-study-sponsor-code"
                v-model="newSponsorCode"
                type="text"
                autocomplete="off"
                maxlength="64"
                placeholder="e.g. CADENCE-PHARMA"
                :disabled="creatingStudy"
              />
            </div>
          </template>

          <div class="create-field create-field-wide">
            <label for="create-study-file">
              Initial protocol draft file (optional — ODM v1.3.2 XML or USDM v4
              JSON, up to 10 MB)
            </label>
            <input
              id="create-study-file"
              ref="createStudyFileInputRef"
              type="file"
              accept=".xml,.json,application/xml,text/xml,application/json"
              class="sr-only"
              :disabled="creatingStudy"
              @change="onCreateStudyFileSelected"
            />
            <div class="create-file-row">
              <button
                id="create-study-choose-file"
                type="button"
                class="btn btn-secondary"
                :disabled="creatingStudy"
                @click="openCreateStudyFilePicker"
              >
                Choose ODM XML or USDM JSON file
              </button>
              <button
                v-if="newStudyImportFile"
                id="create-study-clear-file"
                type="button"
                class="btn btn-secondary"
                :disabled="creatingStudy"
                @click="onClearCreateStudyFileClick"
              >
                Remove file
              </button>
              <span
                v-if="newStudyImportFile"
                id="create-study-selected-file"
                class="create-file-note"
              >
                {{ newStudyImportFile.name }}
              </span>
            </div>
          </div>

          <div
            v-if="defaultStarterSite"
            class="create-field create-field-wide create-checkbox-field"
          >
            <input
              id="create-study-link-sandbox-site"
              v-model="linkSandboxSiteOnCreate"
              type="checkbox"
              :disabled="creatingStudy"
            />
            <label for="create-study-link-sandbox-site">
              Link default workspace site
              <strong>{{ defaultStarterSite.code }}</strong> ({{
                defaultStarterSite.name
              }}, {{ defaultStarterSite.country }}) to this study
            </label>
          </div>

          <div class="create-field create-field-wide">
            <label for="create-study-reason">
              Reason for change (required)
            </label>
            <input
              id="create-study-reason"
              v-model="newStudyReason"
              type="text"
              autocomplete="off"
              maxlength="500"
              placeholder="e.g. Register new clinical study protocol workspace"
              :disabled="creatingStudy"
            />
          </div>
        </div>

        <button
          id="btn-create-study"
          type="submit"
          class="btn btn-primary"
          :disabled="!canCreateStudy"
        >
          {{ createStudyButtonLabel }}
        </button>
      </form>
    </section>
  </component>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { handoffFocus } from "ui";
import {
  describeKernelError,
  kernelApi,
  type KernelOrganizationSummary,
  type KernelSiteSummary,
  type KernelStudySummary,
} from "../../api/kernelApi";
import { useAuthStore } from "../../stores/auth";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";

const props = withDefaults(
  defineProps<{
    inAppShell?: boolean;
    activeStageId?: string;
    activeSectionId?: string;
  }>(),
  {
    inAppShell: false,
    activeStageId: undefined,
    activeSectionId: undefined,
  }
);

const emit = defineEmits<{
  (
    e: "open-study",
    payload: {
      studyCode: string;
      stage?: string;
      section?: string;
      node?: string;
      replace?: boolean;
    }
  ): void;
}>();

const route = useRoute();
const authStore = useAuthStore();

const typedStudy = ref("");

const hasStudyDirectoryService = computed(
  () => typeof kernelApi.listStudies === "function"
);
const hasCreateStudyService = computed(
  () => typeof kernelApi.createStudy === "function"
);

const visibleStudies = ref<KernelStudySummary[]>([]);
const studyDirectoryLoading = ref(false);
const studyDirectoryError = ref("");
const sponsorOrganizations = ref<KernelOrganizationSummary[]>([]);
const workspaceSites = ref<KernelSiteSummary[]>([]);
const linkSandboxSiteOnCreate = ref(true);

const defaultStarterSite = computed<KernelSiteSummary | null>(() => {
  if (workspaceSites.value.length === 0) return null;
  const assigned: string[] = authStore.assignedSiteIds ?? [];
  if (assigned.length > 0) {
    const matched = workspaceSites.value.find((site) =>
      assigned.some((code) => code.toLowerCase() === site.code.toLowerCase())
    );
    if (matched) return matched;
  }
  return workspaceSites.value[0];
});

const newStudyCode = ref("");
const newStudyTitle = ref("");
const newStudyPhase = ref("Phase 2");
const selectedSponsorChoice = ref("__new__");
const newSponsorName = ref("");
const newSponsorCode = ref("");
const newStudyReason = ref("");
const createStudyFileInputRef = ref<HTMLInputElement | null>(null);
const newStudyImportFile = ref<File | null>(null);
const createIdempotencyKey = ref("");
const createImportIdempotencyKey = ref("");
const pendingCreatedStudy = ref<KernelStudySummary | null>(null);
const creatingStudy = ref(false);
const creatingStudyStep = ref<"study" | "import">("study");
const createStudyError = ref("");
const createStudySuccess = ref("");
const lastCreatedStudyCode = ref("");
const MAX_CREATE_IMPORT_BYTES = 10 * 1024 * 1024;

const createStudyButtonLabel = computed(() => {
  if (creatingStudy.value) {
    return newStudyImportFile.value
      ? "Creating study & importing draft…"
      : "Creating study…";
  }
  return newStudyImportFile.value
    ? "Create study & import draft"
    : "Create study";
});

const studyDirectoryStatus = computed(() => {
  if (studyDirectoryLoading.value) return "Loading visible studies…";
  if (studyDirectoryError.value) return "Could not load study directory.";
  const count = visibleStudies.value.length;
  if (count === 0) return "No visible studies in your tenant yet.";
  return count === 1 ? "1 visible study" : `${count} visible studies`;
});

const canCreateStudy = computed(() => {
  if (creatingStudy.value) return false;
  if (!newStudyCode.value.trim() || !newStudyTitle.value.trim()) return false;
  if (!newStudyPhase.value.trim() || !newStudyReason.value.trim()) return false;
  if (selectedSponsorChoice.value === "__new__") {
    return Boolean(newSponsorName.value.trim());
  }
  return Boolean(selectedSponsorChoice.value);
});

function nextStudyIdempotencyKey(): string {
  if (isDemoBuild()) {
    return `study-${demoRandomToken(12, 36, "study-create.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `study-${demoRandomToken(12, 36, "study-create.idempotency")}`;
}

watch(
  [
    newStudyCode,
    newStudyTitle,
    newStudyPhase,
    selectedSponsorChoice,
    newSponsorName,
    newSponsorCode,
  ],
  () => {
    createIdempotencyKey.value = "";
    pendingCreatedStudy.value = null;
  }
);

watch(newStudyReason, () => {
  createIdempotencyKey.value = "";
  createImportIdempotencyKey.value = "";
});

function openCreateStudyFilePicker() {
  createStudyError.value = "";
  createStudyFileInputRef.value?.click();
}

function clearCreateStudyFile() {
  newStudyImportFile.value = null;
  createImportIdempotencyKey.value = "";
  if (createStudyFileInputRef.value) {
    createStudyFileInputRef.value.value = "";
  }
}

function onClearCreateStudyFileClick() {
  clearCreateStudyFile();
  void handoffFocus("#create-study-choose-file");
}

function onCreateStudyFileSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const picked = input?.files?.[0] ?? null;
  createStudyError.value = "";
  createImportIdempotencyKey.value = "";
  if (!picked) {
    newStudyImportFile.value = null;
    return;
  }
  if (picked.size > MAX_CREATE_IMPORT_BYTES) {
    newStudyImportFile.value = null;
    if (input) input.value = "";
    createStudyError.value = `File '${picked.name}' exceeds the 10 MB synchronous upload limit.`;
    return;
  }
  newStudyImportFile.value = picked;
}

async function loadStudyDirectory() {
  if (typeof kernelApi.listStudies !== "function") return;
  studyDirectoryLoading.value = true;
  studyDirectoryError.value = "";
  try {
    visibleStudies.value = await kernelApi.listStudies();
    if (
      (route.query.section === "locks" || route.query.section === "exports") &&
      visibleStudies.value.length > 0
    ) {
      emit("open-study", {
        studyCode: visibleStudies.value[0].code,
        stage: props.activeStageId,
        section: props.activeSectionId,
        replace: true,
      });
      return;
    }
  } catch (err) {
    studyDirectoryError.value = describeKernelError(err);
  } finally {
    studyDirectoryLoading.value = false;
  }

  if (typeof kernelApi.listOrganizations === "function") {
    try {
      const orgs = await kernelApi.listOrganizations("Sponsor");
      const sponsorsOnly = Array.isArray(orgs)
        ? orgs.filter((o) => !o.kind || o.kind === "Sponsor")
        : [];
      sponsorOrganizations.value = sponsorsOnly;
      if (
        sponsorsOnly.length > 0 &&
        selectedSponsorChoice.value === "__new__"
      ) {
        selectedSponsorChoice.value = sponsorsOnly[0].ref;
      }
    } catch {
      // Custom sponsor entry remains available if organization listing fails.
    }
  }

  if (typeof kernelApi.listSites === "function") {
    try {
      const sites = await kernelApi.listSites();
      workspaceSites.value = Array.isArray(sites) ? sites : [];
    } catch {
      workspaceSites.value = [];
    }
  }
}

function openTypedStudy() {
  const code = typedStudy.value.trim();
  if (code) {
    emit("open-study", { studyCode: code });
  }
}

function openStudyByCode(code: string) {
  const clean = code.trim();
  if (clean) {
    emit("open-study", {
      studyCode: clean,
      stage: props.activeStageId,
      section: props.activeSectionId,
    });
  }
}

function openStudyDesign(code: string) {
  const clean = code.trim();
  if (clean) {
    emit("open-study", {
      studyCode: clean,
      stage: "design",
      section: "protocol-versions",
    });
  }
}

async function submitCreateStudy() {
  if (typeof kernelApi.createStudy !== "function") return;
  createStudyError.value = "";
  createStudySuccess.value = "";

  if (!createIdempotencyKey.value) {
    createIdempotencyKey.value = nextStudyIdempotencyKey();
  }

  creatingStudy.value = true;
  creatingStudyStep.value = "study";
  try {
    const requestedCode = newStudyCode.value.trim();
    let created =
      pendingCreatedStudy.value &&
      pendingCreatedStudy.value.code.toUpperCase() ===
        requestedCode.toUpperCase()
        ? pendingCreatedStudy.value
        : null;

    if (!created) {
      const isNewSponsor = selectedSponsorChoice.value === "__new__";
      const starterSite =
        linkSandboxSiteOnCreate.value && defaultStarterSite.value
          ? defaultStarterSite.value
          : null;
      created = await kernelApi.createStudy({
        code: requestedCode,
        title: newStudyTitle.value.trim(),
        phase: newStudyPhase.value.trim(),
        sponsorRef: isNewSponsor ? null : selectedSponsorChoice.value,
        sponsorName: isNewSponsor ? newSponsorName.value.trim() : null,
        sponsorCode:
          isNewSponsor && newSponsorCode.value.trim()
            ? newSponsorCode.value.trim()
            : null,
        linkSandboxSite: Boolean(starterSite),
        initialSiteRef: starterSite?.ref ?? null,
        reason: newStudyReason.value.trim(),
        idempotencyKey: createIdempotencyKey.value,
      });
      pendingCreatedStudy.value = created;

      try {
        await authStore.refresh(-1);
      } catch {
        // Non-OIDC test sessions continue with the existing in-memory session.
      }
      const createdCode = created?.code;
      if (
        createdCode &&
        !authStore.studyIds.some(
          (code: string) => code.toUpperCase() === createdCode.toUpperCase()
        )
      ) {
        authStore.studyIds = [...authStore.studyIds, createdCode];
      }
      if (
        createdCode &&
        Array.isArray(authStore.assignedStudies) &&
        !authStore.assignedStudies.some(
          (code: string) => code.toUpperCase() === createdCode.toUpperCase()
        )
      ) {
        authStore.assignedStudies = [...authStore.assignedStudies, createdCode];
      }
      const starterSiteCode = starterSite?.code;
      if (
        starterSiteCode &&
        !authStore.siteIds.some(
          (code: string) => code.toUpperCase() === starterSiteCode.toUpperCase()
        )
      ) {
        authStore.siteIds = [...authStore.siteIds, starterSiteCode];
      }
      if (
        starterSiteCode &&
        Array.isArray(authStore.assignedSites) &&
        !authStore.assignedSites.some(
          (code: string) => code.toUpperCase() === starterSiteCode.toUpperCase()
        )
      ) {
        authStore.assignedSites = [...authStore.assignedSites, starterSiteCode];
      }

      const existingIdx = visibleStudies.value.findIndex(
        (s) => s.code === created?.code
      );
      if (existingIdx >= 0) {
        visibleStudies.value.splice(existingIdx, 1, created);
      } else {
        visibleStudies.value = [created, ...visibleStudies.value];
      }
    }

    if (
      newStudyImportFile.value &&
      typeof kernelApi.importProtocolDraft === "function"
    ) {
      creatingStudyStep.value = "import";
      if (!createImportIdempotencyKey.value) {
        createImportIdempotencyKey.value = nextStudyIdempotencyKey();
      }
      const imported = await kernelApi.importProtocolDraft({
        studyRef: created.ref || created.code,
        file: newStudyImportFile.value,
        reason: newStudyReason.value.trim(),
        idempotencyKey: createImportIdempotencyKey.value,
      });

      newStudyCode.value = "";
      newStudyTitle.value = "";
      newSponsorName.value = "";
      newSponsorCode.value = "";
      newStudyReason.value = "";
      clearCreateStudyFile();
      createIdempotencyKey.value = "";
      pendingCreatedStudy.value = null;

      emit("open-study", {
        studyCode: created.code,
        stage: "design",
        section: "protocol-versions",
        node: imported.version?.ref || undefined,
      });
      return;
    }

    lastCreatedStudyCode.value = created.code;
    createStudySuccess.value = `Study ${created.code} (${created.title}) registered and ready in your visible study list.`;
    newStudyCode.value = "";
    newStudyTitle.value = "";
    newSponsorName.value = "";
    newSponsorCode.value = "";
    newStudyReason.value = "";
    clearCreateStudyFile();
    createIdempotencyKey.value = "";
    pendingCreatedStudy.value = null;

    await loadStudyDirectory();
    void handoffFocus([
      "#btn-open-created-study",
      "#create-study-success",
      "#create-study-code",
    ]);
  } catch (err) {
    createStudyError.value = describeKernelError(err);
  } finally {
    creatingStudy.value = false;
    creatingStudyStep.value = "study";
  }
}

watch(
  () => route.query.section,
  (sectionId) => {
    if (
      (sectionId === "locks" || sectionId === "exports") &&
      visibleStudies.value.length > 0
    ) {
      emit("open-study", {
        studyCode: visibleStudies.value[0].code,
        stage: props.activeStageId,
        section: props.activeSectionId,
        replace: true,
      });
    }
  }
);

onMounted(() => {
  void loadStudyDirectory();
  if (typeof window !== "undefined") {
    window.addEventListener("cadence:demo-reset", loadStudyDirectory);
  }
});

onUnmounted(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("cadence:demo-reset", loadStudyDirectory);
  }
});
</script>

<style scoped>
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

.open-study {
  max-width: 54rem;
  padding: 32px 16px;
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.open-study h1 {
  margin: 0;
}

.open-study-note {
  color: var(--neutral-dark);
}

.open-study-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.open-study-form label {
  font-weight: 600;
}

.open-study-form input {
  flex: 1 1 12rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
}

.directory-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.directory-section h2 {
  margin: 0;
  font-size: 1.05rem;
}

.directory-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
}

.directory-status {
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.visible-studies-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.study-directory-card {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.study-card-main {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.study-card-top {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.study-card-code {
  font-size: 0.98rem;
}

.study-phase-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  font-size: 0.75rem;
  font-weight: 600;
}

.study-card-title {
  font-size: 0.9rem;
}

.study-card-sponsor {
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.study-card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.inline-banner {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.inline-banner-progress {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.inline-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.inline-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.create-study-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: flex-start;
}

.create-study-grid {
  width: 100%;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
  gap: 12px;
}

.create-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.create-field-wide {
  grid-column: 1 / -1;
}

.create-field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.create-field input:not(.sr-only),
.create-field select {
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font: inherit;
  font-size: 0.9rem;
  width: 100%;
  box-sizing: border-box;
}

.create-file-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.create-file-note {
  font-size: 0.85rem;
  color: var(--neutral-dark);
}
</style>
