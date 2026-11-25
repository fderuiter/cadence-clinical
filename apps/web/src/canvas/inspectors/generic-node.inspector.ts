import GenericNodeInspectorPane from "../../components/canvas/GenericNodeInspectorPane.vue";
import { parseNodeRef } from "../nodeRef";
import type { CanvasInspectorDescriptor } from "./types";

const genericNodeInspector: CanvasInspectorDescriptor = {
  id: "generic-node",
  kinds: ["*"],
  order: 900,
  matches: (ctx) => Boolean(ctx.genericDisplay),
  resolveHeader: (ctx) => {
    const display = ctx.genericDisplay!;
    const activeNodeRef = ctx.genericNode?.ref || display.ref || "";
    const kernelRef =
      !display.masked && parseNodeRef(display.ref) ? display.ref : "";
    const title = display.label || "Record";
    return {
      kindLabel: display.kindLabel || "Record",
      title,
      isCodeTitle: Boolean(display.code && title === display.code),
      activeNodeRef,
      kernelRef,
      canSimulate: false,
      impactLineageMode: "impact",
    };
  },
  resolveDetailsRender: (ctx) => ({
    component: GenericNodeInspectorPane,
    key: `generic-${ctx.genericDisplay!.ref}`,
    props: {
      studyCode: ctx.studyCode,
      display: ctx.genericDisplay!,
      loading: ctx.genericNodeLoading,
      recordLocation: ctx.recordLocation,
      dataEntryLocation: ctx.dataEntryLocation,
      handoffLocation: ctx.handoffLocation,
    },
  }),
};

export default genericNodeInspector;
