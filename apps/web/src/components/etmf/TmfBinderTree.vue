<template>
  <div class="tmf-binder-tree-container">
    <div class="tree-search-bar">
      <input
        v-model="searchQuery"
        type="text"
        placeholder="Search zones, sections, or artifacts..."
        class="search-input"
      />
      <div class="filter-controls">
        <select v-model="selectedZoneFilter" class="zone-filter-select">
          <option value="">All Zones</option>
          <option v-for="node in tree" :key="node.id" :value="node.code">
            Zone {{ node.code }}: {{ node.name }}
          </option>
        </select>
      </div>
    </div>

    <div
      class="tree-root-nodes"
      role="tree"
      tabindex="0"
      aria-label="TMF Binder Folder Tree"
      @keydown="handleTreeKeyDown"
    >
      <div v-if="filteredTree.length === 0" class="empty-tree-message">
        No matching TMF items found.
      </div>
      <div
        v-for="zone in filteredTree"
        :key="zone.id"
        class="tree-node zone-node"
      >
        <div
          :id="'tree-node-' + zone.id"
          class="node-header zone-header"
          role="treeitem"
          :aria-expanded="isExpanded(zone.id)"
          :aria-selected="false"
          tabindex="0"
          :class="{ 'is-expanded': isExpanded(zone.id) }"
          @click="clickZone(zone)"
          @keydown.enter="clickZone(zone)"
          @keydown.space.prevent="clickZone(zone)"
        >
          <span class="toggle-icon">{{ isExpanded(zone.id) ? "▼" : "▶" }}</span>
          <span class="folder-icon">📂</span>
          <span class="zone-label">Zone {{ zone.code }}:</span>
          <span class="node-name">{{ zone.name }}</span>
          <span
            v-if="getUnreadBadgeCount(zone)"
            class="unread-badge zone-badge"
          >
            {{ getUnreadBadgeCount(zone) }}
          </span>
        </div>

        <div
          v-if="isExpanded(zone.id)"
          class="node-children zone-children"
          role="group"
        >
          <div
            v-for="section in zone.children"
            :key="section.id"
            class="tree-node section-node"
          >
            <div
              :id="'tree-node-' + section.id"
              class="node-header section-header"
              role="treeitem"
              :aria-expanded="isExpanded(section.id)"
              :aria-selected="false"
              tabindex="0"
              :class="{ 'is-expanded': isExpanded(section.id) }"
              @click="clickSection(section)"
              @keydown.enter="clickSection(section)"
              @keydown.space.prevent="clickSection(section)"
            >
              <span class="toggle-icon">{{
                isExpanded(section.id) ? "▼" : "▶"
              }}</span>
              <span class="folder-icon">📁</span>
              <span class="section-label">Section {{ section.code }}:</span>
              <span class="node-name">{{ section.name }}</span>
              <span
                v-if="getUnreadBadgeCount(section)"
                class="unread-badge section-badge"
              >
                {{ getUnreadBadgeCount(section) }}
              </span>
            </div>

            <div
              v-if="isExpanded(section.id)"
              class="node-children section-children"
              role="group"
            >
              <div
                v-for="artifact in section.children"
                :id="'tree-node-' + artifact.id"
                :key="artifact.id"
                class="tree-node artifact-node"
                role="treeitem"
                :aria-selected="selectedArtifactId === artifact.code"
                tabindex="0"
                :class="{ 'is-selected': selectedArtifactId === artifact.code }"
                @click="clickArtifact(artifact)"
                @keydown.enter="clickArtifact(artifact)"
                @keydown.space.prevent="clickArtifact(artifact)"
              >
                <div class="node-header artifact-header">
                  <span class="file-icon">📄</span>
                  <span class="artifact-code">[{{ artifact.code }}]</span>
                  <span class="node-name">{{ artifact.name }}</span>
                  <span
                    v-if="getUnreadBadgeCount(artifact)"
                    class="unread-badge artifact-badge"
                  >
                    {{ getUnreadBadgeCount(artifact) }}
                  </span>
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
import { ref, computed, watch, nextTick } from "vue";

const props = defineProps({
  tree: {
    type: Array,
    required: true,
  },
});

const emit = defineEmits(["select-artifact"]);

const searchQuery = ref("");
const selectedZoneFilter = ref("");
const selectedArtifactId = ref(null);

// Tracks open state of collapsible nodes
const expandedNodes = ref({});

