<template>
  <div id="study-canvas" class="canvas">
    <a class="skip-link" href="#canvas-workspace">Skip to workspace</a>

    <header class="topbar">
      <nav class="zoom-path" aria-label="Zoom path">
        <ol>
          <li>
            <router-link id="zoom-studies" :to="{ name: 'study' }">
              Studies
            </router-link>
          </li>
          <li v-if="studyCode">
            <router-link
              id="zoom-study"
              :to="studyLocation()"
              :aria-current="zoomCurrent === 'study' ? 'page' : undefined"
            >
              {{ studyCode }}
            </router-link>
          </li>
          <li v-if="zoomSite">
            <router-link
              id="zoom-site"
              :to="
                studyLocation({
                  stage: 'conduct',
                  section: 'participants',
                  site: zoomSite,
                })
              "
              :aria-current="zoomCurrent === 'site' ? 'page' : undefined"
            >
              Site {{ zoomSite }}
            </router-link>
          </li>
          <li v-if="zoomParticipant">
            <span
              v-if="zoomCurrent === 'participant'"
              id="zoom-participant"
              aria-current="page"
              >{{ zoomParticipant.subject_id }}</span
            >
            <router-link
              v-else
              id="zoom-participant"
              :to="recordLocationFor(zoomParticipant)"
            >
              {{ zoomParticipant.subject_id }}
            </router-link>
          </li>
          <li v-if="selectedObservation">
            <span id="zoom-observation" aria-current="page">{{
              selectedObservation.test_name
            }}</span>
          </li>
        </ol>
      </nav>

      <button
        id="btn-open-palette"
        type="button"
        class="palette-trigger"
        aria-keyshortcuts="Meta+K Control+K"
        @click="paletteOpen = true"
      >
        <span>Go to…</span>
        <kbd>⌘K</kbd>
      </button>

      <div class="user">
        <span id="canvas-user">{{
          authStore.identity?.username || "Signed in"
        }}</span>
        <button
          id="btn-canvas-logout"
          type="button"
          class="btn btn-secondary"
          @click="authStore.logout()"
        >
          Sign out
        </button>
      </div>
    </header>

    <template v-if="studyCode">
      <div class="study-header">
        <h1 id="canvas-study-title">
          {{ studyCode }}
        </h1>
        <nav aria-label="Lifecycle">
          <ol class="lifecycle-rail">
            <li v-for="item in STAGES" :key="item.id">
              <router-link
                :id="`stage-${item.id}`"
                :to="studyLocation({ stage: item.id })"
                class="stage"
                :class="{ current: stage?.id === item.id }"
                :aria-current="stage?.id === item.id ? 'step' : undefined"
              >
                {{ item.label }}
              </router-link>
            </li>
          </ol>
        </nav>
      </div>

      <div class="panes">
        <nav id="canvas-navigator" class="navigator" aria-label="Sections">
          <router-link
            id="nav-study-home"
            :to="studyLocation()"
            class="nav-link"
            :class="{ current: !stage }"
            :aria-current="!stage ? 'page' : undefined"
          >
            Study home
          </router-link>
          <div
            v-for="group in navigatorStages"
            :key="group.id"
            class="nav-group"
          >
            <h2 class="nav-group-title">
              {{ group.label }}
            </h2>
            <ul v-if="group.sections.length">
              <li v-for="item in group.sections" :key="item.id">
                <router-link
                  :id="`nav-${item.id}`"
                  :to="studyLocation({ stage: group.id, section: item.id })"
                  class="nav-link"
                  :class="{ current: section?.id === item.id }"
                  :aria-current="section?.id === item.id ? 'page' : undefined"
                >
                  {{ item.label }}
                </router-link>
              </li>
            </ul>
            <p v-else class="nav-empty">Nothing here has a service yet.</p>
          </div>
        </nav>

        <main
          id="canvas-workspace"
          class="workspace"
          tabindex="-1"
          :aria-label="workspaceLabel"
        >
          <template v-if="recordRef">
            <ParticipantRecord
              v-if="recordParticipant"
              :participant="recordParticipant"
              :selected-observation-id="selectedObservation?.id ?? ''"
              :can-manage="canManage"
              @select-observation="selectObservation"
              @retry="canvas.loadRecord(recordParticipant.subject_id)"
            />
            <p v-else id="record-not-loaded" class="stage-empty">
              This participant isn't among those loaded for {{ studyCode }}.
              <router-link
                :to="
                  studyLocation({ stage: 'conduct', section: 'participants' })
                "
              >
                Find it in Participants </router-link
              >.
            </p>
          </template>

          <template v-else-if="!stage">
            <StudyPulse @drill="drillToParticipants" />
            <StudyActivity :key="studyCode" :study-code="studyCode" />
          </template>

          <ParticipantsSection
            v-else-if="section?.id === 'participants'"
            :status="statusFilter"
            :site-id="siteFilter"
            :selected-id="selectedNode?.id ?? ''"
            :can-manage-participants="canChangeParticipants"
            @select="selectParticipant"
            @filter="applyParticipantFilter"
          />

          <ProtocolVersionsSection
            v-else-if="section?.id === 'protocol-versions'"
            :key="studyCode"
            :study-code="studyCode"
            :selected-ref="selectedVersionText"
            @select="selectVersion"
          />

          <SiteActivationPanel
            v-else-if="section?.id === 'site-activation'"
            :key="studyCode"
            :study-code="studyCode"
          />

          <section
            v-else
            id="stage-overview"
            class="stage-overview"
            aria-labelledby="stage-overview-title"
          >
            <h2 id="stage-overview-title">
              {{ stage.label }}
            </h2>
            <p class="stage-scope">{{ stage.scope }}.</p>
            <ul v-if="stage.sections.length" class="stage-sections">
              <li v-for="item in stage.sections" :key="item.id">
                <router-link
                  :to="studyLocation({ stage: stage.id, section: item.id })"
                >
                  {{ item.label }}
                </router-link>
                <span class="stage-section-description">
                  {{ item.description }}.</span
                >
              </li>
            </ul>
            <p id="stage-empty" class="stage-empty">
              {{
                stage.sections.length
                  ? `The rest of ${stage.label} arrives as its services are built.`
                  : `No part of ${stage.label} has a service yet.`
              }}
              <a
                :href="UNAVAILABLE_SCREENS_ISSUE"
                target="_blank"
                rel="noopener"
                >See what's planned (#4829)</a
              >.
            </p>
          </section>
        </main>

        <CanvasInspector
          :study-code="studyCode"
          :participant="inspectedParticipant"
          :node-ref="inspectedNodeText"
          :missing="Boolean(selectedNode) && !selectedParticipant"
          :observation="selectedObservation"
          :protocol-version="inspectedVersion"
          :protocol-version-error="inspectedVersionError"
          :record-location="
            inspectedParticipant && inspectedParticipant !== recordParticipant
              ? recordLocationFor(inspectedParticipant)
              : null
          "
          :can-verify="canVerify"
          :verify-sdv="canvas.verifySdv"
          :flag-sdv="canvas.flagSdv"
          :can-manage="canManage"
          :can-manage-participants="canChangeParticipants"
          :observation-queries="observationQueries"
          :raise-query="canvas.raiseQuery"
        />
      </div>
    </template>

    <main v-else id="canvas-workspace" class="open-study" tabindex="-1">
      <h1 id="canvas-study-title">Open a study</h1>
      <form
        id="open-study-form"
        class="open-study-form"
        @submit.prevent="openTypedStudy"
      >
        <label for="open-study-code">Study code</label>
        <input
          id="open-study-code"
          v-model="typedStudy"
          type="text"
          autocomplete="off"
          maxlength="255"
          placeholder="e.g. CAD-101"
        />
        <button
          id="btn-open-study"
          type="submit"
          class="btn btn-primary"
          :disabled="!typedStudy.trim()"
        >
          Open
        </button>
      </form>
      <p class="open-study-note">
        No service lists studies yet, so a study is opened by its code.
      </p>
    </main>

    <CanvasPalette
      v-if="paletteOpen"
      :study-code="studyCode"
      :participants="canvas.participants"
      @close="paletteOpen = false"
      @go="goToPaletteTarget"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import { useRoute, useRouter, type LocationQueryRaw } from "vue-router";
