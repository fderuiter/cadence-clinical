<template>
  <div
    id="study-canvas"
    class="canvas"
    :class="{ 'canvas-in-shell': inAppShell }"
    :style="{ '--canvas-topbar-height': `${topbarHeight}px` }"
  >
    <a v-if="!inAppShell" class="skip-link" href="#canvas-workspace"
      >Skip to workspace</a
    >

    <component
      :is="inAppShell ? 'div' : 'header'"
      ref="topbarRef"
      class="topbar"
      :role="inAppShell ? 'region' : undefined"
      :aria-label="inAppShell ? 'Study context' : undefined"
    >
      <nav id="canvas-zoom-path" class="zoom-path" aria-label="Zoom path">
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
          <li v-if="zoomParticipantCode">
            <span
              v-if="zoomCurrent === 'participant'"
              id="zoom-participant"
              aria-current="page"
              >{{ zoomParticipantCode }}</span
            >
            <router-link
              v-else
              id="zoom-participant"
              :to="zoomParticipantLocation"
            >
              {{ zoomParticipantCode }}
            </router-link>
          </li>
          <li v-if="hasOpenForm">
            <span
              v-if="zoomCurrent === 'form'"
              id="zoom-form"
              aria-current="page"
              >{{ zoomFormLabel }}</span
            >
            <router-link v-else id="zoom-form" :to="zoomFormLocation">
              {{ zoomFormLabel }}
            </router-link>
          </li>
          <li v-if="openObservationRef">
            <span id="zoom-observation" aria-current="page">{{
              zoomObservationLabel
            }}</span>
          </li>
        </ol>
      </nav>

      <button
        v-if="!inAppShell"
        id="btn-open-palette"
        type="button"
        class="palette-trigger"
        aria-keyshortcuts="Meta+K Control+K"
        @click="paletteOpen = true"
      >
        <span>Go to…</span>
        <kbd>⌘K</kbd>
      </button>

      <button
        id="btn-ai-assistance"
        type="button"
        class="ai-toggle"
        :aria-pressed="aiEnabled"
        aria-describedby="ai-assistance-note"
        @click="setAiEnabled(!aiEnabled)"
      >
        AI assistance: {{ aiEnabled ? "on" : "off" }}
      </button>
      <span id="ai-assistance-note" class="sr-only">
        Shows or hides Assist and the palette's Ask entry. Proposals an agent
        already made still wait for a decision in Needs attention.
      </span>

      <div v-if="!inAppShell" class="user">
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
    </component>

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

      <div
        class="panes"
        :class="{ 'panes--inspector-collapsed': inspectorCollapsed }"
      >
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

        <component
          :is="inAppShell ? 'div' : 'main'"
          id="canvas-workspace"
          class="workspace"
          :role="inAppShell ? 'region' : undefined"
          tabindex="-1"
          :aria-label="workspaceLabel"
        >
          <ProposalReview
            v-if="proposalRef"
            :key="proposalRef"
            :study-code="studyCode"
            :proposal-ref="proposalRef"
            :reviewer="authStore.userId ?? ''"
          />

          <ObservationView
            v-else-if="openObservationRef"
            :key="openObservationRef"
            embedded
            :study-code="studyCode"
            :observation-ref="openObservationRef"
            :return-to="observationReturnLocation"
            :return-label="observationReturnLabel"
            @loaded="onObservationLoaded"
          />

          <template v-else-if="hasOpenForm">
            <p
              v-if="
                isNewForm &&
                recordRef &&
                !recordParticipant &&
                isRecordParticipantLoading
              "
              id="record-loading"
              class="stage-empty"
              role="status"
            >
              Loading participant…
            </p>
            <p
              v-else-if="isNewForm && recordRef && !recordParticipant"
              id="record-not-loaded"
              class="stage-empty"
            >
              This participant isn't among those loaded for {{ studyCode }}.
              <router-link
                :to="
                  studyLocation({ stage: 'conduct', section: 'participants' })
                "
              >
                Find it in Participants </router-link
              >.
            </p>
            <CanvasFormWorkspace
              v-else
              :study-code="studyCode"
              :participant="activeFormParticipant"
              :enrollment-id="activeEnrollmentId"
              :record-loading="canvas.record.loading"
              :form-id="openFormId"
              :form-label="
                loadedForm && loadedForm.form_id === openFormId
                  ? formatFormLabel(loadedForm)
                  : undefined
              "
              :can-manage-queries="canManage"
              :can-verify-source="canVerify"
              :back-location="formBackLocation"
              :back-label="formBackLabel"
              :form-location="formLocationById"
              :observation-location="observationLocationFromForm"
              @dirty-change="formWorkspaceDirty = $event"
              @form-loaded="onFormLoaded"
              @closed="onFormClosed"
            />
          </template>

          <template v-else-if="recordRef">
            <ParticipantRecord
              v-if="recordParticipant"
              :participant="recordParticipant"
              :can-manage="canManage"
              :form-location="formLocationById"
              :new-form-location="newFormLocationFor(recordParticipant)"
              :observation-location="observationLocationFromRecord"
              @retry="canvas.loadRecord(recordParticipant)"
            />
            <p
              v-else-if="isRecordParticipantLoading"
              id="record-loading"
              class="stage-empty"
              role="status"
            >
              Loading participant…
            </p>
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
            <NeedsAttention
              :key="`attention-${studyCode}`"
              :study-code="studyCode"
            />
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

          <QueriesSection
            v-else-if="section?.id === 'queries'"
            :key="`queries-${studyCode}`"
            :study-code="studyCode"
            :can-manage="canManage"
            :status="queryStatusFilter"
            :site="siteFilter"
            :participant="queryParticipantFilter"
            :changed="queryChangedFilter"
            @filter="applyStudyQueryFilter"
          />

          <LocksSection
            v-else-if="section?.id === 'locks'"
            :key="`locks-${studyCode}`"
            :study-code="studyCode"
          />

          <ExportsSection
            v-else-if="section?.id === 'exports'"
            :key="`exports-${studyCode}`"
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
              <router-link to="/plans">
                Compare product families &amp; pre-release roadmap </router-link
              >.
            </p>
          </section>
        </component>

        <CanvasInspector
          ref="inspectorRef"
          :study-code="studyCode"
          :participant="inspectedParticipant"
          :node-ref="inspectedNodeText"
          :missing="
            Boolean(selectedNode) &&
            !selectedParticipant &&
            !isSelectedParticipantLoading
          "
          :protocol-version="inspectedVersion"
          :protocol-version-error="inspectedVersionError"
          :record-location="
            inspectedParticipant &&
            (inspectedParticipant !== recordParticipant ||
              hasOpenForm ||
              Boolean(openObservationRef))
              ? recordLocationFor(inspectedParticipant)
              : null
          "
          :data-entry-location="
            inspectedParticipant &&
            !(isNewForm && inspectedParticipant === recordParticipant)
              ? dataEntryLocationFor(inspectedParticipant)
              : null
          "
          :can-manage-participants="canChangeParticipants"
          :assist-prompt="askText"
          @update:collapsed="inspectorCollapsed = $event"
        />
      </div>
    </template>

    <component
      :is="inAppShell ? 'div' : 'main'"
      v-else
      id="canvas-workspace"
      class="open-study"
      :role="inAppShell ? 'region' : undefined"
      :aria-label="inAppShell ? 'Open a study' : undefined"
      tabindex="-1"
    >
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
    </component>

    <CanvasPalette
      v-if="!inAppShell && paletteOpen"
      :study-code="studyCode"
      :participants="canvas.paletteParticipants"
      @close="paletteOpen = false"
      @go="goToPaletteTarget"
    />
  </div>
