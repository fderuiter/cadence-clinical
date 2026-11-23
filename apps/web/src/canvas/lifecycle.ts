/**
 * The Clinical Canvas lifecycle (docs/ux/CLINICAL_CANVAS.md §2.2): six stages, each defined in its own
 * module under `./stages/` so parallel stage additions do not collide, plus generated participant state
 * transitions from Execution.
 */
export * from "./stages";
