import { createApp, h } from "vue";
import { createPinia } from "pinia";
import ObservationView from "../../src/views/ObservationView.vue";

/**
 * Mounts the real ObservationView, with the real kernel client, outside the app shell for the browser-to-database
 * check (ADR-2620). Only sign-in is left out: the page has no Keycloak session, and the check's signing proxy stands
 * in for Keycloak and the gateway (see KernelObservationHostPostgresTests). Never part of a build.
 */
const observationRef =
  new URLSearchParams(window.location.search).get("observation") ?? "";

createApp({ render: () => h(ObservationView, { observationRef }) })
  .use(createPinia())
  .mount("#app");
