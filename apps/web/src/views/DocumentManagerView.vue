<!-- apps/web/src/views/DocumentManagerView.vue -->
<template>
  <div class="document-manager-layout">
    <template v-if="!canRead">
      <div
        id="etmf-read-role-banner"
        class="etmf-role-restricted-card"
        role="status"
      >
        <h3>Regulated Document Access Restricted</h3>
        <p>{{ ETMF_READ_ROLE_REASON }}</p>
      </div>
    </template>

    <template v-else>
      <ClinicalSplitWorkspace
        ref="binderSplitRef"
        master-width="320px"
        drawer-size="xl"
        :has-selection="true"
        :drawer-title="binderDrawerTitle"
        drawer-subtitle="DIA eTMF Reference Model Binder"
        close-label="Close binder documents"
        :teleport="false"
      >
        <template #master>
          <aside class="sidebar-binder-tree">
            <TmfBinderTree
              :tree="etmfStore.binderTree"
              @select-artifact="handleArtifactSelect"
            />
          </aside>
        </template>

        <template #inline-content="{ isCompact, isDetailOpen, openDetail }">
          <main class="document-content-area">
            <div class="etmf-workspace-bar">
              <ClinicalTabs
                v-model="activeWorkspaceTab"
                :tabs="etmfWorkspaceTabs"
                variant="pills"
                overflow="wrap"
                aria-label="eTMF workspace views"
                id-prefix="etmf-workspace"
              />

              <form
                v-if="!props.studyId"
                id="etmf-scope-bar"
                class="etmf-scope-form"
                aria-label="eTMF study and site scope"
                @submit.prevent="applyStandaloneScope"
              >
                <label for="etmf-study-select" class="etmf-scope-label"
                  >Study</label
                >
                <ClinicalInput
                  id="etmf-study-select"
                  v-model="standaloneStudyInput"
                  label="Study"
                  hide-label
                  density="compact"
                  :show-query-flag="false"
                  control-class="etmf-scope-input"
                  autocomplete="off"
                  placeholder="e.g. CADENCE-101"
                  :list="
                    studyOptions.length ? 'etmf-studies-datalist' : undefined
                  "
                  @change="applyStandaloneScope"
                />
                <datalist v-if="studyOptions.length" id="etmf-studies-datalist">
                  <option
                    v-for="opt in studyOptions"
                    :key="opt.code"
                    :value="opt.code"
                  >
                    {{ opt.label }}
                  </option>
                </datalist>

                <label for="etmf-site-select" class="etmf-scope-label"
                  >Site</label
                >
                <ClinicalInput
                  id="etmf-site-select"
                  v-model="standaloneSiteInput"
                  label="Site"
                  hide-label
                  density="compact"
                  :show-query-flag="false"
                  control-class="etmf-scope-input etmf-scope-site-input"
                  autocomplete="off"
                  placeholder="All assigned sites"
                  :list="siteOptions.length ? 'etmf-sites-datalist' : undefined"
                  @change="applyStandaloneScope"
                />
                <datalist v-if="siteOptions.length" id="etmf-sites-datalist">
                  <option
                    v-for="site in siteOptions"
                    :key="site"
                    :value="site"
                  />
                </datalist>

                <button
                  id="btn-etmf-apply-scope"
                  type="submit"
                  class="btn btn-secondary etmf-scope-apply-btn"
                >
                  Apply Scope
                </button>
              </form>

              <div v-else class="etmf-study-chip">
                Study: <strong>{{ effectiveStudyId }}</strong>
                <template v-if="effectiveSiteId">
                  · Site: <strong>{{ effectiveSiteId }}</strong>
                </template>
              </div>
            </div>

            <div
              v-if="etmfStore.scopeForbiddenGuidance"
              id="etmf-scope-forbidden-banner"
              class="etmf-scope-guidance-banner"
              role="alert"
            >
              <strong>Study or Site Assignment Required:</strong>
              <span>{{ etmfStore.scopeForbiddenGuidance }}</span>
            </div>

            <div
              v-if="activeWorkspaceTab === 'binder'"
              id="etmf-workspace-panel-binder"
              role="tabpanel"
              aria-labelledby="btn-etmf-tab-binder"
            >
              <div v-if="isCompact" class="etmf-compact-binder-bar">
                <div class="etmf-compact-binder-meta">
                  <strong>{{ binderDrawerTitle }}</strong>
                  <span class="tabular-nums"
                    >{{ etmfStore.documents.length }} document(s)</span
                  >
                </div>
                <button
                  id="btn-open-binder-drawer"
                  type="button"
                  class="btn btn-secondary etmf-open-binder-btn"
                  @click="openDetail($event)"
                >
                  Open Binder Documents
                </button>
              </div>
              <DocumentGrid
                v-if="!isCompact || !isDetailOpen"
                :documents="etmfStore.documents"
                @preview="handlePreviewDocument"
                @inspect="handleInspectDocument"
              />
            </div>
            <div
              v-else
              id="etmf-workspace-panel-qc-queue"
              role="tabpanel"
              aria-labelledby="btn-etmf-tab-qc-queue"
            >
              <CraQcQueue v-if="activeWorkspaceTab === 'qc-queue'" />
            </div>
          </main>
        </template>

        <template #detail>
          <div
            class="etmf-drawer-binder-panel"
            role="region"
            aria-label="Binder documents"
          >
            <DocumentGrid
              :documents="etmfStore.documents"
              @preview="handlePreviewDocument"
              @inspect="handleInspectDocument"
            />
          </div>
        </template>
      </ClinicalSplitWorkspace>
    </template>

    <!-- Document Metadata Inspection Drawer -->
    <ClinicalDrawer
      :is-open="Boolean(inspectedDoc)"
      :teleport="false"
      :dismissible="!reviewingQc && !submittingDraftQc"
      overlay-class="metadata-drawer-overlay"
      panel-class="metadata-drawer"
      body-class="drawer-body"
      @close="inspectedDoc = null"
    >
      <template #header="{ titleId, close }">
        <div
          v-if="inspectedDoc"
          class="drawer-header"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            width: 100%;
            gap: 12px;
          "
        >
          <div style="display: flex; align-items: center; gap: 8px">
            <span aria-hidden="true"
              ><ClinicalIcon name="clipboard-list" :size="16"
            /></span>
            <div>
              <h3 :id="titleId" style="margin: 0; font-size: 16px">
                Document Metadata &amp; 21 CFR Part 11 Manifest
              </h3>
              <p
                style="
                  margin: 2px 0 0 0;
                  font-size: var(--font-size-2xs);
                  color: var(--semantic-color-text-secondary);
                "
              >
                {{ inspectedDoc.filename }}
              </p>
            </div>
          </div>
          <button
            type="button"
            class="btn btn-secondary btn-close-drawer"
            aria-label="Close inspection drawer"
            :disabled="reviewingQc || submittingDraftQc"
            @click="close"
          >
            <ClinicalIcon name="x" :size="14" />
          </button>
        </div>
      </template>

      <DocumentMetadataPanel
        v-if="inspectedDoc"
        v-model:draft-submit-reason="draftSubmitReason"
        v-model:qc-accept-reason="qcAcceptReason"
        :inspected-doc="inspectedDoc"
        :can-stage="canStage"
        :can-review-qc="canReviewQc"
        :submitting-draft-qc="submittingDraftQc"
        :reviewing-qc="reviewingQc"
        :qc-review-error="qcReviewError"
        @close="inspectedDoc = null"
        @submit-draft-qc="handleSubmitDraftForQc"
        @accept-classification="handleAcceptClassification"
        @preview="handlePreviewFromDrawer"
      />

      <template #footer>
        <button
          v-if="inspectedDoc"
          type="button"
          class="btn btn-primary btn-preview-from-drawer"
          style="padding: 8px 16px; font-size: 12px; cursor: pointer"
          :disabled="reviewingQc || submittingDraftQc"
          @click="handlePreviewFromDrawer(inspectedDoc)"
        >
          Open Secure PDF Preview
        </button>
      </template>
    </ClinicalDrawer>

    <!-- Secure PDF Preview Modal -->
    <PdfPreviewModal
      v-if="selectedDoc"
      :document="selectedDoc"
      @close="selectedDoc = null"
    />

    <!-- Real-time eTMF Export Job Progress Drawer -->
    <ExportJobDrawer v-if="etmfStore.isExportDrawerOpen" />
  </div>