// Filtering algorithm: filters tree recursively and expands matching hierarchy nodes
const filteredTree = computed(() => {
  const query = searchQuery.value.trim().toLowerCase();
  const zoneFilter = selectedZoneFilter.value;

  return props.tree
    .map((zone) => {
      // Zone level filter checks
      if (zoneFilter && zone.code !== zoneFilter) {
        return null;
      }

      // If search query is empty, return unmodified
      if (!query) {
        return zone;
      }

      // Filter sections
      const matchingSections = (zone.children || [])
        .map((section) => {
          // Filter artifacts
          const matchingArtifacts = (section.children || []).filter((art) => {
            return (
              art.name.toLowerCase().includes(query) ||
              art.code.toLowerCase().includes(query)
            );
          });

          const sectionMatchesQuery =
            section.name.toLowerCase().includes(query) ||
            section.code.toLowerCase().includes(query);

          if (matchingArtifacts.length > 0 || sectionMatchesQuery) {
            // Auto expand matching sections
            expandedNodes.value[section.id] = true;
            return {
              ...section,
              children:
                matchingArtifacts.length > 0
                  ? matchingArtifacts
                  : section.children,
            };
          }
          return null;
        })
        .filter((sec) => sec !== null);

      const zoneMatchesQuery =
        zone.name.toLowerCase().includes(query) ||
        zone.code.toLowerCase().includes(query);

      if (matchingSections.length > 0 || zoneMatchesQuery) {
        // Auto expand matching zones
        expandedNodes.value[zone.id] = true;
        return {
          ...zone,
          children:
            matchingSections.length > 0 ? matchingSections : zone.children,
        };
      }

      return null;
    })
    .filter((zone) => zone !== null);
});

// Initialize expandedNodes with all zone nodes default open
watch(
  () => props.tree,
  (newTree) => {
    if (newTree && newTree.length > 0) {
      newTree.forEach((zone) => {
        if (expandedNodes.value[zone.id] === undefined) {
          expandedNodes.value[zone.id] = false;
        }
      });
    }
  },
  { immediate: true }
);

function toggleNode(nodeId) {
  expandedNodes.value[nodeId] = !expandedNodes.value[nodeId];
}

function isExpanded(nodeId) {
  return !!expandedNodes.value[nodeId];
}

function selectArtifact(artifact) {
  selectedArtifactId.value = artifact.code;
  emit("select-artifact", artifact.code);
}

const activeFocusedNodeId = ref(null);

// Initialize activeFocusedNodeId if not set
watch(
  filteredTree,
  (newVal) => {
    if (newVal && newVal.length > 0 && !activeFocusedNodeId.value) {
      activeFocusedNodeId.value = newVal[0].id;
    }
  },
  { immediate: true }
);

// Flat visible list
const visibleNodesList = computed(() => {
  const list = [];
  filteredTree.value.forEach((zone) => {
    list.push({ id: zone.id, code: zone.code, node: zone, type: "zone" });
    if (isExpanded(zone.id)) {
      (zone.children || []).forEach((section) => {
        list.push({
          id: section.id,
          code: section.code,
          node: section,
          type: "section",
          parentId: zone.id,
        });
        if (isExpanded(section.id)) {
          (section.children || []).forEach((artifact) => {
            list.push({
              id: artifact.id,
              code: artifact.code,
              node: artifact,
              type: "artifact",
              parentId: section.id,
            });
          });
        }
      });
    }
  });
  return list;
});

// Focus helper
function focusNodeId(nodeId) {
  activeFocusedNodeId.value = nodeId;
  nextTick(() => {
    const el = document.getElementById(`tree-node-${nodeId}`);
    if (el) {
      el.focus();
    }
  });
}

function clickZone(zone) {
  activeFocusedNodeId.value = zone.id;
  toggleNode(zone.id);
}

function clickSection(section) {
  activeFocusedNodeId.value = section.id;
  toggleNode(section.id);
}

function clickArtifact(artifact) {
  activeFocusedNodeId.value = artifact.id;
  selectArtifact(artifact);
}

// Master Keydown Handler for W3C ARIA Tree conformance
function handleTreeKeyDown(e) {
  const list = visibleNodesList.value;
  if (list.length === 0) return;

  // Find index of current active focused node
  let currentIndex = list.findIndex(
    (item) => item.id === activeFocusedNodeId.value
  );
  if (currentIndex === -1) {
    currentIndex = 0;
    activeFocusedNodeId.value = list[0].id;
  }

  const currentItem = list[currentIndex];

  switch (e.key) {
    case "ArrowDown":
      e.preventDefault();
      if (currentIndex < list.length - 1) {
        focusNodeId(list[currentIndex + 1].id);
      }
      break;

    case "ArrowUp":
      e.preventDefault();
      if (currentIndex > 0) {
        focusNodeId(list[currentIndex - 1].id);
      }
      break;

    case "ArrowLeft":
      e.preventDefault();
      if (currentItem.type !== "artifact" && isExpanded(currentItem.id)) {
        // Collapse expanded parent node
        expandedNodes.value[currentItem.id] = false;
      } else if (currentItem.parentId) {
        // Move focus to parent node
        focusNodeId(currentItem.parentId);
      }
      break;

    case "ArrowRight":
      e.preventDefault();
      if (currentItem.type !== "artifact" && !isExpanded(currentItem.id)) {
        // Expand collapsed parent node
        expandedNodes.value[currentItem.id] = true;
      } else if (
        currentItem.type !== "artifact" &&
        isExpanded(currentItem.id)
      ) {
        // Move focus to first child node
        const firstChild = list.find(
          (item) => item.parentId === currentItem.id
        );
        if (firstChild) {
          focusNodeId(firstChild.id);
        }
      }
      break;

    case "Enter":
    case "Space":
      e.preventDefault();
      if (currentItem.type === "artifact") {
        selectArtifact(currentItem.node);
      } else {
        toggleNode(currentItem.id);
      }
      break;
  }
}

