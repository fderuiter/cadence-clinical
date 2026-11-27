import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
  type Ref,
} from "vue";
import type { RouteLocationRaw } from "vue-router";
import type {
  KernelCommand,
  KernelNode,
  KernelProtocolVersion,
} from "../../api/kernelApi";
import { useAiAssistance } from "../../ai/aiAssistance";
import { formatNodeRef, parseNodeRef } from "../../canvas/nodeRef";
import { getNodeDisplay } from "../../canvas/nodeResolver";
import { useKernelCanI } from "../../composables/useKernelCanI";
import { useRovingToolbar } from "../../composables/useRovingToolbar";
import {
  useStudyCanvasStore,
  type CanvasParticipant,
} from "../../stores/studyCanvas";
import { useInspectorSimulation } from "./useInspectorSimulation";

export interface CanvasInspectorProps {
  studyCode: string;
  participant: CanvasParticipant | null;
  nodeRef: string;
  missing: boolean;
  protocolVersion?: KernelProtocolVersion | null;
  protocolVersionError?: string;
  genericNodeRef?: string;
  genericNode?: KernelNode | null;
  genericNodeLoading?: boolean;
  recordLocation?: RouteLocationRaw | null;
  dataEntryLocation?: RouteLocationRaw | null;
  handoffLocation?: RouteLocationRaw | null;
  canManageParticipants?: boolean;
  canUnblindParticipants?: boolean;
  assistPrompt?: string;
  onRetryProtocolVersion?: () => unknown | Promise<unknown>;
}

export type VerbId =
  "details" | "trace" | "impact" | "act" | "explain" | "simulate" | "assist";

interface Verb {
  id: Exclude<VerbId, "details">;
  label: string;
  live: boolean;
  note: string;
}

const NO_RECORD_REF =
  "this selection doesn't have a linked study reference yet.";

