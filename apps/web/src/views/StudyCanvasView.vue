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
          @click="requestCanvasSignOut"
        >
          Sign out
        </button>
      </div>
    </component>

    <template v-if="studyCode">
      <div class="study-header">
        <h1 id="canvas-study-title" tabindex="-1">
          {{ studyCode }}
        </h1>
        <nav aria-label="Lifecycle">
          <ol class="lifecycle-rail">
            <li v-for="item in STAGES" :key="item.id">
              <router-link
                :id="`stage-${item.id}`"
                :to="scopedStudyLocation({ stage: item.id })"
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
        :class="{
          'panes--navigator-collapsed': navigatorCollapsed,
          'panes--inspector-collapsed': inspectorCollapsed,
        }"
      >
        <nav
          id="canvas-navigator"
          class="navigator"
          :class="{ 'navigator--collapsed': navigatorCollapsed }"
          :data-collapsed="navigatorCollapsed ? 'true' : 'false'"
          aria-label="Sections"
        >
          <div class="navigator-rail">
            <button
              id="btn-toggle-navigator"
              type="button"
              class="navigator-toggle"
              :aria-expanded="!navigatorCollapsed"
              aria-controls="canvas-navigator-body"
              :aria-label="
                navigatorCollapsed ? 'Expand navigator' : 'Collapse navigator'
              "
              :title="
                navigatorCollapsed ? 'Expand navigator' : 'Collapse navigator'
              "
              @click="toggleNavigator"
            >
              <span aria-hidden="true">{{
                navigatorCollapsed ? "▸" : "◂"
              }}</span>
            </button>
          </div>
          <div
            v-if="!navigatorCollapsed"
            id="canvas-navigator-body"
            class="navigator-body"
          >
            <router-link
              id="nav-study-home"
              :to="scopedStudyLocation()"
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
                    :to="
                      scopedStudyLocation({ stage: group.id, section: item.id })
                    "
                    class="nav-link"
                    :class="{ current: section?.id === item.id }"
                    :aria-current="section?.id === item.id ? 'page' : undefined"
                  >
                    {{ item.label }}
                  </router-link>
                </li>
              </ul>
              <p v-else class="nav-empty">
                No sections are available in this stage yet.
              </p>
            </div>
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
          <component
            :is="activeWorkspaceRender.component"
            v-if="activeWorkspaceRender"
            :key="activeWorkspaceRender.key"
            v-bind="activeWorkspaceRender.props"
            v-on="activeWorkspaceRender.listeners ?? {}"
          />
        </component>

        <CanvasInspector
          ref="inspectorRef"
          :study-code="studyCode"
          :participant="inspectedParticipant"
          :node-ref="inspectedNodeText"
          :missing="isSelectedParticipantMissing"
          :protocol-version="inspectedVersion"
          :protocol-version-error="inspectedVersionError"
          :generic-node-ref="selectedGenericNodeRef"
          :generic-node="inspectedGenericNode"
          :generic-node-loading="inspectedGenericLoading"
          :record-location="
            inspectorContextParticipant &&
            (!isParticipantRecordOpen ||
              inspectorContextParticipant !== recordParticipant ||
              hasOpenForm ||
              Boolean(openObservationRef))
              ? recordLocationFor(inspectorContextParticipant)
              : null
          "
          :data-entry-location="
            inspectorContextParticipant &&
            !(isNewForm && inspectorContextParticipant === recordParticipant)
              ? dataEntryLocationFor(inspectorContextParticipant)
              : null
          "
          :handoff-location="
            isDemoBuild() && inspectorContextParticipant
              ? ecrfHandoffLocationFor(inspectorContextParticipant)
              : null
          "
          :can-manage-participants="canChangeParticipants"
          :can-unblind-participants="canUnblindParticipants"
          :assist-prompt="askText"
          @update:collapsed="inspectorCollapsed = $event"
          @retry-protocol-version="retryProtocolVersionLoad"
        />
      </div>
    </template>

    <OpenStudyWorkspace
      v-else
      :in-app-shell="inAppShell"
      :active-stage-id="stage?.id"
      :active-section-id="section?.id"
      @open-study="onOpenStudy"
    />

    <CanvasPalette
      v-if="!inAppShell && paletteOpen"
      :study-code="studyCode"
      :participants="canvas.paletteParticipants"
      @close="paletteOpen = false"
      @go="goToPaletteTarget"
    />

    <SignOutConfirmModal
      v-if="!inAppShell"
      id="canvas-signout-confirm-modal"
      v-model="isCanvasSignOutConfirmOpen"
      @confirm="confirmCanvasSignOut"
      @cancel="cancelCanvasSignOut"
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
import { refreshParticipantDetailMutation } from "../stores/studyCanvasParticipantMutations";
import {
  kernelApi,
  type KernelNode,
  type KernelObservation,
  type KernelProtocolVersion,
} from "../api/kernelApi";
import { resolveNode, seedNodeSummary } from "../canvas/nodeResolver";
import { STAGES } from "../canvas/lifecycle";
import {
  collectionInstanceId,
  collectionInstanceRef,
  observationNodeRef,
  parseNodeRef,
} from "../canvas/nodeRef";
import {
  canAnswerQueries,
  canManageParticipants,
  canManageQueries,
  canManageRulePacks,
  canReadEtmf,
  canUnblindParticipant,
  canVerifySource,
} from "../canvas/authority";
import {
  syncInstantiatedClinicalScope,
  type PaletteTarget,
} from "../canvas/palette";
import type {
  CanvasSectionContext,
  StudyQueriesFilterState,
} from "../canvas/sections/types";
import {
  formatFormLabel,
  singleQueryString,
  useCanvasRouteState,
} from "../canvas/useCanvasRouteState";
import { resolveActiveWorkspace } from "../canvas/workspaces/registry";
import type { CanvasWorkspaceContext } from "../canvas/workspaces/types";
import {
  SignOutConfirmModal,
  handoffFocus,
  hasActiveModalFocusTrap,
  useSignOutGuard,
} from "ui";
import { confirmDialog } from "../composables/useConfirmDialog.js";
import { useAuthStore } from "../stores/auth";
import { useKnowledgeStore } from "../stores/knowledge.js";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { participantNodeRef, useStudyCanvasStore } from "../stores/studyCanvas";
import { hasUserScopedDrafts } from "../utils/userScopedStorage.js";
import CanvasInspector from "../components/canvas/CanvasInspector.vue";
import CanvasPalette from "../components/canvas/CanvasPalette.vue";
import OpenStudyWorkspace from "../components/canvas/OpenStudyWorkspace.vue";
import { useAiAssistance } from "../ai/aiAssistance";
import { isDemoBuild } from "../utils/demoMode";

