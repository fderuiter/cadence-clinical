<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" class="command-palette-backdrop" @click.self="close">
    <div
      ref="modalRef"
      class="command-palette-container"
      role="dialog"
      aria-modal="true"
      aria-label="Searchable Command Palette"
    >
      <div class="command-palette-header">
        <span class="command-palette-search-icon" aria-hidden="true">🔍</span>
        <input
          ref="inputRef"
          v-model="query"
          type="text"
          class="command-palette-input"
          placeholder="Type to search routes, actions, subjects, documents, SOPs..."
          aria-label="Search modules and entities"
          @keydown="handleKeyDown"
        />
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
          :key="dest.id || dest.path || dest.name"
          class="command-item"
          :class="{ active: index === selectedIndex }"
          role="option"
          :aria-selected="index === selectedIndex"
          @click="navigateTo(dest)"
          @mouseenter="selectedIndex = index"
        >
          <span class="command-icon" aria-hidden="true">{{ dest.icon }}</span>
          <div class="command-details">
            <div class="command-title-row">
              <span v-if="dest.category" class="command-badge">{{
                dest.category
              }}</span>
              <span class="command-name">{{ dest.name }}</span>
            </div>
            <div class="command-desc">
              {{ dest.description }}
            </div>
          </div>
        </div>
      </div>
      <div v-else class="command-palette-no-results">
        No matching modules or commands found.
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
import { useClinicalStore, DEFAULT_DEMO_SUBJECTS } from "../stores/clinical.ts";
import { useEtmfStore } from "../stores/etmf";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { hasRequiredRole } from "../router";
import { isScreenAvailable } from "../router/availability";
import { useFocusTrap } from "../composables/useFocusTrap";
import { useEscapeClose } from "../composables/useEscapeClose";
import { useWebCopilot } from "../composables/useWebCopilot";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
});

const emit = defineEmits(["close"]);

const router = useRouter();
const authStore = useAuthStore();
const clinicalStore = useClinicalStore();
const etmfStore = useEtmfStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();

const query = ref("");
const selectedIndex = ref(0);
const modalRef = ref(null);
const inputRef = ref(null);

// Apply focus trap and escape close composables when mounted
useFocusTrap(modalRef);
useEscapeClose(close);