</template>

<script>
import { defineLazyOverlay } from "../utils/lazyOverlay.js";

const CraQcQueue = defineLazyOverlay(
  () => import("../components/etmf/CraQcQueue.vue")
);
const ExportJobDrawer = defineLazyOverlay(
  () => import("../components/etmf/ExportJobDrawer.vue")
);
const PdfPreviewModal = defineLazyOverlay(
  () => import("../components/etmf/PdfPreviewModal.vue")
);
const DocumentMetadataPanel = defineLazyOverlay(
  () => import("../components/etmf/DocumentMetadataPanel.vue")
);
</script>

<script setup>
import {
  ClinicalDrawer,
  ClinicalIcon,
  ClinicalInput,
  ClinicalSplitWorkspace,
  ClinicalTabs,
  handoffFocus,
} from "ui";
import { computed, onMounted, ref, watch } from "vue";
import { kernelApi } from "../api/kernelApi";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import {
  ETMF_READ_ROLE_REASON,
  canReadEtmf,
  canReviewEtmfQc,
  canStageEtmfQc,
} from "../canvas/authority";
import DocumentGrid from "../components/etmf/DocumentGrid.vue";
import TmfBinderTree from "../components/etmf/TmfBinderTree.vue";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { useAuthStore } from "../stores/auth";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import { useEtmfStore } from "../stores/etmf";
import { useToastStore } from "../stores/toasts.js";
import { syncWorkspaceScope } from "../stores/workspaceScope";
import {
  extractAssignedSites,
  extractAssignedStudies,
} from "../utils/callerScope";

