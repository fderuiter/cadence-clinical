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
import { useRoute } from "vue-router";
import CtmsLiveWorkspace from "./CtmsLiveWorkspace.vue";
import { useClinicalScopeStore } from "../stores/clinicalScope";
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
const route = useRoute();
const demoBuild = isDemoBuild();

const demoWorkspaceRef = ref(null);
const liveWorkspaceRef = ref(null);

const initialQueryStudy =
  typeof route?.query?.studyId === "string" ? route.query.studyId.trim() : "";
const initialQuerySite =
  typeof route?.query?.siteId === "string" && route.query.siteId.trim()
    ? route.query.siteId.trim()
    : typeof route?.query?.site === "string"
      ? route.query.site.trim()
      : "";

// Apply incoming route/prop scope synchronously before child panels render.
if (props.studyCode.trim()) {
  scopeStore.activeStudyId = props.studyCode.trim();
}
if (props.initialSiteId.trim()) {
  scopeStore.activeSiteId = props.initialSiteId.trim();
}
if (route && route.query) {
  if (initialQueryStudy) {
    scopeStore.activeStudyId = initialQueryStudy;
  }
  if (initialQuerySite) {
    scopeStore.activeSiteId = initialQuerySite;
  }
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
    if (trimmedStudy) {
      scopeStore.activeStudyId = trimmedStudy;
    }
    const trimmedSite =
      typeof nextSiteId === "string" && nextSiteId.trim()
        ? nextSiteId.trim()
        : typeof nextSite === "string"
          ? nextSite.trim()
          : "";
    if (trimmedSite) {
      scopeStore.activeSiteId = trimmedSite;
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