import type { Observation, SubjectSummary } from "../api/executionApi";
import { kernelApi, type KernelProtocolVersion } from "../api/kernelApi";
import {
  STAGES,
  findSection,
  findStage,
  isParticipantState,
} from "../canvas/lifecycle";
import {
  formatNodeRef,
  parseNodeRef,
  tryNodeRef,
  type NodeRef,
} from "../canvas/nodeRef";
import {
  canManageParticipants,
  canManageQueries,
  canVerifySource,
} from "../canvas/authority";
import type { PaletteTarget } from "../canvas/palette";
import { UNAVAILABLE_SCREENS_ISSUE } from "../router/availability.js";
import { useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import CanvasInspector from "../components/canvas/CanvasInspector.vue";
import CanvasPalette from "../components/canvas/CanvasPalette.vue";
import ParticipantRecord from "../components/canvas/ParticipantRecord.vue";
import ParticipantsSection from "../components/canvas/ParticipantsSection.vue";
import ProtocolVersionsSection from "../components/canvas/ProtocolVersionsSection.vue";
import StudyActivity from "../components/canvas/StudyActivity.vue";
import StudyPulse from "../components/canvas/StudyPulse.vue";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";

/**
 * The Clinical Canvas (docs/ux/CLINICAL_CANVAS.md): one study at a time, navigated by lifecycle stage, in
 * three panes (navigator, workspace, inspector), with a ⌘K palette. Everything it shows is read from a
 * service; what no service serves yet is absent rather than mocked (§6.1).
 *
 * The URL holds the whole state, so every view is addressable (UX-2):
 * `/studies/:studyCode?stage=&section=&status=&site=&node=&participant=&observation=`. `node` (the participant or
 * protocol version in the inspector) and `participant` (the participant whose data is open) are kernel NodeRefs (ADR-2481);
 * `observation` is Execution's observation id, which has no kernel reference yet.
 */
interface StudyQuery {
  stage?: string;
  section?: string;
  status?: string;
  site?: string;
  node?: string;
  participant?: string;
  observation?: string;
}

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const canvas = useStudyCanvasStore();

const paletteOpen = ref(false);
const typedStudy = ref("");

const single = (value: unknown): string =>
  typeof value === "string" ? value : "";

const studyCode = computed(() => single(route.params.studyCode).trim());
const section = computed(() => findSection(route.query.section)?.section);
const stage = computed(
  () => findSection(route.query.section)?.stage ?? findStage(route.query.stage)
);
const statusFilter = computed(() => {
  const status = single(route.query.status);
  return isParticipantState(status) ? status : "";
});
const siteFilter = computed(() => single(route.query.site).trim());
const selectedNode = computed<NodeRef | null>(() => {
  const node = parseNodeRef(route.query.node);
  return node?.kind === "participant" ? node : null;
});
const selectedVersionNode = computed<NodeRef | null>(() => {
  const node = parseNodeRef(route.query.node);
  return node?.kind === "protocol_version" ? node : null;
});
const selectedVersionText = computed(() =>
  selectedVersionNode.value ? formatNodeRef(selectedVersionNode.value) : ""
);
// The protocol version in the inspector, read from the kernel whenever the URL names one.
const inspectedVersion = ref<KernelProtocolVersion | null>(null);
const inspectedVersionError = ref("");
let versionRequest = 0;
watch(
  selectedVersionText,
  async (text) => {
    const request = ++versionRequest;
    inspectedVersion.value = null;
    inspectedVersionError.value = "";
    if (!text) return;
    try {
      const version = await kernelApi.getProtocolVersion(text);
      if (request === versionRequest) inspectedVersion.value = version;
    } catch (e) {
      if (request === versionRequest) {
        inspectedVersionError.value =
          e instanceof Error ? e.message : String(e);
      }
    }
  },
  { immediate: true }
);
const selectedParticipant = computed<SubjectSummary | null>(() =>
  selectedNode.value
    ? (canvas.knownParticipants[selectedNode.value.id] ?? null)
    : null
);

const participantRef = (value: unknown): NodeRef | null => {
  const node = parseNodeRef(value);
  return node?.kind === "participant" ? node : null;
};
const recordRef = computed(() => participantRef(route.query.participant));
const recordParticipant = computed<SubjectSummary | null>(() =>
  recordRef.value
    ? (canvas.knownParticipants[recordRef.value.id] ?? null)
    : null
);
const selectedObservation = computed<Observation | null>(() => {
  const id = single(route.query.observation);
  if (!id || !recordParticipant.value) return null;
  return canvas.record.observations.find((o) => o.id === id) ?? null;
});
const inspectedParticipant = computed(
  () => selectedParticipant.value ?? recordParticipant.value
);
const inspectedNodeText = computed(() => {
  const participant = inspectedParticipant.value;
  const node = participant ? tryNodeRef("participant", participant.id) : null;
  return node ? formatNodeRef(node) : "";
});
const canVerify = computed(() => canVerifySource(authStore));
const observationQueries = computed(() =>
  selectedObservation.value
    ? canvas.record.queries.filter(
        (q) => q.observation_id === selectedObservation.value?.id
      )
    : []
);
const canManage = computed(() => canManageQueries(authStore));
const canChangeParticipants = computed(() => canManageParticipants(authStore));

const navigatorStages = computed(() =>
  stage.value ? [stage.value] : STAGES.filter((s) => s.sections.length)
);

const zoomParticipant = computed(
  () => recordParticipant.value ?? selectedParticipant.value
);
const zoomSite = computed(
  () => zoomParticipant.value?.site_id || siteFilter.value || ""
);
const zoomCurrent = computed(() => {
  if (selectedObservation.value) return "observation";
  if (recordParticipant.value) return "participant";
  if (selectedParticipant.value) return "participant";
  return zoomSite.value ? "site" : "study";
});

const workspaceLabel = computed(() => {
  if (recordRef.value) {
    return `${recordParticipant.value?.subject_id ?? "Participant"} data`;
  }
  if (!stage.value) return `${studyCode.value} home`;
  return section.value
    ? `${stage.value.label}: ${section.value.label}`
    : stage.value.label;
});

function studyLocation(query: StudyQuery = {}, code = studyCode.value) {
  const clean: LocationQueryRaw = {};
  for (const [key, value] of Object.entries(query)) {
    if (value) clean[key] = value;
  }
  return { name: "study", params: { studyCode: code }, query: clean };
}

function selectParticipant(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  if (!node) return;
  router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      status: statusFilter.value,
      site: siteFilter.value,
      node: formatNodeRef(node),
    })
  );
  // Stacked panes on a narrow screen: bring the inspector into view (§2.4).
  nextTick(() =>
    document.getElementById("canvas-inspector")?.scrollIntoView?.({
      block: "nearest",
    })
  );
}

