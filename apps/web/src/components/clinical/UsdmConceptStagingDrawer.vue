<template>
  <ClinicalDrawer
    :model-value="isOpen"
    :teleport="false"
    size="lg"
    overlay-class="drawer-backdrop"
    panel-class="usdm-concept-staging-drawer"
    aria-labelledby="drawer-title"
    @close="closeDrawer"
  >
    <template #header>
      <div class="drawer-header-bar">
        <div class="header-title-wrap">
          <Database :size="18" class="header-icon" aria-hidden="true" />
          <h3 id="drawer-title" class="drawer-title">
            USDM Biomedical Concept Staging &amp; Mapping Drawer
          </h3>
        </div>
        <button
          type="button"
          class="btn-close-drawer"
          aria-label="Close drawer"
          @click="closeDrawer"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="usdm-drawer-content">
      <p class="drawer-subtitle">
        Interactive concept resolution for USDM v4.0 protocol onboarding.
        Resolve unmapped tokens via NCIt/CDISC binding, tenant alias creation,
        or GxP design finding exemptions.
      </p>

      <!-- Resolution Progress Meter -->
      <div class="progress-section">
        <div class="progress-info">
          <span class="progress-label">
            Concept Resolution:
            <strong>{{ resolvedCount }} / {{ items.length }}</strong> Resolved
          </span>
          <span class="progress-percent">{{ resolutionPercentage }}%</span>
        </div>
        <div class="progress-bar-bg">
          <div
            class="progress-bar-fill"
            :style="{ width: `${resolutionPercentage}%` }"
            :class="{ complete: resolvedCount === items.length }"
          />
        </div>
        <div
          v-if="resolvedCount < items.length"
          class="alert alert-warning-inline"
        >
          <AlertTriangle :size="15" class="alert-icon" aria-hidden="true" />
          <span>
            Guardrail Active: All USDM lineage nodes require valid concept
            bindings or explicit design finding exemptions prior to protocol
            version commit.
          </span>
        </div>
        <div v-else class="alert alert-success-inline">
          <CheckCircle2 :size="15" class="alert-icon" aria-hidden="true" />
          <span>
            All unmapped USDM concept tokens resolved. Ready to hydrate protocol
            design lineage nodes.
          </span>
        </div>
      </div>

      <!-- Filter / Search Control -->
      <div class="filter-bar">
        <Search :size="16" class="search-input-icon" aria-hidden="true" />
        <input
          v-model="searchQuery"
          type="text"
          aria-label="Filter unmapped concept tokens"
          class="form-control input-search"
          placeholder="Filter unmapped concept tokens by code, name, or node ID..."
        />
      </div>

      <!-- Unmapped Tokens List -->
      <div class="unmapped-tokens-list">
        <div
          v-for="(item, idx) in filteredItems"
          :key="item.sourceNodeId || item.code || idx"
          class="token-card"
          :class="{ resolved: isItemResolved(item) }"
        >
          <!-- Token Header -->
          <div class="token-header">
            <div class="token-title-box">
              <span class="badge badge-node">{{
                item.sourceNodeId || item.source_node_id || "USDM Node"
              }}</span>
              <strong class="token-code">{{ item.code }}</strong>
              <span class="token-name">{{ item.name || item.code }}</span>
            </div>
            <div class="token-status">
              <span v-if="isItemResolved(item)" class="badge badge-success">
                {{ getResolutionBadge(item) }}
              </span>
              <span v-else class="badge badge-danger">UNMAPPED</span>
            </div>
          </div>

          <!-- Action Options -->
          <div class="token-actions-body">
            <div class="action-type-tabs">
              <button
                type="button"
                class="tab-btn"
                :class="{ active: item.action === 'bind' }"
                @click="item.action = 'bind'"
              >
                <Link2 :size="14" aria-hidden="true" />
                <span>Bind Registry Concept</span>
              </button>
              <button
                type="button"
                class="tab-btn"
                :class="{ active: item.action === 'alias' }"
                @click="item.action = 'alias'"
              >
                <Tag :size="14" aria-hidden="true" />
                <span>Create Tenant Alias</span>
              </button>
              <button
                type="button"
                class="tab-btn"
                :class="{ active: item.action === 'finding' }"
                @click="item.action = 'finding'"
              >
                <ShieldCheck :size="14" aria-hidden="true" />
                <span>Design Finding Exemption</span>
              </button>
            </div>

            <!-- Tab 1: Concept Binding -->
            <div v-if="item.action === 'bind'" class="action-panel">
              <label class="form-label" :for="`concept-select-${idx}`">
                Select NCIt / CDISC Concept from Concept Registry:
              </label>
              <div class="binding-input-row">
                <select
                  :id="`concept-select-${idx}`"
                  v-model="item.selectedConceptCode"
                  class="form-control select-concept"
                  @change="onConceptSelect(item)"
                >
                  <option value="">-- Choose Candidate Match --</option>
                  <option
                    v-for="cand in candidateConcepts"
                    :key="cand.code"
                    :value="cand.code"
                  >
                    {{ cand.code }} — {{ cand.decode || cand.name }}
                  </option>
                </select>
                <button
                  type="button"
                  class="btn btn-sm btn-primary btn-bind"
                  :disabled="!item.selectedConceptCode"
                  @click="bindConcept(item)"
                >
                  Bind Concept
                </button>
              </div>
            </div>

            <!-- Tab 2: Tenant Alias Creation -->
            <div v-if="item.action === 'alias'" class="action-panel">
              <div class="alias-grid">
                <div class="form-group">
                  <label class="form-label" :for="`alias-code-${idx}`"
                    >Tenant Alias Code *</label
                  >
                  <input
                    :id="`alias-code-${idx}`"
                    v-model="item.aliasCode"
                    type="text"
                    class="form-control input-alias-code"
                    placeholder="e.g. ALIAS-SPONSOR-01"
                  />
                </div>
                <div class="form-group">
                  <label class="form-label" :for="`alias-name-${idx}`"
                    >Custom Concept Name *</label
                  >
                  <input
                    :id="`alias-name-${idx}`"
                    v-model="item.aliasName"
                    type="text"
                    class="form-control input-alias-name"
                    placeholder="Custom Variable Name"
                  />
                </div>
              </div>
              <button
                type="button"
                class="btn btn-sm btn-secondary btn-create-alias"
                :disabled="!item.aliasCode || !item.aliasName"
                @click="saveAlias(item)"
              >
                Create Tenant Alias
              </button>
            </div>

            <!-- Tab 3: Design Finding Exemption -->
            <div v-if="item.action === 'finding'" class="action-panel">
              <label class="form-label" :for="`finding-note-${idx}`">
                GxP Design Finding Exemption Justification *:
              </label>
              <textarea
                :id="`finding-note-${idx}`"
                v-model="item.findingJustification"
                class="form-control textarea-finding"
                rows="2"
                placeholder="Explain why this item has no standard CDISC equivalent and is accepted as a tenant design finding..."
              />
              <button
                type="button"
                class="btn btn-sm btn-warning btn-accept-finding"
                :disabled="
                  !item.findingJustification ||
                  !item.findingJustification.trim()
                "
                @click="acceptFinding(item)"
              >
                Accept Design Finding
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Part 11 Audit Trail Verification -->
      <div class="audit-section">
        <h5 class="audit-title">
          <FileSignature :size="16" aria-hidden="true" />
          <span>21 CFR Part 11 Electronic Signature &amp; Audit Trail</span>
        </h5>
        <div class="audit-grid">
          <div class="form-group">
            <label class="form-label" for="signer-username"
              >Signer Username / ID *</label
            >
            <input
              id="signer-username"
              v-model="signerUsername"
              type="text"
              class="form-control input-signer-id"
              placeholder="e.g. designer_lead_01"
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="change-reason"
              >GxP Change Justification Reason *</label
            >
            <input
              id="change-reason"
              v-model="changeReason"
              type="text"
              class="form-control input-change-reason"
              placeholder="e.g. USDM Concept Staging & Mapping Resolution for Protocol Import"
            />
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <button
          type="button"
          class="btn btn-secondary btn-cancel"
          @click="closeDrawer"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary btn-confirm-mappings"
          :disabled="!canConfirm"
          @click="confirmAndApply"
        >
          <Zap :size="16" aria-hidden="true" />
          <span>Confirm Staging Mappings &amp; Hydrate Protocol Design</span>
        </button>
      </div>
    </template>
  </ClinicalDrawer>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { ClinicalDrawer } from "ui";
