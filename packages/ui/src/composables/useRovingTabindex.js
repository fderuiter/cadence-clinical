import {
  getCurrentInstance,
  nextTick,
  onBeforeUnmount,
  onMounted,
  unref,
  watch,
} from "vue";

function isTabDisabled(tabEl) {
  if (!tabEl) return true;
  return (
    Boolean(tabEl.disabled) ||
    tabEl.hasAttribute("disabled") ||
    tabEl.getAttribute("aria-disabled") === "true"
  );
}

/**
 * Synchronizes roving `tabindex="0"` / `"-1"` across all `[role="tab"]` descendants
 * of a `role="tablist"` container based on `aria-selected="true"` and disabled state.
 *
 * @param {HTMLElement | null | undefined} container
 * @returns {HTMLElement | null} The active roving stop element, if any.
 */
export function syncRovingTabindex(container) {
  if (!container || typeof container.querySelectorAll !== "function") {
    return null;
  }

  const allTabs = Array.from(container.querySelectorAll('[role="tab"]'));
  if (allTabs.length === 0) {
    return null;
  }

  const enabledTabs = allTabs.filter((tab) => !isTabDisabled(tab));
  const selectedTab =
    enabledTabs.find((tab) => tab.getAttribute("aria-selected") === "true") ||
    enabledTabs[0] ||
    null;

  for (const tab of allTabs) {
    const desired = tab === selectedTab ? "0" : "-1";
    if (tab.getAttribute("tabindex") !== desired) {
      tab.setAttribute("tabindex", desired);
    }
  }

  return selectedTab;
}

/**
 * WAI-ARIA Tabs roving tabindex keyboard and focus manager.
 * Supports `ArrowRight`/`ArrowLeft` (or `ArrowDown`/`ArrowUp` when vertical),
 * `Home`, and `End`, skips disabled tabs, and keeps `tabindex="0"|"-1"` synchronized.
 *
 * @param {import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | HTMLElement | null} containerRef
 * @param {(index: number, tabElement: HTMLElement) => void} [onSelectTab]
 * @param {{ orientation?: "horizontal" | "vertical", manual?: boolean }} [options]
 */