const props = defineProps({
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
});

const authStore = useAuthStore();
const etmfStore = useEtmfStore();
const clinicalStore = useClinicalScopeStore();
const { route, router } = resolveRouteAndRouter();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();

const activeWorkspaceTab = ref("binder");
const binderSplitRef = ref(null);
const binderDrawerTitle = computed(() =>
  etmfStore.selectedArtifactId
    ? `Artifact ${etmfStore.selectedArtifactId} Documents`
    : "Binder Documents"
);
const etmfWorkspaceTabs = [
  {
    id: "binder",
    label: "Binder Documents",
    buttonId: "btn-etmf-tab-binder",
    panelId: "etmf-workspace-panel-binder",
  },
  {
    id: "qc-queue",
    label: "Clinical QC Queue",
    buttonId: "btn-etmf-tab-qc-queue",
    panelId: "etmf-workspace-panel-qc-queue",
  },
];
const selectedDoc = ref(null);
const inspectedDoc = ref(null);
const qcAcceptReason = ref(
  "Verified DIA Reference Model classification and wet-ink signatures."
);
const draftSubmitReason = ref(
  "Submitting draft document for technical QC review."
);
const reviewingQc = ref(false);
const submittingDraftQc = ref(false);
const qcReviewError = ref("");

const directoryStudies = ref([]);
const standaloneStudyInput = ref("");
const standaloneSiteInput = ref("");

const hasActiveAuth = computed(() =>
  Boolean(
    authStore.isAuthenticated ||
    (Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0)
  )
);

const canRead = computed(() => !hasActiveAuth.value || canReadEtmf(authStore));
const canStage = computed(
  () => !hasActiveAuth.value || canStageEtmfQc(authStore)
);
const canReviewQc = computed(
  () => !hasActiveAuth.value || canReviewEtmfQc(authStore)
);

const effectiveStudyId = computed(
  () =>
    props.studyId ||
    etmfStore.currentStudyId ||
    activeStudyCode.value ||
    clinicalStore.activeStudyId
);
const effectiveSiteId = computed(
  () => props.siteId || etmfStore.currentSiteId || clinicalStore.activeSiteId
);

const studyOptions = computed(() => {
  const map = new Map();
  const claimStudies =
    authStore.assignedStudies?.length > 0
      ? authStore.assignedStudies
      : extractAssignedStudies(authStore.tokenParsed || undefined)
          .assignedStudies;
  for (const code of claimStudies || []) {
    if (code) {
      map.set(code, { code, label: code });
    }
  }
  for (const item of directoryStudies.value) {
    if (item?.code) {
      const label = item.title ? `${item.code} — ${item.title}` : item.code;
      map.set(item.code, { code: item.code, label });
    }
  }
  return Array.from(map.values());
});

