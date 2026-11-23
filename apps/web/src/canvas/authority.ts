/**
 * Which canvas and operational actions to offer the signed-in user. Presentation-only mirror of
 * generated `Cadence.Security.Rbac.{RoleNormalization,PermissionMatrix}` and Execution's `SubjectAuthority`,
 * split by clinical domain under `./authority/` to avoid cross-feature merge conflicts.
 */
export * from "./authority/index";
