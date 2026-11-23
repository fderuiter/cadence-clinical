import type { Component } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { KernelNode, KernelProtocolVersion } from "../../api/kernelApi";
import type { CanvasParticipant } from "../../stores/studyCanvas";
import type { NodeDisplaySummary } from "../nodeResolver";

export interface CanvasInspectorContext {
  studyCode: string;
  nodeRef: string;
  participant: CanvasParticipant | null;
  missing: boolean;
  protocolVersion: KernelProtocolVersion | null;
  protocolVersionError: string;
  genericNodeRef: string;
  genericNode: KernelNode | null;
  genericNodeLoading: boolean;
  genericDisplay: NodeDisplaySummary | null;
  recordLocation: RouteLocationRaw | null;
  dataEntryLocation: RouteLocationRaw | null;
  handoffLocation: RouteLocationRaw | null;
  canManageParticipants: boolean;
  canUnblindParticipants: boolean | undefined;
}

export interface CanvasInspectorHeaderMeta {
  kindLabel: string;
  title: string;
  activeNodeRef: string;
  kernelRef: string;
  canSimulate: boolean;
  impactLineageMode: "amendment" | "impact";
}

export interface CanvasInspectorRenderSpec {
  component: Component;
  key?: string;
  props?: Record<string, unknown>;
}

export interface CanvasInspectorDescriptor {
  id: string;
  /** NodeRef kinds handled by this inspector pane (`"*"` for fallback). */
  kinds: readonly string[];
  order: number;
  matches: (ctx: CanvasInspectorContext) => boolean;
  resolveHeader: (ctx: CanvasInspectorContext) => CanvasInspectorHeaderMeta;
  resolveDetailsRender: (
    ctx: CanvasInspectorContext
  ) => CanvasInspectorRenderSpec;
}