import {
  AlertTriangle,
  CheckCircle2,
  Database,
  FileSignature,
  Link2,
  Search,
  ShieldCheck,
  Tag,
  X,
  Zap,
} from "lucide-vue-next";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  unmappedTokens: {
    type: Array,
    default: () => [],
  },
  candidateConcepts: {
    type: Array,
    default: () => [
      { code: "C25298", decode: "Systolic Blood Pressure" },
      { code: "C16468", decode: "Informed Consent Form" },
      { code: "C38054", decode: "12-Lead Electrocardiogram" },
      { code: "C49286", decode: "Safety Laboratory Panel" },
      { code: "C120857", decode: "Visual Analog Scale Pain Score" },
      { code: "C20989", decode: "Physical Examination" },
      { code: "C15003", decode: "Heart Rate Assessment" },
      { code: "C15004", decode: "Body Temperature Assessment" },
    ],
  },
});

const emit = defineEmits(["update:isOpen", "close", "confirm-mappings"]);

const searchQuery = ref("");
const signerUsername = ref("");
const changeReason = ref("USDM Concept Staging & Mapping Resolution");

const items = ref([]);

watch(
  () => props.unmappedTokens,
  (newTokens) => {
    if (Array.isArray(newTokens) && newTokens.length > 0) {
      items.value = newTokens.map((t) => ({
        code: t.code || t.Code || "C00000",
        name: t.name || t.Name || t.code || "Unmapped Concept",
        sourceNodeId:
          t.sourceNodeId || t.SourceNodeId || t.source_node_id || "Node",
        action: "bind",
        selectedConceptCode: t.code || "",
        aliasCode: "",
        aliasName: "",
        findingJustification: "",
        isResolved: false,
        resolutionType: null, // 'BOUND' | 'ALIAS' | 'EXEMPTION'
        resolvedConceptCode: null,
        resolvedConceptName: null,
      }));
    } else {
      items.value = [];
    }
  },
  { immediate: true, deep: true }
);

