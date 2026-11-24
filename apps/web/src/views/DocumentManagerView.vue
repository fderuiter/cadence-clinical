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
      <aside class="sidebar-binder-tree">
        <TmfBinderTree
          :tree="etmfStore.binderTree"
          @select-artifact="handleArtifactSelect"
        />
      </aside>
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

          <!-- Standalone /etmf Study & Site Scope Selector -->
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
            <input
              id="etmf-study-select"
              v-model="standaloneStudyInput"
              type="text"
              class="etmf-scope-input"
              autocomplete="off"
              placeholder="e.g. CADENCE-101"
              :list="studyOptions.length ? 'etmf-studies-datalist' : undefined"
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

            <label for="etmf-site-select" class="etmf-scope-label">Site</label>
            <input
              id="etmf-site-select"
              v-model="standaloneSiteInput"
              type="text"
              class="etmf-scope-input etmf-scope-site-input"
              autocomplete="off"
              placeholder="All assigned sites"
              :list="siteOptions.length ? 'etmf-sites-datalist' : undefined"
              @change="applyStandaloneScope"
            />
            <datalist v-if="siteOptions.length" id="etmf-sites-datalist">
              <option v-for="site in siteOptions" :key="site" :value="site" />
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
          <DocumentGrid
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
          <CraQcQueue />
        </div>
      </main>
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
        <div v-if="inspectedDoc" class="drawer-header">
          <div style="display: flex; align-items: center; gap: 8px">
            <span style="font-size: 20px" aria-hidden="true">📋</span>
            <div>
              <h3 :id="titleId" style="margin: 0; font-size: 16px">
                Document Metadata &amp; 21 CFR Part 11 Manifest
              </h3>
              <p
                style="
                  margin: 2px 0 0 0;
                  font-size: 11px;
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
            style="padding: 4px 8px; font-size: 12px; cursor: pointer"
            aria-label="Close inspection drawer"
            :disabled="reviewingQc || submittingDraftQc"
            @click="close"
          >
            ✕
          </button>
        </div>
      </template>

      <template v-if="inspectedDoc">
        <!-- DIA Reference Model Taxonomy Section -->
        <div class="drawer-section">
          <h4 class="section-heading">DIA Reference Model Taxonomy</h4>
          <div class="meta-grid">
            <div class="meta-item">
              <span class="meta-label">Artifact Code</span>
              <span class="meta-val font-mono">{{
                inspectedDoc.artifact_code || inspectedDoc.taxonomy_code || "—"
              }}</span>
            </div>
            <div class="meta-item">
              <span class="meta-label">Zone &amp; Section</span>
              <span class="meta-val"
                >Zone {{ inspectedDoc.zone_id || inspectedDoc.zone || "—" }} ·
                {{ inspectedDoc.section || "—" }}</span
              >
            </div>
            <div class="meta-item">
              <span class="meta-label">Version</span>
              <span class="meta-val badge status-review"
                >v{{ inspectedDoc.version_index || 1 }}.0</span
              >
            </div>
            <div class="meta-item">
              <span class="meta-label">Lifecycle Status</span>
              <span class="meta-val badge status-approved">{{
                formatClinicalStatus(inspectedDoc.status || "DRAFT")
              }}</span>
            </div>
          </div>
        </div>

        <!-- 21 CFR Part 11 Audit Trail & Change Justification -->
        <div class="drawer-section">
          <h4 class="section-heading">21 CFR Part 11 Audit Record</h4>
          <div class="reason-box">
            <strong>Mandatory Reason for Change:</strong>
            <p
              style="
                margin: 4px 0 0 0;
                font-size: 12px;
                color: var(--semantic-color-text);
              "
            >
              {{ inspectedDoc.reason_for_change || inspectedDoc.reason || "—" }}
            </p>
          </div>
          <div class="meta-grid" style="margin-top: 10px">
            <div class="meta-item">
              <span class="meta-label">Uploaded By</span>
              <span class="meta-val font-mono">{{
                inspectedDoc.created_by || inspectedDoc.uploaded_by || "—"
              }}</span>
            </div>
            <div class="meta-item">
              <span class="meta-label">Uploaded At</span>
              <span class="meta-val font-mono">{{
                inspectedDoc.formattedCreatedAt ||
                inspectedDoc.created_at ||
                "—"
              }}</span>
            </div>
          </div>
        </div>

        <!-- Cryptographic SHA-256 Checksum & Seal -->
        <div class="drawer-section">
          <h4 class="section-heading">Cryptographic Integrity</h4>
          <div class="crypto-seal-card">
            <div
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 6px;
              "
            >
              <span style="font-size: 11px; font-weight: 600"
                >Cryptographic Integrity Verification</span
              >
              <span
                v-if="getDocSha256(inspectedDoc) !== '—'"
                class="badge status-approved"
                style="font-size: 9px"
                >SHA-256 Recorded</span
              >
              <span v-else class="badge status-draft" style="font-size: 9px"
                >Not Computed</span
              >
            </div>
            <SupportDetails
              v-if="getDocSha256(inspectedDoc) !== '—'"
              summary-label="SHA-256 verification digest"
              :reference="getDocSha256(inspectedDoc)"
            />
            <span
              v-else
              style="font-size: 11px; color: var(--semantic-color-text-muted)"
              >Digest not yet recorded</span
            >
          </div>
        </div>

        <!-- De-Identification / Redaction Manifest -->
        <div class="drawer-section">
          <h4 class="section-heading">HIPAA / GDPR De-identification</h4>
          <div class="redaction-info">
            <span v-if="inspectedDoc.is_redacted" class="badge status-approved">
              🛡️ De-Identified Copy (PHI/PII Scrubber Applied)
            </span>
            <span v-else class="badge status-draft">
              🔒 Regulated Source Original
            </span>
          </div>
        </div>

        <!-- Multimodal DIA Document Intelligence & Lifecycle Transitions -->
        <div class="drawer-section">
          <h4 class="section-heading">🤖 DIA Document Intelligence</h4>
          <div class="intelligence-card">
            <div
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 8px;
              "
            >
              <span style="font-size: 12px; font-weight: 600"
                >DIA Classifier Confidence</span
              >
              <span
                class="badge"
                :class="
                  hasClassifierConfidence(inspectedDoc)
                    ? 'status-approved'
                    : 'status-draft'
                "
                style="font-size: 10px; font-weight: bold"
              >
                {{ getDocConfidence(inspectedDoc) }}
              </span>
            </div>
            <div class="meta-grid">
              <div class="meta-item">
                <span class="meta-label">Detected Modality</span>
                <span class="meta-val">{{
                  formatDocumentModality(getDocModality(inspectedDoc))
                }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Signature Verification</span>
                <span class="meta-val" :class="getSigStatusClass(inspectedDoc)">
                  {{ getSigStatusText(inspectedDoc) }}
                </span>
              </div>
              <div
                v-if="getExtractedInvestigator(inspectedDoc)"
                class="meta-item"
              >
                <span class="meta-label">Investigator</span>
                <span class="meta-val font-mono">{{
                  getExtractedInvestigator(inspectedDoc)
                }}</span>
              </div>
              <div v-if="getExtractedProtocol(inspectedDoc)" class="meta-item">
                <span class="meta-label">Protocol #</span>
                <span class="meta-val font-mono">{{
                  getExtractedProtocol(inspectedDoc)
                }}</span>
              </div>
            </div>

            <!-- DRAFT -> TECHNICAL_QC transition -->
            <div v-if="inspectedDoc.status === 'DRAFT'" class="qc-accept-panel">
              <label for="draft-submit-qc-reason" class="meta-label">
                Submit Draft for Technical QC (min 10 characters)
              </label>
              <input
                id="draft-submit-qc-reason"
                v-model="draftSubmitReason"
                type="text"
                class="qc-reason-input"
                :disabled="!canStage || submittingDraftQc"
                placeholder="Ready for technical QC review and taxonomy verification"
              />
              <p
                v-if="!canStage"
                id="etmf-stage-role-note"
                class="qc-role-note"
              >
                {{ ETMF_STAGE_ROLE_REASON }}
              </p>
              <p
                v-if="qcReviewError"
                id="etmf-qc-review-error"
                class="qc-error-note"
                role="alert"
              >
                {{ qcReviewError }}
              </p>
              <button
                id="btn-submit-technical-qc"
                type="button"
                class="btn btn-secondary"
                style="
                  width: 100%;
                  padding: 6px 10px;
                  font-size: 11px;
                  cursor: pointer;
                "
                :disabled="
                  !canStage ||
                  submittingDraftQc ||
                  draftSubmitReason.trim().length < 10
                "
                :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
                @click="handleSubmitDraftForQc(inspectedDoc)"
              >
                {{
                  submittingDraftQc
                    ? "Submitting for Technical QC..."
                    : "📤 Submit for Technical QC"
                }}
              </button>
            </div>

            <!-- TECHNICAL_QC -> APPROVED clinical QC review -->
            <div
              v-else-if="inspectedDoc.status === 'TECHNICAL_QC'"
              class="qc-accept-panel"
            >
              <label for="qc-accept-reason" class="meta-label">
                Clinical QC Review Justification (min 10 characters)
              </label>
              <input
                id="qc-accept-reason"
                v-model="qcAcceptReason"
                type="text"
                class="qc-reason-input"
                :disabled="!canReviewQc || reviewingQc"
                placeholder="Verified artifact taxonomy, dates, and signatures"
              />
              <p
                v-if="!canReviewQc"
                id="etmf-qc-role-note"
                class="qc-role-note"
              >
                {{ ETMF_QC_REVIEW_ROLE_REASON }}
              </p>
              <p
                v-if="qcReviewError"
                id="etmf-qc-review-error"
                class="qc-error-note"
                role="alert"
              >
                {{ qcReviewError }}
              </p>
              <button
                id="btn-accept-ai-classification"
                type="button"
                class="btn btn-secondary"
                style="
                  width: 100%;
                  padding: 6px 10px;
                  font-size: 11px;
                  cursor: pointer;
                "
                :disabled="
                  !canReviewQc ||
                  reviewingQc ||
                  qcAcceptReason.trim().length < 10
                "
                :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
                @click="handleAcceptClassification(inspectedDoc)"
              >
                {{
                  reviewingQc
                    ? "Submitting Clinical QC..."
                    : "✓ Accept AI Classification"
                }}
              </button>
            </div>
          </div>
        </div>
      </template>

      <template #footer>
        <button
          v-if="inspectedDoc"
          type="button"
          class="btn btn-primary btn-preview-from-drawer"
          style="padding: 8px 16px; font-size: 12px; cursor: pointer"
          :disabled="reviewingQc || submittingDraftQc"
          @click="handlePreviewFromDrawer(inspectedDoc)"
        >
          👁️ Open Secure PDF Preview
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
    <ExportJobDrawer />
  </div>
</template>

<script setup>
import { ClinicalDrawer, ClinicalTabs, handoffFocus } from "ui";
import { computed, onMounted, ref, watch } from "vue";
import { kernelApi } from "../api/kernelApi";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import {
  ETMF_QC_REVIEW_ROLE_REASON,
  ETMF_READ_ROLE_REASON,
  ETMF_STAGE_ROLE_REASON,
  canReadEtmf,
  canReviewEtmfQc,
  canStageEtmfQc,
} from "../canvas/authority";
import SupportDetails from "../components/canvas/SupportDetails.vue";
import CraQcQueue from "../components/etmf/CraQcQueue.vue";
import DocumentGrid from "../components/etmf/DocumentGrid.vue";
import ExportJobDrawer from "../components/etmf/ExportJobDrawer.vue";
import PdfPreviewModal from "../components/etmf/PdfPreviewModal.vue";
import TmfBinderTree from "../components/etmf/TmfBinderTree.vue";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { useEtmfStore } from "../stores/etmf";
import { useToastStore } from "../stores/toasts.js";
import {
  extractAssignedSites,
  extractAssignedStudies,
} from "../utils/callerScope";
import {
  formatClinicalStatus,
  formatDocumentModality,
} from "../utils/clinicalLabels";

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
const clinicalStore = useClinicalStore();
const { route, router } = resolveRouteAndRouter();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();

const activeWorkspaceTab = ref("binder");
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

function handleArtifactSelect(artifactCode) {
  if (!canRead.value) return;
  etmfStore.fetchDocuments(artifactCode);
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

function getDocSha256(doc) {
  if (!doc) return "—";
  if (doc.content_checksum) return doc.content_checksum;
  if (doc.contentChecksum) return doc.contentChecksum;
  if (doc.sha256_hash) return doc.sha256_hash;
  if (doc.sha256) return doc.sha256;
  if (doc.cryptographic_seal) return doc.cryptographic_seal;
  if (doc.merkle_hash) return doc.merkle_hash;
  const report = getIntelligenceReport(doc);
  if (report?.sha256_hash) return report.sha256_hash;
  return "—";
}

function getIntelligenceReport(doc) {
  if (!doc) return null;
  const meta = doc.metadata_json || doc.metadataJson || {};
  return meta.document_intelligence || null;
}

function hasClassifierConfidence(doc) {
  const report = getIntelligenceReport(doc);
  return typeof report?.primary_classification?.confidence === "number";
}

function getDocConfidence(doc) {
  const report = getIntelligenceReport(doc);
  if (typeof report?.primary_classification?.confidence === "number") {
    const pct = Math.round(report.primary_classification.confidence * 100);
    const tier = report.confidence_tier || "CLASSIFIED";
    return `${pct}% (${tier})`;
  }
  return "Not Classified";
}

function getDocModality(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.modality) return report.modality;
  return doc.mime_type && doc.mime_type.includes("pdf")
    ? "PDF_BINARY"
    : "STRUCTURED_FORM";
}

function getSigStatusText(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.signature_analysis) {
    const status = report.signature_analysis.status;
    if (status === "FULLY_SIGNED") return "✓ Fully Signed (Verified)";
    if (status === "PARTIALLY_SIGNED") return "⚠️ Partially Signed";
    if (status === "UNSIGNED") return "❌ Unsigned";
    if (status === "SIGNATURE_NOT_REQUIRED") return "⚪ Not Required";
  }
  if (doc.signer || doc.signing_timestamp) {
    return "✓ Signed";
  }
  return "Unsigned";
}

