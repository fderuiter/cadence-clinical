import { ref, watch, unref, nextTick } from "vue";

/**
 * use2DGrid - Reusable 2D Grid Keyboard Navigation & ARIA Focus Manager.
 * Manages 2D roving tabindex, arrow key matrix transitions, clamping,
 * and focus retention across state transitions.
 *
 * @param {Ref<number>|Function|number} rowCount - Total rows in grid or getter
 * @param {Ref<number>|Function|number} colCount - Total columns in grid or getter
 * @param {Object} options - Configuration options
 * @param {Ref<HTMLElement>} [options.containerRef] - Container element reference
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
      return typeof val === "number" && !isNaN(val) ? val : 0;
    } catch {
      return 0;
    }
  };

  const getColCount = () => {
    try {
      const val = typeof colCount === "function" ? colCount() : unref(colCount);
      return typeof val === "number" && !isNaN(val) ? val : 0;
    } catch {
      return 0;
    }
  };

  /**
   * Clamp coordinate values to ensure they remain within current matrix bounds.
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

  // Synchronize active coordinates when data row/col counts change
  watch(
    [() => getRowCount(), () => getColCount()],
    () => {
      const isFocusInGrid =
        containerRef.value &&
        containerRef.value.contains(document.activeElement);
      clampCoordinates();
      if (isFocusInGrid) {
        focusActiveCell();
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
   * Calculate 2D roving tabindex value for a cell.
   * Returns 0 for active cell, -1 for all other cells.
   */
  const getCellTabindex = (rIndex, cIndex) => {
    return isCellActive(rIndex, cIndex) ? 0 : -1;
  };

  /**
   * Select cell coordinates and optionally set DOM focus.
   */
  const selectCell = (rIndex, cIndex, focus = true) => {
    activeRowIndex.value = rIndex;
    activeColIndex.value = cIndex;
    clampCoordinates();

    if (options.onSelectCell) {
      options.onSelectCell(activeRowIndex.value, activeColIndex.value);
    }

    if (focus) {
      focusActiveCell();
    }
  };

  /**
   * Focus active cell DOM element in container.
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

  /**
   * Enter inline editing or action mode for a cell.
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
        exitEditing();
      }
      return;
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
      const targetEl = event.target;
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
      focusActiveCell();
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
