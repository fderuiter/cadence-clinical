/** @type {import("vue").App | null} */
let registeredApp = null;

/**
 * Records the root Vue application instance during bootstrap without importing
 * `vue-i18n` or locale dictionaries onto the cold-boot graph.
 *
 * @param {import("vue").App | null} app
 */
export function registerI18nApp(app) {
  registeredApp = app || null;
}

export function getRegisteredI18nApp() {
  return registeredApp;
}
