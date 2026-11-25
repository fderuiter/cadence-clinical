import type {
  CanvasWorkspaceContext,
  CanvasWorkspaceDescriptor,
} from "./types";

const workspaceModules = import.meta.glob<{
  default: CanvasWorkspaceDescriptor;
}>("./*.workspace.ts", { eager: true });

export const WORKSPACE_DESCRIPTORS: readonly CanvasWorkspaceDescriptor[] =
  Object.freeze(
    Object.values(workspaceModules)
      .map((mod) => mod.default)
      .filter((descriptor): descriptor is CanvasWorkspaceDescriptor =>
        Boolean(descriptor?.id && typeof descriptor.matches === "function")
      )
      .sort((a, b) => {
        const orderDiff = (a.order ?? 9999) - (b.order ?? 9999);
        if (orderDiff !== 0) return orderDiff;
        return a.id.localeCompare(b.id);
      })
  );

export function resolveActiveWorkspace(
  ctx: CanvasWorkspaceContext
): CanvasWorkspaceDescriptor | undefined {
  return WORKSPACE_DESCRIPTORS.find((descriptor) => descriptor.matches(ctx));
}