/** The participant's data, opened at the participant zoom level with the participant in the inspector. */
function recordLocationFor(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  const text = node ? formatNodeRef(node) : "";
  return studyLocation({ stage: "conduct", participant: text });
}

function selectObservation(observation: Observation) {
  router.push(
    studyLocation({
      stage: "conduct",
      participant: single(route.query.participant),
      observation: observation.id,
    })
  );
  nextTick(() =>
    document.getElementById("canvas-inspector")?.scrollIntoView?.({
      block: "nearest",
    })
  );
}

function selectVersion(version: KernelProtocolVersion) {
  if (!version.ref) return;
  router.push(
    studyLocation({
      stage: "design",
      section: "protocol-versions",
      node: version.ref,
    })
  );
  nextTick(() =>
    document.getElementById("canvas-inspector")?.scrollIntoView?.({
      block: "nearest",
    })
  );
}

function applyParticipantFilter(filter: { status: string; siteId: string }) {
  router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      status: filter.status,
      site: filter.siteId,
      node: single(route.query.node),
    })
  );
}

function drillToParticipants(status: string) {
  router.push(
    studyLocation({ stage: "conduct", section: "participants", status })
  );
}

function goToPaletteTarget(target: PaletteTarget) {
  paletteOpen.value = false;
  const node = target.participant
    ? tryNodeRef("participant", target.participant.id)
    : null;
  router.push(
    studyLocation(
      {
        stage: target.stage,
        section: target.section,
        node: node ? formatNodeRef(node) : "",
      },
      target.studyCode
    )
  );
}

