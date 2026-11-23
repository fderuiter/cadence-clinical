import { computed, type ComputedRef, type Ref } from "vue";
import type { LocationQuery } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import {
  collectionInstanceId,
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  parseNodeRef,
  tryNodeRef,
  type NodeRef,
} from "../canvas/nodeRef";
import { canResolveNodesRemotely } from "../canvas/nodeResolver";

function firstQueryValue(value: unknown): string {
  if (Array.isArray(value)) {
    return typeof value[0] === "string" ? value[0] : "";
  }
  return typeof value === "string" ? value : "";
}

function parseParticipantNode(value: unknown): NodeRef | null {
  const parsed = parseNodeRef(value);
  return parsed?.kind === "participant" ? parsed : null;
}

function parseObservationQuery(value: unknown): NodeRef | null {
  const direct = parseNodeRef(value);
  if (direct?.kind === "observation") return direct;
  const canonical = observationNodeRef(value);
  return canonical ? parseNodeRef(canonical) : null;
}

function parseFormQuery(value: unknown): {
  raw: string;
  isNew: boolean;
  formId: string | null;
  node: NodeRef | null;
} {
  const raw = firstQueryValue(value).trim();
  if (!raw) {
    return { raw: "", isNew: false, formId: null, node: null };
  }
  if (raw === "new") {
    return { raw, isNew: true, formId: null, node: null };
  }
  const direct = parseNodeRef(raw);
  if (direct?.kind === "collection_instance") {
    return { raw, isNew: false, formId: direct.id, node: direct };
  }
  const id = collectionInstanceId(raw);
  if (!id) {
    return { raw, isNew: false, formId: null, node: null };
  }
  const refText = collectionInstanceRef(id);
  return {
    raw,
    isNew: false,
    formId: id,
    node: refText ? parseNodeRef(refText) : null,
  };
}

function parseProposalQuery(value: unknown): NodeRef | null {
  const parsed = parseNodeRef(value);
  return parsed?.kind === "proposal" ? parsed : null;
}

export interface UseCanvasSelectionOptions {
  query: () => LocationQuery;
  knownParticipants: () => Record<string, SubjectSummary>;
  loadedFormParticipant?:
    Ref<SubjectSummary | null> | ComputedRef<SubjectSummary | null>;
  lineageParticipant?:
    Ref<SubjectSummary | null> | ComputedRef<SubjectSummary | null>;
  isParticipantPending?: (id: string | undefined) => boolean;
  canResolveRemotely?: () => boolean;
  enrollmentIdForParticipant?: (participant: SubjectSummary) => string | null;
}

/**
 * Resolves the active Clinical Canvas selection (`NodeRef`) in deterministic priority order:
 *   1. `?node=` (explicit inspector target, e.g. participant, protocol_version, site, or source chip)
 *   2. `?observation=` (`observation:<uuid>`)
 *   3. `?form=` when bound to a saved `collection_instance:<uuid>` (`?form=new` stays bound to `participant:<id>`)
 *   4. `?proposal=` (`proposal:<uuid>`)
 *   5. `?participant=` (`participant:<uuid>`) or participant resolved from a loaded form / lineage
 */
