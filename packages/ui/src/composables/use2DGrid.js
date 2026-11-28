import { nextTick, ref, unref, watch } from "vue";

/**
 * use2DGrid - Shared 2D Grid Keyboard Navigation & ARIA Roving Tabindex Manager.
 * Manages 2D roving tabindex (`0` active, `-1` inactive), arrow key matrix transitions,
 * Home/End/PageUp/PageDown traversal, coordinate clamping, and inline edit focus handoff.
 *
 * @param {import("vue").Ref<number>|Function|number} rowCount - Total rows in grid or getter
 * @param {import("vue").Ref<number>|Function|number} colCount - Total columns in grid or getter
 * @param {Object} [options] - Configuration options
 * @param {import("vue").Ref<HTMLElement|null>} [options.containerRef] - Container element reference
 * @param {number} [options.initialRow=0] - Starting row coordinate
 * @param {number} [options.initialCol=0] - Starting column coordinate
 * @param {Function} [options.onSelectCell] - Callback (r, c) when a cell is selected
 * @param {Function} [options.onEditCell] - Callback (r, c) when edit mode starts
 * @param {Function} [options.onCopyCell] - Callback (r, c, extraData) on Ctrl/Cmd+C
 * @param {Function} [options.onCellAction] - Callback (r, c, extraData, event) on Enter/Space
 * @returns {Object} 2D grid state and handler utilities
 */
