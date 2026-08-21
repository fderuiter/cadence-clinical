<template>
  <div
    v-if="codingStore.browserModal.isOpen"
    class="modal-overlay"
    @click.self="close"
  >
    <div class="modal dictionary-browser-modal">
      <!-- Modal Header -->
      <div class="modal-header">
        <div class="header-titles">
          <h3>
            {{
              selectedDictType === "MEDDRA"
                ? "MedDRA Hierarchy Browser"
                : "WHODrug ATC Classification Browser"
            }}
          </h3>
          <span class="header-subtitle">
            Coding target:
            <strong>{{ targetVerbatim || "Manual Selection" }}</strong>
            <span v-if="targetSourceField"> ({{ targetSourceField }})</span>
          </span>
        </div>
        <button
          type="button"
          class="btn-close"
          @click="close"
        >
          ✕
        </button>
      </div>

      <!-- Modal Body -->
      <div class="modal-body-browser">
        <!-- Search & Filter Controls -->
        <div class="browser-controls-grid">
          <div class="form-group flex-2">
            <label for="dict-search-term">Search Term / Code</label>
            <div class="search-input-group">
              <input
                id="dict-search-term"
                v-model="searchTerm"
                type="text"
                class="form-control"
                placeholder="Type verbatim or code..."
                @keyup.enter="performSearch"
              >
              <button
                type="button"
                class="btn btn-primary"
                :disabled="!searchTerm.trim() || codingStore.isSearching"
                @click="performSearch"
              >
                {{ codingStore.isSearching ? "Searching..." : "Search" }}
              </button>
            </div>
          </div>

          <div class="form-group">
            <label for="dict-type-select">Dictionary</label>
            <select
              id="dict-type-select"
              v-model="selectedDictType"
              class="form-control"
              @change="onDictionaryTypeChange"
            >
              <option value="MEDDRA">
                MedDRA
              </option>
              <option value="WHODRUG">
                WHODrug
              </option>
            </select>
          </div>

          <div class="form-group">
            <label for="dict-version-select">Version</label>
            <select
              id="dict-version-select"
              v-model="selectedVersion"
              class="form-control"
              @change="performSearch"
            >
              <template v-if="selectedDictType === 'MEDDRA'">
                <option value="26.0">
                  v26.0 (Active)
                </option>
                <option value="26.1">
                  v26.1
                </option>
                <option value="27.0">
                  v27.0 (Target)
                </option>
              </template>
              <template v-else>
                <option value="2024-03">
                  v2024-03 (Active)
                </option>
                <option value="2024-09">
                  v2024-09 (Target)
                </option>
              </template>
            </select>
          </div>

          <div
            v-if="selectedDictType === 'MEDDRA'"
            class="form-group"
          >
            <label for="dict-level-select">Target Level</label>
            <select
              id="dict-level-select"
              v-model="selectedLevel"
              class="form-control"
              @change="performSearch"
            >
              <option value="LLT">
                LLT (Lowest Level Term)
              </option>
              <option value="PT">
                PT (Preferred Term)
              </option>
              <option value="HLT">
                HLT (High Level Term)
              </option>
              <option value="HLGT">
                HLGT (High Level Group Term)
              </option>
              <option value="SOC">
                SOC (System Organ Class)
              </option>
            </select>
          </div>
        </div>

        <!-- Browser Split View: Results List (Left) & Hierarchy / ATC Tree (Right) -->
        <div class="browser-split-layout">
          <!-- Left Column: Search Results Matches -->
          <div class="results-column card">
            <div class="column-header">
              <h4>Matches &amp; Suggestions</h4>
              <span
                v-if="codingStore.searchStatus"
                class="badge"
                :class="
                  codingStore.searchStatus === 'AUTO-CODED'
                    ? 'badge-success'
                    : codingStore.searchStatus === 'SUGGESTIONS'
                      ? 'badge-info'
                      : 'badge-neutral'
                "
              >
                {{ codingStore.searchStatus }}
              </span>
            </div>

            <!-- Loading Spinner -->
            <div
              v-if="codingStore.isSearching"
              class="results-loading"
            >
              <div class="spinner" />
              <span>Searching terminology index...</span>
            </div>

            <!-- Empty Results -->
            <div
              v-else-if="codingStore.dictionarySearchResults.length === 0"
              class="results-empty"
            >
              <p v-if="searchTerm">
                No matching terms found for "<strong>{{ searchTerm }}</strong>".
              </p>
              <p v-else>
                Enter a term or code above to search dictionary.
              </p>
            </div>

            <!-- Results List -->
            <div
              v-else
              class="results-list"
            >
              <div
                v-for="(match, idx) in codingStore.dictionarySearchResults"
                :key="idx"
                class="result-item-card"
                :class="{
                  'active-result': isMatchSelected(match, idx),
                }"
                @click="selectMatch(match, idx)"
              >
                <div class="result-card-header">
                  <div class="result-term-name">
                    {{ getMatchTerm(match) }}
                  </div>
                  <span class="match-score-badge">
                    {{ Math.round((match.score || 0) * 100) }}% match
                  </span>
                </div>
                <div class="result-card-meta">
                  <span class="code-pill">Code: {{ getMatchCode(match) }}</span>
                  <span
                    v-if="match.primary_soc_flag === 'Y'"
                    class="soc-flag-pill"
                  >
                    Primary SOC
                  </span>
                </div>
              </div>
            </div>
          </div>

          <!-- Right Column: Hierarchy / Context Inspection & Confirmation -->
          <div class="hierarchy-column card">
            <div class="column-header">
              <h4>Hierarchy &amp; Context Details</h4>
            </div>

            <div
              v-if="!activeMatch"
              class="hierarchy-empty-state"
            >
              <p>Select a match on the left to inspect its full hierarchy.</p>
            </div>

            <div
              v-else
              class="hierarchy-content"
            >
              <!-- Selected Concept Summary Banner -->
              <div class="concept-summary-banner">
                <div class="concept-title">
                  {{ getMatchTerm(activeMatch) }}
                </div>
                <div class="concept-code">
                  Code: <code>{{ getMatchCode(activeMatch) }}</code>
                </div>
              </div>

              <!-- MedDRA 5-Level Hierarchy Path -->
              <div
                v-if="selectedDictType === 'MEDDRA'"
                class="hierarchy-tree-view"
              >
                <h5>MedDRA 5-Level Structural Path</h5>
                <div class="tree-nodes-list">
                  <div class="tree-node node-soc">
                    <div class="node-tag">
                      SOC
                    </div>
                    <div class="node-info">
                      <div class="node-name">
                        {{ activeMatch.soc_name || "—" }}
                      </div>
                      <div class="node-code">
                        Code: {{ activeMatch.soc_code || "—" }}
                      </div>
                    </div>
                  </div>

                  <div class="tree-connector">
                    ↓
                  </div>

                  <div class="tree-node node-hlgt">
                    <div class="node-tag">
                      HLGT
                    </div>
                    <div class="node-info">
                      <div class="node-name">
                        {{ activeMatch.hlgt_name || "—" }}
                      </div>
                      <div class="node-code">
                        Code: {{ activeMatch.hlgt_code || "—" }}
                      </div>
                    </div>
                  </div>

                  <div class="tree-connector">
                    ↓
                  </div>

                  <div class="tree-node node-hlt">
                    <div class="node-tag">
                      HLT
                    </div>
                    <div class="node-info">
                      <div class="node-name">
                        {{ activeMatch.hlt_name || "—" }}
                      </div>
                      <div class="node-code">
                        Code: {{ activeMatch.hlt_code || "—" }}
                      </div>
                    </div>
                  </div>

                  <div class="tree-connector">
                    ↓
                  </div>

                  <div class="tree-node node-pt">
                    <div class="node-tag">
                      PT
                    </div>
                    <div class="node-info">
                      <div class="node-name">
                        {{ activeMatch.pt_name || "—" }}
                      </div>
                      <div class="node-code">
                        Code: {{ activeMatch.pt_code || "—" }}
                      </div>
                    </div>
                  </div>

                  <div class="tree-connector">
                    ↓
                  </div>

                  <div class="tree-node node-llt active-target-node">
                    <div class="node-tag">
                      LLT
                    </div>
                    <div class="node-info">
                      <div class="node-name">
                        {{ activeMatch.llt_name || "—" }}
                      </div>
                      <div class="node-code">
                        Code: {{ activeMatch.llt_code || "—" }}
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <!-- WHODrug ATC Tree & Ingredients -->
              <div
                v-else
                class="whodrug-details-view"
              >
                <h5>Anatomical Therapeutic Chemical (ATC) Context</h5>
                <div
                  v-if="
                    activeMatch.atc_context &&
                      activeMatch.atc_context.length > 0
                  "
                  class="atc-list"
                >
                  <div
                    v-for="(atc, aIdx) in activeMatch.atc_context"
                    :key="aIdx"
                    class="atc-card"
                  >
                    <div class="atc-code-pill">
                      {{ atc.atc_code }}
                    </div>
                    <div class="atc-description">
                      {{ atc.description }}
                    </div>
                  </div>
                </div>
                <div
                  v-else
                  class="text-muted-notice"
                >
                  No ATC classifications attached to this drug record.
                </div>

                <h5 style="margin-top: 16px">
                  Active Ingredients
                </h5>
                <div
                  v-if="
                    activeMatch.ingredients &&
                      activeMatch.ingredients.length > 0
                  "
                  class="ingredient-chips"
                >
                  <span
                    v-for="(ing, iIdx) in activeMatch.ingredients"
                    :key="iIdx"
                    class="ingredient-chip"
                  >
                    🧪 {{ ing.ingredient_name }} ({{ ing.ingredient_code }})
                  </span>
                </div>
                <div
                  v-else
                  class="text-muted-notice"
                >
                  No specific single active ingredients indexed.
                </div>
              </div>

              <!-- Part 11 Reason for Change GxP Input -->
              <div class="gxp-confirmation-box">
                <div class="form-group">
                  <label for="override-reason">21 CFR Part 11 Reason for Coding Assignment *</label>
                  <input
                    id="override-reason"
                    v-model="gxpReason"
                    type="text"
                    class="form-control"
                    placeholder="e.g. Coder manual classification after dictionary verification"
                  >
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Modal Footer -->
      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          @click="close"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="!activeMatch || !gxpReason.trim() || isSubmitting"
          @click="applySelectedCode"
        >
          {{ isSubmitting ? "Applying..." : "Assign & Commit Code" }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useCodingStore } from "../stores/coding";

