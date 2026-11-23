import { holdsBackendPermission, type AuthorityAuth } from "./roles";

export const ETMF_QC_REVIEW_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role (etmf_document:transition_clinical_qc).";
export const TRAINING_ASSIGN_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (training_curriculum:assign).";
export const TRAINING_DELEGATION_SIGN_ROLE_REASON =
  "Requires Principal Investigator role (delegation_log:sign).";

/** Stage site startup / study documents into eTMF intelligence QC (etmf_document:create). */
export function canStageEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:create");
}

/** Transition staged eTMF documents through clinical QC review (etmf_document:transition_clinical_qc). */
export function canReviewEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:transition_clinical_qc");
}

/** Assign protocol training curricula to personnel (training_curriculum:assign). */
export function canAssignTrainingCurriculum(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "training_curriculum:assign");
}

/** Sign Knowledge site delegation logs as Principal Investigator (delegation_log:sign). */
export function canSignTrainingDelegations(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "delegation_log:sign");
}
