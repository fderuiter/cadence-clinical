<template>
  <div
    v-if="isOpen"
    class="drawer-backdrop"
    role="presentation"
    @click.self="closeDrawer"
  >
    <div
      ref="drawerRef"
      class="usdm-concept-staging-drawer card"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-title"
      tabindex="-1"
    >
      <!-- Drawer Header -->
      <div class="drawer-header">
        <div class="header-text">
          <h3 id="drawer-title" class="drawer-title">
            📥 USDM Biomedical Concept Staging &amp; Mapping Drawer
          </h3>
          <p class="drawer-subtitle">
            Interactive concept resolution for USDM v4.0 protocol onboarding.
            Resolve unmapped tokens via NCIt/CDISC binding, tenant alias
            creation, or GxP design finding exemptions.
          </p>
        </div>
        <button
          type="button"
          class="btn-close-drawer"
          aria-label="Close drawer"
          @click="closeDrawer"
        >
          ✖
        </button>
      </div>

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
          ⚠️ Guardrail Active: All USDM lineage nodes require valid concept
          bindings or explicit design finding exemptions prior to protocol
          version commit.
        </div>
        <div v-else class="alert alert-success-inline">
          ✅ All unmapped USDM concept tokens resolved. Ready to hydrate
          protocol design lineage nodes.
        </div>
      </div>

      <!-- Filter / Search Control -->
      <div class="filter-bar">
        <input
          v-model="searchQuery"
          type="text"
          aria-label="Filter unmapped concept tokens"
          class="form-control input-search"
          placeholder="🔍 Filter unmapped concept tokens by code, name, or node ID..."
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
                🔗 Bind Registry Concept
              </button>
              <button
                type="button"
                class="tab-btn"
                :class="{ active: item.action === 'alias' }"
                @click="item.action = 'alias'"
              >
                🏷️ Create Tenant Alias
              </button>
              <button
                type="button"
                class="tab-btn"
                :class="{ active: item.action === 'finding' }"
                @click="item.action = 'finding'"
              >
                🛡️ Design Finding Exemption
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
          ✍️ 21 CFR Part 11 Electronic Signature &amp; Audit Trail
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

      <!-- Drawer Footer Actions -->
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
          ⚡ Confirm Staging Mappings &amp; Hydrate Protocol Design
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useFocusTrap } from "ui";

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

const drawerRef = ref(null);
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

useFocusTrap(drawerRef, {
  isOpen: () => props.isOpen,
  onEscape: closeDrawer,
});

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

<style scoped>
.drawer-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  z-index: 1050;
  display: flex;
  justify-content: flex-end;
}

.usdm-concept-staging-drawer {
  width: 640px;
  max-width: 90vw;
  height: 100vh;
  background-color: var(--semantic-color-surface);
  border-left: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  padding: 24px;
  overflow-y: auto;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 20px;

  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 16px;
}

.drawer-title {
  margin: 0 0 6px 0;
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--primary);
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
}

.btn-close-drawer {
  background: none;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: var(--semantic-color-text-secondary);
  padding: 4px;
}

.progress-section {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 20px;
}

.progress-info {
  display: flex;
  justify-content: space-between;
  font-size: 0.85rem;
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

.alert-warning-inline {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  color: var(--semantic-color-warning-dark);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
}

.alert-success-inline {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
}

.filter-bar {
  margin-bottom: 16px;
}

.input-search {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.85rem;
}

.unmapped-tokens-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
  flex: 1;
  margin-bottom: 24px;
}

.token-card {
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 16px;
  background-color: var(--semantic-color-surface);

  transition: border-color 0.2s ease;
}

.token-card.resolved {
  border-color: var(--semantic-color-success);
  background-color: var(--semantic-color-success-bg-soft);
}

.token-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.token-title-box {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.badge-node {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.75rem;
  padding: 2px 6px;
  border-radius: 4px;
}

.token-code {
  font-size: 0.9rem;
  color: var(--semantic-color-primary);
  font-family: monospace;
}

.token-name {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.badge-danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.75rem;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 700;
}

.badge-success {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  font-size: 0.75rem;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 700;
}

.action-type-tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 12px;
}

.tab-btn {
  flex: 1;
  padding: 6px;
  font-size: 0.75rem;
  border: 1px solid var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  border-radius: 4px;
  cursor: pointer;

  transition: all 0.2s ease;
}

.tab-btn.active {
  background-color: var(--primary);
  color: var(--semantic-color-text-inverse);
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
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  margin-bottom: 6px;
}

.binding-input-row {
  display: flex;
  gap: 8px;
}

.select-concept {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
}

.alias-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 8px;
}

.input-alias-code,
.input-alias-name {
  width: 100%;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
}

.textarea-finding {
  width: 100%;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  margin-bottom: 8px;
}

.audit-section {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 20px;
}

.audit-title {
  margin: 0 0 12px 0;
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.audit-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.input-signer-id,
.input-change-reason {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
}

.drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;

  border-top: 1px solid var(--semantic-color-border);
  padding-top: 16px;
}

.btn-confirm-mappings {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  padding: 10px 18px;
  font-size: 0.9rem;
  font-weight: 600;
  border-radius: 6px;
}

.btn-confirm-mappings:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
