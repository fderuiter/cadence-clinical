import { reactive, readonly } from "vue";
import { registerEntityPeekHandler } from "ui";

export interface PeekItem {
  nodeRef: string;
  label?: string;
}

const state = reactive({
  open: false,
  docked: false,
  current: null as PeekItem | null,
  history: [] as PeekItem[],
});

let registered = false;

export function openEntityPeekDrawer(item: PeekItem) {
  if (!item?.nodeRef || item.nodeRef.startsWith("masked:")) {
    return;
  }
  if (state.current && state.current.nodeRef !== item.nodeRef) {
    state.history = [...state.history.slice(-7), state.current];
  }
  state.current = { nodeRef: item.nodeRef, label: item.label };
  state.open = true;
}

export function closeEntityPeekDrawer() {
  state.open = false;
}

export function stepBackEntityPeekDrawer() {
  const prev = state.history[state.history.length - 1];
  if (prev) {
    state.history = state.history.slice(0, -1);
    state.current = prev;
  } else {
    state.open = false;
  }
}

export function toggleEntityPeekDocked() {
  state.docked = !state.docked;
}

export function useEntityPeekDrawer() {
  if (!registered) {
    registerEntityPeekHandler((payload: Record<string, unknown>) => {
      const nodeRef =
        typeof payload?.nodeRef === "string" ? payload.nodeRef : "";
      const label =
        typeof payload?.label === "string" ? payload.label : undefined;
      openEntityPeekDrawer({ nodeRef, label });
    });
    registered = true;
  }
  return {
    state: readonly(state),
    openPeek: openEntityPeekDrawer,
    closePeek: closeEntityPeekDrawer,
    stepBack: stepBackEntityPeekDrawer,
    toggleDocked: toggleEntityPeekDocked,
  };
}