/**
 * The Clinical Canvas (docs/ux/CLINICAL_CANVAS.md): one study at a time, navigated by lifecycle stage, in
 * three panes (navigator, workspace, inspector), with a ⌘K palette. Everything it shows is read from a
 * service; what no service serves yet is absent rather than mocked (§6.1).
 *
 * Route query state and lineage resolution live in `useCanvasRouteState.ts`; lifecycle stages, sections,
 * workspaces, and inspector panes resolve through file-colocated `import.meta.glob` descriptors.
 */
const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const canvas = useStudyCanvasStore();
const { setActiveStudyCode } = useActiveStudyScope();
const inAppShell = inject<boolean>("inAppShell", false);

const { enabled: aiEnabled, setEnabled: setAiEnabled } = useAiAssistance();

const paletteOpen = ref(false);
const inspectorCollapsed = ref(true);
const inspectorRef = ref<InstanceType<typeof CanvasInspector> | null>(null);
const MEDIUM_CANVAS_MEDIA_QUERY = "(min-width: 961px) and (max-width: 1280px)";
const isMediumCanvasViewport = ref(false);
const manualNavigatorCollapsed = ref<boolean | null>(null);
let mediumCanvasMediaList: MediaQueryList | null = null;
const topbarRef = ref<HTMLElement | null>(null);
const topbarHeight = ref(45);
const formWorkspaceDirty = ref(false);
let topbarResizeObserver: ResizeObserver | null = null;
let conductSubjectSeq = 0;

function syncMediumCanvasViewport() {
  if (typeof window === "undefined") {
    isMediumCanvasViewport.value = false;
    return;
  }
  if (typeof window.matchMedia === "function") {
    const mql =
      mediumCanvasMediaList ?? window.matchMedia(MEDIUM_CANVAS_MEDIA_QUERY);
    isMediumCanvasViewport.value = Boolean(mql?.matches);
    return;
  }
  isMediumCanvasViewport.value =
    window.innerWidth >= 961 && window.innerWidth <= 1280;
}

function toggleNavigator() {
  manualNavigatorCollapsed.value = !navigatorCollapsed.value;
}

async function focusAndScrollInspector(options?: {
  scrollIntoView?: boolean;
}): Promise<void> {
  inspectorRef.value?.expand();
  await nextTick();
  syncTopbarHeight();
  await nextTick();
  if (options?.scrollIntoView === false || typeof document === "undefined") {
    return;
  }
  const inspectorEl = document.getElementById("canvas-inspector");
  if (!inspectorEl) return;
  const computedPos =
    typeof window !== "undefined" &&
    typeof window.getComputedStyle === "function"
      ? window.getComputedStyle(inspectorEl).position
      : "";
  const isDesktopSticky =
    typeof window !== "undefined" &&
    window.innerWidth > 960 &&
    computedPos === "sticky";
  if (isDesktopSticky) {
    return;
  }
  inspectorEl.scrollIntoView?.({ block: "start", behavior: "auto" });
}

function syncTopbarHeight() {
  const el = topbarRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0) {
    topbarHeight.value = measured;
    const canvasRoot = el.closest?.(".canvas") as HTMLElement | null;
    canvasRoot?.style?.setProperty("--canvas-topbar-height", `${measured}px`);
  }
}

const {
  activeEnrollmentId,
  activeFormParticipant,
  activeInspectorParticipantNodeParam,
  activeParticipantNodeText,
  activeParticipantScopeParam,
  askText,
  codeQueryParticipant,
  conductSubjectQuery,
  dataEntryLocationFor,
  ecrfHandoffLocationFor,
  formBackLabel,
  formBackLocation,
  formLocationById,
  formQuery,
  hasOpenForm,
  highlightItemQuery,
  initialSpecId,
  initialVisitId,
  inspectedNodeText,
  inspectedParticipant,
  inspectorContextParticipant,
  invalidateLineage,
  isCurrentLineageRequest,
  isNewForm,
  isParticipantRecordOpen,
  isRecordParticipantLoading,
  isResolvingConductSubject,
  isSelectedParticipantLoading,
  loadedForm,
  loadedObservation,
  newFormCellLocationFor,
  newFormLocationFor,
  observationLocationFromForm,
  observationLocationFromRecord,
  observationReturnLabel,
  observationReturnLocation,
  openFormId,
  openObservationRef,
  participantCodeQuery,
  participantSearchFilter,
  proposalRef,
  queryChangedFilter,
  queryOriginFilter,
  queryOwnerFilter,
  queryParticipantFilter,
  queryPriorityFilter,
  queryStatusFilter,
  rawParticipantQuery,
  recordLocationFor,
  recordParticipant,
  recordRef,
  resetLoadedRouteContext,
  resolveEnrollmentLineage,
  resolvedLineage,
  resolvedParticipantId,
  scopedStudyLocation,
  section,
  sectionDescriptor,
  selectedGenericNodeRef,
  selectedNode,
  selectedParticipant,
  selectedVersionText,
  siteFilter,
  stage,
  statusFilter,
  studyCode,
  studyLocation,
  zoomCurrent,
  zoomFormLabel,
  zoomFormLocation,
  zoomObservationLabel,
  zoomParticipant,
  zoomParticipantCode,
  zoomParticipantLocation,
  zoomSite,
} = useCanvasRouteState({ route, canvas });

// The protocol version in the inspector, read from the kernel whenever the URL names one.
const inspectedVersion = ref<KernelProtocolVersion | null>(null);
const inspectedVersionError = ref("");
let versionRequest = 0;

async function loadInspectedProtocolVersion(text: string) {
  const request = ++versionRequest;
  inspectedVersion.value = null;
  inspectedVersionError.value = "";
  if (!text) return;
  try {
    const version = await kernelApi.getProtocolVersion(text);
    if (request === versionRequest) {
      inspectedVersion.value = version;
      const versionNumber =
        version.number !== null && version.number !== undefined
          ? String(version.number)
          : text;
      if (version.ref) {
        seedNodeSummary(version.ref, {
          kind: "protocol_version",
          code: versionNumber,
          title: `Protocol v${versionNumber}`,
          status: version.status,
        });
      }
    }
  } catch (e) {
    if (request === versionRequest) {
      inspectedVersionError.value = e instanceof Error ? e.message : String(e);
    }
  }
}

