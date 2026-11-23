<template>
  <div id="section-ctms" class="dashboard-section active">
    <div v-if="!sectionMode" class="section-header">
      <h2>Site readiness</h2>
      <p>
        Monitor operational site status, track site lifecycle milestones, and
        coordinate site readiness workflows.
      </p>
    </div>

    <CtmsDemoWorkspace
      v-if="demoBuild"
      ref="demoWorkspaceRef"
      :study-code="studyCode"
      :initial-site-id="initialSiteId"
      :section-mode="sectionMode"
    />
    <CtmsLiveWorkspace
      v-else
      ref="liveWorkspaceRef"
      :study-code="studyCode"
      :initial-site-id="initialSiteId"
      :section-mode="sectionMode"
    />
  </div>
</template>

<script setup>
// Modal dialogs in the demo workspace use ClinicalModal from packages/ui.
import { computed, defineAsyncComponent, ref, watch } from "vue";
import * as VueRouter from "vue-router";
import CtmsLiveWorkspace from "./CtmsLiveWorkspace.vue";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import {
  resolveHydratedWorkspaceScope,
  syncWorkspaceScope,
} from "../stores/workspaceScope";
import { isDemoBuild } from "../utils/demoMode.js";

const CtmsDemoWorkspace = defineAsyncComponent(
  () => import("./demo/CtmsDemoWorkspace.vue")
);

const props = defineProps({
  studyCode: { type: String, default: "" },
  initialSiteId: { type: String, default: "" },
  sectionMode: { type: String, default: "" },
});

const scopeStore = useClinicalScopeStore();
const route =
  typeof VueRouter.useRoute === "function" ? VueRouter.useRoute() : null;
const router =
  "useRouter" in VueRouter && typeof VueRouter.useRouter === "function"
    ? VueRouter.useRouter()
    : null;
const demoBuild = isDemoBuild();

const demoWorkspaceRef = ref(null);
const liveWorkspaceRef = ref(null);

// Apply incoming route/prop or hydrated workspace scope synchronously before child panels render.
const hydratedScope = resolveHydratedWorkspaceScope(route, {
  studyId: props.studyCode,
  siteId: props.initialSiteId,
});
if (hydratedScope.studyId || hydratedScope.siteId) {
  syncWorkspaceScope({
    ...(hydratedScope.studyId ? { studyId: hydratedScope.studyId } : {}),
    ...(hydratedScope.siteId || hydratedScope.studyId
      ? { siteId: hydratedScope.siteId }
      : {}),
    router,
    route,
    syncRouteQuery: !demoBuild && !props.sectionMode && !props.studyCode.trim(),
  });
}
if (route && route.query) {
  if (typeof route.query.subjectId === "string" && route.query.subjectId) {
    scopeStore.activeSubjectId = route.query.subjectId;
  }
  if (typeof route.query.visitId === "string" && route.query.visitId) {
    scopeStore.activeVisitId = route.query.visitId;
  }
}

watch(
  [
    () => route?.query?.studyId,
    () => route?.query?.siteId,
    () => route?.query?.site,
  ],
  ([nextStudy, nextSiteId, nextSite]) => {
    const trimmedStudy = typeof nextStudy === "string" ? nextStudy.trim() : "";
    const trimmedSite =
      typeof nextSiteId === "string" && nextSiteId.trim()
        ? nextSiteId.trim()
        : typeof nextSite === "string"
          ? nextSite.trim()
          : "";
    if (trimmedStudy || trimmedSite) {
      syncWorkspaceScope({
        ...(trimmedStudy ? { studyId: trimmedStudy } : {}),
        ...(trimmedSite || trimmedStudy ? { siteId: trimmedSite } : {}),
      });
    }
  }
);

const activeTab = computed({
  get: () =>
    demoWorkspaceRef.value?.activeTab ??
    liveWorkspaceRef.value?.liveTab ??
    "activation",
  set: (next) => {
    if (demoWorkspaceRef.value) {
      demoWorkspaceRef.value.activeTab = next;
    } else if (liveWorkspaceRef.value) {
      liveWorkspaceRef.value.liveTab = next;
    }
  },
});

const effectiveDelegatedStaff = computed(
  () => demoWorkspaceRef.value?.effectiveDelegatedStaff ?? []
);
const sigPassword = computed(() => demoWorkspaceRef.value?.sigPassword ?? "");
const sigTotp = computed(() => demoWorkspaceRef.value?.sigTotp ?? "");

function promptSignature(...args) {
  return demoWorkspaceRef.value?.promptSignature?.(...args);
}

defineExpose({
  activeTab,
  effectiveDelegatedStaff,
  sigPassword,
  sigTotp,
  promptSignature,
});
</script>
