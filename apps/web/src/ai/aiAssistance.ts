import { readonly, ref } from "vue";

/**
 * Whether this viewer wants AI assistance in the Canvas (ADR-2635 §4.5). Off hides the Assist pane and the palette's
 * Ask entry; nothing else changes, because no screen depends on an agent (K-AI-8). Proposals an agent already made
 * stay in Needs Attention and on the review screen with their AI label: they are kernel records a person must still
 * decide. The tenant-wide switch that stops agents running belongs to the Intelligence module (#5374); this is only
 * the viewer's own choice, kept in their browser.
 */
const STORAGE_KEY = "cadence.ai.assistance";

function stored(): boolean {
  try {
    return globalThis.localStorage?.getItem(STORAGE_KEY) !== "off";
  } catch {
    return true;
  }
}

const enabled = ref(stored());

export function useAiAssistance() {
  function setEnabled(value: boolean) {
    enabled.value = value;
    try {
      globalThis.localStorage?.setItem(STORAGE_KEY, value ? "on" : "off");
    } catch {
      // A private window or blocked storage keeps the choice for this page only.
    }
  }

  return { enabled: readonly(enabled), setEnabled };
}

/** Re-reads the stored choice; tests call it after changing localStorage. */
export function reloadAiAssistance() {
  enabled.value = stored();
}
