<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div
    v-if="isOpen"
    class="command-palette-backdrop"
    @click.self="close"
  >
    <div
      ref="modalRef"
      class="command-palette-container"
      role="dialog"
      aria-modal="true"
      aria-label="Searchable Command Palette"
    >
      <div class="command-palette-header">
        <span
          class="command-palette-search-icon"
          aria-hidden="true"
        >🔍</span>
        <input
          ref="inputRef"
          v-model="query"
          type="text"
          class="command-palette-input"
          placeholder="Type to search modules..."
          aria-label="Search modules"
          @keydown="handleKeyDown"
        >
        <button
          class="command-palette-close-btn"
          aria-label="Close command palette"
          @click="close"
        >
          ×
        </button>
      </div>

      <div
        v-if="filteredDestinations.length > 0"
        class="command-palette-list"
        role="listbox"
      >
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/interactive-supports-focus, vuejs-accessibility/mouse-events-have-key-events -->
        <div
          v-for="(dest, index) in filteredDestinations"
          :id="`command-item-${index}`"
          :key="dest.path"
          class="command-item"
          :class="{ active: index === selectedIndex }"
          role="option"
          :aria-selected="index === selectedIndex"
          @click="navigateTo(dest)"
          @mouseenter="selectedIndex = index"
        >
          <span
            class="command-icon"
            aria-hidden="true"
          >{{ dest.icon }}</span>
          <div class="command-details">
            <div class="command-name">
              {{ dest.name }}
            </div>
            <div class="command-desc">
              {{ dest.description }}
            </div>
          </div>
        </div>
      </div>
      <div
        v-else
        class="command-palette-no-results"
      >
        No matching modules found.
      </div>

      <div class="command-palette-footer">
        <div class="command-palette-hints">
          <kbd>↑↓</kbd> to navigate &nbsp;|&nbsp; <kbd>↵</kbd> to select
          &nbsp;|&nbsp; <kbd>esc</kbd> to close
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "../stores/auth";
import { hasRequiredRole } from "../router";
import { useFocusTrap } from "../composables/useFocusTrap";
import { useEscapeClose } from "../composables/useEscapeClose";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
});

const emit = defineEmits(["close"]);

const router = useRouter();
const authStore = useAuthStore();

const query = ref("");
const selectedIndex = ref(0);
const modalRef = ref(null);
const inputRef = ref(null);

// Apply focus trap and escape close composables when mounted
useFocusTrap(modalRef);
useEscapeClose(close);

const searchableDestinations = [
  {
    name: "MDR Protocol Designer",
    path: "/mdr",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Design clinical trial protocols and USDM structure",
    icon: "📋",
  },
  {
    name: "eConsent Authoring",
    path: "/econsent-authoring",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Author and translate informed consent forms (ICF)",
    icon: "✍️",
  },
  {
    name: "eCRF Form Engine",
    path: "/ecrf",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
    ],
    description: "Complete and review patient electronic case report forms",
    icon: "🩺",
  },
  {
    name: "CTMS Dashboard",
    path: "/ctms",
    roles: ["cra", "monitor", "sponsor_admin"],
    description:
      "Monitor trial milestones, site visits, and operational metrics",
    icon: "📊",
  },
  {
    name: "Cryptographic Ledger",
    path: "/audit",
    roles: ["auditor", "tmf_auditor", "sponsor_admin"],
    description:
      "Inspect clinical execution audit trails and GxP block history",
    icon: "🔒",
  },
  {
    name: "Rules Designer",
    path: "/rules",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Configure dynamic skip logic and validation edit checks",
    icon: "⚙️",
  },
  {
    name: "eTMF Document Manager",
    path: "/etmf",
    roles: ["cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin"],
    description: "Manage regulatory binder document repository and zones",
    icon: "📁",
  },
  {
    name: "Notifications",
    path: "/notifications",
    roles: [], // any authenticated user
    description: "View system alerts, broadcasts, and action items",
    icon: "🔔",
  },
];

