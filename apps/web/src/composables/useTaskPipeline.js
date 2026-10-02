import { ref, onUnmounted, getCurrentInstance } from "vue";

/**
 * Composable utility to manage asynchronous task execution with stage progress,
 * reactive AbortController lifecycle, and automatic Object URL memory cleanup.
 */
export function useTaskPipeline() {
  const isProcessing = ref(false);
  const isCompleted = ref(false);
  const isCancelled = ref(false);
  const isError = ref(false);
  const status = ref("idle"); // 'idle' | 'processing' | 'success' | 'error' | 'cancelled'
  const progressPercent = ref(0);
  const currentStage = ref("");
  const errorMessage = ref("");
  const abortController = ref(null);
  const activeObjectUrls = ref([]);

  /**
   * Register a Blob Object URL for lifecycle management and automatic cleanup.
   * @param {string} url - The URL created by URL.createObjectURL
   * @returns {string} The registered URL
   */
  function registerObjectUrl(url) {
    if (url && typeof url === "string") {
      activeObjectUrls.value.push(url);
    }
    return url;
  }

  /**
   * Revoke and clean up all registered Object URLs.
   */
  function cleanupObjectUrls() {
    while (activeObjectUrls.value.length > 0) {
      const url = activeObjectUrls.value.pop();
      if (url) {
        try {
          URL.revokeObjectURL(url);
        } catch {
          // Ignore revocation errors in teardown
        }
      }
    }
  }

  /**
   * Initialize a new task session and return an AbortSignal.
   * @returns {AbortSignal}
   */
  function startTask() {
    cleanupObjectUrls();
    if (abortController.value) {
      abortController.value.abort("New task started");
    }
    abortController.value = new AbortController();
    isProcessing.value = true;
    isCompleted.value = false;
    isCancelled.value = false;
    isError.value = false;
    status.value = "processing";
    progressPercent.value = 0;
    currentStage.value = "Initializing task...";
    errorMessage.value = "";
    return abortController.value.signal;
  }

  /**
   * Update stage progress percentage and description.
   * @param {number} percent - Progress percentage (0-100)
   * @param {string} [stageDescription] - Stage description text
   */
  function updateProgress(percent, stageDescription) {
    if (isCancelled.value) return;
    const clamped = Math.min(100, Math.max(0, Math.round(percent)));
    progressPercent.value = clamped;
    if (stageDescription !== undefined) {
      currentStage.value = stageDescription;
    }
  }

  /**
   * Mark task as completed successfully.
   * @param {string} [stageDescription] - Final stage description
   */
  function completeTask(stageDescription = "Task completed successfully") {
    if (isCancelled.value) return;
    isProcessing.value = false;
    isCompleted.value = true;
    status.value = "success";
    progressPercent.value = 100;
    if (stageDescription) {
      currentStage.value = stageDescription;
    }
  }

  /**
   * Mark task as failed with an error message and clean up resources.
   * @param {Error|string} err - Error object or error string
   */
  function failTask(err) {
    if (isCancelled.value) return;
    cleanupObjectUrls();
    isProcessing.value = false;
    isError.value = true;
    status.value = "error";
    const msg = typeof err === "string" ? err : err?.message || "Task failed";
    errorMessage.value = msg;
    currentStage.value = "Task execution failed";
  }

  /**
   * Cancel task using AbortController and clean up resources.
   * @param {string} [reason] - Reason for cancellation
   */
  function cancelTask(reason = "Operation cancelled by user") {
    if (abortController.value && !abortController.value.signal.aborted) {
      abortController.value.abort(reason);
    }
    cleanupObjectUrls();
    isProcessing.value = false;
    isCancelled.value = true;
    status.value = "cancelled";
    errorMessage.value = reason;
    currentStage.value = "Task cancelled";
  }

  /**
   * Reset task state to idle.
   */
  function resetTask() {
    cleanupObjectUrls();
    if (abortController.value) {
      abortController.value.abort("Reset task");
      abortController.value = null;
    }
    isProcessing.value = false;
    isCompleted.value = false;
    isCancelled.value = false;
    isError.value = false;
    status.value = "idle";
    progressPercent.value = 0;
    currentStage.value = "";
    errorMessage.value = "";
  }

  /**
   * Run an async multi-stage pipeline callback with AbortSignal support.
   * @param {Function} taskFn - Async function (context: { signal, updateProgress, registerObjectUrl, isAborted })
   * @returns {Promise<any>}
   */
  async function runTask(taskFn) {
    const signal = startTask();
    const context = {
      signal,
      updateProgress,
      registerObjectUrl,
      isAborted: () => signal.aborted,
    };

    const handleAbort = () => {
      cancelTask();
    };

    signal.addEventListener("abort", handleAbort, { once: true });

    try {
      const result = await taskFn(context);
      signal.removeEventListener("abort", handleAbort);

      if (signal.aborted) {
        cancelTask();
        return null;
      }

      completeTask();
      return result;
    } catch (err) {
      signal.removeEventListener("abort", handleAbort);
      if (signal.aborted || err?.name === "AbortError") {
        cancelTask();
      } else {
        failTask(err);
      }
      throw err;
    }
  }

  if (getCurrentInstance()) {
    onUnmounted(() => {
      cleanupObjectUrls();
      if (abortController.value) {
        abortController.value.abort("Component unmounted");
      }
    });
  }

  return {
    isProcessing,
    isCompleted,
    isCancelled,
    isError,
    status,
    progressPercent,
    currentStage,
    errorMessage,
    abortController,
    activeObjectUrls,
    startTask,
    updateProgress,
    completeTask,
    failTask,
    cancelTask,
    resetTask,
    runTask,
    registerObjectUrl,
    cleanupObjectUrls,
  };
}
