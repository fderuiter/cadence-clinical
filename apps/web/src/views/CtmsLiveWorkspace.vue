<template>
  <div id="ctms-live-workspace" class="live-workspace">
    <nav
      v-if="!sectionMode"
      id="ctms-live-tabs"
      class="live-tabs"
      aria-label="Site readiness workspaces"
    >
      <button
        v-for="tab in liveTabs"
        :id="`ctms-tab-${tab.id}`"
        :key="tab.id"
        type="button"
        class="live-tab-btn"
        :class="{ active: activeSection === tab.id }"
        :aria-pressed="activeSection === tab.id"
        @click="liveTab = tab.id"
      >
        {{ tab.label }}
      </button>
    </nav>

    <SiteActivationPanel
      v-if="activeSection === 'activation'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />
    <CtmsGreenlightSection
      v-else-if="activeSection === 'startup'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />
    <CtmsMilestonesSection
      v-else-if="activeSection === 'milestones'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />
    <CtmsMonitoringSection
      v-else-if="activeSection === 'monitoring'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />
    <CtmsDelegationSection
      v-else-if="activeSection === 'delegation'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />
    <RtsmSupplySection
      v-else-if="activeSection === 'supply'"
      :study-code="scopedStudyProp"
      :site-id="siteId"
    />

    <p
      v-if="!sectionMode"
      id="ctms-unbacked-scope-note"
      class="live-unbacked-note"
    >
      Grant financials, CRA workload allocation, recruitment target updates, and
      adaptive dose escalation remain gated to demo builds in this pre-release
      environment (Orbit and Allocate roadmap). See <PlansRoadmapLink />.
    </p>
  </div>
</template>

<script setup>
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";
import PlansRoadmapLink from "../components/clinical/PlansRoadmapLink.vue";
import CtmsGreenlightSection from "../components/clinical/CtmsGreenlightSection.vue";
import CtmsMilestonesSection from "../components/clinical/CtmsMilestonesSection.vue";
import CtmsMonitoringSection from "../components/clinical/CtmsMonitoringSection.vue";
import CtmsDelegationSection from "../components/clinical/CtmsDelegationSection.vue";
import RtsmSupplySection from "../components/clinical/RtsmSupplySection.vue";
import { useClinicalScopeStore } from "../stores/clinicalScope";

const props = defineProps({
  studyCode: { type: String, default: "" },
  initialSiteId: { type: String, default: "" },
  sectionMode: { type: String, default: "" },
});

const route = useRoute();
const scopeStore = useClinicalScopeStore();

const scopedStudyProp = computed(() => {
  const routeStudy =
    typeof route?.query?.studyId === "string" ? route.query.studyId.trim() : "";
  return props.studyCode.trim() || routeStudy;
});

const siteId = computed(() => {
  const routeSite =
    typeof route?.query?.siteId === "string" && route.query.siteId.trim()
      ? route.query.siteId.trim()
      : typeof route?.query?.site === "string"
        ? route.query.site.trim()
        : "";
  return (
    props.initialSiteId.trim() ||
    routeSite ||
    (scopeStore.activeSiteId || "").trim()
  );
});

const liveTabs = [
  { id: "activation", label: "Site activation" },
  { id: "startup", label: "Startup readiness" },
  { id: "milestones", label: "Site milestones" },
  { id: "monitoring", label: "Monitoring visits" },
  { id: "delegation", label: "Delegation log" },
  { id: "supply", label: "Investigational supply" },
];

const liveTab = ref(
  route?.path === "/ssu" || route?.query?.tab === "startup"
    ? "startup"
    : typeof route?.query?.tab === "string" &&
        liveTabs.some((t) => t.id === route.query.tab)
      ? route.query.tab
      : "activation"
);

watch(
  () => route?.query?.tab,
  (nextTab) => {
    if (typeof nextTab === "string" && liveTabs.some((t) => t.id === nextTab)) {
      liveTab.value = nextTab;
    }
  }
);

const activeSection = computed(() => props.sectionMode || liveTab.value);

defineExpose({ liveTab });
</script>

<style scoped>
.live-workspace {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.live-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 10px;
}

.live-tab-btn {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-touch-target-staff);
  padding: 8px 14px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  font-weight: 600;
  font-size: 0.88rem;
  cursor: pointer;
}

.live-tab-btn.active {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.live-unbacked-note {
  margin: 0;
  padding: 10px 14px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-size: 0.88rem;
}
</style>
