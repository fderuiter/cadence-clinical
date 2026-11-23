<template>
  <!-- eslint-disable vuejs-accessibility/interactive-supports-focus, vuejs-accessibility/click-events-have-key-events -->
  <div class="documents-tree-container">
    <div class="tree-header">
      <div class="tree-title-row">
        <span class="tree-title-icon"
          ><ClinicalIcon name="folder-tree" :size="20"
        /></span>
        <h3 class="tree-title">Clinical Document Tree</h3>
      </div>
      <div class="tree-search-bar">
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Filter studies, sites, zones..."
          class="search-input"
          aria-label="Filter document folder hierarchy"
        />
      </div>
      <div class="tree-quick-filters">
        <select
          v-model="selectedStudyScope"
          class="filter-select"
          aria-label="Filter study scope"
          @change="onStudySelectChange"
        >
          <option value="">All Studies</option>
          <option
            v-for="study in studiesTree"
            :key="study.id"
            :value="study.id"
          >
            {{ study.code }} — {{ study.id }}
          </option>
        </select>
      </div>
    </div>

    <!-- Tree Root Nodes -->
    <div
      class="tree-scroll-area"
      role="tree"
      tabindex="-1"
      aria-label="Clinical Document Workspace Folder Tree"
      @keydown="handleTreeKeyDown"
    >
      <!-- "All Documents" Global Root Node -->
      <div
        class="tree-node global-root-node"
        role="treeitem"
        :aria-selected="!selectedNodeKey"
        :class="{ 'is-selected': !selectedNodeKey }"
        tabindex="0"
        @click="selectGlobalRoot"
      >
        <div class="node-header">
          <ClinicalIcon name="folder-open" :size="16" class="folder-icon" />
          <span class="node-name font-bold">All Documents (Cross-Study)</span>
        </div>
      </div>

      <div v-if="filteredStudies.length === 0" class="empty-tree-message">
        No matching studies or folders found.
      </div>

      <!-- Studies Loop -->
      <div
        v-for="study in filteredStudies"
        :key="study.id"
        class="tree-node study-node"
      >
        <div
          :id="'tree-node-' + study.id"
          class="node-header study-header"
          role="treeitem"
          :aria-expanded="isExpanded(study.id)"
          :aria-selected="selectedNodeKey === study.id"
          :class="{
            'is-expanded': isExpanded(study.id),
            'is-selected': selectedNodeKey === study.id,
          }"
          tabindex="0"
          @click="clickStudy(study)"
        >
          <span class="toggle-icon"
            ><ClinicalIcon
              :name="isExpanded(study.id) ? 'chevron-down' : 'chevron-right'"
              :size="16"
          /></span>
          <span class="folder-icon"
            ><ClinicalIcon name="building-2" :size="16"
          /></span>
          <span class="study-code">[{{ study.code }}]</span>
          <span class="node-name" :title="study.title">{{ study.id }}</span>
        </div>

        <!-- Sites Loop -->
        <div
          v-if="isExpanded(study.id)"
          class="node-children study-children"
          role="group"
        >
          <div
            v-for="site in study.sites"
            :key="site.id"
            class="tree-node site-node"
          >
            <div
              :id="'tree-node-' + study.id + '-' + site.id"
              class="node-header site-header"
              role="treeitem"
              :aria-expanded="isExpanded(study.id + '-' + site.id)"
              :aria-selected="selectedNodeKey === study.id + '-' + site.id"
              :class="{
                'is-expanded': isExpanded(study.id + '-' + site.id),
                'is-selected': selectedNodeKey === study.id + '-' + site.id,
              }"
              tabindex="0"
              @click="clickSite(study, site)"
            >
              <span class="toggle-icon"
                ><ClinicalIcon
                  :name="
                    isExpanded(study.id + '-' + site.id)
                      ? 'chevron-down'
                      : 'chevron-right'
                  "
                  :size="16"
              /></span>
              <span class="folder-icon"
                ><ClinicalIcon name="map-pin" :size="16"
              /></span>
              <span class="node-name">{{ site.name }}</span>
            </div>

            <!-- TMF Zones Loop -->
            <div
              v-if="isExpanded(study.id + '-' + site.id)"
              class="node-children site-children"
              role="group"
            >
              <div
                v-for="zone in site.zones"
                :key="zone.id"
                class="tree-node zone-node"
              >
                <div
                  :id="'tree-node-' + zone.id"
                  class="node-header zone-header"
                  role="treeitem"
                  :aria-expanded="isExpanded(zone.id)"
                  :aria-selected="selectedNodeKey === zone.id"
                  :class="{
                    'is-expanded': isExpanded(zone.id),
                    'is-selected': selectedNodeKey === zone.id,
                  }"
                  tabindex="0"
                  @click="clickZone(zone)"
                >
                  <span class="toggle-icon"
                    ><ClinicalIcon
                      :name="
                        isExpanded(zone.id) ? 'chevron-down' : 'chevron-right'
                      "
                      :size="16"
                  /></span>
                  <span class="folder-icon"
                    ><ClinicalIcon name="folder" :size="16"
                  /></span>
                  <span class="zone-label">Zone {{ zone.code }}:</span>
                  <span class="node-name">{{ zone.name }}</span>
                </div>

                <!-- Sections Loop -->
                <div
                  v-if="isExpanded(zone.id)"
                  class="node-children zone-children"
                  role="group"
                >
                  <div
                    v-for="sec in zone.sections"
                    :key="sec.id"
                    class="tree-node section-node"
                  >
                    <div
                      :id="'tree-node-' + sec.id"
                      class="node-header section-header"
                      role="treeitem"
                      :aria-expanded="isExpanded(sec.id)"
                      :aria-selected="selectedNodeKey === sec.id"
                      :class="{
                        'is-expanded': isExpanded(sec.id),
                        'is-selected': selectedNodeKey === sec.id,
                      }"
                      tabindex="0"
                      @click="clickSection(sec)"
                    >
                      <span class="toggle-icon"
                        ><ClinicalIcon
                          :name="
                            isExpanded(sec.id)
                              ? 'chevron-down'
                              : 'chevron-right'
                          "
                          :size="16"
                      /></span>
                      <span class="folder-icon"
                        ><ClinicalIcon name="folder-open" :size="16"
                      /></span>
                      <span class="section-label">S{{ sec.code }}:</span>
                      <span class="node-name">{{ sec.name }}</span>
                    </div>

                    <!-- Folders Loop -->
                    <div
                      v-if="isExpanded(sec.id)"
                      class="node-children section-children"
                      role="group"
                    >
                      <div
                        v-for="folder in sec.folders"
                        :id="'tree-node-' + folder.id"
                        :key="folder.id"
                        class="tree-node folder-node"
                        role="treeitem"
                        :aria-selected="selectedNodeKey === folder.id"
                        :class="{
                          'is-selected': selectedNodeKey === folder.id,
                        }"
                        tabindex="0"
                        @click="clickFolder(folder)"
                      >
                        <div class="node-header folder-header">
                          <span class="folder-icon"
                            ><ClinicalIcon name="file-text" :size="16"
                          /></span>
                          <span class="folder-code">[{{ folder.code }}]</span>
                          <span class="node-name">{{ folder.name }}</span>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { ClinicalIcon } from "ui";

