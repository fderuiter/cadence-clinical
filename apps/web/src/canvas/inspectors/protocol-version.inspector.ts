import ProtocolVersionInspectorPane from "../../components/canvas/ProtocolVersionInspectorPane.vue";
import { parseNodeRef } from "../nodeRef";
import type { CanvasInspectorDescriptor } from "./types";

const protocolVersionInspector: CanvasInspectorDescriptor = {
  id: "protocol-version",
  kinds: ["protocol_version"],
  order: 20,
  matches: (ctx) => Boolean(ctx.protocolVersion),
  resolveHeader: (ctx) => {
    const version = ctx.protocolVersion!;
    const ref = version.ref ?? "";
    const parsed = ref ? parseNodeRef(ref) : null;
    return {
      kindLabel: "Protocol version",
      title: `Version ${version.number}`,
      activeNodeRef: ref,
      kernelRef: parsed ? ref : "",
      canSimulate: parsed?.kind === "protocol_version",
      impactLineageMode: version.parent_version ? "amendment" : "impact",
    };
  },
  resolveDetailsRender: (ctx) => ({
    component: ProtocolVersionInspectorPane,
    key: `protocol-version-${ctx.protocolVersion!.ref}`,
    props: {
      protocolVersion: ctx.protocolVersion!,
    },
  }),
};

export default protocolVersionInspector;
