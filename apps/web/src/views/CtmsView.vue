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
      :section-mode="sectionMode"
    />
  </div>
</template>

<script setup>
// Modal dialogs in the demo workspace use ClinicalModal from packages/ui.
import { computed, defineAsyncComponent, ref } from "vue";
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

// Apply incoming route/prop scope synchronously before child panels render.
if (props.studyCode) {
  scopeStore.activeStudyId = props.studyCode;
}
if (props.initialSiteId) {
  scopeStore.activeSiteId = props.initialSiteId;
}
if (route && route.query) {
  if (typeof route.query.studyId === "string" && route.query.studyId) {
    scopeStore.activeStudyId = route.query.studyId;
  }
  if (typeof route.query.siteId === "string" && route.query.siteId) {
    scopeStore.activeSiteId = route.query.siteId;
  } else if (typeof route.query.site === "string" && route.query.site) {
    scopeStore.activeSiteId = route.query.site;
  }
  if (typeof route.query.subjectId === "string" && route.query.subjectId) {
    scopeStore.activeSubjectId = route.query.subjectId;
  }
  if (typeof route.query.visitId === "string" && route.query.visitId) {
    scopeStore.activeVisitId = route.query.visitId;
  }
}

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
