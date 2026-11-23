import {
  computed,
  onBeforeUnmount,
  ref,
  watch,
  type ComputedRef,
  type Ref,
} from "vue";

export interface UseRovingToolbarOptions {
  /** Ordered list of button IDs currently rendered in the toolbar. */
  itemIds: Ref<readonly string[]> | ComputedRef<readonly string[]>;
  /** Optional initial active button ID (defaults to the first item). */
  initialId?: string;
}

/**
 * Implements the WAI-ARIA toolbar roving `tabindex` pattern:
 * - Exactly one button in `role="toolbar"` has `tabindex="0"`; all other buttons have `tabindex="-1"`.
 * - `ArrowRight`, `ArrowLeft`, `Home`, and `End` move focus across all toolbar buttons
 *   (including `aria-disabled="true"` buttons so users can read contextual guidance)
 *   without automatically activating buttons.
 */
export function useRovingToolbar(
  toolbarRef: Ref<HTMLElement | null>,
  options: UseRovingToolbarOptions
) {
  const ids = computed(() => options.itemIds.value);
  const activeId = ref<string>(
    options.initialId && ids.value.includes(options.initialId)
      ? options.initialId
      : (ids.value[0] ?? "")
  );

  watch(
    ids,
    (nextIds) => {
      if (!nextIds.length) {
        activeId.value = "";
        return;
      }
      if (!activeId.value || !nextIds.includes(activeId.value)) {
        activeId.value = nextIds[0];
      }
    },
    { immediate: true }
  );

  function getTabIndex(id: string): 0 | -1 {
    const list = ids.value;
    if (!list.length) return -1;
    const resolvedActive =
      activeId.value && list.includes(activeId.value)
        ? activeId.value
        : list[0];
    return id === resolvedActive ? 0 : -1;
  }

  function setActiveId(id: string): void {
    if (id && ids.value.includes(id)) {
      activeId.value = id;
    }
  }

  function getToolbarButtons(): HTMLButtonElement[] {
    const container = toolbarRef.value;
    if (!container) return [];
    return Array.from(
      container.querySelectorAll<HTMLButtonElement>("button:not([disabled])")
    );
  }

  function onToolbarFocusIn(event: FocusEvent): void {
    const target = event.target;
    if (!(target instanceof HTMLElement)) return;
    const button = target.closest("button");
    if (button?.id && ids.value.includes(button.id)) {
      activeId.value = button.id;
    }
  }

  function onToolbarKeydown(event: KeyboardEvent): void {
    if (
      event.key !== "ArrowRight" &&
      event.key !== "ArrowLeft" &&
      event.key !== "Home" &&
      event.key !== "End"
    ) {
      return;
    }

    const buttons = getToolbarButtons();
    if (!buttons.length) return;

    const targetEl =
      event.target instanceof HTMLElement
        ? event.target.closest("button")
        : null;
    let currentIndex = targetEl
      ? buttons.indexOf(targetEl as HTMLButtonElement)
      : -1;
    if (currentIndex === -1) {
      currentIndex = buttons.findIndex((btn) => btn.id === activeId.value);
    }
    if (currentIndex === -1) {
      currentIndex = 0;
    }

    let nextIndex = currentIndex;
    if (event.key === "ArrowRight") {
      nextIndex = (currentIndex + 1) % buttons.length;
    } else if (event.key === "ArrowLeft") {
      nextIndex = (currentIndex - 1 + buttons.length) % buttons.length;
    } else if (event.key === "Home") {
      nextIndex = 0;
    } else if (event.key === "End") {
      nextIndex = buttons.length - 1;
    }

    event.preventDefault();
    const nextButton = buttons[nextIndex];
    if (!nextButton) return;
    if (nextButton.id) {
      activeId.value = nextButton.id;
    }
    nextButton.focus();
  }

  let attachedEl: HTMLElement | null = null;
  function detachListeners() {
    if (!attachedEl) return;
    attachedEl.removeEventListener("keydown", onToolbarKeydown);
    attachedEl.removeEventListener("focusin", onToolbarFocusIn);
    attachedEl = null;
  }

  watch(
    toolbarRef,
    (el) => {
      detachListeners();
      if (el) {
        el.addEventListener("keydown", onToolbarKeydown);
        el.addEventListener("focusin", onToolbarFocusIn);
        attachedEl = el;
      }
    },
    { immediate: true, flush: "sync" }
  );

  onBeforeUnmount(() => {
    detachListeners();
  });

  return {
    activeId,
    getTabIndex,
    setActiveId,
    onToolbarFocusIn,
    onToolbarKeydown,
  };
}