export function useCanvasSelection(options: UseCanvasSelectionOptions) {
  const canRemote = options.canResolveRemotely ?? canResolveNodesRemotely;
  const isPending = options.isParticipantPending ?? (() => false);

  const explicitNode = computed<NodeRef | null>(() =>
    parseNodeRef(options.query().node)
  );

  const recordRef = computed<NodeRef | null>(() =>
    parseParticipantNode(options.query().participant)
  );

  const formState = computed(() => parseFormQuery(options.query().form));
  const formQuery = computed(() => formState.value.raw);
  const isNewForm = computed(() => formState.value.isNew);
  const openFormId = computed(() => formState.value.formId);
  const openFormNode = computed<NodeRef | null>(() => formState.value.node);
  const openFormRef = computed(() =>
    openFormNode.value ? formatNodeRef(openFormNode.value) : ""
  );

  const openObservationNode = computed<NodeRef | null>(() =>
    parseObservationQuery(options.query().observation)
  );
  const openObservationRef = computed(() =>
    openObservationNode.value
      ? `${openObservationNode.value.kind}:${openObservationNode.value.id}`
      : ""
  );

  const proposalNode = computed<NodeRef | null>(() =>
    parseProposalQuery(options.query().proposal)
  );
  const proposalRef = computed(() =>
    proposalNode.value ? formatNodeRef(proposalNode.value) : ""
  );

  const recordParticipant = computed<SubjectSummary | null>(() => {
    const id = recordRef.value?.id;
    return id ? (options.knownParticipants()[id] ?? null) : null;
  });

  const activeFormParticipant = computed<SubjectSummary | null>(
    () =>
      recordParticipant.value ??
      options.loadedFormParticipant?.value ??
      options.lineageParticipant?.value ??
      null
  );

  const fallbackParticipantNode = computed<NodeRef | null>(() => {
    if (recordRef.value) return recordRef.value;
    const participant = activeFormParticipant.value;
    return participant ? tryNodeRef("participant", participant.id) : null;
  });

  /** Highest-priority active `NodeRef` inspected by `#canvas-inspector`. */
  const activeNode = computed<NodeRef | null>(() => {
    if (explicitNode.value) return explicitNode.value;
    if (openObservationNode.value) return openObservationNode.value;
    if (openFormNode.value) return openFormNode.value;
    if (proposalNode.value) return proposalNode.value;
    return fallbackParticipantNode.value;
  });

  const selectedNode = computed<NodeRef | null>(() =>
    activeNode.value?.kind === "participant" ? activeNode.value : null
  );

  const selectedVersionNode = computed<NodeRef | null>(() =>
    activeNode.value?.kind === "protocol_version" ? activeNode.value : null
  );

  const selectedVersionText = computed(() =>
    selectedVersionNode.value ? formatNodeRef(selectedVersionNode.value) : ""
  );

  const selectedParticipant = computed<SubjectSummary | null>(() => {
    if (!selectedNode.value) return null;
    const byId = options.knownParticipants()[selectedNode.value.id];
    if (byId) return byId;
    if (
      !explicitNode.value &&
      activeFormParticipant.value?.id === selectedNode.value.id
    ) {
      return activeFormParticipant.value;
    }
    return null;
  });

  const isSelectedParticipantLoading = computed(() =>
    isPending(selectedNode.value?.id)
  );

  const selectedGenericNodeRef = computed(() => {
    const node = activeNode.value;
    if (!node || node.kind === "protocol_version") return "";
    if (node.kind === "participant") {
      // Only fall back to remote generic node lookup when ?node=participant:<id> was explicitly requested
      // and Execution has neither loaded nor is still loading that participant.
      if (
        !explicitNode.value ||
        selectedParticipant.value ||
        isSelectedParticipantLoading.value ||
        !canRemote()
      ) {
        return "";
      }
    }
    return formatNodeRef(node);
  });

  const inspectedParticipant = computed<SubjectSummary | null>(
    () => selectedParticipant.value
  );

  const inspectedNodeText = computed(() => {
    if (!activeNode.value) return "";
    if (activeNode.value.kind === "participant") {
      // Preserve cold-link behavior: if ?participant= points to an unloaded participant without ?node=,
      // only emit a participant NodeRef once the participant is loaded (or when ?node= explicitly selected it).
      if (!explicitNode.value && !selectedParticipant.value) {
        return "";
      }
      const enrollmentId = selectedParticipant.value
        ? options.enrollmentIdForParticipant?.(selectedParticipant.value)
        : null;
      if (enrollmentId) {
        const enrollmentNode = tryNodeRef("enrollment", enrollmentId);
        if (enrollmentNode) return formatNodeRef(enrollmentNode);
      }
    }
    return formatNodeRef(activeNode.value);
  });

  const activeParticipantNodeText = computed(() =>
    fallbackParticipantNode.value
      ? formatNodeRef(fallbackParticipantNode.value)
      : ""
  );

  return {
    explicitNode,
    activeNode,
    recordRef,
    recordParticipant,
    activeFormParticipant,
    activeParticipantNodeText,
    formQuery,
    isNewForm,
    openFormId,
    openFormNode,
    openFormRef,
    openObservationNode,
    openObservationRef,
    proposalNode,
    proposalRef,
    selectedNode,
    selectedParticipant,
    isSelectedParticipantLoading,
    selectedVersionNode,
    selectedVersionText,
    selectedGenericNodeRef,
    inspectedParticipant,
    inspectedNodeText,
  };
}
