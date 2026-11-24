import { holdsBackendPermission, type AuthorityAuth } from "./roles";

export const ETMF_READ_ROLE_REASON =
  "Requires Clinical Research Coordinator, Investigator, CRA, Monitor, Sponsor Data Manager, Auditor, External Monitor, or System Administrator role.";
export const ETMF_STAGE_ROLE_REASON =
  "Requires Clinical Research Coordinator, CRA, Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_TAG_ROLE_REASON =
  "Requires Clinical Research Coordinator, CRA, Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_QC_REVIEW_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_EXPORT_ROLE_REASON =
  "Requires Sponsor Data Manager, CRA, Monitor, or Auditor role.";
export const TRAINING_ASSIGN_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role.";
export const TRAINING_DELEGATION_SIGN_ROLE_REASON =
  "Requires Principal Investigator role.";

/** Read eTMF documents, taxonomy, QC queues, and binder metadata. */
export function canReadEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:read");
}

/** Stage site startup / study documents into eTMF intelligence QC. */
export function canStageEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:create");
}

/** Update eTMF document taxonomy classifications. */
export function canTagEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:tag");
}

/** Transition staged eTMF documents through clinical QC review. */
export function canReviewEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:transition_clinical_qc");
}

/** Export study binder documents or archives. */
export function canExportEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:export");
}

/** Assign protocol training curricula to personnel. */
export function canAssignTrainingCurriculum(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "training_curriculum:assign");
}

/** Sign Knowledge site delegation logs as Principal Investigator. */
export function canSignTrainingDelegations(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "delegation_log:sign");
}