const allowedDestinations = computed(() => {
  if (!authStore.isAuthenticated) {
    return [];
  }
  return searchableDestinations.filter((dest) => {
    if (!dest.roles || dest.roles.length === 0) {
      return true;
    }
    return hasRequiredRole(authStore.normalizedRoles || [], dest.roles);
  });
});

const filteredDestinations = computed(() => {
  const q = query.value.trim().toLowerCase();
  if (!q) {
    return allowedDestinations.value;
  }
  return allowedDestinations.value.filter((dest) => {
    return (
      dest.name.toLowerCase().includes(q) ||
      dest.description.toLowerCase().includes(q) ||
      dest.path.toLowerCase().includes(q)
    );
  });
});

watch(query, () => {
  selectedIndex.value = 0;
});

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      query.value = "";
      selectedIndex.value = 0;
      nextTick(() => {
        if (inputRef.value) {
          inputRef.value.focus();
        }
      });
    }
  }
);

function close() {
  emit("close");
}

function navigateTo(dest) {
  if (dest && dest.path) {
    router.push(dest.path);
    close();
  }
}

function handleKeyDown(e) {
  if (e.key === "ArrowDown") {
    e.preventDefault();
    if (filteredDestinations.value.length > 0) {
      selectedIndex.value =
        (selectedIndex.value + 1) % filteredDestinations.value.length;
    }
  } else if (e.key === "ArrowUp") {
    e.preventDefault();
    if (filteredDestinations.value.length > 0) {
      selectedIndex.value =
        (selectedIndex.value - 1 + filteredDestinations.value.length) %
        filteredDestinations.value.length;
    }
  } else if (e.key === "Enter") {
    e.preventDefault();
    if (filteredDestinations.value.length > 0) {
      const selected = filteredDestinations.value[selectedIndex.value];
      if (selected) {
        navigateTo(selected);
      }
    }
  }
}
</script>

<style scoped>
.command-palette-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding-top: 15vh;
  z-index: 9999;
}

.command-palette-container {
  background: white;
  border-radius: 12px;
  border: 1px solid var(--border);
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.1),
    0 8px 10px -6px rgba(0, 0, 0, 0.1);
  width: 100%;
  max-width: 600px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.command-palette-header {
  padding: 16px;
  border-bottom: 1px solid var(--border);
  display: flex;
  align-items: center;
  gap: 12px;
}

.command-palette-search-icon {
  font-size: 1.25rem;
  color: var(--primary-light);
}

.command-palette-input {
  flex: 1;
  border: none;
  outline: none;
  font-size: 1.1rem;
  color: var(--primary);
  font-family: var(--font);
  background: transparent;
}

.command-palette-close-btn {
  background: none;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
  color: var(--primary-light);
  padding: 0 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  line-height: 1;
}

.command-palette-close-btn:hover {
  color: var(--primary);
}

.command-palette-list {
  max-height: 350px;
  overflow-y: auto;
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.command-item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 14px;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  border-left: 4px solid transparent;
}

.command-item.active {
  background-color: var(--neutral-light);
  border-left-color: var(--accent);
}

.command-icon {
  font-size: 1.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
}

.command-details {
  display: flex;
  flex-direction: column;
}

.command-name {
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--primary);
}

.command-desc {
  font-size: 0.8rem;
  color: var(--primary-light);
  margin-top: 2px;
}

.command-palette-no-results {
  padding: 24px;
  text-align: center;
  color: var(--primary-light);
  font-size: 0.95rem;
}

.command-palette-footer {
  padding: 10px 16px;
  background-color: var(--neutral-light);
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: var(--primary-light);
}

.command-palette-hints kbd {
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 4px;
  padding: 1px 5px;
  font-family: monospace;
  font-size: 0.75rem;
  box-shadow: 0 1px 0 rgba(0, 0, 0, 0.1);
}
</style>