const filteredItems = computed(() => {
  if (!searchQuery.value.trim()) return items.value;
  const q = searchQuery.value.toLowerCase();
  return items.value.filter(
    (i) =>
      i.code.toLowerCase().includes(q) ||
      (i.name && i.name.toLowerCase().includes(q)) ||
      (i.sourceNodeId && i.sourceNodeId.toLowerCase().includes(q))
  );
});

const isItemResolved = (item) => Boolean(item.isResolved);

const resolvedCount = computed(() => items.value.filter(isItemResolved).length);

const resolutionPercentage = computed(() => {
  if (!items.value.length) return 100;
  return Math.round((resolvedCount.value / items.value.length) * 100);
});

const canConfirm = computed(() => {
  return (
    items.value.length > 0 &&
    resolvedCount.value === items.value.length &&
    signerUsername.value.trim() !== "" &&
    changeReason.value.trim() !== ""
  );
});

const getResolutionBadge = (item) => {
  if (item.resolutionType === "BOUND")
    return `BOUND: ${item.resolvedConceptCode}`;
  if (item.resolutionType === "ALIAS") return `ALIAS: ${item.aliasCode}`;
  if (item.resolutionType === "EXEMPTION") return "EXEMPTION ACCEPTED";
  return "RESOLVED";
};

const onConceptSelect = (item) => {
  const match = props.candidateConcepts.find(
    (c) => c.code === item.selectedConceptCode
  );
  if (match) {
    item.resolvedConceptCode = match.code;
    item.resolvedConceptName = match.decode || match.name;
  }
};

const bindConcept = (item) => {
  if (!item.selectedConceptCode) return;
  const match = props.candidateConcepts.find(
    (c) => c.code === item.selectedConceptCode
  );
  item.resolvedConceptCode = item.selectedConceptCode;
  item.resolvedConceptName = match
    ? match.decode || match.name
    : item.selectedConceptCode;
  item.resolutionType = "BOUND";
  item.isResolved = true;
};

const saveAlias = (item) => {
  if (!item.aliasCode || !item.aliasName) return;
  item.resolvedConceptCode = item.aliasCode;
  item.resolvedConceptName = item.aliasName;
  item.resolutionType = "ALIAS";
  item.isResolved = true;
};

const acceptFinding = (item) => {
  if (!item.findingJustification || !item.findingJustification.trim()) return;
  item.resolvedConceptCode = item.code;
  item.resolvedConceptName = item.name;
  item.resolutionType = "EXEMPTION";
  item.isResolved = true;
};

const closeDrawer = () => {
  emit("update:isOpen", false);
  emit("close");
};