export function use2DGrid(rowCount, colCount, options = {}) {
  const containerRef = options.containerRef || ref(null);
  const activeRowIndex = ref(options.initialRow ?? 0);
  const activeColIndex = ref(options.initialCol ?? 0);
  const isEditing = ref(false);

  const getRowCount = () => {
    try {
      const val = typeof rowCount === "function" ? rowCount() : unref(rowCount);
      return typeof val === "number" && !Number.isNaN(val) ? val : 0;
    } catch {
      return 0;
    }
  };

  const getColCount = () => {
    try {
      const val = typeof colCount === "function" ? colCount() : unref(colCount);
      return typeof val === "number" && !Number.isNaN(val) ? val : 0;
    } catch {
      return 0;
    }
  };

  /**
   * Clamp coordinate values so they remain within current matrix bounds.
   */
  const clampCoordinates = () => {
    const rows = getRowCount();
    const cols = getColCount();

    if (rows <= 0) {
      activeRowIndex.value = 0;
    } else if (activeRowIndex.value >= rows) {
      activeRowIndex.value = Math.max(0, rows - 1);
    } else if (activeRowIndex.value < 0) {
      activeRowIndex.value = 0;
    }

    if (cols <= 0) {
      activeColIndex.value = 0;
    } else if (activeColIndex.value >= cols) {
      activeColIndex.value = Math.max(0, cols - 1);
    } else if (activeColIndex.value < 0) {
      activeColIndex.value = 0;
    }
  };

  /**
   * Focus active cell DOM element (`[data-row][data-col]`) inside containerRef.
   */
  const focusActiveCell = async () => {
    await nextTick();
    if (!containerRef.value) return;

    const r = activeRowIndex.value;
    const c = activeColIndex.value;

    const cellEl = containerRef.value.querySelector(
      `[data-row="${r}"][data-col="${c}"]`
    );

    if (cellEl && typeof cellEl.focus === "function") {
      cellEl.focus();
    }
  };

  watch(
    [() => getRowCount(), () => getColCount()],
    () => {
      const activeEl =
        typeof document !== "undefined" ? document.activeElement : null;
      const isFocusInGrid = Boolean(
        containerRef.value &&
        activeEl &&
        typeof containerRef.value.contains === "function" &&
        containerRef.value.contains(activeEl)
      );
      clampCoordinates();
      if (isFocusInGrid) {
        void focusActiveCell();
      }
    },
    { immediate: true }
  );

  /**
   * Check if given coordinates match the active focus position.
   */
  const isCellActive = (rIndex, cIndex) => {
    return activeRowIndex.value === rIndex && activeColIndex.value === cIndex;
  };

  /**
   * Calculate 2D roving tabindex value for a cell (`0` active, `-1` inactive).
   */
  const getCellTabindex = (rIndex, cIndex) => {
    return isCellActive(rIndex, cIndex) ? 0 : -1;
  };

  /**
   * Select cell coordinates and optionally move DOM focus.
   */
  const selectCell = (rIndex, cIndex, focus = true) => {
    activeRowIndex.value = rIndex;
    activeColIndex.value = cIndex;
    clampCoordinates();

    if (options.onSelectCell) {
      options.onSelectCell(activeRowIndex.value, activeColIndex.value);
    }

    if (focus) {
      void focusActiveCell();
    }
  };

  /**
   * Enter inline editing mode for a cell.
   */
  const startEditing = async (rIndex, cIndex) => {
    if (typeof rIndex === "number" && typeof cIndex === "number") {
      activeRowIndex.value = rIndex;
      activeColIndex.value = cIndex;
    }
    isEditing.value = true;
    if (options.onEditCell) {
      options.onEditCell(activeRowIndex.value, activeColIndex.value);
    }
  };

  /**
   * Exit inline editing mode and return focus directly to active cell.
   */
  const exitEditing = async () => {
    isEditing.value = false;
    await focusActiveCell();
  };

  /**
   * Keyboard navigation event listener for 2D matrix traversal.
   */
  const handleCellKeyDown = (event, rIndex, cIndex, extraData) => {
    if (isEditing.value) {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        void exitEditing();
      }
      return;
    }

    const targetEl = event.target;
    const isTextEntryChild =
      targetEl &&
      targetEl !== event.currentTarget &&
      typeof targetEl.closest === "function" &&
      Boolean(
        targetEl.closest(
          'input:not([type="checkbox"]):not([type="radio"]), textarea, select, [contenteditable="true"]'
        )
      );

    if (isTextEntryChild) {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        void focusActiveCell();
        return;
      }
      if (
        event.key === "ArrowLeft" ||
        event.key === "ArrowRight" ||
        event.key === "Home" ||
        event.key === "End" ||
        event.key === " "
      ) {
        return;
      }
      if (
        (event.key === "ArrowUp" || event.key === "ArrowDown") &&
        Boolean(targetEl.closest('input[type="number"], select, textarea'))
      ) {
        return;
      }
    }

    if (typeof rIndex === "number" && typeof cIndex === "number") {
      activeRowIndex.value = rIndex;
      activeColIndex.value = cIndex;
    }

    const rows = getRowCount();
    const cols = getColCount();
    let handled = false;

    if (
      (event.ctrlKey || event.metaKey) &&
      (event.key === "c" || event.key === "C")
    ) {
      if (options.onCopyCell) {
        options.onCopyCell(
          activeRowIndex.value,
          activeColIndex.value,
          extraData
        );
        handled = true;
      }
    } else if (event.key === "ArrowUp") {
      if (activeRowIndex.value > 0) {
        activeRowIndex.value--;
        handled = true;
      }
    } else if (event.key === "ArrowDown") {
      if (activeRowIndex.value < rows - 1) {
        activeRowIndex.value++;
        handled = true;
      }
    } else if (event.key === "ArrowLeft") {
      if (activeColIndex.value > 0) {
        activeColIndex.value--;
        handled = true;
      }
    } else if (event.key === "ArrowRight") {
      if (activeColIndex.value < cols - 1) {
        activeColIndex.value++;
        handled = true;
      }
    } else if (event.key === "Home") {
      if (event.ctrlKey || event.metaKey) {
        activeRowIndex.value = 0;
        activeColIndex.value = 0;
      } else {
        activeColIndex.value = 0;
      }
      handled = true;
    } else if (event.key === "End") {
      if (event.ctrlKey || event.metaKey) {
        activeRowIndex.value = Math.max(0, rows - 1);
        activeColIndex.value = Math.max(0, cols - 1);
      } else {
        activeColIndex.value = Math.max(0, cols - 1);
      }
      handled = true;
    } else if (event.key === "PageUp") {
      activeRowIndex.value = Math.max(0, activeRowIndex.value - 5);
      handled = true;
    } else if (event.key === "PageDown") {
      activeRowIndex.value = Math.min(
        Math.max(0, rows - 1),
        activeRowIndex.value + 5
      );
      handled = true;
    } else if (event.key === "Enter" || event.key === " ") {
      const isInteractiveChild =
        targetEl &&
        targetEl !== event.currentTarget &&
        typeof targetEl.closest === "function" &&
        Boolean(
          targetEl.closest(
            'button, a[href], input, select, textarea, [role="button"], [role="checkbox"], [role="link"]'
          )
        );
      if (isInteractiveChild) {
        return;
      }
      if (options.onCellAction) {
        const actionResult = options.onCellAction(
          activeRowIndex.value,
          activeColIndex.value,
          extraData,
          event
        );
        if (actionResult !== false) {
          handled = true;
        }
      }
    }

    if (handled) {
      event.preventDefault();
      clampCoordinates();
      void focusActiveCell();
    }
  };

  return {
    containerRef,
    activeRowIndex,
    activeColIndex,
    isEditing,
    isCellActive,
    getCellTabindex,
    selectCell,
    focusActiveCell,
    startEditing,
    exitEditing,
    handleCellKeyDown,
    clampCoordinates,
  };
}