const codingStore = useCodingStore();

const searchTerm = ref("");
const selectedDictType = ref("MEDDRA");
const selectedVersion = ref("26.0");
const selectedLevel = ref("LLT");
const activeMatch = ref(null);
const activeMatchIndex = ref(-1);
const gxpReason = ref("Manual coding decision via Dictionary Browser");
const isSubmitting = ref(false);

const targetAssignment = computed(() => codingStore.browserModal.assignment);
const targetVerbatim = computed(
  () => targetAssignment.value?.verbatim_text || ""
);
const targetSourceField = computed(
  () => targetAssignment.value?.source_field || ""
);

// Sync with store state on modal open
watch(
  () => codingStore.browserModal.isOpen,
  (newVal) => {
    if (newVal) {
      searchTerm.value = codingStore.browserModal.searchTerm || "";
      selectedDictType.value =
        codingStore.browserModal.dictionaryType || "MEDDRA";
      selectedVersion.value =
        codingStore.browserModal.version ||
        (selectedDictType.value === "MEDDRA" ? "26.0" : "2024-03");
      selectedLevel.value = "LLT";
      activeMatch.value = null;
      activeMatchIndex.value = -1;
      gxpReason.value = "Manual coding decision via Dictionary Browser";
    }
  }
);

// Auto-select first match when search results update
watch(
  () => codingStore.dictionarySearchResults,
  (newResults) => {
    if (newResults && newResults.length > 0) {
      activeMatch.value = newResults[0];
      activeMatchIndex.value = 0;
    } else {
      activeMatch.value = null;
      activeMatchIndex.value = -1;
    }
  }
);

