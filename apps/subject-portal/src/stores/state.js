// `state`: one reactive object over all the portal's Pinia stores.
//
// The feature modules (and the tests, through `src/index.js`) read and write
// `state.<key>` exactly as they did when the portal kept a single module-level
// `reactive()` object. Each top-level key belongs to the store that declares
// it; a key no store declares is kept on the ui store, as the old object
// accepted new keys. Reads and writes go straight to the store's reactive
// `$state`, so templates, `watch` and `computed` track them as before.
import { pinia } from "./pinia.js";
import { useAuditStore } from "./audit.js";
import { useComplianceStore } from "./compliance.js";
import { useConsentStore } from "./consent.js";
import { useInboxStore } from "./inbox.js";
import { useSessionStore } from "./session.js";
import { useSyncStore } from "./sync.js";
import { useTasksStore } from "./tasks.js";
import { useUiStore } from "./ui.js";
import { useWalletStore } from "./wallet.js";

const stores = [
  useSessionStore,
  useTasksStore,
  useComplianceStore,
  useInboxStore,
  useConsentStore,
  useSyncStore,
  useWalletStore,
  useAuditStore,
  useUiStore,
].map((useStore) => useStore(pinia));

const owners = new Map();
for (const store of stores) {
  for (const key of Object.keys(store.$state)) owners.set(key, store);
}
const fallbackStore = useUiStore(pinia);

// Vue's own markers (`__v_isRef`, `__v_skip`, ...) and symbols stay on the
// facade itself, so it is never mistaken for a ref or a store property.
const isOwnKey = (key) => typeof key === "symbol" || key.startsWith("__v_");

export const state = new Proxy(
  {},
  {
    get(target, key) {
      if (isOwnKey(key)) return Reflect.get(target, key);
      return (owners.get(key) || fallbackStore).$state[key];
    },
    set(target, key, value) {
      if (isOwnKey(key)) return Reflect.set(target, key, value);
      (owners.get(key) || fallbackStore).$state[key] = value;
      return true;
    },
  }
);
