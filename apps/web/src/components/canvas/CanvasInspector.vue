<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    :class="{ 'inspector--collapsed': isCollapsed }"
    :data-collapsed="isCollapsed ? 'true' : 'false'"
    aria-labelledby="canvas-inspector-title"
  >
    <div class="inspector-rail">
      <button
        id="btn-toggle-inspector"
        type="button"
        class="inspector-toggle"
        :aria-expanded="!isCollapsed"
        aria-controls="canvas-inspector-body"
        :aria-label="isCollapsed ? 'Expand inspector' : 'Collapse inspector'"
        :title="isCollapsed ? 'Expand inspector' : 'Collapse inspector'"
        @click="toggleCollapsed"
      >
        <span aria-hidden="true">{{ isCollapsed ? "◂" : "▸" }}</span>
      </button>
    </div>

    <div
      id="canvas-inspector-body"
      v-show="!isCollapsed"
      class="inspector-body"
    >
      <template v-if="hasSelection">
        <header class="inspector-header">
          <span class="inspector-kind">{{ headerKind }}</span>
          <h2
            id="canvas-inspector-title"
            class="inspector-title"
            :class="{ 'inspector-title--code': isCodeTitle }"
            tabindex="-1"
          >
            {{ headerTitle }}
          </h2>
          <details v-if="activeNodeRef" class="inspector-ref-details">
            <summary class="inspector-ref-summary">Record reference</summary>
            <div class="inspector-ref-row">
              <code id="canvas-inspector-ref" class="inspector-ref">{{
                activeNodeRef
              }}</code>
              <button
                id="btn-copy-inspector-ref"
                type="button"
                class="btn-copy-ref"
                @click="copyInspectorRef"
              >
                {{ copiedRef ? "Copied" : "Copy" }}
              </button>
            </div>
          </details>
        </header>

        <div ref="toolbarRef" class="verbs" role="toolbar" aria-label="Verbs">
          <button
            id="verb-details"
            type="button"
            class="verb"
            :class="{ 'verb-current': currentVerb === 'details' }"
            :aria-pressed="currentVerb === 'details'"
            :tabindex="getTabIndex('verb-details')"
            @click="choose('details')"
          >
            Details
          </button>
          <template v-for="verb in verbs" :key="verb.id">
            <button
              v-if="verb.live"
              :id="`verb-${verb.id}`"
              type="button"
              class="verb"
              :class="{ 'verb-current': currentVerb === verb.id }"
              :aria-pressed="currentVerb === verb.id"
              :tabindex="getTabIndex(`verb-${verb.id}`)"
              @click="choose(verb.id)"
            >
              {{ verb.label }}
            </button>
            <button
              v-else
              :id="`verb-${verb.id}`"
              type="button"
              class="verb"
              aria-disabled="true"
              :tabindex="getTabIndex(`verb-${verb.id}`)"
              :aria-describedby="
                shownVerb === verb.id ? 'verb-note' : undefined
              "
              @click="shownVerb = verb.id"
            >
              {{ verb.label }}
            </button>
          </template>
        </div>
        <p v-if="shownVerbNote" id="verb-note" class="verb-note" role="status">
          {{ shownVerbNote }}
        </p>

        <AssistPanel
          v-if="currentVerb === 'assist' && kernelRef && aiEnabled"
          :study-code="studyCode"
          :node-ref="kernelRef"
          :prefill="assistPrompt"
          @act="choose('act')"
        />

        <KernelActPanel
          v-else-if="currentVerb === 'act' && kernelRef"
          :node-ref="kernelRef"
          @explain="explainCommand"
          @open-explain="openExplain()"
        />

        <KernelLineagePanel
          v-else-if="
            (currentVerb === 'trace' || currentVerb === 'impact') && kernelRef
          "
          :node-ref="kernelRef"
          :mode="lineageMode"
          @explain="openExplain()"
        />

        <section
          v-else-if="currentVerb === 'explain' && kernelRef"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">
            {{
              explained
                ? `Why: ${formatCommandTitle(explained.command)}`
                : "Explain"
            }}
          </h3>
          <KernelExplanation
            v-if="explained"
            :explanation="explained.decision.explanation"
          />
          <p v-else id="kernel-explain-empty" class="inspector-empty">
            Choose Explain beside an action under Act to see why it is permitted
            or blocked.
          </p>
        </section>

        <InspectorSimulatePanel
          v-else-if="currentVerb === 'simulate' && kernelRef"
          :simulation-result="simulationResult"
          :simulation-loading="simulationLoading"
          :simulation-error="simulationError"
          @retry="runSimulation"
          @explain="openExplain()"
        />

        <InspectorRecordDetails
          v-else-if="protocolVersion"
          :study-code="studyCode"
          :protocol-version="protocolVersion"
        />

        <template v-else-if="participant || genericDisplay">
          <InspectorParticipantDetails
            v-if="participant"
            :study-code="studyCode"
            :participant="participant"
            :can-manage-participants="canManageParticipants"
            :can-unblind-participants="canUnblindParticipants"
          />
          <InspectorRecordDetails
            v-else
            :study-code="studyCode"
            :generic-display="genericDisplay"
            :generic-node-loading="genericNodeLoading"
          />
          <router-link
            v-if="recordLocation"
            id="inspector-open-record"
            class="inspector-link"
            :to="recordLocation"
          >
            Open participant's data
          </router-link>
          <router-link
            v-if="dataEntryLocation"
            id="inspector-open-data-entry"
            class="inspector-link"
            :to="dataEntryLocation"
          >
            Open in data entry
          </router-link>
          <router-link
            v-if="isDemoBuild() && handoffLocation"
            id="inspector-handoff-standalone"
            data-testid="inspector-ecrf-handoff"
            class="inspector-link"
            :to="handoffLocation"
          >
            Open standalone form view
          </router-link>
        </template>
      </template>

      <template v-else-if="protocolVersionError">
        <h2 id="canvas-inspector-title" class="inspector-title" tabindex="-1">
          Protocol version
        </h2>
        <CanvasErrorAlert
          id="inspector-version-error"
          message="This protocol version couldn't be loaded. Check that it exists and is visible to your account."
          :detail="protocolVersionError"
          @retry="() => props.onRetryProtocolVersion?.()"
          @explain="openProtocolVersionExplain"
        />
        <section
          v-if="forceExplainOpen"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">Explain</h3>
          <KernelExplanation
            v-if="versionErrorCanI.decision.value?.explanation"
            :explanation="versionErrorCanI.decision.value.explanation"
          />
          <p v-else id="kernel-explain-empty" class="inspector-empty">
            Choose Retry above or verify your account's study access to inspect
            available protocol actions.
          </p>
        </section>
      </template>

      <template v-else-if="missing">
        <h2 id="canvas-inspector-title" class="inspector-title">
          Participant not loaded
        </h2>
        <p class="inspector-empty">
          This participant isn't among those loaded for {{ studyCode }}. Find it
          in Participants to see its details.
        </p>
      </template>

      <template v-else>
        <h2 id="canvas-inspector-title" class="inspector-title">Inspector</h2>
        <p class="inspector-empty">
          Select a participant or study record to see its details here.
        </p>
        <section
          v-if="forceExplainOpen"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">Explain</h3>
          <p id="kernel-explain-empty" class="inspector-empty">
            Select a study record to inspect why its actions are permitted or
            blocked.
          </p>
        </section>
        <AssistPanel
          v-if="assistPrompt && aiEnabled"
          :study-code="studyCode"
          :prefill="assistPrompt"
        />
      </template>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { formatCommandTitle } from "../../canvas/nodeResolver";