const confirmAndApply = () => {
  if (!canConfirm.value) return;

  const resolvedMappings = items.value.map((item) => ({
    sourceNodeId: item.sourceNodeId,
    originalCode: item.code,
    resolvedCode: item.resolvedConceptCode,
    resolvedName: item.resolvedConceptName,
    resolutionType: item.resolutionType,
    findingJustification: item.findingJustification || null,
  }));

  const auditEntry = {
    signerUsername: signerUsername.value,
    changeReason: changeReason.value,
    timestamp: new Date().toISOString(),
    resolvedMappingsCount: resolvedMappings.length,
  };

  emit("confirm-mappings", {
    resolvedMappings,
    auditEntry,
  });

  closeDrawer();
};
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
.header-icon {
  color: var(--semantic-color-primary);
}

.drawer-title {
  color: var(--text-main);
}

.usdm-drawer-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.drawer-subtitle {
  margin: 0;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
}

.progress-section {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px;
  font-variant-numeric: tabular-nums;
}

.progress-info {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-body);
  margin-bottom: 8px;
}

.progress-bar-bg {
  height: 8px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 4px;
  overflow: hidden;
  margin-bottom: 12px;
}

.progress-bar-fill {
  height: 100%;
  background-color: var(--semantic-color-warning-action);
  transition: width 0.3s ease;
}

.progress-bar-fill.complete {
  background-color: var(--semantic-color-success);
}

.alert-warning-inline,
.alert-success-inline {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: var(--font-size-xs);
  line-height: 1.4;
}

.alert-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.alert-warning-inline {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  color: var(--semantic-color-warning-text);
}

.alert-success-inline {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.filter-bar {
  position: relative;
  display: flex;
  align-items: center;
}

.search-input-icon {
  position: absolute;
  left: 12px;
  color: var(--semantic-color-text-secondary);
  pointer-events: none;
}

.input-search {
  width: 100%;
  min-height: 44px;
  padding: 8px 12px 8px 36px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: var(--font-size-md);
  box-sizing: border-box;
}

.unmapped-tokens-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.token-card {
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 14px;
  background-color: var(--semantic-color-surface);
  transition: border-color 0.2s ease;
  min-width: 0;
}

.token-card.resolved {
  border-color: var(--semantic-color-success);
  background-color: var(--semantic-color-success-bg-soft);
}

.token-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.token-title-box {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
}

.badge-node {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
}

.token-code {
  font-size: var(--font-size-sm);
  color: var(--text-main);
  font-family: var(--semantic-font-mono);
  word-break: break-word;
}

.token-name {
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
  word-break: break-word;
}

.badge-danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-size: var(--font-size-2xs);
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 700;
}

.badge-success {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  font-size: var(--font-size-2xs);
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 700;
}

.action-type-tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.tab-btn {
  flex: 1 1 150px;
  min-height: 44px;
  padding: 8px 10px;
  font-size: var(--font-size-xs);
  font-weight: 600;
  border: 1px solid var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  transition: all 0.2s ease;
}

.tab-btn.active {
  background-color: var(--primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--primary);
}

.action-panel {
  background-color: var(--semantic-color-surface-muted);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
}

.form-label {
  display: block;
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--semantic-color-text-body);
  margin-bottom: 6px;
}

.binding-input-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.select-concept,
.input-alias-code,
.input-alias-name,
.textarea-finding,
.input-signer-id,
.input-change-reason {
  width: 100%;
  min-height: 44px;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: var(--font-size-md);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-sizing: border-box;
}

.select-concept {
  flex: 1 1 200px;
  min-width: 0;
}

.alias-grid,
.audit-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 8px;
}

.audit-grid {
  margin-bottom: 0;
}

.textarea-finding {
  margin-bottom: 8px;
}

.btn-bind,
.btn-create-alias,
.btn-accept-finding,
.btn-cancel,
.btn-confirm-mappings {
  min-height: 44px;
  padding: 8px 14px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.audit-section {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px;
}

.audit-title {
  margin: 0 0 12px 0;
  font-size: var(--font-size-sm);
  font-weight: 700;
  color: var(--semantic-color-text-dark);
  display: flex;
  align-items: center;
  gap: 6px;
}

.drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
}

.btn-confirm-mappings {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  font-size: var(--font-size-sm);
  font-weight: 600;
  border-radius: 6px;
  text-align: center;
}

.btn-confirm-mappings:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@media (max-width: 640px) {
  .alias-grid,
  .audit-grid {
    grid-template-columns: 1fr;
  }

  .binding-input-row,
  .drawer-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .btn-bind,
  .btn-create-alias,
  .btn-accept-finding,
  .btn-cancel,
  .btn-confirm-mappings {
    width: 100%;
  }
}
</style>
