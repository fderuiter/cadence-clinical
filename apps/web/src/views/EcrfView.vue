<template>
  <div id="section-ecrf" class="dashboard-section active">
    <div class="section-header">
      <h2>{{ $t("ecrf.title") }}</h2>
      <p>
        {{ $t("ecrf.description") }}
      </p>
    </div>

    <EcrfDemoWorkspace
      v-if="demoBuild"
      ref="demoWorkspaceRef"
      @dirty-change="workspaceDirty = $event"
      @panel-dirty-change="formPanelDirty = $event"
      @form-loaded="onFormPanelLoaded"
      @closed="onFormPanelClosed"
    />
    <EcrfLiveWorkspace
      v-else
      :loaded-form-id="loadedFormId"
      :loaded-form-title="loadedFormTitle"
      @dirty-change="onLiveDirtyChange"
      @form-loaded="onFormPanelLoaded"
      @closed="onFormPanelClosed"
    />
  </div>
</template>

<script setup>
import {
  computed,
  defineAsyncComponent,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import * as VueRouter from "vue-router";
import EcrfLiveWorkspace from "./EcrfLiveWorkspace.vue";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import { useAuthStore } from "../stores/auth";
import { confirmDialog } from "../composables/useConfirmDialog.js";
import {
  allowUnbackedAction,
  unbackedActionsEnabled,
} from "../utils/unbackedActions.js";

const EcrfDemoWorkspace = defineAsyncComponent(
  () => import("./demo/EcrfDemoWorkspace.vue")
);

const { useRoute, onBeforeRouteLeave } = VueRouter;
const useRouter = "useRouter" in VueRouter ? VueRouter.useRouter : undefined;
const onBeforeRouteUpdate =
  "onBeforeRouteUpdate" in VueRouter
    ? VueRouter.onBeforeRouteUpdate
    : undefined;

const scopeStore = useClinicalScopeStore();
const authStore = useAuthStore();
const route = useRoute();
const router = typeof useRouter === "function" ? useRouter() : null;
const demoBuild = unbackedActionsEnabled();

const demoWorkspaceRef = ref(null);
const workspaceDirty = ref(false);
const formPanelDirty = ref(false);
const loadedFormId = ref(null);
const loadedFormTitle = ref("");

function routeQuery(name, targetRoute = route) {
  const value = targetRoute?.query?.[name];
  return typeof value === "string" && value.length > 0 ? value : null;
}

if (route && route.query) {
  if (route.query.studyId) {
    scopeStore.activeStudyId = String(route.query.studyId);
  }
  if (route.query.siteId) {
    scopeStore.activeSiteId = String(route.query.siteId);
  }
  if (route.query.subjectId) {
    scopeStore.activeSubjectId = String(route.query.subjectId);
  }
}

watch(
  () => routeQuery("formId"),
  (next) => {
    if (!next) {
      loadedFormId.value = null;
      loadedFormTitle.value = "";
    }
  }
);

function onLiveDirtyChange(next) {
  formPanelDirty.value = next;
  workspaceDirty.value = next;
}

function onFormPanelLoaded(loaded) {
  const nextId = loaded?.form_id || null;
  loadedFormId.value = nextId;
  const baseTitle =
    loaded?.collection_title || loaded?.collection_spec_id || "";
  loadedFormTitle.value =
    baseTitle && loaded?.planned_activity_id
      ? `${baseTitle} · ${loaded.planned_activity_id}`
      : baseTitle;
  if (!nextId || routeQuery("formId") === nextId) return;
  if (typeof router?.replace === "function") {
    void router.replace({
      query: {
        ...(route?.query || {}),
        formId: nextId,
      },
    });
  }
}

function onFormPanelClosed() {
  formPanelDirty.value = false;
  if (!demoBuild) {
    workspaceDirty.value = false;
  }
  loadedFormId.value = null;
  loadedFormTitle.value = "";
  if (!routeQuery("formId") && !routeQuery("itemId")) return;
  if (typeof router?.replace === "function") {
    const nextQuery = { ...(route?.query || {}) };
    delete nextQuery.formId;
    delete nextQuery.itemId;
    void router.replace({ query: nextQuery });
  }
}

function shouldSuppressUnsavedGuard() {
  return Boolean(authStore.sessionExpired || authStore.isLoggingOut);
}

function handleBeforeUnload(event) {
  if (workspaceDirty.value && !shouldSuppressUnsavedGuard()) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

function confirmLeaveDirtyDraft() {
  return confirmDialog({
    title: "Unsaved eCRF Changes",
    message:
      "You have unsaved eCRF draft changes. Are you sure you want to leave?",
    confirmLabel: "Leave Without Saving",
    cancelLabel: "Stay on Form",
    variant: "warning",
  });
}

onMounted(() => {
  window.addEventListener("beforeunload", handleBeforeUnload);
});

onUnmounted(() => {
  window.removeEventListener("beforeunload", handleBeforeUnload);
});

if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave(() => {
    if (workspaceDirty.value && !shouldSuppressUnsavedGuard()) {
      return confirmLeaveDirtyDraft().then((confirmLeave) =>
        confirmLeave ? undefined : false
      );
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to, from) => {
    if (!formPanelDirty.value || shouldSuppressUnsavedGuard()) return;
    const nextFormId = routeQuery("formId", to);
    const currentFormId =
      loadedFormId.value ?? routeQuery("formId", from ?? route);
    if (nextFormId !== currentFormId) {
      return confirmLeaveDirtyDraft().then((confirmSwitch) =>
        confirmSwitch ? undefined : false
      );
    }
  });
}

// Forwarded bindings and unbacked-action guards when demo workspace is absent (ADR-2622, #4827)
const screeningResult = computed(
  () => demoWorkspaceRef.value?.screeningResult ?? null
);
const isReconsentGated = computed(
  () => demoWorkspaceRef.value?.isReconsentGated ?? false
);
const sdvStates = computed(() => demoWorkspaceRef.value?.sdvStates ?? {});

function getSdvKey(fieldId) {
  return demoWorkspaceRef.value?.getSdvKey?.(fieldId) ?? "";
}

function commitChange(...args) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.commitChange(...args);
  }
  allowUnbackedAction("Editing the sample form");
}

function saveChange(...args) {
  return demoWorkspaceRef.value?.saveChange?.(...args);
}

function handleCompleteReconsent(...args) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.handleCompleteReconsent(...args);
  }
  allowUnbackedAction("Recording re-consent");
}

function onSubjectScreen(...args) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.onSubjectScreen(...args);
  }
  return scopeStore.screenSubject();
}

async function submitEcrf() {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.submitEcrf();
  }
  allowUnbackedAction("Submitting the sample form");
}

function createQuery(fieldId, msg = null) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.createQuery(fieldId, msg);
  }
  allowUnbackedAction("Raising a query");
}

function respondQuery(fieldId, resp = null) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.respondQuery(fieldId, resp);
  }
  allowUnbackedAction("Answering a query");
}

function closeQuery(fieldId) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.closeQuery(fieldId);
  }
  allowUnbackedAction("Closing a query");
}

function reopenQuery(fieldId) {
  if (demoWorkspaceRef.value) {
    return demoWorkspaceRef.value.reopenQuery(fieldId);
  }
  allowUnbackedAction("Reopening a query");
}

defineExpose({
  screeningResult,
  isReconsentGated,
  sdvStates,
  getSdvKey,
  commitChange,
  saveChange,
  handleCompleteReconsent,
  onSubjectScreen,
  submitEcrf,
  createQuery,
  respondQuery,
  closeQuery,
  reopenQuery,
});
</script>
