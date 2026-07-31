<template>
  <div class="tmf-binder-tree-container">
    <div class="tree-search-bar">
      <input
        type="text"
        v-model="searchQuery"
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

    <div class="tree-root-nodes">
      <div v-if="filteredTree.length === 0" class="empty-tree-message">
        No matching TMF items found.
      </div>
      <div
        v-for="zone in filteredTree"
        :key="zone.id"
        class="tree-node zone-node"
      >
        <div
          class="node-header zone-header"
          @click="toggleNode(zone.id)"
          :class="{ 'is-expanded': isExpanded(zone.id) }"
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

        <div v-if="isExpanded(zone.id)" class="node-children zone-children">
          <div
            v-for="section in zone.children"
            :key="section.id"
            class="tree-node section-node"
          >
            <div
              class="node-header section-header"
              @click="toggleNode(section.id)"
              :class="{ 'is-expanded': isExpanded(section.id) }"
            >
              <span class="toggle-icon">{{ isExpanded(section.id) ? "▼" : "▶" }}</span>
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

            <div v-if="isExpanded(section.id)" class="node-children section-children">
              <div
                v-for="artifact in section.children"
                :key="artifact.id"
                class="tree-node artifact-node"
                :class="{ 'is-selected': selectedArtifactId === artifact.code }"
                @click="selectArtifact(artifact)"
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
import { ref, computed, watch } from "vue";

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
              children: matchingArtifacts.length > 0 ? matchingArtifacts : section.children,
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
          children: matchingSections.length > 0 ? matchingSections : zone.children,
        };
      }

      return null;
    })
    .filter((zone) => zone !== null);
});
</script>

<style scoped>
.tmf-binder-tree-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: #ffffff;
  border-right: 1px solid #e2e8f0;
  padding: 16px;
  box-sizing: border-box;
}

.tree-search-bar {
  margin-bottom: 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.search-input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.9rem;
  outline: none;
  transition: border-color 0.2s;
}

.search-input:focus {
  border-color: #3b82f6;
}

.zone-filter-select {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.85rem;
  background-color: #f8fafc;
  outline: none;
}

.tree-root-nodes {
  flex: 1;
  overflow-y: auto;
  padding-right: 4px;
}

.empty-tree-message {
  padding: 16px;
  text-align: center;
  color: #64748b;
  font-style: italic;
  font-size: 0.9rem;
}

.tree-node {
  margin-bottom: 4px;
}

.node-header {
  display: flex;
  align-items: center;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
  user-select: none;
  transition: background-color 0.2s;
  font-size: 0.9rem;
}

.node-header:hover {
  background-color: #f1f5f9;
}

.toggle-icon {
  font-size: 0.7rem;
  width: 16px;
  color: #64748b;
}

.folder-icon,
.file-icon {
  margin-right: 8px;
  font-size: 1.1rem;
}

.zone-label {
  font-weight: 700;
  color: #1e293b;
  margin-right: 6px;
}

.section-label {
  font-weight: 600;
  color: #475569;
  margin-right: 6px;
}

.artifact-code {
  font-family: monospace;
  font-size: 0.8rem;
  color: #2563eb;
  background-color: #eff6ff;
  padding: 2px 4px;
  border-radius: 4px;
  margin-right: 8px;
}

.node-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.unread-badge {
  background-color: #ef4444;
  color: #ffffff;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 10px;
  margin-left: 8px;
  min-width: 14px;
  text-align: center;
}

.node-children {
  margin-left: 16px;
  padding-left: 8px;
  border-left: 1px dashed #cbd5e1;
}

.artifact-node {
  border-radius: 6px;
  margin-left: 12px;
}

.artifact-node:hover {
  background-color: #f1f5f9;
}

.artifact-node.is-selected {
  background-color: #dbeafe;
  border-left: 3px solid #2563eb;
}

.artifact-node.is-selected .node-name {
  font-weight: 600;
  color: #1e40af;
}
</style>