function openTypedStudy() {
  const code = typedStudy.value.trim();
  if (code) router.push(studyLocation({}, code));
}

// Switching study drops everything read for the previous one.
watch(
  studyCode,
  (code) => {
    canvas.openStudy(code);
  },
  { immediate: true }
);

// Open a participant's data once the canvas knows which participant it is.
watch(
  [studyCode, () => recordParticipant.value?.subject_id],
  ([, code]) => {
    if (code && canvas.record.subjectCode !== code) canvas.loadRecord(code);
  },
  { immediate: true }
);

// Load what the current view shows, once per study and filter.
watch(
  [
    studyCode,
    () => stage.value?.id,
    () => section.value?.id,
    statusFilter,
    siteFilter,
  ],
  () => {
    if (!studyCode.value || recordRef.value) return;
    if (!stage.value) {
      if (!canvas.pulse && !canvas.pulseLoading) canvas.loadPulse();
      return;
    }
    if (section.value?.id !== "participants") return;
    const filter = { status: statusFilter.value, siteId: siteFilter.value };
    const loaded = canvas.participantsFilter;
    const sameFilter =
      (loaded.status ?? "") === filter.status &&
      (loaded.siteId ?? "") === filter.siteId;
    const hasPage =
      canvas.participants.length > 0 || canvas.participantsTotal > 0;
    if (!sameFilter || (!hasPage && !canvas.participantsLoading)) {
      canvas.loadParticipants({
        status: filter.status || undefined,
        siteId: filter.siteId || undefined,
      });
    }
  },
  { immediate: true }
);

