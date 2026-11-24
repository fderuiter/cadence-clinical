// Mounts the Vue shell with the portal's Pinia instance and router.
import { createApp } from "vue";
import { enablePortalClinicalIcons } from "ui";

import App from "../App.vue";
import {
  preparePortalRouterForMount,
  setPortalRouterMounted,
} from "../router/index.js";
import { pinia } from "../stores/pinia.js";
import { state } from "../stores/state.js";

enablePortalClinicalIcons();

let mountedApp = null;

// Mounts (or re-mounts) the portal into `el`.
export function mountPortal(el) {
  unmountApp();
  const portalRouter = preparePortalRouterForMount();
  mountedApp = createApp(App);
  mountedApp.use(pinia);
  mountedApp.use(portalRouter);
  mountedApp.mount(el);
  setPortalRouterMounted(true);
  return mountedApp;
}

export function unmountApp() {
  setPortalRouterMounted(false);
  if (mountedApp) {
    try {
      mountedApp.unmount();
    } catch {
      // Ignore unmount errors during teardown
    }
    mountedApp = null;
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
  }
}
