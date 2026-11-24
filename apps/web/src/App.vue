<template>
  <AppShell>
    <router-view />
  </AppShell>
  <ContextualHelpDrawer v-if="showContextualHelp" />
  <Teleport to="body">
    <ToastHost />
  </Teleport>
  <ConfirmationDialogHost v-if="confirmStore.activeRequest" />
</template>

<script setup>
import {
  computed,
  defineAsyncComponent,
  getCurrentInstance,
  onMounted,
  shallowRef,
} from "vue";
import { getActivePinia } from "pinia";
import { useRoute } from "vue-router";
import { provideEntityLinkResolver } from "ui";
import AppShell from "./components/AppShell.vue";
import ToastHost from "./components/ToastHost.vue";
import { useAuthStore } from "./stores/auth.js";
import { useConfirmStore } from "./stores/confirm.js";
import { isDemoBuild } from "./utils/demoMode.js";
import "./style.css";

const appInstance = getCurrentInstance();
const lazyEntityResolver = shallowRef(null);
let entityResolverPromise = null;

function ensureEntityResolverLoaded() {
  if (!entityResolverPromise) {
    entityResolverPromise = import("./canvas/entityLinkResolver").then(
      ({ createCanvasEntityResolver }) => {
        const impl = createCanvasEntityResolver(
          () =>
            appInstance?.appContext?.config?.globalProperties?.$router ?? null,
          () =>
            appInstance?.appContext?.config?.globalProperties?.$route ?? null
        );
        lazyEntityResolver.value = impl;
        return impl;
      }
    );
  }
  return entityResolverPromise;
}

provideEntityLinkResolver({
  resolve: (input) => {
    const impl = lazyEntityResolver.value;
    if (impl) {
      return impl.resolve(input);
    }
    ensureEntityResolverLoaded();
    return null;
  },
  navigate: async (payload) => {
    const impl =
      lazyEntityResolver.value ?? (await ensureEntityResolverLoaded());
    await impl.navigate(payload);
  },
  openFullView: async (payload) => {
    const impl =
      lazyEntityResolver.value ?? (await ensureEntityResolverLoaded());
    await impl.openFullView(payload);
  },
  fetchAuditEntry: async (auditRef) => {
    const impl =
      lazyEntityResolver.value ?? (await ensureEntityResolverLoaded());
    return await impl.fetchAuditEntry(auditRef);
  },
});

const ContextualHelpDrawer = defineAsyncComponent(
  () => import("./components/ContextualHelpDrawer.vue")
);

const ConfirmationDialogHost = defineAsyncComponent(
  () => import("./components/ConfirmationDialogHost.vue")
);

const pinia = getActivePinia();
const route = useRoute();
const authStore = useAuthStore();
const confirmStore = useConfirmStore(pinia);

const showContextualHelp = computed(
  () =>
    Boolean(authStore.isAuthenticated) &&
    route?.name !== "login" &&
    route?.path !== "/login"
);

onMounted(async () => {
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
</script>

<style>
/* Any additional global or fallback styles if needed */
</style>
