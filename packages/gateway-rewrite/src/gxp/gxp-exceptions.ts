export class AuditJustificationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuditJustificationError";
  }
}

export class ComplianceError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ComplianceError";
  }
}

export class InvalidStateTransitionError extends Error {
  public readonly errorCode: string = "INVALID_STATE_TRANSITION";
  public readonly error_code: string = "INVALID_STATE_TRANSITION";

  constructor(message: string) {
    super(message);
    this.name = "InvalidStateTransitionError";
  }
}

export class LockedFactorMutationError extends Error {
  public readonly errorCode: string = "LOCKED_FACTOR_MUTATION";
  public readonly error_code: string = "LOCKED_FACTOR_MUTATION";

  constructor(message: string) {
    super(message);
    this.name = "LockedFactorMutationError";
  }
}
