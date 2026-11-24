<template>
  <section
    id="sites-section"
    class="sites-section"
    aria-labelledby="sites-section-title"
  >
    <header class="sites-header">
      <div>
        <h2 id="sites-section-title" tabindex="-1">Sites</h2>
        <p id="sites-status-line" class="sites-status" role="status">
          {{ statusLine }}
        </p>
      </div>
      <button
        id="btn-refresh-sites"
        type="button"
        class="btn btn-secondary"
        :disabled="loading"
        @click="loadAll"
      >
        {{ loading ? "Refreshing…" : "Refresh" }}
      </button>
    </header>

    <div
      v-if="loadError"
      id="sites-load-error"
      class="inline-banner inline-banner-error"
      role="alert"
    >
      {{ loadError }}
    </div>

    <div
      v-if="feedbackBanner"
      id="sites-feedback-banner"
      class="inline-banner inline-banner-success"
      role="status"
    >
      {{ feedbackBanner }}
    </div>

    <div v-if="studySites.length > 0" class="table-wrap">
      <table id="study-sites-table" class="sites-table">
        <thead>
          <tr>
            <th scope="col">Site</th>
            <th scope="col">Name</th>
            <th scope="col">Country</th>
            <th scope="col">Institution</th>
            <th scope="col">Status</th>
            <th scope="col">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="entry in studySites"
            :id="entry.site_code ? `study-site-${entry.site_code}` : undefined"
            :key="entry.ref || entry.site_code || 'masked'"
            :class="{
              selected:
                Boolean(siteId) &&
                Boolean(entry.site_code) &&
                entry.site_code === siteId,
            }"
          >
            <template v-if="entry.mask">
              <td colspan="6" class="masked-cell">
                Masked study site ({{ entry.mask }})
              </td>
            </template>
            <template v-else>
              <td class="code-cell">
                <strong>{{ entry.site_code }}</strong>
              </td>
              <td>{{ entry.site_name }}</td>
              <td>{{ entry.country }}</td>
              <td>
                <span v-if="entry.institution_name">
                  {{ entry.institution_name }}
                  <span v-if="entry.institution_code" class="muted-code">
                    ({{ entry.institution_code }})
                  </span>
                </span>
                <span v-else class="muted-code">—</span>
              </td>
              <td>
                <span class="status-pill">{{
                  entry.status || "Selected"
                }}</span>
              </td>
              <td class="actions-cell">
                <button
                  v-if="entry.site_code"
                  :id="`btn-site-participants-${entry.site_code}`"
                  type="button"
                  class="btn btn-secondary btn-compact"
                  @click="emit('openParticipants', entry.site_code)"
                >
                  Participants
                </button>
                <button
                  v-if="entry.site_code"
                  :id="`btn-site-activation-${entry.site_code}`"
                  type="button"
                  class="btn btn-secondary btn-compact"
                  @click="emit('openActivation', entry.site_code)"
                >
                  Site activation
                </button>
              </td>
            </template>
          </tr>
        </tbody>
      </table>
    </div>

    <section
      v-if="unlinkedSites.length > 0"
      id="link-existing-site-section"
      class="panel-card"
      aria-labelledby="link-existing-site-heading"
    >
      <h3 id="link-existing-site-heading">
        Link an existing workspace site to {{ studyCode }}
      </h3>
      <p class="panel-hint">
        Select a clinical site already registered in your workspace and record
        why it is joining {{ studyCode }}.
      </p>

      <div
        v-if="linkError"
        id="link-existing-site-error"
        class="inline-banner inline-banner-error"
        role="alert"
      >
        {{ linkError }}
      </div>

      <form
        id="link-existing-site-form"
        class="site-form"
        @submit.prevent="submitLinkExisting"
      >
        <div class="form-grid">
          <div class="field">
            <label for="link-site-picker">Workspace site</label>
            <select
              id="link-site-picker"
              v-model="selectedExistingSiteRef"
              :disabled="linking"
            >
              <option
                v-for="site in unlinkedSites"
                :key="site.ref"
                :value="site.ref"
              >
                {{ site.code }} — {{ site.name }} ({{ site.country }})
              </option>
            </select>
          </div>
          <div class="field">
            <label for="link-site-reason">Reason for change (required)</label>
            <input
              id="link-site-reason"
              v-model="linkReason"
              type="text"
              maxlength="500"
              autocomplete="off"
              placeholder="e.g. Select site for clinical study conduct"
              :disabled="linking"
              required
            />
          </div>
        </div>
        <button
          id="btn-link-existing-site"
          type="submit"
          class="btn btn-primary"
          :disabled="linking || !selectedExistingSiteRef || !linkReason.trim()"
        >
          {{ linking ? "Linking site…" : `Link site to ${studyCode}` }}
        </button>
      </form>
    </section>

    <section
      id="register-site-section"
      class="panel-card"
      aria-labelledby="register-site-heading"
    >
      <h3 id="register-site-heading">Register a clinical site</h3>
      <p class="panel-hint">
        Registers a clinical site under a Site Institution organization in your
        tenant and optionally links it directly to {{ studyCode }}.
      </p>

      <div
        v-if="registerError"
        id="register-site-error"
        class="inline-banner inline-banner-error"
        role="alert"
      >
        {{ registerError }}
      </div>

      <form
        id="register-site-form"
        class="site-form"
        @submit.prevent="submitRegisterSite"
      >
        <div class="form-grid">
          <div class="field">
            <label for="register-site-code">Site code</label>
            <input
              id="register-site-code"
              v-model="newSiteCode"
              type="text"
              maxlength="64"
              autocomplete="off"
              placeholder="e.g. SITE-01"
              :disabled="registering"
              required
            />
          </div>

          <div class="field">
            <label for="register-site-country">
              Country (2-letter ISO 3166-1)
            </label>
            <input
              id="register-site-country"
              v-model="newSiteCountry"
              type="text"
              maxlength="2"
              autocomplete="off"
              placeholder="US"
              :disabled="registering"
              required
            />
          </div>

          <div class="field field-wide">
            <label for="register-site-name">Site name</label>
            <input
              id="register-site-name"
              v-model="newSiteName"
              type="text"
              maxlength="200"
              autocomplete="off"
              placeholder="e.g. Metro Cardiology Research Center"
              :disabled="registering"
              required
            />
          </div>

          <div class="field field-wide">
            <label for="register-site-institution-choice">
              Site institution
            </label>
            <select
              id="register-site-institution-choice"
              v-model="selectedInstitutionChoice"
              :disabled="registering"
            >
              <option
                v-for="org in siteInstitutions"
                :key="org.ref"
                :value="org.ref"
              >
                {{ org.name }} ({{ org.code }})
              </option>
              <option value="__new__">Register new site institution…</option>
            </select>
          </div>

          <template v-if="selectedInstitutionChoice === '__new__'">
            <div class="field">
              <label for="register-site-institution-name">
                Institution name
              </label>
              <input
                id="register-site-institution-name"
                v-model="newInstitutionName"
                type="text"
                maxlength="200"
                autocomplete="off"
                placeholder="e.g. Metro University Hospital"
                :disabled="registering"
                required
              />
            </div>

            <div class="field">
              <label for="register-site-institution-code">
                Institution code (optional)
              </label>
              <input
                id="register-site-institution-code"
                v-model="newInstitutionCode"
                type="text"
                maxlength="64"
                autocomplete="off"
                placeholder="e.g. METRO-HOSP"
                :disabled="registering"
              />
            </div>
          </template>

          <div class="field field-wide checkbox-field">
            <input
              id="register-site-link-immediately"
              v-model="linkImmediately"
              type="checkbox"
              :disabled="registering"
            />
            <label for="register-site-link-immediately">
              Link site to {{ studyCode }} immediately after registration
            </label>
          </div>

          <div class="field field-wide">
            <label for="register-site-reason">
              Reason for change (required)
            </label>
            <input
              id="register-site-reason"
              v-model="registerReason"
              type="text"
              maxlength="500"
              autocomplete="off"
              placeholder="e.g. Register clinical site for study startup and participant screening"
              :disabled="registering"
              required
            />
          </div>
        </div>

        <button
          id="btn-register-site"
          type="submit"
          class="btn btn-primary"
          :disabled="!canRegisterSite"
        >
          {{ registerButtonLabel }}
        </button>
      </form>
    </section>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { handoffFocus } from "ui";