function onGlobalKeydown(event: KeyboardEvent) {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    paletteOpen.value = !paletteOpen.value;
  }
}

onMounted(() => document.addEventListener("keydown", onGlobalKeydown));
onUnmounted(() => document.removeEventListener("keydown", onGlobalKeydown));
</script>

<style scoped>
.canvas {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted, #f8fafc);
  color: var(--primary);
  font-family: var(--font);
}

.skip-link {
  position: absolute;
  left: -9999px;
}

.skip-link:focus {
  left: 16px;
  top: 8px;
  z-index: 60;
}

.topbar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 8px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface, #fff);
}

.zoom-path {
  flex: 1;
  min-width: 0;
}

.zoom-path ol {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.zoom-path li + li::before {
  content: "/";
  margin-right: 6px;
  color: var(--neutral-dark);
}

.zoom-path a {
  color: inherit;
}

.zoom-path [aria-current="page"] {
  font-weight: 600;
}

.palette-trigger {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.palette-trigger kbd {
  font-family: inherit;
  font-size: 0.8rem;
}

.user {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9rem;
}

.study-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 24px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface, #fff);
}

.study-header h1 {
  margin: 0;
  font-size: 1.25rem;
}

.lifecycle-rail {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.stage {
  display: inline-block;
  padding: 4px 12px;
  border-radius: 999px;
  color: var(--neutral-dark);
  text-decoration: none;
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stage.current {
  background: var(--accent-bg);
  color: var(--primary);
  font-weight: 600;
}

.panes {
  flex: 1;
  display: grid;
  grid-template-columns: 14rem minmax(0, 1fr) 20rem;
  min-height: 0;
}

.navigator {
  padding: 12px;
  border-right: 1px solid var(--border);
  background: var(--semantic-color-surface, #fff);
}

.nav-group-title {
  margin: 16px 0 4px;
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.navigator ul {
  margin: 0;
  padding: 0;
  list-style: none;
}

.nav-link {
  display: block;
  padding: 6px 8px;
  border-radius: 6px;
  color: inherit;
  text-decoration: none;
}

.nav-link.current {
  background: var(--accent-bg);
  font-weight: 600;
}

.nav-empty {
  margin: 0;
  padding: 0 8px;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.workspace {
  padding: 16px;
  min-width: 0;
}

.canvas :deep(#canvas-inspector) {
  border-left: 1px solid var(--border);
  background: var(--semantic-color-surface, #fff);
}

.stage-overview h2 {
  margin: 0 0 4px;
  font-size: 1rem;
}

.stage-scope,
.stage-empty,
.stage-section-description,
.open-study-note {
  color: var(--neutral-dark);
}

.stage-sections {
  padding-left: 18px;
}

.open-study {
  max-width: 40rem;
  padding: 32px 16px;
}

.open-study-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.open-study-form label {
  font-weight: 600;
}

.open-study-form input {
  flex: 1 1 12rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

@media (max-width: 960px) {
  .panes {
    grid-template-columns: minmax(0, 1fr);
  }

  .navigator {
    border-right: 0;
    border-bottom: 1px solid var(--border);
  }

  .canvas :deep(#canvas-inspector) {
    border-left: 0;
    border-top: 1px solid var(--border);
  }
}
</style>
