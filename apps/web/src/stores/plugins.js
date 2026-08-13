import { ref } from "vue";

export const stateTrackingPlugin = ({ store, options }) => {
  console.log(`[Plugin] stateTrackingPlugin called for store: ${store.$id}, trackActions:`, options?.trackActions);
  if (options.trackActions) {
    // 1. Dynamic Injection: Create and attach state properties if they are not already defined
    Object.entries(options.trackActions).forEach(([actionName, config]) => {
      const loadingKey = config.loading || `${actionName}Loading`;
      const errorKey = config.error || `${actionName}Error`;

      if (!(loadingKey in store)) {
        store[loadingKey] = ref(false);
      }
      if (!(errorKey in store)) {
        store[errorKey] = ref(null);
      }
    });

    // 2. Action Interception via store.$onAction
    store.$onAction(({ name, after, onError }) => {
      const config = options.trackActions[name];
      if (!config) return;

      const loadingKey = config.loading || `${name}Loading`;
      const errorKey = config.error || `${name}Error`;

      console.log(`[Plugin] Action start: ${name}, setting ${loadingKey}=true, ${errorKey}=null`);
      // Set pending state
      store[loadingKey] = true;
      store[errorKey] = null;

      after(() => {
        console.log(`[Plugin] Action after: ${name}, setting ${loadingKey}=false`);
        store[loadingKey] = false;
      });

      onError((error) => {
        console.log(`[Plugin] Action onError: ${name}, error=${error.message || error}, setting ${loadingKey}=false, ${errorKey}=error`);
        store[loadingKey] = false;
        // Ignore AbortError / canceled requests to avoid UI flashing or false error banners
        const isAbortError =
          error?.name === "AbortError" ||
          error?.message?.toLowerCase().includes("abort") ||
          error?.statusText === "abort";
        if (!isAbortError) {
          store[errorKey] = error.message || error;
        }
      });
    });
  }
};
