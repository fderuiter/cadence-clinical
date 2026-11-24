<template>
  <section
    id="study-sites-panel"
    class="study-sites"
    :class="{ 'study-sites--compact': compact }"
    aria-labelledby="study-sites-title"
    :aria-busy="loading"
  >
    <header class="study-sites-header">
      <div>
        <h2 id="study-sites-title" class="study-sites-title" tabindex="-1">
          Investigational sites
        </h2>
        <p id="study-sites-status" class="study-sites-status" role="status">
          {{ statusSummary }}
        </p>
      </div>
      <router-link
        v-if="compact"
        id="btn-manage-study-sites"
        class="btn btn-secondary"
        :to="resolvedManageSitesLocation"
      >
        {{ canManageSites ? "Manage sites" : "View sites" }}
      </router-link>
    </header>

    <div
      v-if="loadError"
      id="study-sites-error"
      class="sites-banner sites-banner-error"
      role="alert"
    >
      {{ loadError }}
    </div>

    <ul
      v-if="studySites.length > 0"
      id="study-linked-sites-list"
      class="linked-sites-list"
    >
      <li
        v-for="site in studySites"
        :id="`study-site-${site.site_code}`"
        :key="site.ref"
        class="linked-site-card"
      >
        <div class="linked-site-main">
          <div class="linked-site-top">
            <strong class="linked-site-code">{{ site.site_code }}</strong>
            <span class="site-country-badge">{{ site.country }}</span>
            <span class="site-status-badge">{{ site.status }}</span>
          </div>
          <div class="linked-site-name">{{ site.site_name }}</div>
          <div class="linked-site-inst">
            Institution: {{ site.institution_name }} ({{
              site.institution_code
            }})
          </div>
        </div>
        <div class="linked-site-actions">
          <button
            :id="`btn-site-participants-${site.site_code}`"
            type="button"
            class="btn btn-secondary"
            @click="emit('open-participants', site.site_code)"
          >
            Participants
          </button>
        </div>
      </li>
    </ul>

    <template v-if="!compact && canManageSites">
      <div
        v-if="actionSuccess"
        id="study-sites-success"
        class="sites-banner sites-banner-success"
        role="status"
      >
        {{ actionSuccess }}
      </div>
      <div
        v-if="actionError"
        id="study-sites-action-error"
        class="sites-banner sites-banner-error"
        role="alert"
      >
        {{ actionError }}
      </div>

      <form
        v-if="unlinkedWorkspaceSites.length > 0"
        id="link-existing-site-form"
        class="site-form"
        aria-labelledby="link-existing-site-heading"
        @submit.prevent="submitLinkExistingSite"
      >
        <h3 id="link-existing-site-heading" class="site-form-title">
          Link a workspace site to {{ studyCode }}
        </h3>
        <div class="site-form-grid">
          <div class="site-field">
            <label for="select-workspace-site">Available site</label>
            <select
              id="select-workspace-site"
              v-model="selectedWorkspaceSiteRef"
              :disabled="submitting"
            >
              <option
                v-for="site in unlinkedWorkspaceSites"
                :key="site.ref"
                :value="site.ref"
              >
                {{ site.code }} — {{ site.name }} ({{ site.country }})
              </option>
            </select>
          </div>
          <div class="site-field">
            <label for="link-site-reason">Reason for change</label>
            <input
              id="link-site-reason"
              v-model="linkSiteReason"
              type="text"
              maxlength="500"
              autocomplete="off"
              placeholder="e.g. Link sandbox site to study for screening"
              :disabled="submitting"
              required
            />
          </div>
        </div>
        <button
          id="btn-link-study-site"
          type="submit"
          class="btn btn-primary"
          :disabled="
            submitting || !selectedWorkspaceSiteRef || !linkSiteReason.trim()
          "
        >
          Link site to {{ studyCode }}
        </button>
      </form>

      <form
        id="register-site-form"
        class="site-form"
        aria-labelledby="register-site-heading"
        @submit.prevent="submitRegisterSite"
      >
        <h3 id="register-site-heading" class="site-form-title">
          Register a new investigational site
        </h3>
        <div class="site-form-grid">
          <div class="site-field site-field-wide">
            <label for="select-site-institution">Site institution</label>
            <select
              id="select-site-institution"
              v-model="selectedInstitutionChoice"
              :disabled="submitting"
            >
              <option
                v-for="inst in institutions"
                :key="inst.ref"
                :value="inst.ref"
              >
                {{ inst.name }} ({{ inst.code }})
              </option>
              <option value="__new__">Register new site institution…</option>
            </select>
          </div>

          <template v-if="selectedInstitutionChoice === '__new__'">
            <div class="site-field">
              <label for="input-institution-name">Institution name</label>
              <input
                id="input-institution-name"
                v-model="newInstitutionName"
                type="text"
                maxlength="200"
                autocomplete="off"
                placeholder="e.g. University Medical Center Utrecht"
                :disabled="submitting"
                required
              />
            </div>
            <div class="site-field">
              <label for="input-institution-code">
                Institution code (optional)
              </label>
              <input
                id="input-institution-code"
                v-model="newInstitutionCode"
                type="text"
                maxlength="64"
                autocomplete="off"
                placeholder="e.g. INST-UMCU"
                :disabled="submitting"
              />
            </div>
          </template>

          <div class="site-field">
            <label for="input-site-code">Site code</label>
            <input
              id="input-site-code"
              v-model="newSiteCode"
              type="text"
              maxlength="64"
              autocomplete="off"
              placeholder="e.g. SITE-NL-01"
              :disabled="submitting"
              required
            />
          </div>

          <div class="site-field">
            <label for="input-site-country">ISO country code (2 letters)</label>
            <input
              id="input-site-country"
              v-model="newSiteCountry"
              type="text"
              maxlength="2"
              autocomplete="off"
              placeholder="e.g. NL"
              :disabled="submitting"
              required
            />
          </div>

          <div class="site-field site-field-wide">
            <label for="input-site-name">Investigational site name</label>
            <input
              id="input-site-name"
              v-model="newSiteName"
              type="text"
              maxlength="200"
              autocomplete="off"
              placeholder="e.g. Utrecht Cardiology Research Unit"
              :disabled="submitting"
              required
            />
          </div>

          <div class="site-field site-field-wide site-checkbox-row">
            <input
              id="checkbox-link-new-site"
              v-model="linkNewSiteImmediately"
              type="checkbox"
              :disabled="submitting"
            />
            <label for="checkbox-link-new-site">
              Link this site to {{ studyCode }} immediately
            </label>
          </div>

          <div class="site-field site-field-wide">
            <label for="input-site-reason">Reason for change</label>
            <input
              id="input-site-reason"
              v-model="registerSiteReason"
              type="text"
              maxlength="500"
              autocomplete="off"
              placeholder="e.g. Register investigational site and link to study"
              :disabled="submitting"
              required
            />
          </div>
        </div>

        <button
          id="btn-register-site"
          type="submit"
          class="btn btn-primary"
          :disabled="!canSubmitNewSite"
        >
          {{
            linkNewSiteImmediately
              ? `Register & link site to ${studyCode}`
              : "Register site"
          }}
        </button>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  describeKernelError,
  kernelApi,
  type KernelOrganizationSummary,
  type KernelSiteSummary,
  type KernelStudySiteSummary,
} from "../../api/kernelApi";
import {
  clearKernelStudyCache,
  seedKernelStudyRef,
  syncCallerSiteScope,
} from "../../canvas/kernelStudy";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import { buildScopedStudyLocation } from "../../canvas/palette";
import { useAuthStore } from "../../stores/auth.js";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";