const staticRoutes = [
  {
    id: "route-mdr",
    name: "MDR Protocol Designer",
    path: "/mdr",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Design clinical trial protocols and USDM structure",
    icon: "📋",
    category: "[Route]",
  },
  {
    id: "route-digitization",
    name: "Protocol AI Digitizer",
    path: "/digitization",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "AI-assisted PDF protocol digitization and USDM conversion",
    icon: "⚡",
    category: "[Route]",
  },
  {
    id: "route-icf-builder",
    name: "eConsent & ICF Builder",
    path: "/icf-builder",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Author and manage informed consent forms (ICF)",
    icon: "📝",
    category: "[Route]",
  },
  {
    id: "route-econsent-authoring",
    name: "eConsent Authoring",
    path: "/econsent-authoring",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Author and translate informed consent forms (ICF)",
    icon: "✍️",
    category: "[Route]",
  },
  {
    id: "route-amendment-diff",
    name: "Amendments & Migration",
    path: "/amendment-diff",
    roles: [
      "sponsor_designer",
      "data_manager",
      "sponsor_admin",
      "super_admin",
      "cra",
      "monitor",
      "site_investigator",
      "crc",
    ],
    description:
      "Compare protocol amendment versions and subject migration impact",
    icon: "🔀",
    category: "[Route]",
  },
  {
    id: "route-ecrf",
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
    category: "[Route]",
  },
  {
    id: "route-imaging",
    name: "DICOM & RECIST Viewer",
    path: "/imaging",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
      "sponsor_designer",
    ],
    description:
      "Inspect medical imaging DICOM series and RECIST lesion criteria",
    icon: "🔬",
    category: "[Route]",
  },
  {
    id: "route-devices",
    name: "Device & IVD Suite",
    path: "/devices",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
      "sponsor_designer",
      "auditor",
    ],
    description:
      "Investigational device telemetry and medical device investigation",
    icon: "🩺",
    category: "[Route]",
  },
  {
    id: "route-cec",
    name: "CEC Adjudication",
    path: "/cec",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
      "sponsor_designer",
      "auditor",
      "tmf_auditor",
    ],
    description: "Clinical Event Committee adjudication and endpoint review",
    icon: "⚖️",
    category: "[Route]",
  },
  {
    id: "route-ctms",
    name: "CTMS Dashboard",
    path: "/ctms",
    roles: ["cra", "monitor", "sponsor_admin"],
    description:
      "Monitor trial milestones, site visits, and operational metrics",
    icon: "📊",
    category: "[Route]",
  },
  {
    id: "route-audit",
    name: "Cryptographic Ledger",
    path: "/audit",
    roles: ["auditor", "tmf_auditor", "sponsor_admin"],
    description:
      "Inspect clinical execution audit trails and GxP block history",
    icon: "🔒",
    category: "[Route]",
  },
  {
    id: "route-rules",
    name: "Rules Designer",
    path: "/rules",
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
    description: "Configure dynamic skip logic and validation edit checks",
    icon: "⚙️",
    category: "[Route]",
  },
  {
    id: "route-coding",
    name: "Medical Coding",
    path: "/coding",
    roles: ["data_manager", "sponsor_designer", "sponsor_admin", "super_admin"],
    description:
      "Auto-code verbatim terms with MedDRA and WHODrug dictionaries",
    icon: "🏷️",
    category: "[Route]",
  },
  {
    id: "route-data-lock",
    name: "Data Lock Console",
    path: "/data-lock",
    roles: ["data_manager", "sponsor_designer", "sponsor_admin", "super_admin"],
    description: "Manage database freeze, soft lock, and hard lock controls",
    icon: "🔐",
    category: "[Route]",
  },
  {
    id: "route-lakehouse",
    name: "Clinical Lakehouse",
    path: "/lakehouse",
    roles: [
      "data_manager",
      "sponsor_designer",
      "sponsor_admin",
      "sponsor_statistician",
      "unblinded_statistician",
      "super_admin",
    ],
    description: "Query raw CDISC SDTM datasets and lakehouse analytics",
    icon: "💾",
    category: "[Route]",
  },
  {
    id: "route-sensors",
    name: "Connected Sensors & IoT",
    path: "/sensors",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_designer",
      "sponsor_admin",
      "cra",
      "monitor",
      "super_admin",
    ],
    description:
      "Monitor continuous wearable sensors and IoT telemetry streams",
    icon: "⌚",
    category: "[Route]",
  },
  {
    id: "route-labs",
    name: "Lab Governance",
    path: "/labs",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "cra",
      "monitor",
      "sponsor_designer",
      "sponsor_admin",
      "super_admin",
    ],
    description: "Manage central laboratory ranges, certificates, and alerts",
    icon: "🧪",
    category: "[Route]",
  },
  {
    id: "route-dct",
    name: "Telehealth & DTP Logistics",
    path: "/dct",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_designer",
      "sponsor_admin",
      "cra",
      "monitor",
      "super_admin",
    ],
    description:
      "Manage virtual telehealth visits and Direct-to-Patient drug delivery",
    icon: "📹",
    category: "[Route]",
  },
  {
    id: "route-safety",
    name: "Safety & DSUR Compiler",
    path: "/safety",
    roles: [
      "safety_physician",
      "qppv",
      "qppv_officer",
      "qualified_person_pv",
      "safety_officer",
      "sponsor_pv_officer",
      "sponsor_medical_monitor",
      "medical_monitor",
      "sponsor_admin",
      "data_manager",
      "super_admin",
    ],
    description:
      "Compile Development Safety Update Reports (DSUR) and safety cases",
    icon: "🛡️",
    category: "[Route]",
  },
  {
    id: "route-exports",
    name: "Data Export Wizard",
    path: "/exports",
    roles: ["data_manager", "sponsor_designer", "sponsor_admin", "super_admin"],
    description:
      "Export CDISC Dataset-JSON, ODM-XML, and SAS transport packages",
    icon: "📤",
    category: "[Route]",
  },
  {
    id: "route-etmf",
    name: "eTMF Document Manager",
    path: "/etmf",
    roles: ["cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin"],
    description: "Manage regulatory binder document repository and zones",
    icon: "📁",
    category: "[Route]",
  },
  {
    id: "route-quality",
    name: "Quality & RBQM Cockpit",
    path: "/quality",
    roles: [
      "sponsor_designer",
      "data_manager",
      "sponsor_admin",
      "super_admin",
      "cra",
      "monitor",
      "site_investigator",
      "crc",
      "auditor",
      "tmf_auditor",
    ],
    description: "Risk-based quality management (RBQM) and KRI metrics",
    icon: "🎯",
    category: "[Route]",
  },
  {
    id: "route-notifications",
    name: "Notifications",
    path: "/notifications",
    roles: [],
    description: "View system alerts, broadcasts, and action items",
    icon: "🔔",
    category: "[Route]",
  },
  {
    id: "route-tickets",
    name: "Clinical Issues & Tickets",
    path: "/tickets",
    roles: [],
    description: "Track clinical queries, protocol deviations, and issues",
    icon: "🎫",
    category: "[Route]",
  },
  {
    id: "route-documents",
    name: "Documents Hub",
    path: "/documents",
    roles: [],
    description: "Central repository for study documents and files",
    icon: "🗂️",
    category: "[Route]",
  },
  {
    id: "route-knowledge",
    name: "Knowledge & SOP Hub",
    path: "/knowledge",
    roles: [],
    description: "Search operational guidance, regulatory compliance, and SOPs",
    icon: "📚",
    category: "[Route]",
  },
  {
    id: "route-training",
    name: "Study Training & Gating",
    path: "/training",
    roles: [],
    description: "Track protocol training compliance and GxP gating status",
    icon: "🎓",
    category: "[Route]",
  },
  {
    id: "route-participant-wallet",
    name: "Participant Financial Wallet",
    path: "/participant-wallet",
    roles: [],
    description: "Manage subject stipends, reimbursements, and financial logs",
    icon: "💳",
    category: "[Route]",
  },
  {
    id: "route-lay-summaries",
    name: "EU CTR Lay Summaries",
    path: "/lay-summaries",
    roles: [],
    description: "Manage EU CTR Annex V plain language lay summaries",
    icon: "📜",
    category: "[Route]",
  },
  {
    id: "route-overview",
    name: "Overview Hub",
    path: "/overview",
    roles: [],
    description: "Executive trial progress overview and module status hub",
    icon: "🏠",
    category: "[Route]",
  },
  {
    id: "route-studies",
    name: "Study Workspace",
    path: "/studies",
    roles: [],
    description: "Open the study-centric workspace and Clinical Canvas",
    icon: "📐",
    category: "[Route]",
  },
];