import {
  describeKernelError,
  kernelApi,
  type KernelOrganizationSummary,
  type KernelSiteSummary,
  type KernelStudySiteSummary,
} from "../../api/kernelApi";
import {
  invalidateStudySitesCache,
  syncCallerSiteScope,
} from "../../canvas/kernelStudy";
import { useAuthStore } from "../../stores/auth";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";

const props = withDefaults(
  defineProps<{
    studyCode: string;
    siteId?: string;
  }>(),
  {
    siteId: "",
  }
);

const emit = defineEmits<{
  openParticipants: [siteCode: string];
  openActivation: [siteCode: string];
}>();

const loading = ref(false);
const authStore = useAuthStore();
const loadError = ref("");
const feedbackBanner = ref("");

const studySites = ref<KernelStudySiteSummary[]>([]);
const tenantSites = ref<KernelSiteSummary[]>([]);
const siteInstitutions = ref<KernelOrganizationSummary[]>([]);

const selectedExistingSiteRef = ref("");
const linkReason = ref("");
const linking = ref(false);
const linkError = ref("");
const linkExistingIdempotencyKey = ref("");

const newSiteCode = ref("");
const newSiteName = ref("");
const newSiteCountry = ref("US");
const selectedInstitutionChoice = ref("__new__");
const newInstitutionName = ref("");
const newInstitutionCode = ref("");
const linkImmediately = ref(true);
const registerReason = ref("");
const registering = ref(false);
const registerError = ref("");
const createSiteIdempotencyKey = ref("");
const linkCreatedSiteIdempotencyKey = ref("");
const pendingCreatedSite = ref<KernelSiteSummary | null>(null);