function retryProtocolVersionLoad() {
  if (selectedVersionText.value) {
    void loadInspectedProtocolVersion(selectedVersionText.value);
  }
}

watch(
  selectedVersionText,
  (text) => {
    void loadInspectedProtocolVersion(text);
  },
  { immediate: true }
);

const inspectedGenericNode = ref<KernelNode | null>(null);
const inspectedGenericLoading = ref(false);
let genericNodeRequest = 0;
watch(inspectedNodeText, (next, prev) => {
  if (next && next !== prev) {
    inspectorRef.value?.expand();
  }
});
watch(
  selectedGenericNodeRef,
  async (text) => {
    const request = ++genericNodeRequest;
    inspectedGenericNode.value = null;
    inspectedGenericLoading.value = false;
    if (!text) return;
    inspectedGenericLoading.value = true;
    const resolved = await resolveNode(text);
    if (request !== genericNodeRequest) return;
    inspectedGenericLoading.value = false;
    inspectedGenericNode.value = resolved;
  },
  { immediate: true }
);
const isSelectedParticipantMissing = computed(
  () =>
    Boolean(selectedNode.value) &&
    !selectedParticipant.value &&
    !isSelectedParticipantLoading.value &&
    !inspectedGenericLoading.value &&
    !inspectedGenericNode.value
);

const canManage = computed(() => canManageQueries(authStore));
const canAnswer = computed(() => canAnswerQueries(authStore));
const canManageRules = computed(() => canManageRulePacks(authStore));
const canVerify = computed(() => canVerifySource(authStore));
const canChangeParticipants = computed(() => canManageParticipants(authStore));
const canUnblindParticipants = computed(() => canUnblindParticipant(authStore));
const callerAuthKey = computed(() =>
  [
    authStore.isAuthenticated ? "1" : "0",
    authStore.user?.id ?? "",
    authStore.user?.username ?? "",
    ...(authStore.rawRoles ?? []),
  ].join("|")
);
const canReadDocuments = computed(() =>
  !authStore.isAuthenticated && (authStore.rawRoles?.length ?? 0) === 0
    ? true
    : canReadEtmf(authStore)
);

const navigatorStages = computed(() =>
  stage.value ? [stage.value] : STAGES.filter((s) => s.sections.length)
);

function onOpenStudy(payload: {
  studyCode: string;
  stage?: string;
  section?: string;
  node?: string;
  replace?: boolean;
}) {
  const location = studyLocation(
    {
      stage: payload.stage,
      section: payload.section,
      node: payload.node,
    },
    payload.studyCode
  );
  if (payload.replace) {
    void router.replace(location);
  } else {
    void router.push(location);
  }
}

async function selectParticipant(participant: SubjectSummary) {
  const nodeText = participantNodeRef(participant);
  if (!nodeText) return;
  await router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      status: statusFilter.value,
      site: siteFilter.value,
      q: participantSearchFilter.value,
      node: nodeText,
    })
  );
  await canvas.requestInspectorFocus({
    nodeRef: nodeText,
    scrollIntoView: true,
  });
}