const props = defineProps({
  studiesTree: {
    type: Array,
    required: true,
  },
  initialStudyId: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["select-node"]);

const searchQuery = ref("");
const selectedStudyScope = ref(props.initialStudyId || "");
const selectedNodeKey = ref(props.initialStudyId || null);
const expandedNodes = ref({});

// Auto-expand default studies or initial study
watch(
  () => props.studiesTree,
  (trees) => {
    if (trees && trees.length > 0) {
      trees.forEach((study) => {
        if (expandedNodes.value[study.id] === undefined) {
          expandedNodes.value[study.id] = true;
        }
      });
    }
  },
  { immediate: true }
);

watch(
  () => props.initialStudyId,
  (newVal) => {
    if (newVal) {
      selectedStudyScope.value = newVal;
      selectedNodeKey.value = newVal;
      expandedNodes.value[newVal] = true;
    }
  }
);

function isExpanded(nodeId) {
  return !!expandedNodes.value[nodeId];
}

function toggleNode(nodeId) {
  expandedNodes.value[nodeId] = !expandedNodes.value[nodeId];
}

const filteredStudies = computed(() => {
  const q = searchQuery.value.trim().toLowerCase();
  const studyFilter = selectedStudyScope.value;

  return props.studiesTree
    .filter((study) => {
      if (studyFilter && study.id !== studyFilter) return false;
      if (!q) return true;
      const matchStudy =
        study.id.toLowerCase().includes(q) ||
        study.title.toLowerCase().includes(q) ||
        study.code.toLowerCase().includes(q);
      if (matchStudy) return true;

      // check if any child site or zone matches
      return study.sites.some((site) => {
        return (
          site.name.toLowerCase().includes(q) ||
          site.id.toLowerCase().includes(q)
        );
      });
    })
    .map((study) => {
      if (q) {
        expandedNodes.value[study.id] = true;
      }
      return study;
    });
});

function selectGlobalRoot() {
  selectedNodeKey.value = null;
  emit("select-node", {
    type: "root",
    studyId: null,
    siteId: null,
    zoneCode: null,
    sectionCode: null,
    folderCode: null,
  });
}

function clickStudy(study) {
  selectedNodeKey.value = study.id;
  toggleNode(study.id);
  emit("select-node", {
    type: "study",
    studyId: study.id,
    siteId: null,
    zoneCode: null,
    sectionCode: null,
    folderCode: null,
  });
}

function clickSite(study, site) {
  const key = `${study.id}-${site.id}`;
  selectedNodeKey.value = key;
  toggleNode(key);
  emit("select-node", {
    type: "site",
    studyId: study.id,
    siteId: site.id,
    zoneCode: null,
    sectionCode: null,
    folderCode: null,
  });
}

function clickZone(zone) {
  selectedNodeKey.value = zone.id;
  toggleNode(zone.id);
  emit("select-node", {
    type: "zone",
    studyId: zone.studyId,
    siteId: zone.siteId,
    zoneCode: zone.code,
    sectionCode: null,
    folderCode: null,
  });
}

function clickSection(sec) {
  selectedNodeKey.value = sec.id;
  toggleNode(sec.id);
  emit("select-node", {
    type: "section",
    studyId: sec.studyId,
    siteId: sec.siteId,
    zoneCode: sec.zoneCode,
    sectionCode: sec.code,
    folderCode: null,
  });
}

function clickFolder(folder) {
  selectedNodeKey.value = folder.id;
  emit("select-node", {
    type: "folder",
    studyId: folder.studyId,
    siteId: folder.siteId,
    zoneCode: folder.zoneCode,
    sectionCode: folder.sectionCode,
    folderCode: folder.code,
  });
}

function onStudySelectChange() {
  if (!selectedStudyScope.value) {
    selectGlobalRoot();
  } else {
    const found = props.studiesTree.find(
      (s) => s.id === selectedStudyScope.value
    );
    if (found) {
      clickStudy(found);
    }
  }
}

function handleTreeKeyDown(e) {
  // Keydown accessibility for tree
  if (e.key === "Enter" || e.key === " ") {
    const target = e.target;
    if (target && typeof target.click === "function") {
      target.click();
    }
  }
}
</script>

<style scoped>
.documents-tree-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--semantic-color-surface);
  border-right: 1px solid var(--border);
  box-sizing: border-box;
}

