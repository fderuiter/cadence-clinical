import { onMounted, onUnmounted, nextTick } from "vue";

/**
 * useRovingTabindex - Horizontal arrow-key focus and selection manager (roving tabindex pattern)
 *
 * @param {Ref<HTMLElement>} containerRef - Ref to the tab list container containing role="tab" elements
 * @param {Function} onSelectTab - Callback with parameters (index, tabElement) when a tab should be activated
 */
export function useRovingTabindex(containerRef, onSelectTab) {
  const handleKeyDown = (e) => {
    if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
    if (!containerRef.value) return;

    const tabs = Array.from(
      containerRef.value.querySelectorAll('[role="tab"]')
    );
    if (tabs.length === 0) return;

    const focusedIndex = tabs.indexOf(document.activeElement);
    if (focusedIndex === -1) return;

    e.preventDefault();

    let targetIndex = focusedIndex;
    if (e.key === "ArrowRight") {
      targetIndex = (focusedIndex + 1) % tabs.length;
    } else if (e.key === "ArrowLeft") {
      targetIndex = (focusedIndex - 1 + tabs.length) % tabs.length;
    }

    if (targetIndex !== focusedIndex) {
      onSelectTab(targetIndex, tabs[targetIndex]);

      nextTick(() => {
        if (!containerRef.value) return;
        const updatedTabs = Array.from(
          containerRef.value.querySelectorAll('[role="tab"]')
        );
        if (updatedTabs[targetIndex]) {
          updatedTabs[targetIndex].focus();
        }
      });
    }
  };

  onMounted(() => {
    if (containerRef.value) {
      containerRef.value.addEventListener("keydown", handleKeyDown);
    }
  });

  onUnmounted(() => {
    if (containerRef.value) {
      containerRef.value.removeEventListener("keydown", handleKeyDown);
    }
  });
}