</template>

<script setup lang="ts">
import {
  computed,
  inject,
  nextTick,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import {
  onBeforeRouteLeave,
  onBeforeRouteUpdate,
  useRoute,
  useRouter,
  type LocationQueryRaw,
  type RouteLocationNormalized,
} from "vue-router";
import type { EcrfForm } from "../api/ecrfFormsApi";
import type { SubjectSummary } from "../api/executionApi";
import {
  kernelApi,
  type KernelObservation,
  type KernelProtocolVersion,
} from "../api/kernelApi";
import {
  STAGES,
  findSection,
  findStage,
  isParticipantState,
} from "../canvas/lifecycle";
import {
  collectionInstanceId,
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
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
import { useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import CanvasFormWorkspace from "../components/canvas/CanvasFormWorkspace.vue";
import CanvasInspector from "../components/canvas/CanvasInspector.vue";
import CanvasPalette from "../components/canvas/CanvasPalette.vue";
import ExportsSection from "../components/canvas/ExportsSection.vue";
import LocksSection from "../components/canvas/LocksSection.vue";
import ParticipantRecord from "../components/canvas/ParticipantRecord.vue";
import ParticipantsSection from "../components/canvas/ParticipantsSection.vue";
import ProtocolVersionsSection from "../components/canvas/ProtocolVersionsSection.vue";
import QueriesSection, {
  type StudyQueriesFilterState,
} from "../components/canvas/QueriesSection.vue";
import StudyActivity from "../components/canvas/StudyActivity.vue";
import StudyPulse from "../components/canvas/StudyPulse.vue";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";
import NeedsAttention from "../components/ai/NeedsAttention.vue";
import ProposalReview from "../components/ai/ProposalReview.vue";
import ObservationView from "./ObservationView.vue";
import { useAiAssistance } from "../ai/aiAssistance";

/**
 * The Clinical Canvas (docs/ux/CLINICAL_CANVAS.md): one study at a time, navigated by lifecycle stage, in
 * three panes (navigator, workspace, inspector), with a ⌘K palette. Everything it shows is read from a
 * service; what no service serves yet is absent rather than mocked (§6.1).
 *
 * The URL holds the whole state, so every view is addressable (UX-2):
 * `/studies/:studyCode?stage=&section=&status=&site=&node=&participant=&form=&observation=&proposal=&ask=`. `node` (the
 * participant or protocol version in the inspector), `participant` (the participant whose data is open), `form` (a
 * `collection_instance` NodeRef or `new`), `observation` (an `observation` NodeRef) and `proposal` (the proposal under
 * review in the workspace, ADR-2635) are kernel NodeRefs (ADR-2481). `ask` is a question from the palette for the
 * inspector's Assist.
 */
interface StudyQuery {
  stage?: string;
  section?: string;
  status?: string;
  site?: string;
  subject?: string;
  changed?: string;
  node?: string;
  participant?: string;
  form?: string;
  observation?: string;
  proposal?: string;
  ask?: string;
}

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const canvas = useStudyCanvasStore();
const inAppShell = inject<boolean>("inAppShell", false);

const { enabled: aiEnabled, setEnabled: setAiEnabled } = useAiAssistance();

const paletteOpen = ref(false);
const typedStudy = ref("");
const inspectorCollapsed = ref(true);
const inspectorRef = ref<InstanceType<typeof CanvasInspector> | null>(null);
const topbarRef = ref<HTMLElement | null>(null);
const topbarHeight = ref(45);
let topbarResizeObserver: ResizeObserver | null = null;

function syncTopbarHeight() {
  const el = topbarRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0) {
    topbarHeight.value = measured;
  }
}

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
const queryStatusFilter = computed(() => {
  const status = single(route.query.status).toLowerCase();
  return ["open", "answered", "closed", "cancelled"].includes(status)
    ? status
    : "";
});
const siteFilter = computed(() => single(route.query.site).trim());
const queryParticipantFilter = computed(() =>
  single(route.query.subject).trim()
);
const queryChangedFilter = computed(() => {
  const changed = single(route.query.changed).toLowerCase();
  return changed === "true" || changed === "false" ? changed : "";
});
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
const formQuery = computed(() => single(route.query.form).trim());
const isNewForm = computed(() => formQuery.value === "new");
const openFormId = computed(() => collectionInstanceId(formQuery.value));
const hasOpenForm = computed(
  () => isNewForm.value || Boolean(openFormId.value)
);
const openObservationRef = computed(
  () => observationNodeRef(route.query.observation) ?? ""
);
const proposalRef = computed(() => {
  const node = parseNodeRef(route.query.proposal);
  return node?.kind === "proposal" ? formatNodeRef(node) : "";
});
const askText = computed(() => single(route.query.ask).trim());
const recordParticipant = computed<SubjectSummary | null>(() =>
  recordRef.value
    ? (canvas.knownParticipants[recordRef.value.id] ?? null)
    : null
);

const loadedForm = ref<EcrfForm | null>(null);
const formWorkspaceDirty = ref(false);
const loadedObservation = ref<KernelObservation | null>(null);
const resolvedLineage = ref<{ siteCode: string; subjectCode: string } | null>(
  null
);
const resolvedParticipantId = ref("");
let lineageRequest = 0;

function isParticipantPending(id: string | undefined): boolean {
  if (!studyCode.value || !id) return false;
  if (canvas.knownParticipants[id]) return false;
  if (canvas.participantLookups[id]) return true;
  return !canvas.failedParticipantIds[id];
}

const isRecordParticipantLoading = computed(() =>
  isParticipantPending(recordRef.value?.id)
);
const isSelectedParticipantLoading = computed(() =>
  isParticipantPending(selectedNode.value?.id)
);

const loadedFormParticipant = computed<SubjectSummary | null>(() => {
  const pid = loadedForm.value?.participant_id;
  return pid ? (canvas.knownParticipants[pid] ?? null) : null;
});
const lineageParticipant = computed<SubjectSummary | null>(() =>
  resolvedParticipantId.value
    ? (canvas.knownParticipants[resolvedParticipantId.value] ?? null)
    : null
);
const activeFormParticipant = computed<SubjectSummary | null>(
  () =>
    recordParticipant.value ??
    loadedFormParticipant.value ??
    lineageParticipant.value
);
const activeParticipantNodeText = computed(() => {
  if (recordRef.value) return formatNodeRef(recordRef.value);
  const participant = activeFormParticipant.value;
  const node = participant ? tryNodeRef("participant", participant.id) : null;
  return node ? formatNodeRef(node) : "";
});
const activeEnrollmentId = computed(() => {
  if (
    activeFormParticipant.value &&
    canvas.record.subjectCode === activeFormParticipant.value.subject_id
  ) {
    return canvas.record.enrollmentId;
  }
  if (loadedForm.value?.enrollment_id) return loadedForm.value.enrollment_id;
  return null;
});

const inspectedParticipant = computed(
  () =>
    selectedParticipant.value ??
    recordParticipant.value ??
    loadedFormParticipant.value ??
    lineageParticipant.value
);
const inspectedNodeText = computed(() => {
  const participant = inspectedParticipant.value;
  const node = participant ? tryNodeRef("participant", participant.id) : null;
  return node ? formatNodeRef(node) : "";
});
const canManage = computed(() => canManageQueries(authStore));
const canVerify = computed(() => canVerifySource(authStore));
const canChangeParticipants = computed(() => canManageParticipants(authStore));

const navigatorStages = computed(() =>
  stage.value ? [stage.value] : STAGES.filter((s) => s.sections.length)
);

const zoomParticipant = computed(
  () =>
    recordParticipant.value ??
    loadedFormParticipant.value ??
    selectedParticipant.value ??
    lineageParticipant.value
);
const zoomParticipantCode = computed(
  () =>
    zoomParticipant.value?.subject_id ||
    resolvedLineage.value?.subjectCode ||
    ""
);
const zoomSite = computed(
  () =>
    zoomParticipant.value?.site_id ||
    resolvedLineage.value?.siteCode ||
    siteFilter.value ||
    ""
);
const zoomCurrent = computed(() => {
  if (openObservationRef.value) return "observation";
  if (hasOpenForm.value) return "form";
  if (zoomParticipantCode.value) return "participant";
  return zoomSite.value ? "site" : "study";
});

function formatFormLabel(form: EcrfForm): string {
  const title = form.collection_title || form.collection_spec_id;
  return form.planned_activity_id
    ? `${title} · ${form.planned_activity_id}`
    : title;
}

const zoomFormLabel = computed(() => {
  if (isNewForm.value) return "Forms";
  if (loadedForm.value && loadedForm.value.form_id === openFormId.value) {
    return formatFormLabel(loadedForm.value);
  }
  return "Case report form";
});

const zoomObservationLabel = computed(
  () => loadedObservation.value?.concept || "Observation"
);

const workspaceLabel = computed(() => {
  if (proposalRef.value) return "Proposal review";
  if (openObservationRef.value) {
    return `Observation ${zoomObservationLabel.value}`;
  }
  if (hasOpenForm.value) {
    return zoomFormLabel.value;
  }
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
  inspectorRef.value?.expand();
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

function newFormLocationFor(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  const text = node ? formatNodeRef(node) : "";
  return studyLocation({ stage: "conduct", participant: text, form: "new" });
}

function dataEntryLocationFor(participant: SubjectSummary) {
  return newFormLocationFor(participant);
}

function formLocationById(formId: string) {
  const formRef = collectionInstanceRef(formId);
  return studyLocation({
    stage: "conduct",
    site: !activeParticipantNodeText.value
      ? zoomSite.value || undefined
      : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
  });
}

function observationLocationFromRecord(observationId: string, formId?: string) {
  const obsRef = observationNodeRef(observationId);
  const formRef = formId ? collectionInstanceRef(formId) : null;
  return studyLocation({
    stage: "conduct",
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    observation: obsRef ?? undefined,
  });
}

function observationLocationFromForm(observationId: string, formId: string) {
  const obsRef = observationNodeRef(observationId);
  const formRef = collectionInstanceRef(formId || openFormId.value || "");
  return studyLocation({
    stage: "conduct",
    site: !activeParticipantNodeText.value
      ? zoomSite.value || undefined
      : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    observation: obsRef ?? undefined,
  });
}

const zoomParticipantLocation = computed(() => {
  if (zoomParticipant.value) return recordLocationFor(zoomParticipant.value);
  return studyLocation({
    stage: "conduct",
    section: "participants",
    site: zoomSite.value || undefined,
  });
});

const zoomFormLocation = computed(() => {
  if (openFormId.value) return formLocationById(openFormId.value);
  return studyLocation({
    stage: "conduct",
    participant: activeParticipantNodeText.value || undefined,
    form: "new",
  });
});

const formBackLocation = computed(() => {
  if (activeFormParticipant.value) {
    return recordLocationFor(activeFormParticipant.value);
  }
  return studyLocation({
    stage: "conduct",
    section: "participants",
    site: zoomSite.value || undefined,
  });
});

const formBackLabel = computed(() => {
  if (activeFormParticipant.value) {
    return `Back to ${activeFormParticipant.value.subject_id}`;
  }
  if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
  return `Back to ${studyCode.value} participants`;
});

const observationReturnLocation = computed(() => {
  if (openFormId.value) return formLocationById(openFormId.value);
  if (activeFormParticipant.value) {
    return recordLocationFor(activeFormParticipant.value);
  }
  if (zoomSite.value) {
    return studyLocation({
      stage: "conduct",
      section: "participants",
      site: zoomSite.value,
    });
  }
  return studyLocation();
});

const observationReturnLabel = computed(() => {
  if (openFormId.value) {
    return loadedForm.value && loadedForm.value.form_id === openFormId.value
      ? `Back to ${formatFormLabel(loadedForm.value)}`
      : "Back to form";
  }
  if (activeFormParticipant.value) {
    return `Back to ${activeFormParticipant.value.subject_id}`;
  }
  if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
  return `Back to ${studyCode.value} home`;
});

async function resolveEnrollmentLineage(
  enrollmentRef: string
): Promise<{ req: number; siteCode: string; subjectCode: string } | null> {
  if (!enrollmentRef || typeof kernelApi.getZoomPath !== "function") {
    return null;
  }
  const req = ++lineageRequest;
  try {
    const steps = await kernelApi.getZoomPath(enrollmentRef);
    if (req !== lineageRequest) return null;
    const siteStep = steps.find(
      (s) => s.level === "study_site" && s.mask === null && s.label
    );
    const enrollmentStep = steps.find(
      (s) => s.level === "enrollment" && s.mask === null && s.label
    );
    if (siteStep?.label || enrollmentStep?.label) {
      const lineage = {
        siteCode: siteStep?.label ?? "",
        subjectCode: enrollmentStep?.label ?? "",
      };
      resolvedLineage.value = lineage;
      return { req, ...lineage };
    }
  } catch {
    // Keep whatever scope context is already known in the URL.
  }
  return null;
}

function onFormLoaded(form: EcrfForm) {
  loadedForm.value = form;
  if (isNewForm.value && form.form_id) {
    const formRef = collectionInstanceRef(form.form_id);
    if (formRef) {
      router.replace(
        studyLocation({
          stage: "conduct",
          site: !activeParticipantNodeText.value
            ? zoomSite.value || undefined
            : undefined,
          participant: activeParticipantNodeText.value || undefined,
          form: formRef,
        })
      );
    }
  }
  if (!zoomParticipant.value && form.enrollment_id) {
    void resolveEnrollmentLineage(`enrollment:${form.enrollment_id}`);
  }
}

function onFormClosed() {
  formWorkspaceDirty.value = false;
  loadedForm.value = null;
  router.push(formBackLocation.value);
}

async function onObservationLoaded(obs: KernelObservation) {
  loadedObservation.value = obs;
  if (!zoomParticipant.value && obs.enrollment_ref) {
    const lineage = await resolveEnrollmentLineage(obs.enrollment_ref);
    if (lineage?.subjectCode && !zoomParticipant.value) {
      const participant = await canvas.resolveParticipantByCode(
        lineage.subjectCode
      );
      if (lineage.req === lineageRequest && participant) {
        resolvedParticipantId.value = participant.id;
      }
    }
  }
}

function selectVersion(version: KernelProtocolVersion) {
  if (!version.ref) return;
  inspectorRef.value?.expand();
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

function applyStudyQueryFilter(filter: StudyQueriesFilterState) {
  router.replace(
    studyLocation({
      stage: "data",
      section: "queries",
      status: filter.status,
      site: filter.site,
      subject: filter.participant,
      changed: filter.changed,
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
  if (target.ask) {
    inspectorRef.value?.expand();
    // Ask keeps the view and its selection, and opens Assist with the question.
    router.push({
      name: "study",
      params: { studyCode: target.studyCode },
      query: { ...route.query, ask: target.ask },
    });
    return;
  }
  if (target.participant) {
    inspectorRef.value?.expand();
  }
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
    loadedForm.value = null;
    loadedObservation.value = null;
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
    canvas.openStudy(code);
  },
  { immediate: true }
);

watch(
  [
    studyCode,
    () => recordRef.value?.id,
    () => selectedNode.value?.id,
    () => loadedForm.value?.participant_id,
  ],
  ([code, recordId, selectedId, formParticipantId]) => {
    if (!code) return;
    const ids = new Set(
      [recordId, selectedId, formParticipantId].filter((id): id is string =>
        Boolean(id)
      )
    );
    for (const id of ids) {
      if (!canvas.knownParticipants[id]) {
        void canvas.ensureParticipantById(id);
      }
    }
  },
  { immediate: true }
);

watch([paletteOpen, studyCode], ([open, code]) => {
  if (!inAppShell && open && code) {
    void canvas.ensurePaletteParticipants(code);
  }
});

watch(openFormId, (id) => {
  if (!id) {
    loadedForm.value = null;
  } else if (loadedForm.value && loadedForm.value.form_id !== id) {
    loadedForm.value = null;
  }
});

watch(openObservationRef, (refText) => {
  resolvedParticipantId.value = "";
  lineageRequest++;
  if (!refText) {
    loadedObservation.value = null;
  }
});

watch([hasOpenForm, openObservationRef], ([formOpen, obsRef]) => {
  if (!formOpen && !obsRef) {
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
  }
});

// Open a participant's data once the canvas knows which participant it is.
watch(
  [studyCode, () => recordParticipant.value?.subject_id],
  ([, code]) => {
    const participant = recordParticipant.value;
    if (participant && code && canvas.record.subjectCode !== code)
      canvas.loadRecord(participant);
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
    if (
      !studyCode.value ||
      recordRef.value ||
      hasOpenForm.value ||
      openObservationRef.value
    )
      return;
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

const UNSAVED_FORM_CONFIRM_MESSAGE =
  "You have unsaved eCRF draft changes. Are you sure you want to leave?";

function leavesActiveForm(to: RouteLocationNormalized): boolean {
  const nextStudyCode = single(to.params.studyCode).trim();
  if (!nextStudyCode || nextStudyCode !== studyCode.value) return true;
  if (parseNodeRef(to.query.proposal)?.kind === "proposal") return true;
  if (observationNodeRef(to.query.observation)) return true;

  const nextFormQuery = single(to.query.form).trim();
  if (nextFormQuery === "new") return true;
  const nextFormId = collectionInstanceId(nextFormQuery);
  if (!nextFormId) return true;

  const currentFormId = loadedForm.value?.form_id || openFormId.value;
  return Boolean(currentFormId && nextFormId !== currentFormId);
}

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (formWorkspaceDirty.value) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

function confirmDiscardUnsavedForm(): boolean {
  // eslint-disable-next-line no-alert -- Vue Router navigation guards require synchronous window.confirm.
  return window.confirm(UNSAVED_FORM_CONFIRM_MESSAGE);
}

if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave(() => {
    if (formWorkspaceDirty.value && !confirmDiscardUnsavedForm()) {
      return false;
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to) => {
    if (
      formWorkspaceDirty.value &&
      leavesActiveForm(to) &&
      !confirmDiscardUnsavedForm()
    ) {
      return false;
    }
  });
}

function onGlobalKeydown(event: KeyboardEvent) {
  if (inAppShell) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    paletteOpen.value = !paletteOpen.value;
  }
}

onMounted(() => {
  window.addEventListener("beforeunload", handleBeforeUnload);
  if (!inAppShell) {
    document.addEventListener("keydown", onGlobalKeydown);
  }
  syncTopbarHeight();
  if (
    typeof ResizeObserver !== "undefined" &&
    topbarRef.value instanceof HTMLElement
  ) {
    topbarResizeObserver = new ResizeObserver(() => syncTopbarHeight());
    topbarResizeObserver.observe(topbarRef.value);
  }
});
onUnmounted(() => {
  window.removeEventListener("beforeunload", handleBeforeUnload);
  document.removeEventListener("keydown", onGlobalKeydown);
  if (topbarResizeObserver) {
    topbarResizeObserver.disconnect();
    topbarResizeObserver = null;
  }
});
</script>

<style scoped>
.canvas {
  --canvas-sticky-offset: 0px;
  --canvas-pane-top: calc(
    var(--canvas-sticky-offset) + var(--canvas-topbar-height, 45px)
  );
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted, #f8fafc);
  color: var(--primary);
  font-family: var(--font);
}

.canvas.canvas-in-shell {
  --canvas-sticky-offset: var(--shell-header-height, 57px);
  min-height: calc(100vh - var(--canvas-sticky-offset));
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
  position: sticky;
  top: var(--canvas-sticky-offset);
  z-index: 20;
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

.ai-toggle {
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.ai-toggle[aria-pressed="true"] {
  border-color: var(--semantic-color-ai-border, #c4b5fd);
  background: var(--semantic-color-ai-bg, #f5f3ff);
  color: var(--semantic-color-ai, #6d28d9);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
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

.panes.panes--inspector-collapsed {
  grid-template-columns: 14rem minmax(0, 1fr) 2.75rem;
}

.navigator {
  position: sticky;
  top: var(--canvas-pane-top);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
  padding: 12px;
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
  border-left: 1px solid var(--border);
  border-right: 1px solid var(--border);
}

.canvas :deep(#canvas-inspector) {
  position: sticky;
  top: var(--canvas-pane-top);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
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
  .panes,
  .panes.panes--inspector-collapsed {
    grid-template-columns: minmax(0, 1fr);
  }

  .navigator {
    position: static;
    max-height: none;
    overflow-y: visible;
    align-self: stretch;
    border-bottom: 1px solid var(--border);
  }

  .workspace {
    border-left: 0;
    border-right: 0;
  }

  .canvas :deep(#canvas-inspector) {
    position: static;
    max-height: none;
    overflow-y: visible;
    align-self: stretch;
    border-top: 1px solid var(--border);
  }
}
</style>
