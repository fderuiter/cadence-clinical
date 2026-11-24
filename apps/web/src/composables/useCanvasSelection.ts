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

function isParticipantNodeKind(
  kind: string | undefined
): kind is "enrollment" | "participant" {
  return kind === "enrollment" || kind === "participant";
}

function parseParticipantNode(value: unknown): NodeRef | null {
  const parsed = parseNodeRef(value);
  return isParticipantNodeKind(parsed?.kind) ? parsed : null;
}

function canonicalParticipantNode(participant: SubjectSummary): NodeRef | null {
  const enrollmentNode = parseNodeRef(participant.enrollment_ref);
  if (enrollmentNode?.kind === "enrollment") {
    return { kind: "enrollment", id: enrollmentNode.id };
  }
  return tryNodeRef("participant", participant.id);
}

function findInKnownParticipants(
  known: Record<string, SubjectSummary>,
  node: NodeRef | null | undefined
): SubjectSummary | null {
  if (!node) return null;
  if (known[node.id]) return known[node.id];
  if (node.kind === "enrollment") {
    for (const participant of Object.values(known)) {
      const enrollmentNode = parseNodeRef(participant.enrollment_ref);
      if (
        enrollmentNode?.kind === "enrollment" &&
        enrollmentNode.id === node.id
      ) {
        return participant;
      }
    }
  }
  return null;
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
  findParticipant?: (node: NodeRef | null | undefined) => SubjectSummary | null;
  loadedFormParticipant?:
    Ref<SubjectSummary | null> | ComputedRef<SubjectSummary | null>;
  lineageParticipant?:
    Ref<SubjectSummary | null> | ComputedRef<SubjectSummary | null>;
  isParticipantPending?: (id: string | undefined) => boolean;
  isParticipantNodePending?: (node: NodeRef | null | undefined) => boolean;
  canResolveRemotely?: () => boolean;
}

/**
 * Resolves the active Clinical Canvas selection (`NodeRef`) in deterministic priority order:
 *   1. `?node=` (explicit inspector target, e.g. enrollment, participant, protocol_version, site, or source chip)
 *   2. `?observation=` (`observation:<uuid>`)
 *   3. `?form=` when bound to a saved `collection_instance:<uuid>` (`?form=new` stays bound to `enrollment:<id>` / `participant:<id>`)
 *   4. `?proposal=` (`proposal:<uuid>`)
 *   5. `?participant=` (`enrollment:<uuid>` or `participant:<uuid>`) or participant resolved from a loaded form / lineage
 */
export function useCanvasSelection(options: UseCanvasSelectionOptions) {
  const canRemote = options.canResolveRemotely ?? canResolveNodesRemotely;
  const isPending = options.isParticipantPending ?? (() => false);
  const lookupParticipant = (
    node: NodeRef | null | undefined
  ): SubjectSummary | null =>
    options.findParticipant
      ? options.findParticipant(node)
      : findInKnownParticipants(options.knownParticipants(), node);
  const isNodePending = (node: NodeRef | null | undefined): boolean =>
    options.isParticipantNodePending
      ? options.isParticipantNodePending(node)
      : isPending(node?.id);

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

  const recordParticipant = computed<SubjectSummary | null>(() =>
    lookupParticipant(recordRef.value)
  );

  const activeFormParticipant = computed<SubjectSummary | null>(
    () =>
      recordParticipant.value ??
      options.loadedFormParticipant?.value ??
      options.lineageParticipant?.value ??
      null
  );

  const fallbackParticipantNode = computed<NodeRef | null>(() => {
    const participant = activeFormParticipant.value;
    if (participant) {
      const canonical = canonicalParticipantNode(participant);
      if (canonical) return canonical;
    }
    return recordRef.value;
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
    isParticipantNodeKind(activeNode.value?.kind) ? activeNode.value : null
  );

  const selectedVersionNode = computed<NodeRef | null>(() =>
    activeNode.value?.kind === "protocol_version" ? activeNode.value : null
  );

  const selectedVersionText = computed(() =>
    selectedVersionNode.value ? formatNodeRef(selectedVersionNode.value) : ""
  );

  const selectedParticipant = computed<SubjectSummary | null>(() => {
    if (!selectedNode.value) return null;
    const found = lookupParticipant(selectedNode.value);
    if (found) return found;
    if (!explicitNode.value && activeFormParticipant.value) {
      const canonical = canonicalParticipantNode(activeFormParticipant.value);
      if (
        activeFormParticipant.value.id === selectedNode.value.id ||
        (canonical &&
          canonical.kind === selectedNode.value.kind &&
          canonical.id === selectedNode.value.id)
      ) {
        return activeFormParticipant.value;
      }
    }
    return null;
  });

  const isSelectedParticipantLoading = computed(() =>
    isNodePending(selectedNode.value)
  );

  const selectedGenericNodeRef = computed(() => {
    const node = activeNode.value;
    if (!node || node.kind === "protocol_version") return "";
    if (isParticipantNodeKind(node.kind)) {
      // Only fall back to remote generic node lookup when ?node=enrollment:<id> or ?node=participant:<id>
      // was explicitly requested and Execution has neither loaded nor is still loading that participant.
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
    if (isParticipantNodeKind(activeNode.value.kind)) {
      // Preserve cold-link behavior: if ?participant= points to an unloaded participant without ?node=,
      // only emit a participant NodeRef once the participant is loaded (or when ?node= explicitly selected it).
      if (!explicitNode.value && !selectedParticipant.value) {
        return "";
      }
      if (selectedParticipant.value) {
        const canonical = canonicalParticipantNode(selectedParticipant.value);
        if (
          canonical &&
          (canonical.kind === "enrollment" || !explicitNode.value)
        ) {
          return formatNodeRef(canonical);
        }
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