const authStore = useAuthStore();

const props = withDefaults(
  defineProps<{
    studyCode: string;
    compact?: boolean;
    canManageSites?: boolean;
    manageSitesLocation?: RouteLocationRaw | null;
    activeSite?: string;
    activeParticipant?: string;
    activeNode?: string;
  }>(),
  {
    compact: false,
    canManageSites: false,
    manageSitesLocation: null,
    activeSite: "",
    activeParticipant: "",
    activeNode: "",
  }
);

const resolvedManageSitesLocation = computed<RouteLocationRaw>(() => {
  if (props.manageSitesLocation) return props.manageSitesLocation;
  return buildScopedStudyLocation(props.studyCode, {
    stage: "conduct",
    section: "sites",
    site: props.activeSite || undefined,
    participant: props.activeParticipant || undefined,
    node: props.activeNode || undefined,
  });
});

const emit = defineEmits<{
  "open-participants": [siteCode: string];
  changed: [sites: KernelStudySiteSummary[]];
}>();

const studySites = ref<KernelStudySiteSummary[]>([]);
const workspaceSites = ref<KernelSiteSummary[]>([]);
const institutions = ref<KernelOrganizationSummary[]>([]);
const loading = ref(false);
const loadError = ref("");
const submitting = ref(false);
const actionError = ref("");
const actionSuccess = ref("");

const selectedWorkspaceSiteRef = ref("");
const linkSiteReason = ref("");