function onFormLoaded(form: EcrfForm) {
  loadedForm.value = form;
  const loadedRef = collectionInstanceRef(form.form_id);
  if (loadedRef) {
    seedNodeSummary(loadedRef, {
      kind: "collection_instance",
      title: formatFormLabel(form),
      status: form.complete ? "Complete" : "In progress",
    });
  }
  for (const item of form.items ?? []) {
    const obsRef = item.observation_id
      ? observationNodeRef(item.observation_id)
      : null;
    if (obsRef && item.concept) {
      seedNodeSummary(obsRef, {
        kind: "observation",
        code: item.concept,
        title: item.concept,
      });
    }
  }
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

const {
  isConfirmOpen: isCanvasSignOutConfirmOpen,
  requestSignOut: requestCanvasSignOut,
  confirmSignOut: confirmCanvasSignOut,
  cancelSignOut: cancelCanvasSignOut,
} = useSignOutGuard({
  hasExternalDrafts: () => hasUserScopedDrafts(),
  onConfirmSignOut: async () => {
    await authStore.logout();
    if (
      router &&
      typeof router.replace === "function" &&
      route?.path !== "/login" &&
      route?.name !== "login"
    ) {
      await router.replace("/login");
    }
  },
});

function onFormClosed() {
  formWorkspaceDirty.value = false;
  loadedForm.value = null;
  router.push(formBackLocation.value);
}

async function onObservationLoaded(obs: KernelObservation) {
  loadedObservation.value = obs;
  const targetObsRef = obs.ref || openObservationRef.value;
  if (targetObsRef) {
    seedNodeSummary(targetObsRef, {
      kind: "observation",
      code: obs.concept,
      title: obs.concept,
      status: obs.locks?.length ? "Locked" : "Recorded",
    });
  }
  if (!zoomParticipant.value && obs.enrollment_ref) {
    const lineage = await resolveEnrollmentLineage(obs.enrollment_ref);
    if (
      isDemoBuild() &&
      siteFilter.value &&
      lineage?.siteCode &&
      lineage.siteCode !== siteFilter.value
    ) {
      resolvedLineage.value = null;
      return;
    }
    if (lineage?.subjectCode && !zoomParticipant.value) {
      const participant = await canvas.resolveParticipantByCode(
        lineage.subjectCode
      );
      if (isCurrentLineageRequest(lineage.req) && participant) {
        resolvedParticipantId.value = participant.id;
      }
    }
  }
}

async function selectVersion(version: KernelProtocolVersion) {
  if (!version.ref) return;
  await router.push(
    studyLocation({
      stage: "design",
      section: "protocol-versions",
      node: version.ref,
    })
  );
  await canvas.requestInspectorFocus({
    nodeRef: version.ref,
    scrollIntoView: true,
  });
}

function applyParticipantFilter(filter: {
  status: string;
  siteId: string;
  q?: string;
}) {
  const nextSearch = (filter.q ?? "").trim();
  const location = studyLocation({
    stage: "conduct",
    section: "participants",
    status: filter.status,
    site: filter.siteId,
    q: nextSearch,
    node: singleQueryString(route.query.node),
  });
  const onlySearchChanged =
    filter.status === statusFilter.value &&
    filter.siteId === siteFilter.value &&
    nextSearch !== participantSearchFilter.value;
  if (onlySearchChanged) {
    router.replace(location);
  } else {
    router.push(location);
  }
}

function applyStudyQueryFilter(filter: StudyQueriesFilterState) {
  router.replace(
    studyLocation({
      stage: "data",
      section: "queries",
      status: filter.status,
      owner: filter.owner,
      origin: filter.origin,
      priority: filter.priority,
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

function openParticipantsForSite(siteCode: string) {
  router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      site: siteCode,
    })
  );
}

function openActivationForSite(siteCode: string) {
  router.push(
    studyLocation({
      stage: "start",
      section: "site-activation",
      site: siteCode || undefined,
    })
  );
}

async function goToPaletteTarget(target: PaletteTarget) {
  paletteOpen.value = false;
  const targetStudy = (target.studyCode || studyCode.value).trim();
  const isCrossStudy = Boolean(
    studyCode.value && targetStudy && targetStudy !== studyCode.value
  );
  if (isCrossStudy) {
    syncInstantiatedClinicalScope({
      activeStudyId: targetStudy,
      activeSiteId: "",
      activeSubjectId: "",
    });
  }
  if (target.ask) {
    // Ask keeps the view and its selection within the same study, and opens Assist with the question.
    await router.push({
      name: "study",
      params: { studyCode: targetStudy },
      query: isCrossStudy
        ? { ask: target.ask }
        : { ...route.query, ask: target.ask },
    });
    await canvas.requestInspectorFocus({
      nodeRef: null,
      scrollIntoView: true,
    });
    return;
  }
  if (target.participant) {
    if (targetStudy && targetStudy !== canvas.studyCode) {
      canvas.openStudy(targetStudy);
    }
    canvas.rememberParticipants([target.participant]);
    const nodeText = participantNodeRef(target.participant);
    const activeSite = siteFilter.value || zoomSite.value;
    const compatibleSite =
      !isCrossStudy &&
      activeSite &&
      (!target.participant.site_id || target.participant.site_id === activeSite)
        ? activeSite
        : undefined;
    await router.push(
      studyLocation(
        {
          stage: target.stage,
          section: target.section,
          site: compatibleSite,
          node: nodeText,
        },
        targetStudy
      )
    );
    await canvas.requestInspectorFocus({
      nodeRef: nodeText,
      scrollIntoView: true,
    });
    return;
  }
  if (isCrossStudy) {
    await router.push(
      studyLocation(
        {
          stage: target.stage,
          section: target.section,
        },
        targetStudy
      )
    );
    return;
  }
  await router.push(
    scopedStudyLocation(
      {
        stage: target.stage,
        section: target.section,
      },
      targetStudy
    )
  );
}

const sectionContext = computed<CanvasSectionContext>(() => ({
  studyCode: studyCode.value,
  siteFilter: siteFilter.value,
  activeSite: siteFilter.value || zoomSite.value,
  activeParticipantScopeParam: activeParticipantScopeParam.value,
  activeInspectorParticipantNodeParam:
    activeInspectorParticipantNodeParam.value,
  statusFilter: statusFilter.value,
  participantSearchFilter: participantSearchFilter.value,
  selectedParticipantOrRecordId:
    selectedParticipant.value?.id ??
    recordParticipant.value?.id ??
    (selectedNode.value?.kind === "participant"
      ? selectedNode.value.id
      : recordRef.value?.kind === "participant"
        ? recordRef.value.id
        : ""),
  selectedVersionText: selectedVersionText.value,
  canChangeParticipants: canChangeParticipants.value,
  canManageSites: Boolean(authStore.canManageWorkspaceSites),
  canReadDocuments: canReadDocuments.value,
  canManage: canManage.value,
  canAnswer: canAnswer.value,
  canManageRules: canManageRules.value,
  queryStatusFilter: queryStatusFilter.value,
  queryOwnerFilter: queryOwnerFilter.value,
  queryOriginFilter: queryOriginFilter.value,
  queryPriorityFilter: queryPriorityFilter.value,
  queryParticipantFilter: queryParticipantFilter.value,
  queryChangedFilter: queryChangedFilter.value,
  canvas,
  onSelectParticipant: selectParticipant,
  onApplyParticipantFilter: applyParticipantFilter,
  onSelectVersion: selectVersion,
  onOpenParticipantsForSite: openParticipantsForSite,
  onOpenActivationForSite: openActivationForSite,
  onApplyStudyQueryFilter: applyStudyQueryFilter,
}));

const workspaceContext = computed<CanvasWorkspaceContext>(() => ({
  studyCode: studyCode.value,
  reviewer: authStore.userId ?? "",
  stage: stage.value,
  section: section.value,
  sectionDescriptor: sectionDescriptor.value,
  sectionContext: sectionContext.value,
  proposalRef: proposalRef.value,
  openObservationRef: openObservationRef.value,
  zoomObservationLabel: zoomObservationLabel.value,
  observationReturnLocation: observationReturnLocation.value,
  observationReturnLabel: observationReturnLabel.value,
  hasOpenForm: hasOpenForm.value,
  isNewForm: isNewForm.value,
  openFormId: openFormId.value,
  loadedFormLabel:
    loadedForm.value && loadedForm.value.form_id === openFormId.value
      ? formatFormLabel(loadedForm.value)
      : undefined,
  zoomFormLabel: zoomFormLabel.value,
  highlightItemQuery: highlightItemQuery.value,
  initialSpecId: initialSpecId.value,
  initialVisitId: initialVisitId.value,
  recordRefId: recordRef.value?.id ?? "",
  participantCodeQuery: participantCodeQuery.value,
  isParticipantRecordOpen: isParticipantRecordOpen.value,
  recordParticipant: recordParticipant.value,
  isRecordParticipantLoading: isRecordParticipantLoading.value,
  activeFormParticipant: activeFormParticipant.value,
  activeEnrollmentId: activeEnrollmentId.value,
  isResolvingConductSubject: isResolvingConductSubject.value,
  canManage: canManage.value,
  canAnswer: canAnswer.value,
  canVerify: canVerify.value,
  canManageSites: Boolean(authStore.canManageWorkspaceSites),
  isDemo: isDemoBuild(),
  canvas,
  participantsLocation: studyLocation({
    stage: "conduct",
    section: "participants",
  }),
  formBackLocation: formBackLocation.value,
  formBackLabel: formBackLabel.value,
  formLocationById,
  newFormLocationFor,
  newFormCellLocationFor,
  ecrfHandoffLocationFor,
  observationLocationFromRecord,
  observationLocationFromForm,
  sectionLocationForStage: (stageId: string, sectionId: string) =>
    scopedStudyLocation({ stage: stageId, section: sectionId }),
  onObservationLoaded,
  onFormDirtyChange: (dirty: boolean) => {
    formWorkspaceDirty.value = dirty;
  },
  onFormLoaded,
  onFormClosed,
  onRetryParticipantRecord: () => {
    if (recordParticipant.value) {
      canvas.loadRecord(recordParticipant.value);
    }
  },
  onDrillToParticipants: drillToParticipants,
  onOpenParticipantsForSite: openParticipantsForSite,
}));

const activeWorkspaceDescriptor = computed(() =>
  studyCode.value ? resolveActiveWorkspace(workspaceContext.value) : undefined
);

const activeWorkspaceRender = computed(() =>
  activeWorkspaceDescriptor.value?.resolveRender(workspaceContext.value)
);

const workspaceLabel = computed(() =>
  activeWorkspaceDescriptor.value
    ? activeWorkspaceDescriptor.value.resolveLabel(workspaceContext.value)
    : `${studyCode.value} home`
);

// Switching study drops everything read for the previous one.
watch(
  [
    studyCode,
    () => singleQueryString(route.query.studyId ?? route.query.study_id).trim(),
  ],
  ([code, queryStudyCode]) => {
    if (!code && queryStudyCode) {
      const nextQuery: LocationQueryRaw = { ...route.query };
      delete nextQuery.studyId;
      delete nextQuery.study_id;
      router.replace({
        name: "study",
        params: { studyCode: queryStudyCode },
        query: nextQuery,
      });
      return;
    }
    formWorkspaceDirty.value = false;
    resetLoadedRouteContext();
    canvas.openStudy(code);
    if (code) {
      setActiveStudyCode(code);
    }
  },
  { immediate: true }
);

watch(callerAuthKey, (next, prev) => {
  if (prev !== undefined && next !== prev) {
    canvas.clearParticipantAllocationCache();
  }
});

watch(
  [
    studyCode,
    () =>
      recordRef.value ? `${recordRef.value.kind}:${recordRef.value.id}` : "",
    () =>
      selectedNode.value
        ? `${selectedNode.value.kind}:${selectedNode.value.id}`
        : "",
    () => loadedForm.value?.participant_id,
    () => codeQueryParticipant.value?.id,
    callerAuthKey,
  ],
  ([code, , , formParticipantId, codeParticipantId]) => {
    if (!code) return;
    const postRandomizationStates = [
      "RANDOMIZED",
      "ACTIVE",
      "UNBLINDED",
      "RECONSENT_REQUIRED",
      "COMPLETED",
      "WITHDRAWN",
    ];
    const hydratedIds = new Set<string>();
    for (const node of [recordRef.value, selectedNode.value]) {
      if (!node) continue;
      const known = canvas.findKnownParticipant(node);
      if (!known) {
        if (node.kind === "enrollment") {
          void canvas.ensureParticipantByEnrollmentId(node.id);
        } else {
          void canvas.ensureParticipantById(node.id);
        }
      } else if (!hydratedIds.has(known.id)) {
        hydratedIds.add(known.id);
        if (
          known.treatment_group === undefined &&
          postRandomizationStates.includes(known.status)
        ) {
          void refreshParticipantDetailMutation(canvas, known.id);
        }
      }
    }
    const directIds = new Set(
      [formParticipantId, codeParticipantId].filter((id): id is string =>
        Boolean(id)
      )
    );
    for (const id of directIds) {
      if (hydratedIds.has(id)) continue;
      hydratedIds.add(id);
      const known = canvas.knownParticipants[id];
      if (!known) {
        void canvas.ensureParticipantById(id);
      } else if (
        known.treatment_group === undefined &&
        postRandomizationStates.includes(known.status)
      ) {
        void refreshParticipantDetailMutation(canvas, id);
      }
    }
  },
  { immediate: true }
);

watch(
  [studyCode, conductSubjectQuery],
  async ([code, subjectCode]) => {
    const seq = ++conductSubjectSeq;
    if (!code || !subjectCode) {
      isResolvingConductSubject.value = false;
      return;
    }
    isResolvingConductSubject.value = true;
    try {
      const resolved = await canvas.resolveParticipantByCode(subjectCode, code);
      if (seq !== conductSubjectSeq) return;
      const nodeText = resolved ? participantNodeRef(resolved) : "";
      if (nodeText) {
        await router.replace(
          studyLocation(
            {
              stage: "conduct",
              site: siteFilter.value || undefined,
              participant: nodeText,
              form: formQuery.value || undefined,
              spec: initialSpecId.value || undefined,
              visit: initialVisitId.value || undefined,
              item: highlightItemQuery.value || undefined,
            },
            code
          )
        );
      } else {
        await router.replace(
          studyLocation(
            {
              stage: "conduct",
              section: "participants",
              site: siteFilter.value || undefined,
              q: subjectCode,
            },
            code
          )
        );
      }
    } finally {
      if (seq === conductSubjectSeq) {
        isResolvingConductSubject.value = false;
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
  invalidateLineage();
  if (!refText) {
    loadedObservation.value = null;
  }
});

const autoCollapseNavigator = computed(
  () =>
    isMediumCanvasViewport.value &&
    (hasOpenForm.value || Boolean(openObservationRef.value))
);
const navigatorCollapsed = computed(
  () => manualNavigatorCollapsed.value ?? autoCollapseNavigator.value
);

watch([studyCode, autoCollapseNavigator], () => {
  manualNavigatorCollapsed.value = null;
});

watch([hasOpenForm, openObservationRef], ([formOpen, obsRef]) => {
  if (!formOpen && !obsRef) {
    resolvedLineage.value = null;
    invalidateLineage();
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

watch(
  [studyCode, participantCodeQuery],
  ([code, subjectCode]) => {
    if (!code || !subjectCode || codeQueryParticipant.value) return;
    void canvas.resolveParticipantByCode(subjectCode, code);
  },
  { immediate: true }
);

watch(
  [studyCode, zoomSite, zoomParticipantCode],
  ([code, site, subject]) => {
    syncInstantiatedClinicalScope(
      code
        ? {
            activeStudyId: code,
            activeSiteId: site || "",
            activeSubjectId: subject || "",
          }
        : {
            activeStudyId: "",
            activeSiteId: "",
            activeSubjectId: "",
          }
    );
  },
  { immediate: true }
);

// Load what the current workspace descriptor shows, once per study and filter.
watch(
  [
    studyCode,
    () => activeWorkspaceDescriptor.value?.id,
    () => stage.value?.id,
    () => section.value?.id,
    statusFilter,
    siteFilter,
    participantSearchFilter,
  ],
  () => {
    if (!studyCode.value) return;
    activeWorkspaceDescriptor.value?.loadOnEnter?.(workspaceContext.value);
  },
  { immediate: true }
);

const UNSAVED_FORM_CONFIRM_MESSAGE =
  "You have unsaved eCRF draft changes. Are you sure you want to leave?";

function leavesActiveForm(to: RouteLocationNormalized): boolean {
  const nextStudyCode = singleQueryString(to.params.studyCode).trim();
  if (!nextStudyCode || nextStudyCode !== studyCode.value) return true;
  if (parseNodeRef(to.query.proposal)?.kind === "proposal") return true;
  if (observationNodeRef(to.query.observation)) return true;

  const nextFormQuery = singleQueryString(to.query.form).trim();
  if (nextFormQuery === "new") {
    if (!isNewForm.value) return true;
    const nextSpec = singleQueryString(to.query.spec).trim();
    const nextVisit = singleQueryString(to.query.visit).trim();
    const nextParticipant = singleQueryString(to.query.participant).trim();
    return (
      nextSpec !== initialSpecId.value ||
      nextVisit !== initialVisitId.value ||
      nextParticipant !== rawParticipantQuery.value
    );
  }
  const nextFormId = collectionInstanceId(nextFormQuery);
  if (!nextFormId) return true;

  const currentFormId = loadedForm.value?.form_id || openFormId.value;
  return Boolean(currentFormId && nextFormId !== currentFormId);
}

function shouldSuppressUnsavedGuard(to?: RouteLocationNormalized): boolean {
  if (
    !authStore.isAuthenticated ||
    authStore.sessionExpired ||
    authStore.isLoggingOut
  ) {
    return true;
  }
  if (
    to &&
    (to.name === "forbidden" ||
      to.path === "/forbidden" ||
      to.name === "login" ||
      to.path === "/login")
  ) {
    return true;
  }
  return false;
}

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (formWorkspaceDirty.value && !shouldSuppressUnsavedGuard()) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

async function confirmDiscardUnsavedForm(): Promise<boolean> {
  return Boolean(
    await confirmDialog({
      title: "Unsaved Form Changes",
      message: UNSAVED_FORM_CONFIRM_MESSAGE,
      confirmLabel: "Leave Without Saving",
      cancelLabel: "Stay on Form",
      variant: "warning",
    })
  );
}

if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave((to) => {
    if (formWorkspaceDirty.value && !shouldSuppressUnsavedGuard(to)) {
      return confirmDiscardUnsavedForm().then((confirmed) =>
        confirmed ? undefined : false
      );
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to) => {
    if (
      formWorkspaceDirty.value &&
      !shouldSuppressUnsavedGuard(to) &&
      leavesActiveForm(to)
    ) {
      return confirmDiscardUnsavedForm().then((confirmed) =>
        confirmed ? undefined : false
      );
    }
  });
}

function onGlobalKeydown(event: KeyboardEvent) {
  if (inAppShell) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "e") {
    canvas.handleExplainShortcut(event);
    return;
  }
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    if (hasActiveModalFocusTrap({ excludeId: "canvas-palette" })) {
      return;
    }
    paletteOpen.value = !paletteOpen.value;
  }
}

watch(
  () => ({
    studyCode: studyCode.value,
    stage: stage.value?.id || "",
    section: section.value?.id || "",
    siteCode: zoomSite.value || "",
    subjectCode: zoomParticipantCode.value || canvas.record.subjectCode || "",
    nodeRef: inspectedNodeText.value || "",
    participantNodeRef: activeParticipantNodeText.value || "",
    form: formQuery.value || "",
    formId: openFormId.value || "",
    formSpecId:
      loadedForm.value?.collection_spec_id || initialSpecId.value || "",
    isNewForm: isNewForm.value,
    observationRef: openObservationRef.value || "",
    proposalRef: proposalRef.value || "",
  }),
  (scopeSnapshot) => {
    knowledgeStore.setCanvasEscalationContext(scopeSnapshot);
  },
  { immediate: true }
);

interface WorkspaceBranchSnapshot {
  key: string;
  groupKey: string;
  depth: number;
  targets: Array<string | (() => HTMLElement | null)>;
  subjectCode: string;
  formId: string;
  isNewForm: boolean;
  specId: string;
  visitId: string;
  highlightItemId: string;
}

const workspaceBranch = computed<WorkspaceBranchSnapshot>(() => {
  const baseContext = {
    subjectCode: zoomParticipantCode.value,
    formId: openFormId.value || "",
    isNewForm: isNewForm.value,
    specId: initialSpecId.value,
    visitId: initialVisitId.value,
    highlightItemId: highlightItemQuery.value,
  };

  if (!studyCode.value || !activeWorkspaceDescriptor.value) {
    return {
      key: "open-study",
      groupKey: "open-study",
      depth: 0,
      targets: ["#canvas-study-title", "#open-study-code"],
      ...baseContext,
    };
  }

  return {
    ...activeWorkspaceDescriptor.value.resolveBranch(workspaceContext.value),
    ...baseContext,
  };
});

const savedBranchTriggers = new Map<string, string>();
let returnFocusObserver: MutationObserver | null = null;
let returnFocusTimer: ReturnType<typeof setTimeout> | null = null;

function stopReturnFocusObserver() {
  if (returnFocusObserver) {
    returnFocusObserver.disconnect();
    returnFocusObserver = null;
  }
  if (returnFocusTimer) {
    clearTimeout(returnFocusTimer);
    returnFocusTimer = null;
  }
}

function escapeId(id: string): string {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return `#${CSS.escape(id)}`;
  }
  return `#${id.replace(/([^\w-])/g, "\\$1")}`;
}

function captureElementTriggerSelector(el: Element | null): string | null {
  if (!el || typeof document === "undefined") return null;
  const workspace = document.getElementById("canvas-workspace");
  const interactive = el.closest?.("a, button, [role='button']") ?? el;
  if (!workspace || !workspace.contains(interactive)) return null;
  const rawId = interactive.getAttribute("id");
  if (rawId) {
    return escapeId(rawId);
  }
  const testId = interactive.getAttribute("data-testid");
  if (testId) {
    return `[data-testid="${testId.replace(/"/g, '\\"')}"]`;
  }
  return null;
}

function onWorkspaceInteractionCapture(event: Event) {
  if (
    event.type === "keydown" &&
    event instanceof KeyboardEvent &&
    event.key !== "Enter" &&
    event.key !== " "
  ) {
    return;
  }
  const target = event.target instanceof Element ? event.target : null;
  const selector = captureElementTriggerSelector(target);
  if (selector) {
    savedBranchTriggers.set(workspaceBranch.value.groupKey, selector);
  }
}

watch(
  workspaceBranch,
  (next, prev) => {
    if (!prev || next.key === prev.key) return;
    stopReturnFocusObserver();

    if (typeof document !== "undefined") {
      const activeSelector = captureElementTriggerSelector(
        document.activeElement
      );
      if (activeSelector) {
        savedBranchTriggers.set(prev.groupKey, activeSelector);
      }
    }

    // Opening a new form inside CanvasFormWorkspace replaces ?form=new with ?form=collection_instance:...
    if (
      prev.groupKey === next.groupKey &&
      prev.isNewForm &&
      Boolean(next.formId)
    ) {
      void handoffFocus([
        ...(next.highlightItemId ? [`#ecrf-item-${next.highlightItemId}`] : []),
        () =>
          document.querySelector<HTMLElement>(
            "#ecrf-form-page input:not([disabled]), #ecrf-form-page select:not([disabled])"
          ),
        "#canvas-form-workspace-title",
      ]);
      return;
    }

    const isZoomOut = next.depth < prev.depth;
    const returnSelectors: string[] = [];
    if (isZoomOut) {
      const saved = savedBranchTriggers.get(next.groupKey);
      if (saved) returnSelectors.push(saved);
      if (prev.formId) {
        returnSelectors.push(`#record-form-link-${prev.formId}`);
      }
      if (prev.specId && prev.visitId) {
        returnSelectors.push(`#matrix-open-${prev.specId}-${prev.visitId}`);
      } else if (prev.specId) {
        returnSelectors.push(`#unscheduled-open-${prev.specId}`);
      }
      if (prev.highlightItemId && prev.depth === 4) {
        returnSelectors.push(
          `#ecrf-item-observation-${prev.highlightItemId}`,
          `#ecrf-item-${prev.highlightItemId}`
        );
      }
      if (prev.subjectCode) {
        returnSelectors.push(`#participant-${prev.subjectCode}`);
      }
    }

    void (async () => {
      const focused = await handoffFocus([...returnSelectors, ...next.targets]);
      if (
        !isZoomOut ||
        returnSelectors.length === 0 ||
        !focused ||
        typeof MutationObserver === "undefined" ||
        typeof document === "undefined"
      ) {
        return;
      }
      const matchedReturnImmediately = returnSelectors.some(
        (sel) => document.querySelector(sel) === focused
      );
      if (matchedReturnImmediately) return;

      const workspace = document.getElementById("canvas-workspace");
      if (!workspace) return;

      returnFocusObserver = new MutationObserver(() => {
        if (document.activeElement !== focused) {
          stopReturnFocusObserver();
          return;
        }
        for (const sel of returnSelectors) {
          const candidate = document.querySelector<HTMLElement>(sel);
          if (candidate && !candidate.hasAttribute("disabled")) {
            stopReturnFocusObserver();
            void handoffFocus(candidate, { awaitNextTick: false });
            return;
          }
        }
      });
      returnFocusObserver.observe(workspace, {
        childList: true,
        subtree: true,
      });
      returnFocusTimer = setTimeout(() => stopReturnFocusObserver(), 1500);
    })();
  },
  { flush: "sync" }
);

function handleDemoReset() {
  formWorkspaceDirty.value = false;
  resetLoadedRouteContext();
  invalidateLineage();
  const code = studyCode.value;
  canvas.openStudy(code);
  if (code) {
    activeWorkspaceDescriptor.value?.loadOnEnter?.(workspaceContext.value);
  }
}

let unregisterCanvasExplain: (() => void) | null = null;
let unregisterInspectorFocus: (() => void) | null = null;

onMounted(() => {
  syncMediumCanvasViewport();
  if (typeof window !== "undefined") {
    if (typeof window.matchMedia === "function") {
      mediumCanvasMediaList = window.matchMedia(MEDIUM_CANVAS_MEDIA_QUERY);
      if (typeof mediumCanvasMediaList?.addEventListener === "function") {
        mediumCanvasMediaList.addEventListener(
          "change",
          syncMediumCanvasViewport
        );
      } else if (typeof mediumCanvasMediaList?.addListener === "function") {
        mediumCanvasMediaList.addListener(syncMediumCanvasViewport);
      }
    }
    window.addEventListener("resize", syncMediumCanvasViewport);
  }
  unregisterInspectorFocus = canvas.registerInspectorCoordinator(
    async (request) => {
      await focusAndScrollInspector({
        scrollIntoView: request.scrollIntoView,
      });
    }
  );
  unregisterCanvasExplain = canvas.registerExplainCoordinator((options) => {
    if (options?.fromShortcut) return false;
    const targetRef = options?.targetNodeRef;
    if (targetRef && parseNodeRef(targetRef)) {
      void router
        .push({
          name: "study",
          params: { studyCode: studyCode.value },
          query: {
            ...route.query,
            node: targetRef,
          },
        })
        .then(() => {
          void nextTick(() => {
            inspectorRef.value?.openExplain?.();
          });
        });
      return true;
    }
    return Boolean(inspectorRef.value?.openExplain?.(options));
  });
  window.addEventListener("beforeunload", handleBeforeUnload);
  window.addEventListener("cadence:demo-reset", handleDemoReset);
  document.addEventListener("click", onWorkspaceInteractionCapture, true);
  document.addEventListener("keydown", onWorkspaceInteractionCapture, true);
  if (!inAppShell) {
    document.addEventListener("keydown", onGlobalKeydown);
  }
  syncTopbarHeight();
  window.addEventListener("resize", syncTopbarHeight);
  if (
    typeof ResizeObserver !== "undefined" &&
    topbarRef.value instanceof HTMLElement
  ) {
    topbarResizeObserver = new ResizeObserver(() => syncTopbarHeight());
    topbarResizeObserver.observe(topbarRef.value, { box: "border-box" });
  }
});
onUnmounted(() => {
  knowledgeStore.clearCanvasEscalationContext();
  stopReturnFocusObserver();
  unregisterInspectorFocus?.();
  unregisterInspectorFocus = null;
  unregisterCanvasExplain?.();
  unregisterCanvasExplain = null;
  if (mediumCanvasMediaList) {
    if (typeof mediumCanvasMediaList.removeEventListener === "function") {
      mediumCanvasMediaList.removeEventListener(
        "change",
        syncMediumCanvasViewport
      );
    } else if (typeof mediumCanvasMediaList.removeListener === "function") {
      mediumCanvasMediaList.removeListener(syncMediumCanvasViewport);
    }
    mediumCanvasMediaList = null;
  }
  if (typeof window !== "undefined") {
    window.removeEventListener("resize", syncMediumCanvasViewport);
    window.removeEventListener("resize", syncTopbarHeight);
  }
  window.removeEventListener("beforeunload", handleBeforeUnload);
  window.removeEventListener("cadence:demo-reset", handleDemoReset);
  document.removeEventListener("click", onWorkspaceInteractionCapture, true);
  document.removeEventListener("keydown", onWorkspaceInteractionCapture, true);
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
  background: var(--semantic-color-surface-muted);
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
  gap: 12px;
  padding: 2px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface);
}

.zoom-path {
  flex: 1;
  min-width: 0;
}

.zoom-path ol {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.zoom-path li {
  display: inline-flex;
  align-items: center;
}

.zoom-path li + li::before {
  content: "/";
  margin-right: 4px;
  color: var(--neutral-dark);
}

.zoom-path a {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  min-width: 44px;
  padding: 0 6px;
  border-radius: 6px;
  color: inherit;
}

.zoom-path [aria-current="page"] {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  padding: 0 6px;
  font-weight: 600;
}

@media (min-width: 961px) {
  .topbar {
    padding: 0 14px;
    gap: 8px;
    flex-wrap: nowrap;
  }

  .zoom-path ol {
    flex-wrap: nowrap;
    overflow-x: auto;
    font-size: 0.85rem;
  }
}

@media (min-width: 768px) and (max-width: 960px) {
  .topbar {
    padding: 0 12px;
    gap: 8px;
  }

  .zoom-path ol {
    flex-wrap: nowrap;
    overflow-x: auto;
    font-size: 0.84rem;
  }

  .study-header {
    padding: 8px 12px;
    gap: 6px 16px;
  }
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
  border-color: var(--semantic-color-info-border);
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
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
  background: var(--semantic-color-surface);
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
  min-width: 0;
  max-width: 100%;
}

.panes.panes--navigator-collapsed {
  grid-template-columns: 2.75rem minmax(0, 1fr) 20rem;
}

.panes.panes--inspector-collapsed {
  grid-template-columns: 14rem minmax(0, 1fr) 2.75rem;
}

.panes.panes--navigator-collapsed.panes--inspector-collapsed {
  grid-template-columns: 2.75rem minmax(0, 1fr) 2.75rem;
}

.navigator {
  position: sticky;
  top: var(--canvas-pane-top);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  min-width: 0;
  background: var(--semantic-color-surface);
}

.navigator.navigator--collapsed {
  padding: 8px 6px;
  align-items: center;
}

.navigator-rail {
  display: flex;
  justify-content: flex-end;
  width: 100%;
}

.navigator.navigator--collapsed .navigator-rail {
  justify-content: center;
}

.navigator-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  min-height: 28px;
  padding: 2px 6px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  font-size: 0.85rem;
  line-height: 1;
  cursor: pointer;
}

.navigator-toggle:hover {
  background: var(--neutral-light);
  color: var(--primary);
}

.navigator-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
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
  max-width: 100%;
  box-sizing: border-box;
  border-left: 1px solid var(--border);
  border-right: 1px solid var(--border);
}

.workspace > * {
  min-width: 0;
  max-width: 100%;
}

.canvas :deep(#canvas-inspector) {
  position: sticky;
  top: var(--canvas-pane-top);
  scroll-margin-top: calc(var(--canvas-pane-top, 0px) + 12px);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
  background: var(--semantic-color-surface);
}

@media (min-width: 961px) and (max-width: 1280px) {
  .study-header {
    padding: 8px 12px;
    gap: 6px 16px;
  }

  .panes {
    grid-template-columns: 11rem minmax(0, 1fr) 15.5rem;
  }

  .panes.panes--navigator-collapsed {
    grid-template-columns: 2.75rem minmax(0, 1fr) 15.5rem;
  }

  .panes.panes--inspector-collapsed {
    grid-template-columns: 11rem minmax(0, 1fr) 2.75rem;
  }

  .panes.panes--navigator-collapsed.panes--inspector-collapsed {
    grid-template-columns: 2.75rem minmax(0, 1fr) 2.75rem;
  }
}

@media (max-width: 960px) {
  .panes,
  .panes.panes--navigator-collapsed,
  .panes.panes--inspector-collapsed,
  .panes.panes--navigator-collapsed.panes--inspector-collapsed {
    grid-template-columns: minmax(0, 1fr);
  }

  .navigator {
    position: static;
    max-height: none;
    overflow-y: visible;
    align-self: stretch;
    border-bottom: 1px solid var(--border);
  }

  .navigator.navigator--collapsed {
    align-items: stretch;
    padding: 6px 12px;
  }

  .navigator.navigator--collapsed .navigator-rail {
    justify-content: flex-start;
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

@media (max-width: 640px) {
  .topbar {
    position: static;
  }
}
</style>
