// Mounts the Vue shell with the portal's Pinia instance and router.
import { createApp } from "vue";

import App from "../App.vue";
import { getPortalRouter } from "../router/index.js";
import { pinia } from "../stores/pinia.js";

let mountedApp = null;

// Mounts (or re-mounts) the portal into `el`.
export function mountPortal(el) {
  unmountApp();
  mountedApp = createApp(App);
  mountedApp.use(pinia);
  mountedApp.use(getPortalRouter());
  mountedApp.mount(el);
  return mountedApp;
}

export function unmountApp() {
  if (mountedApp) {
    try {
      mountedApp.unmount();
    } catch {
      // Ignore unmount errors during teardown
    }
    mountedApp = null;
  }
}
