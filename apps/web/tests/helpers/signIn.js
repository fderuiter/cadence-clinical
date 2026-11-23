/**
 * Signs a test user in the way the app does: through `setAuth` with a
 * keycloak-js style object, so identity and roles come from token claims.
 *
 * @param {ReturnType<import("../../src/stores/auth").useAuthStore>} authStore
 * @param {string[]} roles - Keycloak realm roles, e.g. ["Sponsor Admin"]
 * @param {{ username?: string, sub?: string, token?: string, studyIds?: string[], siteIds?: string[], assignedStudies?: string[], assignedSites?: string[] }} [options]
 */
export function signInAs(authStore, roles, options = {}) {
  const {
    username = "test_user",
    sub = `${username}-id`,
    token = "test-access-token",
    studyIds,
    siteIds,
    assignedStudies,
    assignedSites,
  } = options;
  const effectiveStudyIds = studyIds ?? assignedStudies;
  const effectiveSiteIds = siteIds ?? assignedSites;
  authStore.setAuth({
    authenticated: true,
    token,
    tokenParsed: {
      sub,
      preferred_username: username,
      realm_access: { roles },
      ...(effectiveStudyIds !== undefined
        ? { study_ids: effectiveStudyIds }
        : {}),
      ...(effectiveSiteIds !== undefined ? { site_ids: effectiveSiteIds } : {}),
    },
  });
  return authStore;
}