export function useRovingTabindex(containerRef, onSelectTab, options = {}) {
  let activeContainer = null;
  let observer = null;

  const resolveContainer = () => {
    if (typeof containerRef === "function") {
      return containerRef() || null;
    }
    return unref(containerRef) || null;
  };

  const resolveOrientation = (container) => {
    if (
      options.orientation === "vertical" ||
      options.orientation === "horizontal"
    ) {
      return options.orientation;
    }
    return container?.getAttribute("aria-orientation") === "vertical"
      ? "vertical"
      : "horizontal";
  };

  const handleKeyDown = (event) => {
    const container = resolveContainer();
    if (!container) return;

    const orientation = resolveOrientation(container);
    const nextKey = orientation === "vertical" ? "ArrowDown" : "ArrowRight";
    const prevKey = orientation === "vertical" ? "ArrowUp" : "ArrowLeft";
    const isManual = Boolean(options.manual);

    const isNavKey =
      event.key === nextKey ||
      event.key === prevKey ||
      event.key === "Home" ||
      event.key === "End";
    const isActivateKey =
      isManual && (event.key === "Enter" || event.key === " ");

    if (!isNavKey && !isActivateKey) return;

    const allTabs = Array.from(container.querySelectorAll('[role="tab"]'));
    if (allTabs.length === 0) return;

    const ownerDoc = container.ownerDocument || document;
    const activeEl = ownerDoc.activeElement;
    const focusedTab =
      activeEl && typeof activeEl.closest === "function"
        ? activeEl.closest('[role="tab"]')
        : activeEl;
    const focusedIndex = allTabs.indexOf(focusedTab);
    if (focusedIndex === -1) return;

    const enabledIndices = allTabs
      .map((tab, idx) => (isTabDisabled(tab) ? -1 : idx))
      .filter((idx) => idx !== -1);
    if (enabledIndices.length === 0) return;

    if (isActivateKey) {
      if (!isTabDisabled(allTabs[focusedIndex])) {
        event.preventDefault();
        if (typeof onSelectTab === "function") {
          onSelectTab(focusedIndex, allTabs[focusedIndex]);
        }
        nextTick(() => {
          syncRovingTabindex(resolveContainer());
        });
      }
      return;
    }

    event.preventDefault();

    const currentEnabledPos = enabledIndices.indexOf(focusedIndex);
    let targetIndex = focusedIndex;

    if (event.key === nextKey) {
      if (currentEnabledPos !== -1) {
        targetIndex =
          enabledIndices[(currentEnabledPos + 1) % enabledIndices.length];
      } else {
        targetIndex =
          enabledIndices.find((idx) => idx > focusedIndex) ?? enabledIndices[0];
      }
    } else if (event.key === prevKey) {
      if (currentEnabledPos !== -1) {
        targetIndex =
          enabledIndices[
            (currentEnabledPos - 1 + enabledIndices.length) %
              enabledIndices.length
          ];
      } else {
        const preceding = enabledIndices.filter((idx) => idx < focusedIndex);
        targetIndex =
          preceding.length > 0
            ? preceding[preceding.length - 1]
            : enabledIndices[enabledIndices.length - 1];
      }
    } else if (event.key === "Home") {
      targetIndex = enabledIndices[0];
    } else if (event.key === "End") {
      targetIndex = enabledIndices[enabledIndices.length - 1];
    }

    const targetTab = allTabs[targetIndex];
    if (!targetTab) return;

    const shouldActivate =
      !isManual &&
      (targetIndex !== focusedIndex ||
        targetTab.getAttribute("aria-selected") !== "true");

    if (shouldActivate && typeof onSelectTab === "function") {
      onSelectTab(targetIndex, targetTab);
    }

    nextTick(() => {
      const latestContainer = resolveContainer();
      if (!latestContainer) return;
      const updatedTabs = Array.from(
        latestContainer.querySelectorAll('[role="tab"]')
      );
      const nextFocusEl = updatedTabs[targetIndex];
      if (isManual && nextFocusEl) {
        for (const tab of updatedTabs) {
          const desired = tab === nextFocusEl ? "0" : "-1";
          if (tab.getAttribute("tabindex") !== desired) {
            tab.setAttribute("tabindex", desired);
          }
        }
      } else {
        syncRovingTabindex(latestContainer);
      }
      if (nextFocusEl && typeof nextFocusEl.focus === "function") {
        nextFocusEl.focus();
      }
    });
  };

  const detach = () => {
    if (observer) {
      observer.disconnect();
      observer = null;
    }
    if (activeContainer) {
      activeContainer.removeEventListener("keydown", handleKeyDown);
      activeContainer = null;
    }
  };

  const attach = (container) => {
    if (!container || typeof container.addEventListener !== "function") return;
    activeContainer = container;
    activeContainer.addEventListener("keydown", handleKeyDown);
    syncRovingTabindex(activeContainer);

    if (typeof MutationObserver !== "undefined") {
      observer = new MutationObserver(() => {
        if (!options.manual) {
          syncRovingTabindex(activeContainer);
        }
      });
      observer.observe(activeContainer, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ["aria-selected", "disabled", "aria-disabled"],
      });
    }
  };

  const stopWatch = watch(
    resolveContainer,
    (nextEl) => {
      if (nextEl === activeContainer) {
        if (nextEl) syncRovingTabindex(nextEl);
        return;
      }
      detach();
      attach(nextEl);
    },
    { immediate: true, flush: "post" }
  );

  const stop = () => {
    stopWatch();
    detach();
  };

  if (getCurrentInstance()) {
    onMounted(() => {
      const el = resolveContainer();
      if (el !== activeContainer) {
        detach();
        attach(el);
      } else if (el) {
        syncRovingTabindex(el);
      }
    });
    onBeforeUnmount(stop);
  }

  return {
    syncTabindex: () => syncRovingTabindex(resolveContainer()),
    stop,
  };
}