// Map of mocked unread notifications for demonstration purposes
const mockUnreadBadges = {
  "01.01.01": 2, // Clinical Trial Protocol
  "01.01.02": 1, // Protocol Amendment
  "05.02.05": 3, // Informed Consent Form
  "10.01.02": 1, // Define-XML Specifications
};

// Calculate unread counts dynamically for sections/zones
function getUnreadBadgeCount(node) {
  if (node.type === "artifact") {
    return mockUnreadBadges[node.code] || 0;
  }

  let total = 0;
  if (node.children) {
    node.children.forEach((child) => {
      total += getUnreadBadgeCount(child);
    });
  }
  return total;
}
</script>

<style scoped>
.tmf-binder-tree-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--color-surface);
  border-right: 1px solid var(--color-border);
  padding: var(--spacing-md);
  box-sizing: border-box;
}

.tree-search-bar {
  margin-bottom: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.search-input {
  width: 100%;
  padding: var(--spacing-sm) var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  font-size: 0.9rem;
  outline: 2px solid transparent;
  transition: border-color 0.2s;
}

.search-input:focus {
  border-color: var(--color-accent);
}

.zone-filter-select {
  width: 100%;
  padding: var(--spacing-xs) var(--spacing-xs);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  font-size: 0.85rem;
  background-color: var(--color-surface-muted);
  outline: 2px solid transparent;
}

.tree-root-nodes {
  flex: 1;
  overflow-y: auto;
  padding-right: var(--spacing-2xs);
}

.empty-tree-message {
  padding: var(--spacing-md);
  text-align: center;
  color: var(--color-text-muted);
  font-style: italic;
  font-size: 0.9rem;
}

.tree-node {
  margin-bottom: var(--spacing-2xs);
}

.node-header {
  display: flex;
  align-items: center;
  padding: var(--spacing-xs) var(--spacing-xs);
  border-radius: 6px;
  cursor: pointer;
  user-select: none;
  transition: background-color 0.2s;
  font-size: 0.9rem;
}

.node-header:hover {
  background-color: var(--color-surface-muted);
}

.toggle-icon {
  font-size: 0.7rem;
  width: 16px;
  color: var(--color-text-muted);
}

.folder-icon,
.file-icon {
  margin-right: var(--spacing-xs);
  font-size: 1.1rem;
}

.zone-label {
  font-weight: 700;
  color: var(--color-text);
  margin-right: var(--spacing-2xs);
}

.section-label {
  font-weight: 600;
  color: var(--color-text-muted);
  margin-right: var(--spacing-2xs);
}

.artifact-code {
  font-family: monospace;
  font-size: 0.8rem;
  color: var(--color-accent);
  background-color: var(--color-primary-light);
  padding: var(--spacing-2xs) var(--spacing-2xs);
  border-radius: 4px;
  margin-right: var(--spacing-xs);
}

.node-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.unread-badge {
  background-color: var(--color-error);
  color: var(--color-surface);
  font-size: 0.75rem;
  font-weight: 700;
  padding: var(--spacing-2xs) var(--spacing-xs);
  border-radius: 10px;
  margin-left: var(--spacing-xs);
  min-width: 14px;
  text-align: center;
}

.node-children {
  margin-left: var(--spacing-md);
  padding-left: var(--spacing-xs);
  border-left: 1px dashed var(--color-border);
}

.artifact-node {
  border-radius: 6px;
  margin-left: var(--spacing-sm);
}

.artifact-node:hover {
  background-color: var(--color-surface-muted);
}

.artifact-node.is-selected {
  background-color: var(--color-primary-light);
  border-left: 3px solid var(--color-accent);
}

.artifact-node.is-selected .node-name {
  font-weight: 600;
  color: var(--color-primary-dark);
}
</style>
