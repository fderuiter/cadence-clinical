import type {
  CanvasInspectorContext,
  CanvasInspectorDescriptor,
} from "./types";

const inspectorModules = import.meta.glob<{
  default: CanvasInspectorDescriptor;
}>("./*.inspector.ts", { eager: true });

export const INSPECTOR_DESCRIPTORS: readonly CanvasInspectorDescriptor[] =
  Object.freeze(
    Object.values(inspectorModules)
      .map((mod) => mod.default)
      .filter((descriptor): descriptor is CanvasInspectorDescriptor =>
        Boolean(
          descriptor?.id &&
          Array.isArray(descriptor.kinds) &&
          typeof descriptor.matches === "function"
        )
      )
      .sort((a, b) => {
        const orderDiff = (a.order ?? 9999) - (b.order ?? 9999);
        if (orderDiff !== 0) return orderDiff;
        return a.id.localeCompare(b.id);
      })
  );

export function resolveActiveInspector(
  ctx: CanvasInspectorContext
): CanvasInspectorDescriptor | undefined {
  return INSPECTOR_DESCRIPTORS.find((descriptor) => descriptor.matches(ctx));
}

export function findInspectorForKind(
  kind: string
): CanvasInspectorDescriptor | undefined {
  return (
    INSPECTOR_DESCRIPTORS.find((descriptor) =>
      descriptor.kinds.includes(kind)
    ) ??
    INSPECTOR_DESCRIPTORS.find((descriptor) => descriptor.kinds.includes("*"))
  );
}