.tree-header {
  padding: 16px;
  border-bottom: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 10px;
  background-color: var(--semantic-color-surface);
}

.tree-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.tree-title-icon {
  font-size: 1.2rem;
}

.tree-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--primary);
}

.search-input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.82rem;
  box-sizing: border-box;
}

.search-input:focus-visible {
  border-color: var(--accent);
}

.filter-select {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.8rem;
  background-color: var(--semantic-color-surface-muted);
  box-sizing: border-box;
}

.tree-scroll-area {
  flex: 1;
  overflow-y: auto;
  padding: 12px 8px;
}

.empty-tree-message {
  padding: 16px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  font-style: italic;
  font-size: 0.82rem;
}

.tree-node {
  margin-bottom: 2px;
}

.node-header {
  display: flex;
  align-items: center;
  padding: 6px 8px;
  border-radius: 6px;
  cursor: pointer;
  user-select: none;
  font-size: 0.82rem;
  transition: background-color 0.15s;
}

.node-header:hover {
  background-color: var(--semantic-color-surface-header);
}

.node-header.is-selected {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.global-root-node {
  margin-bottom: 8px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  padding-bottom: 4px;
}

.toggle-icon {
  font-size: 0.65rem;
  width: 14px;
  color: var(--semantic-color-text-secondary);
  flex-shrink: 0;
}

.folder-icon {
  margin-right: 6px;
  font-size: 0.95rem;
  flex-shrink: 0;
}

.study-code {
  font-family: monospace;
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--semantic-color-primary);
  margin-right: 4px;
}

.zone-label,
.section-label {
  font-weight: 700;
  margin-right: 4px;
  color: var(--semantic-color-text-body);
}

.folder-code {
  font-family: monospace;
  font-size: 0.72rem;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-interactive-primary);
  padding: 1px 4px;
  border-radius: 4px;
  margin-right: 6px;
}

.node-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.font-bold {
  font-weight: 700;
}

.node-children {
  margin-left: 12px;
  padding-left: 6px;
  border-left: 1px dashed var(--semantic-color-border-control);
}
</style>