export function useCanvasInspectorController(
  props: CanvasInspectorProps,
  onCollapsedChange: (collapsed: boolean) => void
) {
  const { enabled: aiEnabled } = useAiAssistance();
  const canvas = useStudyCanvasStore();
  const versionErrorCanI = useKernelCanI();
  const forceExplainOpen = ref(false);

  const genericDisplay = computed(() => {
    if (!props.genericNodeRef || props.missing) return null;
    return getNodeDisplay(
      props.genericNodeRef,
      props.genericNode?.title ?? null
    );
  });

  const hasSelection = computed(() =>
    Boolean(props.participant || props.protocolVersion || genericDisplay.value)
  );

  const headerKind = computed(() => {
    if (props.participant) return "Participant";
    if (props.protocolVersion) return "Protocol version";
    return genericDisplay.value?.kindLabel ?? "Record";
  });

  const headerTitle = computed(() => {
    if (props.participant) return props.participant.subject_id;
    if (props.protocolVersion) return `Version ${props.protocolVersion.number}`;
    return genericDisplay.value?.label ?? "Record";
  });

  const isCodeTitle = computed(() =>
    Boolean(
      props.participant ||
      (genericDisplay.value?.code &&
        headerTitle.value === genericDisplay.value.code)
    )
  );

  const canonicalParticipantEnrollmentRef = computed(() => {
    if (!props.participant) return "";
    const parsed = parseNodeRef(props.participant.enrollment_ref);
    return parsed?.kind === "enrollment"
      ? formatNodeRef({ kind: "enrollment", id: parsed.id })
      : "";
  });

  const activeNodeRef = computed(() => {
    if (props.participant) {
      return canonicalParticipantEnrollmentRef.value || props.nodeRef;
    }
    if (props.protocolVersion?.ref) return props.protocolVersion.ref;
    if (props.genericNode?.ref) return props.genericNode.ref;
    return genericDisplay.value?.ref ?? "";
  });

  const copiedRef = ref(false);
  async function copyInspectorRef() {
    if (!activeNodeRef.value) return;
    try {
      await globalThis.navigator?.clipboard?.writeText?.(activeNodeRef.value);
      copiedRef.value = true;
      setTimeout(() => {
        copiedRef.value = false;
      }, 1500);
    } catch {
      copiedRef.value = false;
    }
  }

  const hasActiveContent = computed(() =>
    Boolean(
      hasSelection.value ||
      props.protocolVersionError ||
      props.missing ||
      forceExplainOpen.value ||
      (props.assistPrompt && aiEnabled.value)
    )
  );

  const manualCollapsed = ref<boolean | null>(null);
  const isCollapsed = computed(
    () => manualCollapsed.value ?? !hasActiveContent.value
  );

  function toggleCollapsed() {
    manualCollapsed.value = !isCollapsed.value;
  }

  function expand() {
    manualCollapsed.value = false;
  }

  const kernelRef = computed(() => {
    if (props.participant) {
      if (canonicalParticipantEnrollmentRef.value) {
        return canonicalParticipantEnrollmentRef.value;
      }
      return parseNodeRef(props.nodeRef) ? props.nodeRef : "";
    }
    const version = props.protocolVersion?.ref;
    if (version && parseNodeRef(version)) return version;
    if (
      genericDisplay.value &&
      !genericDisplay.value.masked &&
      parseNodeRef(genericDisplay.value.ref)
    ) {
      return genericDisplay.value.ref;
    }
    return "";
  });

  const currentVerb = ref<VerbId>("details");
  const explained = ref<KernelCommand | null>(null);
  const shownVerb = ref<string | null>(null);
  const toolbarRef: Ref<HTMLElement | null> = ref(null);

  const lineageMode = computed(() => {
    if (currentVerb.value === "trace") return "trace";
    return props.protocolVersion?.parent_version && !props.participant
      ? "amendment"
      : "impact";
  });

  const canSimulate = computed(() =>
    Boolean(
      !props.participant &&
      props.protocolVersion?.ref &&
      parseNodeRef(props.protocolVersion.ref)?.kind === "protocol_version"
    )
  );

  const verbs = computed<Verb[]>(() => [
    {
      id: "trace",
      label: "Trace",
      live: Boolean(kernelRef.value),
      note: `Trace shows upstream source records, when available — ${NO_RECORD_REF}`,
    },
    {
      id: "impact",
      label: "Impact",
      live: Boolean(kernelRef.value),
      note: `Impact shows downstream dependent records, when available — ${NO_RECORD_REF}`,
    },
    {
      id: "explain",
      label: "Explain",
      live: Boolean(kernelRef.value),
      note: `Explain shows why an action is permitted or blocked, when available — ${NO_RECORD_REF}`,
    },
    {
      id: "simulate",
      label: "Simulate",
      live: canSimulate.value,
      note: "Participant simulation isn't available in this workspace yet; select a protocol version to check its schedule.",
    },
    {
      id: "act",
      label: "Act",
      live: Boolean(kernelRef.value),
      note: `Act previews available actions and eligibility, when available — ${NO_RECORD_REF}`,
    },
    ...(aiEnabled.value
      ? [
          {
            id: "assist" as const,
            label: "Assist",
            live: Boolean(kernelRef.value),
            note: `Assist reviews related proposals for this record, when available — ${NO_RECORD_REF}`,
          },
        ]
      : []),
  ]);

  const toolbarItemIds = computed(() => [
    "verb-details",
    ...verbs.value.map((verb) => `verb-${verb.id}`),
  ]);
  const { getTabIndex, setActiveId } = useRovingToolbar(toolbarRef, {
    itemIds: toolbarItemIds,
  });

  const {
    simulationResult,
    simulationLoading,
    simulationError,
    runSimulation,
    resetSimulation,
  } = useInspectorSimulation(canSimulate, kernelRef);

  watch(currentVerb, (verb) => {
    if (
      verb === "simulate" &&
      canSimulate.value &&
      !simulationResult.value &&
      !simulationLoading.value
    ) {
      void runSimulation();
    }
  });

  const shownVerbNote = computed(
    () =>
      verbs.value.find((v) => !v.live && v.id === shownVerb.value)?.note ?? ""
  );

  function choose(verb: VerbId) {
    currentVerb.value = verb;
    shownVerb.value = null;
    setActiveId(`verb-${verb}`);
  }

  function focusExplainSection() {
    void nextTick(() => {
      if (typeof document === "undefined") return;
      document.getElementById("kernel-explain")?.focus();
    });
  }

  function explainCommand(command: KernelCommand) {
    explained.value = command;
    expand();
    choose("explain");
    focusExplainSection();
  }

  function openProtocolVersionExplain() {
    forceExplainOpen.value = true;
    expand();
    if (parseNodeRef(props.nodeRef)) {
      void versionErrorCanI.probe("protocol.read", props.nodeRef);
    }
    focusExplainSection();
  }

  function openExplain(options?: {
    fromShortcut?: boolean;
    targetNodeRef?: string | null;
  }): boolean {
    const fromShortcut = Boolean(options?.fromShortcut);
    if (kernelRef.value || explained.value) {
      expand();
      if (kernelRef.value) {
        choose("explain");
      }
      focusExplainSection();
      return true;
    }
    if (fromShortcut) return false;
    if (props.protocolVersionError) {
      openProtocolVersionExplain();
      return true;
    }
    if (options?.targetNodeRef) return false;
    forceExplainOpen.value = true;
    expand();
    focusExplainSection();
    return true;
  }

  let unregisterExplain: (() => void) | null = null;
  onMounted(() => {
    unregisterExplain = canvas.registerExplainCoordinator((options) =>
      openExplain(options)
    );
  });
  onBeforeUnmount(() => {
    unregisterExplain?.();
    unregisterExplain = null;
  });

  watch(
    () => [props.nodeRef, props.protocolVersion?.ref, props.genericNodeRef],
    () => {
      resetSimulation();
      shownVerb.value = null;
      currentVerb.value = "details";
      setActiveId("verb-details");
      explained.value = null;
      forceExplainOpen.value = false;
      versionErrorCanI.reset();
      copiedRef.value = false;
    }
  );

  watch(
    () => [
      props.assistPrompt,
      props.nodeRef,
      props.protocolVersion?.ref,
      props.genericNodeRef,
    ],
    () => {
      if (props.assistPrompt && aiEnabled.value && kernelRef.value) {
        choose("assist");
      }
    },
    { immediate: true }
  );

  watch(
    () => [
      props.nodeRef,
      props.participant?.id ?? "",
      props.protocolVersion?.ref ?? "",
      props.protocolVersionError ?? "",
      props.genericNodeRef ?? "",
      props.missing,
      props.assistPrompt ?? "",
      aiEnabled.value,
    ],
    () => {
      manualCollapsed.value = null;
    }
  );

  watch(
    isCollapsed,
    (collapsed) => {
      onCollapsedChange(collapsed);
    },
    { immediate: true }
  );

  watch(aiEnabled, (on) => {
    if (!on && currentVerb.value === "assist") choose("details");
  });

  return {
    aiEnabled,
    versionErrorCanI,
    forceExplainOpen,
    genericDisplay,
    hasSelection,
    headerKind,
    headerTitle,
    isCodeTitle,
    activeNodeRef,
    copiedRef,
    copyInspectorRef,
    isCollapsed,
    toggleCollapsed,
    expand,
    kernelRef,
    lineageMode,
    verbs,
    currentVerb,
    explained,
    shownVerb,
    shownVerbNote,
    toolbarRef,
    getTabIndex,
    simulationResult,
    simulationLoading,
    simulationError,
    runSimulation,
    choose,
    explainCommand,
    openProtocolVersionExplain,
    openExplain,
  };
}
