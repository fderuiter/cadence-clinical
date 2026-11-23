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
import CtmsLiveWorkspace from "./CtmsLiveWorkspace.vue";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
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
const { route, router } = resolveRouteAndRouter();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();
const demoBuild = isDemoBuild();
const isStandaloneRoute = computed(
  () => !props.sectionMode && !props.studyCode
);

const demoWorkspaceRef = ref(null);
const liveWorkspaceRef = ref(null);

const initialRouteScope = readRouteScopeQuery(route);
const hydratedScope = resolveHydratedWorkspaceScope(route, {
  studyId: props.studyCode,
  siteId: props.initialSiteId,
});

// Apply incoming route/prop scope synchronously before child panels render.
if (props.studyCode.trim()) {
  const fixedStudy = props.studyCode.trim();
  setActiveStudyCode(fixedStudy);
  scopeStore.activeStudyId = fixedStudy;
} else if (initialRouteScope.studyId) {
  setActiveStudyCode(initialRouteScope.studyId);
  scopeStore.activeStudyId = initialRouteScope.studyId;
} else if (
  !scopeStore.activeStudyId &&
  (hydratedScope.studyId || activeStudyCode.value)
) {
  scopeStore.activeStudyId = hydratedScope.studyId || activeStudyCode.value;
}
if (props.initialSiteId.trim()) {
  scopeStore.activeSiteId = props.initialSiteId.trim();
} else if (
  !initialRouteScope.siteId &&
  hydratedScope.siteId &&
  !scopeStore.activeSiteId
) {
  scopeStore.activeSiteId = hydratedScope.siteId;
}
if (scopeStore.activeStudyId || scopeStore.activeSiteId) {
  syncWorkspaceScope({
    ...(scopeStore.activeStudyId ? { studyId: scopeStore.activeStudyId } : {}),
    ...(scopeStore.activeSiteId || scopeStore.activeStudyId
      ? { siteId: scopeStore.activeSiteId }
      : {}),
  });
}
if (route && route.query) {
  const hasExplicitRouteScope = Boolean(
    initialRouteScope.studyId ||
    initialRouteScope.siteId ||
    initialRouteScope.subjectId
  );
  if (isStandaloneRoute.value && hasExplicitRouteScope) {
    scopeStore.activeSiteId = initialRouteScope.siteId;
    scopeStore.activeSubjectId = initialRouteScope.subjectId;
  } else {
    if (initialRouteScope.siteId) {
      scopeStore.activeSiteId = initialRouteScope.siteId;
    }
    if (initialRouteScope.subjectId) {
      scopeStore.activeSubjectId = initialRouteScope.subjectId;
    }
  }
  if (initialRouteScope.visitId) {
    scopeStore.activeVisitId = initialRouteScope.visitId;
  }
}

watch(
  () => [
    route?.query?.studyId,
    route?.query?.siteId,
    route?.query?.site,
    route?.query?.subjectId,
    route?.query?.participant,
  ],
  () => {
    if (!route?.query) return;
    const nextScope = readRouteScopeQuery(route);
    if (nextScope.studyId) {
      setActiveStudyCode(nextScope.studyId);
    }
    if (isStandaloneRoute.value) {
      const resolvedStudy = nextScope.studyId || activeStudyCode.value;
      scopeStore.activeStudyId = resolvedStudy;
      scopeStore.activeSiteId = nextScope.siteId;
      scopeStore.activeSubjectId = nextScope.subjectId;
      syncWorkspaceScope({
        ...(resolvedStudy ? { studyId: resolvedStudy } : {}),
        siteId: nextScope.siteId,
      });
      return;
    }
    if (nextScope.studyId) {
      scopeStore.activeStudyId = nextScope.studyId;
    }
    if (nextScope.siteId) {
      scopeStore.activeSiteId = nextScope.siteId;
    }
  }
);

watch(activeStudyCode, (next) => {
  if (!props.studyCode.trim() && next && next !== scopeStore.activeStudyId) {
    scopeStore.activeStudyId = next;
  }
});

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

watch(
  [
    () => scopeStore.activeStudyId,
    () => scopeStore.activeSiteId,
    () => scopeStore.activeSubjectId,
  ],
  ([nextStudy, nextSite, nextSubject]) => {
    if (nextStudy) {
      setActiveStudyCode(nextStudy);
    }
    if (!isStandaloneRoute.value) return;
    void replaceRouteQueryParams(router, route, {
      studyId: nextStudy || null,
      siteId: nextSite || null,
      subjectId: nextSubject || null,
    });
  }
);

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