const selectedInstitutionChoice = ref("__new__");
const newInstitutionName = ref("");
const newInstitutionCode = ref("");
const newSiteCode = ref("");
const newSiteName = ref("");
const newSiteCountry = ref("US");
const linkNewSiteImmediately = ref(true);
const registerSiteReason = ref("");

let requestEpoch = 0;

function nextIdempotencyKey(prefix: string): string {
  if (isDemoBuild()) {
    return `${prefix}-${demoRandomToken(12, 36, "study-sites.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return `${prefix}-${globalThis.crypto.randomUUID()}`;
  }
  return `${prefix}-${demoRandomToken(12, 36, "study-sites.idempotency")}`;
}

const unlinkedWorkspaceSites = computed(() => {
  const linkedCodes = new Set(
    studySites.value.map((s) => s.site_code.toUpperCase())
  );
  return workspaceSites.value.filter(
    (s) => !linkedCodes.has(s.code.toUpperCase())
  );
});

watch(unlinkedWorkspaceSites, (sites) => {
  if (sites.length === 0) {
    selectedWorkspaceSiteRef.value = "";
  } else if (!sites.some((s) => s.ref === selectedWorkspaceSiteRef.value)) {
    selectedWorkspaceSiteRef.value = sites[0].ref;
  }
});

const statusSummary = computed(() => {
  if (loading.value && studySites.value.length === 0) {
    return "Loading linked sites…";
  }
  if (loadError.value) {
    return "Study sites couldn't be loaded.";
  }
  const count = studySites.value.length;
  if (count === 0) {
    return `No investigational sites linked to ${props.studyCode} yet.`;
  }
  return count === 1
    ? `1 investigational site linked to ${props.studyCode}.`
    : `${count} investigational sites linked to ${props.studyCode}.`;
});

const canSubmitNewSite = computed(() => {
  if (submitting.value) return false;
  if (!newSiteCode.value.trim() || !newSiteName.value.trim()) return false;
  if (!/^[A-Za-z]{2}$/.test(newSiteCountry.value.trim())) return false;
  if (!registerSiteReason.value.trim()) return false;
  if (selectedInstitutionChoice.value === "__new__") {
    return Boolean(newInstitutionName.value.trim());
  }
  return Boolean(selectedInstitutionChoice.value);
});

async function loadAll(code: string) {
  if (!code || typeof kernelApi.listStudySites !== "function") return;
  const req = ++requestEpoch;
  loading.value = true;
  loadError.value = "";
  try {
    const linked = await kernelApi.listStudySites(code);
    if (req !== requestEpoch) return;
    studySites.value = Array.isArray(linked) ? linked : [];
    const firstStudyRef = studySites.value.find((s) => s.study_ref)?.study_ref;
    if (firstStudyRef) {
      seedKernelStudyRef(code, firstStudyRef);
    }
    for (const site of studySites.value) {
      if (site.ref && !site.mask) {
        seedNodeSummary(site.ref, {
          kind: "study_site",
          code: site.site_code ?? null,
          title: site.site_name ?? null,
          status: site.status ?? null,
        });
      }
    }
    emit("changed", studySites.value);

    if (!props.compact && props.canManageSites) {
      const [sitesRes, orgsRes] = await Promise.all([
        typeof kernelApi.listSites === "function"
          ? kernelApi.listSites().catch(() => [])
          : Promise.resolve([]),
        typeof kernelApi.listOrganizations === "function"
          ? kernelApi.listOrganizations("SiteInstitution").catch(() => [])
          : Promise.resolve([]),
      ]);
      if (req !== requestEpoch) return;
      workspaceSites.value = Array.isArray(sitesRes) ? sitesRes : [];
      institutions.value = Array.isArray(orgsRes) ? orgsRes : [];
      if (
        institutions.value.length > 0 &&
        selectedInstitutionChoice.value === "__new__"
      ) {
        selectedInstitutionChoice.value = institutions.value[0].ref;
      }
    }
  } catch (err) {
    if (req !== requestEpoch) return;
    loadError.value = describeKernelError(err);
  } finally {
    if (req === requestEpoch) {
      loading.value = false;
    }
  }
}

watch(
  () => [props.studyCode, props.compact, props.canManageSites] as const,
  ([code]) => {
    void loadAll(code);
  },
  { immediate: true }
);

async function submitLinkExistingSite() {
  if (
    !selectedWorkspaceSiteRef.value ||
    !linkSiteReason.value.trim() ||
    typeof kernelApi.linkSiteToStudy !== "function"
  ) {
    return;
  }
  submitting.value = true;
  actionError.value = "";
  actionSuccess.value = "";
  try {
    const linked = await kernelApi.linkSiteToStudy({
      studyRef: props.studyCode,
      siteRef: selectedWorkspaceSiteRef.value,
      reason: linkSiteReason.value.trim(),
      idempotencyKey: nextIdempotencyKey("link-site"),
    });
    await syncCallerSiteScope(authStore, linked.site_code);
    clearKernelStudyCache();
    linkSiteReason.value = "";
    actionSuccess.value = `Linked site ${linked.site_code} (${linked.country}) to ${props.studyCode}.`;
    await loadAll(props.studyCode);
  } catch (err) {
    actionError.value = describeKernelError(err);
  } finally {
    submitting.value = false;
  }
}

async function submitRegisterSite() {
  if (!canSubmitNewSite.value || typeof kernelApi.createSite !== "function") {
    return;
  }
  submitting.value = true;
  actionError.value = "";
  actionSuccess.value = "";
  const reason = registerSiteReason.value.trim();
  const country = newSiteCountry.value.trim().toUpperCase();
  try {
    let institutionRef: string | null =
      selectedInstitutionChoice.value === "__new__"
        ? null
        : selectedInstitutionChoice.value;

    if (
      selectedInstitutionChoice.value === "__new__" &&
      typeof kernelApi.createOrganization === "function"
    ) {
      const createdOrg = await kernelApi.createOrganization({
        name: newInstitutionName.value.trim(),
        code: newInstitutionCode.value.trim() || null,
        kind: "SiteInstitution",
        reason,
        idempotencyKey: nextIdempotencyKey("register-inst"),
      });
      institutionRef = createdOrg.ref;
    }

    const createdSite = await kernelApi.createSite({
      siteCode: newSiteCode.value.trim().toUpperCase(),
      name: newSiteName.value.trim(),
      country,
      institutionRef,
      institutionName: institutionRef ? null : newInstitutionName.value.trim(),
      institutionCode: institutionRef
        ? null
        : newInstitutionCode.value.trim() || null,
      reason,
      idempotencyKey: nextIdempotencyKey("register-site"),
    });

    if (
      linkNewSiteImmediately.value &&
      typeof kernelApi.linkSiteToStudy === "function"
    ) {
      await kernelApi.linkSiteToStudy({
        studyRef: props.studyCode,
        siteRef: createdSite.ref,
        reason,
        idempotencyKey: nextIdempotencyKey("link-new-site"),
      });
      actionSuccess.value = `Registered site ${createdSite.code} (${createdSite.country}) and linked it to ${props.studyCode}.`;
    } else {
      actionSuccess.value = `Registered site ${createdSite.code} (${createdSite.country}).`;
    }
    await syncCallerSiteScope(authStore, createdSite.code);
    clearKernelStudyCache();

    newSiteCode.value = "";
    newSiteName.value = "";
    newInstitutionName.value = "";
    newInstitutionCode.value = "";
    registerSiteReason.value = "";
    await loadAll(props.studyCode);
  } catch (err) {
    actionError.value = describeKernelError(err);
  } finally {
    submitting.value = false;
  }
}
</script>

<style scoped>
.study-sites {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.study-sites-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.study-sites-title {
  margin: 0;
  font-size: 1rem;
}

.study-sites-status {
  margin: 2px 0 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}

.linked-sites-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.linked-site-card {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.linked-site-main {
  min-width: 0;
  max-width: 100%;
}

.linked-site-top {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.linked-site-code,
#input-site-code,
#input-site-country,
#input-institution-code {
  font-family: var(--semantic-font-mono);
}

.site-country-badge,
.site-status-badge {
  padding: 1px 6px;
  border-radius: 999px;
  background: var(--accent-bg);
  font-size: 0.75rem;
  font-weight: 600;
}

.linked-site-name {
  font-size: 0.9rem;
  overflow-wrap: anywhere;
}

.linked-site-inst {
  color: var(--neutral-dark);
  font-size: 0.8rem;
  overflow-wrap: anywhere;
}

.site-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.site-form-title {
  margin: 0;
  font-size: 0.95rem;
}

.site-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 200px), 1fr));
  gap: 10px;
}

.site-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.site-field-wide {
  grid-column: 1 / -1;
}

.site-checkbox-row {
  flex-direction: row;
  align-items: center;
  gap: 8px;
}

.site-field input[type="text"],
.site-field select {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.sites-banner {
  padding: 8px 10px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.sites-banner-success {
  background: var(--accent-bg);
}

.sites-banner-error {
  border: 1px solid var(--semantic-color-error);
  color: var(--semantic-color-error);
}
</style>