const actionItems = computed(() => {
  const actions = [
    {
      id: "action-create-ticket",
      name: "Create Clinical Ticket / Issue",
      description:
        "Open clinical ticket creation workflow for protocol deviations or queries",
      icon: "➕",
      category: "[Action]",
      roles: [],
      path: "/tickets",
      action: () => {
        router.push("/tickets?action=new");
      },
    },
    {
      id: "action-open-help",
      name: "Open Contextual Help & SOP Guidance",
      description: "Open in-page help drawer for regulatory SOP guidance",
      icon: "📖",
      category: "[Action]",
      roles: [],
      action: () => {
        knowledgeStore.openDrawer();
      },
    },
    {
      id: "action-open-onboarding",
      name: "Open Setup & Onboarding Checklist",
      description: "View setup checklist and completion status",
      icon: "🚀",
      category: "[Action]",
      roles: [],
      action: () => {
        onboardingStore.openDrawer();
      },
    },
  ];

  // Dynamic persona switching actions
  const personas = authStore.availablePersonas || [];
  personas.forEach((p) => {
    actions.push({
      id: `action-persona-${p.key}`,
      name: `Switch View: ${p.label}`,
      description: `Switch active workspace view persona to ${p.label}`,
      icon: "👤",
      category: "[Action]",
      roles: p.requires || [],
      action: () => {
        authStore.switchPersona(p.key);
      },
    });
  });

  return actions;
});

const subjectItems = computed(() => {
  const subjects =
    clinicalStore.subjects && clinicalStore.subjects.length > 0
      ? clinicalStore.subjects
      : DEFAULT_DEMO_SUBJECTS;
  return subjects.map((subj) => ({
    id: `subj-${subj.id}`,
    name: `Subject ${subj.id}`,
    description: `Status: ${subj.status || "ACTIVE"} | Protocol v${subj.active_protocol_version || "1.0.0"}${subj.siteId ? ` | Site: ${subj.siteId}` : ""}`,
    icon: "🩺",
    category: "[Subject]",
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
    ],
    path: "/ecrf",
    action: () => {
      router.push({ path: "/ecrf", query: { subjectId: subj.id } });
    },
  }));
});