import { isDemoBuild } from "../../utils/demoMode";
import AssistPanel from "../ai/AssistPanel.vue";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";
import InspectorParticipantDetails from "./InspectorParticipantDetails.vue";
import InspectorRecordDetails from "./InspectorRecordDetails.vue";
import InspectorSimulatePanel from "./InspectorSimulatePanel.vue";
import KernelActPanel from "./KernelActPanel.vue";
import KernelExplanation from "./KernelExplanation.vue";
import KernelLineagePanel from "./KernelLineagePanel.vue";
import {
  useCanvasInspectorController,
  type CanvasInspectorProps,
} from "./useCanvasInspectorController";

const props = defineProps<CanvasInspectorProps>();

const emit = defineEmits<{
  (e: "update:collapsed", collapsed: boolean): void;
}>();

const {
  activeNodeRef,
  aiEnabled,
  choose,
  copiedRef,
  copyInspectorRef,
  currentVerb,
  expand,
  explainCommand,
  explained,
  forceExplainOpen,
  genericDisplay,
  getTabIndex,
  hasSelection,
  headerKind,
  headerTitle,
  isCodeTitle,
  isCollapsed,
  kernelRef,
  lineageMode,
  openExplain,
  openProtocolVersionExplain,
  runSimulation,
  shownVerb,
  shownVerbNote,
  simulationError,
  simulationLoading,
  simulationResult,
  toggleCollapsed,
  toolbarRef,
  verbs,
  versionErrorCanI,
} = useCanvasInspectorController(props, (collapsed) =>
  emit("update:collapsed", collapsed)
);

defineExpose({ expand, openExplain });
</script>

<style scoped src="./canvasInspector.css"></style>
<style scoped>
.inspector-title--code,
.inspector-mono {
  font-family: var(--semantic-font-mono);
}

#inspector-version-forms,
#inspector-version-notes {
  font-variant-numeric: tabular-nums;
}

.inspector-ref {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}
</style>