function onDictionaryTypeChange() {
  selectedVersion.value =
    selectedDictType.value === "MEDDRA" ? "26.0" : "2024-03";
  performSearch();
}

function performSearch() {
  if (!searchTerm.value.trim()) return;
  codingStore.searchDictionary({
    term: searchTerm.value,
    dictionaryType: selectedDictType.value,
    version: selectedVersion.value,
    targetLevel: selectedLevel.value,
  });
}

function selectMatch(match, index) {
  activeMatch.value = match;
  activeMatchIndex.value = index;
}

function isMatchSelected(match, index) {
  return activeMatchIndex.value === index;
}

function getMatchTerm(match) {
  if (!match) return "";
  return (
    match.llt_name ||
    match.pt_name ||
    match.preferred_name ||
    match.drug_name ||
    "Term"
  );
}

function getMatchCode(match) {
  if (!match) return "";
  return match.llt_code || match.pt_code || match.drug_code || match.code || "";
}

async function applySelectedCode() {
  if (!activeMatch.value || !targetAssignment.value) return;
  isSubmitting.value = true;
  try {
    const code = getMatchCode(activeMatch.value);
    const term = getMatchTerm(activeMatch.value);

    await codingStore.applyAction({
      assignmentId: targetAssignment.value.id,
      action: "OVERRIDE",
      code,
      term,
      reasonForChange: gxpReason.value,
    });

    close();
  } catch (err) {
    console.error("Failed to assign code:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function close() {
  codingStore.closeBrowser();
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.65);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1050;
}

.dictionary-browser-modal {
  background-color: white;
  width: 95%;
  max-width: 1080px;
  max-height: 90vh;
  border-radius: 8px;
  box-shadow: 0 20px 35px rgba(0, 0, 0, 0.25);
  display: flex;
  flex-direction: column;
}

.modal-header {
  padding: 16px 24px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  background-color: #f8fafc;
}

.header-titles h3 {
  margin: 0;
  font-size: 1.25rem;
  color: var(--primary);
}

.header-subtitle {
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.3rem;
  cursor: pointer;
  color: var(--neutral-dark);
}

.modal-body-browser {
  padding: 20px 24px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
  flex: 1;
}

.browser-controls-grid {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  align-items: flex-end;
  background-color: #f8fafc;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid var(--border);
}

.flex-2 {
  flex: 2;
  min-width: 260px;
}

.search-input-group {
  display: flex;
  gap: 8px;
}

.browser-split-layout {
  display: grid;
  grid-template-columns: 1fr 1.3fr;
  gap: 16px;
  min-height: 380px;
}

@media (max-width: 768px) {
  .browser-split-layout {
    grid-template-columns: 1fr;
  }
}

.card {
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 16px;
  display: flex;
  flex-direction: column;
}

.column-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border);
}

