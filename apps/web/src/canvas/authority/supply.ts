import { holdsBackendPermission, type AuthorityAuth } from "./roles";

/** Read and reconcile blinded site IP kit inventory (rtsm_supply:read/write — pharmacist only). */
export function canReconcileIpKits(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "rtsm_supply:write");
}

/** Seed a site's IP kit inventory (ip_kit_inventory:seed — sysadmin & sponsor_dm). */
export function canSeedIpKits(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ip_kit_inventory:seed");
}

/** Record cell and gene therapy chain-of-custody handovers (cgt_chain_of_custody:write). */
export function canRecordChainOfCustody(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "cgt_chain_of_custody:write");
}

/** Read blinded randomization scheme configuration (randomization_scheme:read). */
export function canReadRandomizationSchemes(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "randomization_scheme:read");
}

/** Draft and approve study randomization schemes (randomization_scheme:draft/approve — unblinded_statistician only). */
export function canManageRandomizationSchemes(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "randomization_scheme:draft");
}