function getSigStatusClass(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.signature_analysis) {
    const status = report.signature_analysis.status;
    if (status === "FULLY_SIGNED") return "text-success";
    if (status === "PARTIALLY_SIGNED" || status === "UNSIGNED")
      return "text-warning";
  }
  if (doc.signer || doc.signing_timestamp) {
    return "text-success";
  }
  return "";
}

function getExtractedInvestigator(doc) {
  const report = getIntelligenceReport(doc);
  if (
    report &&
    report.extracted_metadata &&
    report.extracted_metadata.investigator_name
  ) {
    return report.extracted_metadata.investigator_name;
  }
  return doc.document_owner_id || null;
}

function getExtractedProtocol(doc) {
  const report = getIntelligenceReport(doc);
  if (
    report &&
    report.extracted_metadata &&
    report.extracted_metadata.protocol_number
  ) {
    return report.extracted_metadata.protocol_number;
  }
  return doc.study_id || null;
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
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  box-sizing: border-box;
}

.document-content-area {
  flex: 1;
  min-width: 0;
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
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
  color: var(--semantic-color-warning-dark);
}

#etmf-workspace-panel-binder,
#etmf-workspace-panel-qc-queue {
  min-width: 0;
  max-width: 100%;
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

.qc-accept-panel {
  margin-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.qc-reason-input {
  width: 100%;
  box-sizing: border-box;
  padding: 6px 8px;
  font-size: 12px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-radius: 4px;
}

.qc-role-note {
  margin: 0;
  font-size: 11px;
  color: var(--semantic-color-warning-dark);
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 4px;
  padding: 6px 8px;
}

.qc-error-note {
  margin: 0;
  font-size: 11px;
  color: var(--semantic-color-error-text-dark);
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 4px;
  padding: 6px 8px;
}

.metadata-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  max-width: 100vw;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.metadata-drawer {
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
  max-width: 100vw;
  background: var(--semantic-color-surface);
  height: 100%;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 16px rgba(0, 0, 0, 0.1);
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.btn-close-drawer {
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.drawer-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-heading {
  margin: 0;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--text-muted);
}

.meta-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.meta-label {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.meta-val {
  font-size: 13px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.font-mono {
  font-family: var(--font-mono, monospace);
}

.reason-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-left: 3px solid var(--semantic-color-primary);
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 12px;
}

.crypto-seal-card {
  padding: 10px 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px dashed var(--border);
  border-radius: 6px;
}

.sha256-hash-display {
  display: block;
  font-family: var(--font-mono, monospace);
  font-size: 11px;
  color: var(--semantic-color-primary);
  word-break: break-all;
}

.intelligence-card {
  padding: 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
}

.text-success {
  color: var(--semantic-color-success);
}

.text-warning {
  color: var(--semantic-color-warning);
}

.drawer-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
}
</style>