const siteOptions = computed(() => {
  if (authStore.assignedSites?.length > 0) {
    return authStore.assignedSites;
  }
  return extractAssignedSites(authStore.tokenParsed || undefined);
});

function syncScope(study, site) {
  const normalizedStudy = typeof study === "string" ? study.trim() : "";
  const normalizedSite = typeof site === "string" ? site.trim() : "";
  etmfStore.setScope(normalizedStudy, normalizedSite);
  if (normalizedStudy) {
    setActiveStudyCode(normalizedStudy);
    clinicalStore.activeStudyId = normalizedStudy;
  }
  clinicalStore.activeSiteId = normalizedSite;
  syncWorkspaceScope({
    ...(normalizedStudy ? { studyId: normalizedStudy } : {}),
    siteId: normalizedSite,
  });
  standaloneStudyInput.value = normalizedStudy;
  standaloneSiteInput.value = normalizedSite;
}

async function loadStudyDirectoryOptions() {
  if (props.studyId || !canRead.value) return;
  if (!authStore.token || typeof kernelApi?.listStudies !== "function") return;
  try {
    const res = await kernelApi.listStudies();
    const studies = Array.isArray(res)
      ? res
      : Array.isArray(res?.studies)
        ? res.studies
        : [];
    if (studies.length > 0) {
      directoryStudies.value = studies;
      if (!standaloneStudyInput.value) {
        const defaultCode = studies[0]?.code || "";
        if (defaultCode) {
          syncScope(defaultCode, standaloneSiteInput.value);
          void replaceRouteQueryParams(router, route, {
            studyId: defaultCode,
            siteId: standaloneSiteInput.value || null,
            subjectId: clinicalStore.activeSubjectId || null,
          });
          await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
        }
      }
    }
  } catch {
    // Study picker still works with token-assigned studies or typed study codes.
  }
}

async function applyStandaloneScope() {
  if (!canRead.value) return;
  const nextStudy = standaloneStudyInput.value.trim();
  const nextSite = standaloneSiteInput.value.trim();
  syncScope(nextStudy, nextSite);

  await replaceRouteQueryParams(router, route, {
    studyId: nextStudy || null,
    siteId: nextSite || null,
    subjectId: clinicalStore.activeSubjectId || null,
  });

  await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
  if (activeWorkspaceTab.value === "qc-queue") {
    await etmfStore.fetchQcQueue(nextStudy, nextSite).catch(() => {});
  }
  if (nextStudy) {
    await etmfStore.fetchExportJobs(nextStudy).catch(() => {});
  }
}

watch(
  () => [
    route?.query?.studyId,
    route?.query?.siteId,
    route?.query?.site,
    route?.query?.subjectId,
    route?.query?.participant,
  ],
  async () => {
    if (props.studyId || !route?.query) return;
    const nextScope = readRouteScopeQuery(route);
    const resolvedStudy = nextScope.studyId || activeStudyCode.value;
    clinicalStore.activeSubjectId = nextScope.subjectId;
    if (
      resolvedStudy !== standaloneStudyInput.value ||
      nextScope.siteId !== standaloneSiteInput.value
    ) {
      syncScope(resolvedStudy, nextScope.siteId);
      await etmfStore.fetchBinderTree(resolvedStudy || undefined);
      if (canRead.value) {
        await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
      }
    }
  }
);

watch(
  () => [props.studyId, props.siteId],
  async ([nextStudy, nextSite], [prevStudy, prevSite]) => {
    if (!nextStudy && nextSite === prevSite) return;
    syncScope(nextStudy || etmfStore.currentStudyId, nextSite || "");
    await etmfStore.fetchBinderTree(nextStudy || undefined);
    if (canRead.value && (nextStudy !== prevStudy || nextSite !== prevSite)) {
      await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
    }
  }
);

function handleArtifactSelect(artifactCode, triggerEl = null) {
  if (!canRead.value) return;
  activeWorkspaceTab.value = "binder";
  etmfStore.fetchDocuments(artifactCode);
  if (binderSplitRef.value) {
    binderSplitRef.value.openDetail(triggerEl);
  }
}

