export interface UnsavedGuardAuthState {
  isAuthenticated?: boolean;
  sessionExpired?: boolean;
  isLoggingOut?: boolean;
}

export interface UnsavedGuardTargetRoute {
  name?: string | symbol | null;
  path?: string | null;
}

export function shouldSuppressUnsavedNavigationGuard(
  auth: UnsavedGuardAuthState,
  to?: UnsavedGuardTargetRoute | null
): boolean {
  if (!auth.isAuthenticated || auth.sessionExpired || auth.isLoggingOut) {
    return true;
  }
  if (
    to &&
    (to.name === "forbidden" ||
      to.path === "/forbidden" ||
      to.name === "login" ||
      to.path === "/login")
  ) {
    return true;
  }
  return false;
}