const documentItems = computed(() => {
  const docs =
    etmfStore.documentsList && etmfStore.documentsList.length > 0
      ? etmfStore.documentsList
      : etmfStore.documents && etmfStore.documents.length > 0
        ? etmfStore.documents
        : [
            {
              id: "DOC-100",
              filename: "protocol_v1_draft.pdf",
              artifact_type: "Clinical Trial Protocol",
              status: "DRAFT",
              zone: 1,
            },
            {
              id: "DOC-999",
              filename: "icf_v1.0_signed.pdf",
              artifact_type: "Informed Consent Form",
              status: "APPROVED",
              zone: 5,
            },
          ];

  return docs.map((doc) => ({
    id: `doc-${doc.id}`,
    name: doc.filename || `Document ${doc.id}`,
    description: `Artifact: ${doc.artifact_type || "Document"} | Status: ${doc.status || "DRAFT"} | Zone ${doc.zone || 1}`,
    icon: "📁",
    category: "[Document]",
    roles: ["cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin"],
    path: "/etmf",
    action: () => {
      router.push({ path: "/etmf", query: { docId: doc.id } });
    },
  }));
});

const sopItems = computed(() => {
  const mockArticles = [
    {
      id: "art-ecrf-sop",
      title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
      category_name: "Clinical Operations",
      version_label: "2.1",
      status: "PUBLISHED",
    },
    {
      id: "art-mdr-sop",
      title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
      category_name: "Study Authoring & MDR",
      version_label: "3.0",
      status: "PUBLISHED",
    },
    {
      id: "art-ctms-sop",
      title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
      category_name: "Site Monitoring & CTMS",
      version_label: "1.0",
      status: "PUBLISHED",
    },
    {
      id: "art-audit-sop",
      title:
        "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
      category_name: "Regulatory & Quality Compliance",
      version_label: "1.0",
      status: "PUBLISHED",
    },
  ];

  return mockArticles.map((art) => ({
    id: `sop-${art.id}`,
    name: art.title,
    description: `Category: ${art.category_name} | Version: v${art.version_label} | Status: ${art.status}`,
    icon: "📚",
    category: "[SOP]",
    roles: [],
    action: () => {
      knowledgeStore.selectArticle(art);
      knowledgeStore.openDrawer();
    },
  }));
});

const allRegistryItems = computed(() => {
  return [
    ...staticRoutes,
    ...actionItems.value,
    ...subjectItems.value,
    ...documentItems.value,
    ...sopItems.value,
  ];
});

const {
  recommendations: aiRecommendations,
  fetchIntentDebounced,
  clearRecommendations,
} = useWebCopilot();

const aiCopilotItems = computed(() => {
  if (!authStore.isAuthenticated) {
    return [];
  }
  return (aiRecommendations.value || [])
    .filter((rec) => {
      if (rec.roles && rec.roles.length > 0) {
        if (!hasRequiredRole(authStore.normalizedRoles || [], rec.roles)) {
          return false;
        }
      }
      return true;
    })
    .map((rec) => ({
      id: rec.id || `ai-${rec.targetRoute}`,
      name: rec.name,
      description: rec.description,
      icon: rec.icon || "🤖",
      category: rec.category || "[AI Copilot]",
      path: rec.targetRoute,
      roles: rec.roles || [],
      action: () => {
        if (rec.targetRoute) {
          router.push(rec.targetRoute);
        }
      },
    }));
});

const allowedDestinations = computed(() => {
  if (!authStore.isAuthenticated) {
    return [];
  }
  return allRegistryItems.value.filter((dest) => {
    if (dest.path) {
      const resolved = router.resolve(dest.path);
      if (resolved && resolved.name && !isScreenAvailable(resolved.name)) {
        return false;
      }
    }
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
  const staticMatches = allowedDestinations.value.filter((dest) => {
    return (
      dest.name.toLowerCase().includes(q) ||
      (dest.description && dest.description.toLowerCase().includes(q)) ||
      (dest.path && dest.path.toLowerCase().includes(q)) ||
      (dest.category && dest.category.toLowerCase().includes(q))
    );
  });

  const aiItems = aiCopilotItems.value.filter(
    (aiItem) => !staticMatches.some((s) => s.id === aiItem.id)
  );

  return [...aiItems, ...staticMatches];
});

watch(query, (newQuery) => {
  selectedIndex.value = 0;
  const q = newQuery.trim();
  if (q) {
    fetchIntentDebounced(q, {
      roles: (authStore.normalizedRoles || []).join(","),
    });
  } else {
    clearRecommendations();
  }
});

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      query.value = "";
      clearRecommendations();
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
  if (!dest) return;
  if (typeof dest.action === "function") {
    dest.action();
  } else if (dest.path) {
    router.push(dest.path);
  }
  close();
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

.command-title-row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.command-name {
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--primary);
}

.command-badge {
  display: inline-block;
  font-size: 0.7rem;
  font-weight: 700;
  padding: 1px 6px;
  border-radius: 4px;
  background-color: #e2e8f0;
  color: #1e293b;
  letter-spacing: 0.02em;
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