function handlePreviewDocument(doc) {
  selectedDoc.value = doc;
}

function handleInspectDocument(doc) {
  qcReviewError.value = "";
  inspectedDoc.value = doc;
}

function handlePreviewFromDrawer(doc) {
  selectedDoc.value = doc;
}

async function handleSubmitDraftForQc(doc) {
  if (!doc || !canStage.value) return;
  const docId = doc.id || doc.document_id || doc.documentId;
  const reason = draftSubmitReason.value.trim();
  if (!docId || reason.length < 10) {
    qcReviewError.value =
      "A submission justification of at least 10 characters is required.";
    return;
  }

  submittingDraftQc.value = true;
  qcReviewError.value = "";
  let transitionedToQc = false;
  try {
    const updated = await etmfStore.submitDraftForQc(docId, {
      reason_for_change: reason,
    });
    if (inspectedDoc.value) {
      const nextStatus = updated?.status ?? "TECHNICAL_QC";
      inspectedDoc.value = {
        ...inspectedDoc.value,
        ...(updated || {}),
        status: nextStatus,
        approval_status: nextStatus,
      };
      transitionedToQc = nextStatus !== "DRAFT";
    }
    useToastStore().push({
      tone: "success",
      title: "Draft submitted for Technical QC",
      message: `${doc.filename || docId} moved to Technical QC queue.`,
    });
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError("Technical QC submission failed", err);
    qcReviewError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  } finally {
    submittingDraftQc.value = false;
  }

  if (transitionedToQc) {
    await handoffFocus(["#qc-accept-reason", ".btn-preview-from-drawer"], {
      includeDefaultFallbacks: false,
    });
  }
}

async function handleAcceptClassification(doc) {
  if (!doc || !canReviewQc.value) return;
  const docId = doc.id || doc.document_id || doc.documentId;
  const reason = qcAcceptReason.value.trim();
  if (!docId || reason.length < 10) {
    qcReviewError.value =
      "A clinical QC justification of at least 10 characters is required.";
    return;
  }

  reviewingQc.value = true;
  qcReviewError.value = "";
  let transitionedFromQc = false;
  try {
    const updated = await etmfStore.reviewQcDocument(docId, {
      decision: "ACCEPT",
      reason_for_change: reason,
    });
    if (inspectedDoc.value) {
      const nextStatus = updated?.status ?? inspectedDoc.value.status;
      inspectedDoc.value = {
        ...inspectedDoc.value,
        ...(updated || {}),
        status: nextStatus,
        approval_status: nextStatus,
      };
      transitionedFromQc = nextStatus !== "TECHNICAL_QC";
    }
    useToastStore().push({
      tone: "success",
      title: "Artifact approved in eTMF",
      message: `${doc.filename || docId} passed clinical QC review.`,
    });
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError("Clinical QC review failed", err);
    qcReviewError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  } finally {
    reviewingQc.value = false;
  }

  if (transitionedFromQc) {
    await handoffFocus([".btn-preview-from-drawer"], {
      includeDefaultFallbacks: false,
    });
  }
}

onMounted(async () => {
  const routeScope = readRouteScopeQuery(route);
  const queryStudy = routeScope.studyId;
  const querySite = routeScope.siteId;

  const initialStudy =
    props.studyId ||
    queryStudy ||
    activeStudyCode.value ||
    clinicalStore.activeStudyId ||
    etmfStore.currentStudyId ||
    (authStore.assignedStudies?.length ? authStore.assignedStudies[0] : "");
  const initialSite =
    props.siteId ||
    querySite ||
    clinicalStore.activeSiteId ||
    etmfStore.currentSiteId ||
    "";

  if (initialStudy || initialSite) {
    syncScope(initialStudy, initialSite);
  }
  if (routeScope.subjectId) {
    clinicalStore.activeSubjectId = routeScope.subjectId;
  }
  if (routeScope.visitId) {
    clinicalStore.activeVisitId = routeScope.visitId;
  }

  if (!canRead.value) {
    return;
  }

  await etmfStore.fetchBinderTree(initialStudy || undefined);

  if (initialStudy && etmfStore.documentsList.length === 0) {
    await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
  }

  await loadStudyDirectoryOptions();
});

