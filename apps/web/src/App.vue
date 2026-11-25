<template>
  <AppShell>
    <router-view />
  </AppShell>
  <ContextualHelpDrawer
    v-if="showContextualHelp && hasActivatedContextualHelp"
  />
  <Teleport v-if="peekState.open" to="body">
    <EntityPeekDrawerHost />
  </Teleport>
  <Teleport v-if="hasActivatedToastHost" to="body">
    <ToastHost />
  </Teleport>
  <ConfirmationDialogHost v-if="confirmStore.activeRequest" />
</template>

<script>
import { defineLazyOverlay } from "./utils/lazyOverlay.js";

const ContextualHelpDrawer = defineLazyOverlay(
  () => import("./components/ContextualHelpDrawer.vue")
);

const EntityPeekDrawerHost = defineLazyOverlay(
  () => import("./components/canvas/EntityPeekDrawerHost.vue")
);

const ToastHost = defineLazyOverlay(() => import("./components/ToastHost.vue"));

const ConfirmationDialogHost = defineLazyOverlay(
  () => import("./components/ConfirmationDialogHost.vue")
);
</script>

<script setup>
import {
  computed,
  onMounted,
  onUnmounted,
  provide,
  ref,
  shallowRef,
  watch,
} from "vue";
import { useRoute } from "vue-router";
import { hasRegisteredUnsavedDrafts } from "ui";
import AppShell from "./components/AppShell.vue";
import { useEntityPeekDrawer } from "./composables/useEntityPeekDrawer";
import { useAuthStore } from "./stores/auth.js";
import { useConfirmStore } from "./stores/confirm.js";
import { resolveStorePinia } from "./stores/onboardingSummary.js";
import { useToastStore } from "./stores/toasts.js";
import { isDemoBuild } from "./utils/demoMode.js";
import {
  hasBlockingModalDialog,
  isEditableOrFormControl,
} from "./utils/lazyOverlay.js";
import "./style.css";

const { state: peekState } = useEntityPeekDrawer();
const route = useRoute();
const authStore = useAuthStore();
const pinia = resolveStorePinia(authStore);
const confirmStore = useConfirmStore(pinia);
const toastStore = useToastStore(pinia);

const hasActivatedToastHost = ref(false);
watch(
  () => toastStore.toasts.length > 0,
  (hasToasts) => {
    if (hasToasts) {
      hasActivatedToastHost.value = true;
    }
  },
  { immediate: true }
);

const loadedNodeResolver = shallowRef(null);
let nodeResolverPromise = null;

function loadNodeResolver() {
  if (loadedNodeResolver.value) {
    return Promise.resolve(loadedNodeResolver.value);
  }
  if (!nodeResolverPromise) {
    nodeResolverPromise = import("./canvas/nodeResolver").then((mod) => {
      loadedNodeResolver.value = mod;
      return mod;
    });
  }
  return nodeResolverPromise;
}

provide("cadenceEntityResolver", {
  ensure(refs) {
    if (loadedNodeResolver.value) {
      loadedNodeResolver.value.ensureNodesResolved(refs);
      return;
    }
    void loadNodeResolver().then((mod) => mod.ensureNodesResolved(refs));
  },
  getDisplay(refText, fallbackTitle = null) {
    if (loadedNodeResolver.value) {
      return loadedNodeResolver.value.getNodeDisplay(refText, fallbackTitle);
    }
    void loadNodeResolver();
    return null;
  },
  resolve(refText) {
    if (loadedNodeResolver.value) {
      return loadedNodeResolver.value.resolveNode(refText);
    }
    return loadNodeResolver().then((mod) => mod.resolveNode(refText));
  },
});

provide("cadenceConfirmUnsavedDrafts", {
  hasUnsaved: () => hasRegisteredUnsavedDrafts(),
  confirm: () =>
    confirmStore.confirm({
      title: "Unsaved Form Changes",
      message:
        "You have unsaved eCRF draft changes. Are you sure you want to leave?",
      confirmLabel: "Leave Without Saving",
      cancelLabel: "Stay on Form",
      variant: "warning",
    }),
});

const showContextualHelp = computed(
  () =>
    Boolean(authStore.isAuthenticated) &&
    route?.name !== "login" &&
    route?.path !== "/login"
);

const isKnowledgeOverlayActive = computed(() => {
  const activePinia = resolveStorePinia(authStore) || pinia;
  const knowledgeState = activePinia?.state?.value?.knowledge;
  return Boolean(knowledgeState?.isOpen || knowledgeState?.isEscalating);
});

const hasActivatedContextualHelp = ref(false);

watch(
  () => showContextualHelp.value && isKnowledgeOverlayActive.value,
  (active) => {
    if (active) {
      hasActivatedContextualHelp.value = true;
    }
  },
  { immediate: true }
);

watch(showContextualHelp, (allowed) => {
  if (!allowed) {
    hasActivatedContextualHelp.value = false;
  }
});

function handlePreMountHelpKeydown(e) {
  if (!showContextualHelp.value || hasActivatedContextualHelp.value) return;
  if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
  if (e.key !== "F1" && e.key !== "?") return;
  if (hasBlockingModalDialog("contextual-help-drawer")) return;
  if (isEditableOrFormControl(e.target)) return;

  e.preventDefault();
  hasActivatedContextualHelp.value = true;
  const targetPinia = resolveStorePinia(authStore) || pinia;
  const mountedStore = targetPinia?._s?.get("knowledge");
  if (mountedStore && typeof mountedStore.toggleDrawer === "function") {
    mountedStore.toggleDrawer();
    return;
  }
  void import("./stores/knowledge.js").then(({ useKnowledgeStore }) => {
    const store = targetPinia
      ? useKnowledgeStore(targetPinia)
      : useKnowledgeStore();
    store.openDrawer();
  });
}

onMounted(async () => {
  if (typeof window !== "undefined") {
    window.addEventListener("keydown", handlePreMountHelpKeydown);
  }
  if (!isDemoBuild() || !pinia) {
    return;
  }
  const { useClinicalStore } = await import("./stores/clinical");
  const store = useClinicalStore(pinia);
  if (store.ledgerBlocks.length === 0) {
    await store.addLedgerBlock(
      "GENESIS",
      {
        platform: import.meta.env.VITE_BRAND_NAME || "Cadence Clinical",
        environment: "Interactive Web Sandbox",
        compliantStandards: [
          "21 CFR Part 11",
          "GAMP 5 (Category 4/5)",
          /* deid: ignore */ "IEC 62304",
          /* deid: ignore */ "ISO 14155:2020",
        ],
      },
      "System boot and cryptographic ledger initialized successfully."
    );
  }
});

onUnmounted(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("keydown", handlePreMountHelpKeydown);
  }
});
</script>

<style>
/* Any additional global or fallback styles if needed */
</style>
