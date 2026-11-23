import { holdsBackendPermission, type AuthorityAuth } from "./roles";

export const ETMF_READ_ROLE_REASON =
  "Viewing regulated trial documents requires an assigned Clinical Research Coordinator, Investigator, Clinical Research Associate (CRA), Monitor, Sponsor Data Manager, Auditor, External Monitor, or System Administrator role.";
export const ETMF_STAGE_ROLE_REASON =
  "Uploading or staging trial documents requires an assigned Clinical Research Coordinator, Clinical Research Associate (CRA), Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_TAG_ROLE_REASON =
  "Updating document classifications requires an assigned Clinical Research Coordinator, Clinical Research Associate (CRA), Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_QC_REVIEW_ROLE_REASON =
  "Clinical quality control review requires an assigned Clinical Research Associate (CRA), Monitor, Sponsor Data Manager, or System Administrator role.";
export const ETMF_EXPORT_ROLE_REASON =
  "Exporting study binder archives requires an assigned Sponsor Data Manager, Clinical Research Associate (CRA), Monitor, or Auditor role.";
export const TRAINING_ASSIGN_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (training_curriculum:assign).";
export const TRAINING_DELEGATION_SIGN_ROLE_REASON =
  "Requires Principal Investigator role (delegation_log:sign).";

/** Read eTMF documents, taxonomy, QC queues, and binder metadata (etmf_document:read). */
export function canReadEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:read");
}

/** Stage site startup / study documents into eTMF intelligence QC (etmf_document:create). */
export function canStageEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:create");
}

/** Update eTMF document taxonomy classifications (etmf_document:tag). */
export function canTagEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:tag");
}

/** Transition staged eTMF documents through clinical QC review (etmf_document:transition_clinical_qc). */
export function canReviewEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:transition_clinical_qc");
}

/** Export study binder documents or archives (etmf_document:export). */
export function canExportEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "etmf_document:export");
}

/** Assign protocol training curricula to personnel (training_curriculum:assign). */
export function canAssignTrainingCurriculum(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "training_curriculum:assign");
}

/** Sign Knowledge site delegation logs as Principal Investigator (delegation_log:sign). */
export function canSignTrainingDelegations(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "delegation_log:sign");
}