defineExpose({
  selectedDoc,
  inspectedDoc,
  qcAcceptReason,
  draftSubmitReason,
  reviewingQc,
  submittingDraftQc,
  standaloneStudyInput,
  standaloneSiteInput,
  applyStandaloneScope,
  handleInspectDocument,
  handlePreviewDocument,
  handlePreviewFromDrawer,
  handleSubmitDraftForQc,
  handleAcceptClassification,
});
</script>

<style scoped>
.document-manager-layout {
  display: flex;
  height: calc(100vh - 120px);
  gap: 20px;
  background-color: var(--semantic-color-surface-muted);
  margin: -20px;
  padding: 20px;
  overflow: hidden;
  position: relative;
}

.etmf-role-restricted-card {
  flex: 1;
  align-self: flex-start;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-warning-border);
  border-left: 4px solid var(--semantic-color-warning-border-strong);
  border-radius: 8px;
  padding: 24px;
  color: var(--semantic-color-text);
}

.etmf-role-restricted-card h3 {
  margin: 0 0 8px 0;
  font-size: 1rem;
}

.etmf-role-restricted-card p {
  margin: 0;
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
}

.sidebar-binder-tree {
  width: 340px;
  min-width: 300px;
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: var(--shadow-sm);
  box-sizing: border-box;
}

.document-content-area {
  flex: 1;
  min-width: 0;
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: var(--shadow-sm);
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
}

.etmf-workspace-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
  min-width: 0;
  box-sizing: border-box;
}

.etmf-scope-form {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.etmf-scope-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.etmf-scope-input {
  padding: 5px 10px;
  font-size: 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  min-width: 140px;
  max-width: 100%;
  box-sizing: border-box;
}

.etmf-scope-site-input {
  min-width: 125px;
}

.etmf-scope-apply-btn {
  padding: 5px 10px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  max-width: 100%;
  box-sizing: border-box;
}

.etmf-scope-guidance-banner {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 12px 16px 0;
  padding: 12px 16px;
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-left: 4px solid var(--semantic-color-warning-border-strong);
  border-radius: 6px;
  font-size: 13px;
  color: var(--semantic-color-warning-text);
}

#etmf-workspace-panel-binder,
#etmf-workspace-panel-qc-queue {
  min-width: 0;
  max-width: 100%;
}

.etmf-compact-binder-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 16px;
  background: var(--semantic-color-surface);
  border-top: 1px solid var(--semantic-color-border);
}

.etmf-compact-binder-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  min-width: 0;
}

.etmf-open-binder-btn {
  min-height: 44px;
}

.etmf-drawer-binder-panel {
  min-width: 0;
  max-width: 100%;
}

@media (max-width: 1279px) {
  .document-manager-layout {
    flex-direction: column;
    height: auto;
    min-height: 0;
    margin: 0;
    padding: 0;
    gap: 12px;
    background-color: transparent;
    overflow-x: hidden;
    overflow-y: visible;
  }

  .sidebar-binder-tree {
    width: 100%;
    min-width: 0;
    max-height: 320px;
  }

  .document-content-area {
    width: 100%;
    min-width: 0;
    overflow: visible;
  }
}

@media (max-width: 900px) {
  .document-manager-layout {
    flex-direction: column;
    height: auto;
    min-height: 0;
    margin: 0;
    padding: 0;
    gap: 12px;
    background-color: transparent;
    overflow-x: hidden;
    overflow-y: visible;
  }

  .sidebar-binder-tree {
    width: 100%;
    min-width: 0;
    max-height: 300px;
  }

  .document-content-area {
    width: 100%;
    min-width: 0;
    overflow: visible;
  }

  .etmf-workspace-bar {
    padding: 10px 12px;
    gap: 10px;
  }

  .etmf-scope-form {
    width: 100%;
  }

  .etmf-scope-input,
  .etmf-scope-site-input {
    min-width: 0;
    flex: 1 1 100%;
    width: 100%;
  }

  .etmf-scope-apply-btn {
    width: 100%;
  }
}

.etmf-study-chip {
  font-size: 12px;
  color: var(--semantic-color-text-secondary);
}
</style>