function nextIdempotencyKey(prefix: string): string {
  if (isDemoBuild()) {
    return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
}

const linkedSiteCodes = computed(() => {
  const codes = new Set<string>();
  for (const s of studySites.value) {
    if (s.site_code) {
      codes.add(s.site_code.toUpperCase());
    }
  }
  return codes;
});

const unlinkedSites = computed(() =>
  tenantSites.value.filter(
    (site) => !linkedSiteCodes.value.has(site.code.toUpperCase())
  )
);

const statusLine = computed(() => {
  if (loading.value && studySites.value.length === 0) {
    return `Loading sites for ${props.studyCode}…`;
  }
  const count = studySites.value.length;
  if (count === 0) {
    return `No clinical sites linked to ${props.studyCode} yet. Register or link a site below before screening participants at a site.`;
  }
  return count === 1
    ? `1 clinical site linked to ${props.studyCode}.`
    : `${count} clinical sites linked to ${props.studyCode}.`;
});

const canRegisterSite = computed(() => {
  if (registering.value) return false;
  if (
    !newSiteCode.value.trim() ||
    !newSiteName.value.trim() ||
    newSiteCountry.value.trim().length !== 2 ||
    !registerReason.value.trim()
  ) {
    return false;
  }
  if (selectedInstitutionChoice.value === "__new__") {
    return Boolean(newInstitutionName.value.trim());
  }
  return Boolean(selectedInstitutionChoice.value);
});

const registerButtonLabel = computed(() => {
  if (registering.value) {
    return linkImmediately.value
      ? `Registering & linking to ${props.studyCode}…`
      : "Registering site…";
  }
  return linkImmediately.value
    ? `Register & link to ${props.studyCode}`
    : "Register site";
});

watch(
  [
    newSiteCode,
    newSiteName,
    newSiteCountry,
    selectedInstitutionChoice,
    newInstitutionName,
    newInstitutionCode,
    registerReason,
  ],
  () => {
    createSiteIdempotencyKey.value = "";
    linkCreatedSiteIdempotencyKey.value = "";
    pendingCreatedSite.value = null;
  }
);

watch([selectedExistingSiteRef, linkReason], () => {
  linkExistingIdempotencyKey.value = "";
});

async function loadAll() {
  if (!props.studyCode) return;
  loading.value = true;
  loadError.value = "";
  try {
    const [linkedResult, sitesResult, orgsResult] = await Promise.allSettled([
      typeof kernelApi.listStudySites === "function"
        ? kernelApi.listStudySites(props.studyCode)
        : Promise.resolve([]),
      typeof kernelApi.listSites === "function"
        ? kernelApi.listSites()
        : Promise.resolve([]),
      typeof kernelApi.listOrganizations === "function"
        ? kernelApi.listOrganizations("SiteInstitution")
        : Promise.resolve([]),
    ]);

    if (linkedResult.status === "fulfilled") {
      studySites.value = linkedResult.value;
    } else {
      loadError.value = describeKernelError(linkedResult.reason);
    }

    if (sitesResult.status === "fulfilled") {
      tenantSites.value = sitesResult.value;
    }

    if (orgsResult.status === "fulfilled") {
      siteInstitutions.value = orgsResult.value.filter(
        (org) => !org.kind || org.kind === "SiteInstitution"
      );
      if (
        siteInstitutions.value.length > 0 &&
        selectedInstitutionChoice.value === "__new__" &&
        !newInstitutionName.value.trim()
      ) {
        selectedInstitutionChoice.value = siteInstitutions.value[0].ref;
      }
    }

    if (
      unlinkedSites.value.length > 0 &&
      !unlinkedSites.value.some((s) => s.ref === selectedExistingSiteRef.value)
    ) {
      selectedExistingSiteRef.value = unlinkedSites.value[0].ref;
    } else if (unlinkedSites.value.length === 0) {
      selectedExistingSiteRef.value = "";
    }
  } finally {
    loading.value = false;
  }
}

async function submitLinkExisting() {
  if (
    typeof kernelApi.linkSiteToStudy !== "function" ||
    !selectedExistingSiteRef.value ||
    !linkReason.value.trim()
  ) {
    return;
  }

  linkError.value = "";
  feedbackBanner.value = "";
  if (!linkExistingIdempotencyKey.value) {
    linkExistingIdempotencyKey.value = nextIdempotencyKey("study-site-link");
  }

  linking.value = true;
  let linkedSiteCode = "";
  try {
    const linked = await kernelApi.linkSiteToStudy({
      studyRef: props.studyCode,
      siteRef: selectedExistingSiteRef.value,
      reason: linkReason.value.trim(),
      idempotencyKey: linkExistingIdempotencyKey.value,
    });
    linkedSiteCode = linked.site_code || "";
    await syncCallerSiteScope(authStore, linked.site_code);
    invalidateStudySitesCache(props.studyCode);
    feedbackBanner.value = `Site ${linked.site_code || ""} linked to ${props.studyCode}.`;
    linkReason.value = "";
    linkExistingIdempotencyKey.value = "";
    await loadAll();
  } catch (err) {
    linkError.value = describeKernelError(err);
  } finally {
    linking.value = false;
  }

  if (linkedSiteCode || !linkError.value) {
    await handoffFocus(
      [
        "#link-site-picker",
        () =>
          linkedSiteCode
            ? document.getElementById(`btn-site-participants-${linkedSiteCode}`)
            : null,
        "#register-site-code",
        "#sites-section-title",
      ],
      {
        container: () => document.getElementById("sites-section"),
      }
    );
  }
}

async function submitRegisterSite() {
  if (typeof kernelApi.createSite !== "function" || !canRegisterSite.value) {
    return;
  }

  registerError.value = "";
  feedbackBanner.value = "";
  if (!createSiteIdempotencyKey.value) {
    createSiteIdempotencyKey.value = nextIdempotencyKey("site-create");
  }

  registering.value = true;
  try {
    const requestedCode = newSiteCode.value.trim();
    let created =
      pendingCreatedSite.value &&
      pendingCreatedSite.value.code.toUpperCase() ===
        requestedCode.toUpperCase()
        ? pendingCreatedSite.value
        : null;

    if (!created) {
      const isNewInstitution = selectedInstitutionChoice.value === "__new__";
      created = await kernelApi.createSite({
        code: requestedCode,
        name: newSiteName.value.trim(),
        country: newSiteCountry.value.trim().toUpperCase(),
        institutionRef: isNewInstitution
          ? null
          : selectedInstitutionChoice.value,
        institutionName: isNewInstitution
          ? newInstitutionName.value.trim()
          : null,
        institutionCode:
          isNewInstitution && newInstitutionCode.value.trim()
            ? newInstitutionCode.value.trim()
            : null,
        reason: registerReason.value.trim(),
        idempotencyKey: createSiteIdempotencyKey.value,
      });
      pendingCreatedSite.value = created;
    }

    if (
      linkImmediately.value &&
      typeof kernelApi.linkSiteToStudy === "function"
    ) {
      if (!linkCreatedSiteIdempotencyKey.value) {
        linkCreatedSiteIdempotencyKey.value =
          nextIdempotencyKey("study-site-link");
      }
      await kernelApi.linkSiteToStudy({
        studyRef: props.studyCode,
        siteRef: created.ref,
        siteCode: created.code,
        reason: registerReason.value.trim(),
        idempotencyKey: linkCreatedSiteIdempotencyKey.value,
      });
      await syncCallerSiteScope(authStore, created.code);
      invalidateStudySitesCache(props.studyCode);
      feedbackBanner.value = `Site ${created.code} (${created.name}) registered and linked to ${props.studyCode}.`;
    } else {
      await syncCallerSiteScope(authStore, created.code);
      feedbackBanner.value = `Site ${created.code} (${created.name}) registered in your workspace.`;
    }

    newSiteCode.value = "";
    newSiteName.value = "";
    newInstitutionName.value = "";
    newInstitutionCode.value = "";
    registerReason.value = "";
    createSiteIdempotencyKey.value = "";
    linkCreatedSiteIdempotencyKey.value = "";
    pendingCreatedSite.value = null;

    await loadAll();
  } catch (err) {
    registerError.value = describeKernelError(err);
  } finally {
    registering.value = false;
  }

  if (!registerError.value) {
    await handoffFocus(["#register-site-code", "#sites-section-title"], {
      container: () => document.getElementById("sites-section"),
    });
  }
}

watch(
  () => props.studyCode,
  () => {
    feedbackBanner.value = "";
    loadError.value = "";
    loadAll();
  }
);

onMounted(() => {
  loadAll();
});
</script>

<style scoped>
.sites-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.sites-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.sites-header h2 {
  margin: 0 0 4px;
  font-size: 1.25rem;
}

.sites-status {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.92rem;
}

.table-wrap {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.sites-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.sites-table th,
.sites-table td {
  padding: 10px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.sites-table th {
  font-weight: 600;
  background: var(--semantic-color-surface-muted);
}

.sites-table tr:last-child td {
  border-bottom: none;
}

.sites-table tr.selected {
  background: var(--semantic-color-surface-muted);
}

.code-cell {
  white-space: nowrap;
}

.muted-code {
  color: var(--semantic-color-text-secondary);
  font-size: 0.84rem;
}

.masked-cell {
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
}

.actions-cell {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.btn-compact {
  padding: 4px 8px;
  font-size: 0.82rem;
}

.panel-card {
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.panel-card h3 {
  margin: 0;
  font-size: 1.05rem;
}

.panel-hint {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
}

.site-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-wide {
  grid-column: 1 / -1;
}

.field label {
  font-weight: 600;
  font-size: 0.88rem;
}

.field input,
.field select {
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.checkbox-field {
  flex-direction: row;
  align-items: center;
  gap: 8px;
}

.checkbox-field input {
  padding: 0;
}

.site-form > button {
  align-self: flex-start;
}

.inline-banner {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.9rem;
}

.inline-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error);
}

.inline-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}
</style>