.column-header h4 {
  margin: 0;
  font-size: 0.95rem;
  color: var(--primary);
}

.results-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  overflow-y: auto;
  max-height: 360px;
}

.result-item-card {
  padding: 10px 14px;
  border: 1px solid var(--border);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
  background-color: #ffffff;
}

.result-item-card:hover {
  border-color: var(--accent);
  background-color: rgba(2, 101, 151, 0.03);
}

.active-result {
  border-color: var(--accent) !important;
  background-color: #eff6ff !important;
  box-shadow: 0 0 0 2px var(--accent-bg);
}

.result-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.result-term-name {
  font-weight: 600;
  color: var(--primary);
  font-size: 0.92rem;
}

.match-score-badge {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--accent);
  background-color: white;
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid #bfdbfe;
}

.result-card-meta {
  margin-top: 6px;
  display: flex;
  gap: 6px;
  align-items: center;
}

.code-pill {
  font-size: 0.78rem;
  background-color: #f1f5f9;
  color: #334155;
  padding: 2px 6px;
  border-radius: 4px;
}

.soc-flag-pill {
  font-size: 0.72rem;
  font-weight: 600;
  background-color: var(--success-bg);
  color: var(--success);
  padding: 2px 6px;
  border-radius: 4px;
}

.concept-summary-banner {
  background-color: #f0f9ff;
  border: 1px solid #bae6fd;
  padding: 12px;
  border-radius: 6px;
  margin-bottom: 16px;
}

.concept-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: #0369a1;
}

.concept-code {
  font-size: 0.85rem;
  color: #0284c7;
  margin-top: 2px;
}

.hierarchy-tree-view,
.whodrug-details-view {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.hierarchy-tree-view h5,
.whodrug-details-view h5 {
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--neutral-dark);
  margin-bottom: 6px;
}

.tree-nodes-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.tree-node {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  border-radius: 6px;
  background-color: #f8fafc;
  border: 1px solid var(--border);
}

.active-target-node {
  background-color: #e0f2fe;
  border-color: #7dd3fc;
}

.node-tag {
  font-weight: 800;
  font-size: 0.75rem;
  width: 44px;
  text-align: center;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--primary);
  color: white;
}

.node-info {
  display: flex;
  flex-direction: column;
}

.node-name {
  font-weight: 600;
  font-size: 0.88rem;
  color: var(--primary);
}

.node-code {
  font-size: 0.75rem;
  color: var(--neutral-dark);
}

.tree-connector {
  text-align: center;
  color: var(--neutral-dark);
  font-size: 0.8rem;
  line-height: 1;
}

.atc-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.atc-card {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background-color: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.atc-code-pill {
  font-family: var(--font-mono, monospace);
  font-weight: 700;
  font-size: 0.85rem;
  background-color: #e2e8f0;
  padding: 2px 8px;
  border-radius: 4px;
}

.atc-description {
  font-size: 0.85rem;
  color: var(--primary);
}

.ingredient-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ingredient-chip {
  background-color: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 4px 10px;
  border-radius: 16px;
  font-size: 0.8rem;
}

.gxp-confirmation-box {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
}

.modal-footer {
  padding: 16px 24px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  background-color: #f8fafc;
  border-bottom-left-radius: 8px;
  border-bottom-right-radius: 8px;
}

.results-loading,
.results-empty,
.hierarchy-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 36px 16px;
  text-align: center;
  color: var(--neutral-dark);
  font-size: 0.88rem;
}

.text-muted-notice {
  font-size: 0.8rem;
  color: var(--neutral-dark);
  font-style: italic;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--primary);
}

.form-control {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.88rem;
  font-family: inherit;
}

.badge {
  display: inline-block;
  padding: 2px 8px;
  font-size: 0.72rem;
  font-weight: 700;
  border-radius: 12px;
}

.badge-success {
  background-color: var(--success-bg);
  color: var(--success);
}

.badge-info {
  background-color: #e0f2fe;
  color: #0369a1;
}

.badge-neutral {
  background-color: #f1f5f9;
  color: #475569;
}
</style>
