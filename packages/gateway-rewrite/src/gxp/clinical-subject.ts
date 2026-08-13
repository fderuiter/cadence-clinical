export class ClinicalSubject {
  private _subjectId?: string;
  private _studyId?: string;
  private _status: string = "SCREENING";
  private _stratFactors: Record<string, any> = {};
  private _isUnblinded: boolean = false;
  private _unblindedBy?: string;
  private _unblindedReason?: string;
  private _unblindedAt?: Date;
  private _randomizationId?: string;
  private _kitReference?: string;
  private _withdrawalReason?: string;
  private _withdrawnAt?: Date;
  private _siteId?: string;
  private _reasonForChange?: string;

  public id!: string;

  constructor(init?: any) {
    if (init) {
      this.id = init.id || init.subjectId || init.subject_id;
      this.subjectId = init.subjectId || init.subject_id;
      this.studyId = init.studyId || init.study_id;
      this.status = init.status || "SCREENING";
      this.stratFactors = init.stratFactors || init.strat_factors || {};
      this.isUnblinded =
        init.isUnblinded !== undefined
          ? init.isUnblinded
          : init.is_unblinded !== undefined
            ? init.is_unblinded
            : false;
      this.unblindedBy = init.unblindedBy || init.unblinded_by;
      this.unblindedReason = init.unblindedReason || init.unblinded_reason;
      this.unblindedAt = init.unblindedAt || init.unblinded_at;
      this.randomizationId =
        init.randomizationId || init.random_id || init.randomization_id;
      this.kitReference =
        init.kitReference || init.kit_ref || init.kit_reference;
      this.withdrawalReason = init.withdrawalReason || init.withdrawal_reason;
      this.withdrawnAt = init.withdrawnAt || init.withdrawn_at;
      this.siteId = init.siteId || init.site_id;
      this.reasonForChange = init.reasonForChange || init.reason_for_change;
    }
  }

  get subjectId() {
    return this._subjectId;
  }
  set subjectId(val) {
    this._subjectId = val;
    if (val && !this.id) this.id = val;
  }
  get subject_id() {
    return this._subjectId;
  }
  set subject_id(val) {
    this._subjectId = val;
    if (val && !this.id) this.id = val;
  }

  get studyId() {
    return this._studyId;
  }
  set studyId(val) {
    this._studyId = val;
  }
  get study_id() {
    return this._studyId;
  }
  set study_id(val) {
    this._studyId = val;
  }

  get status() {
    return this._status;
  }
  set status(val) {
    this._status = val;
  }

  get stratFactors() {
    return this._stratFactors;
  }
  set stratFactors(val) {
    this._stratFactors = val;
  }
  get strat_factors() {
    return this._stratFactors;
  }
  set strat_factors(val) {
    this._stratFactors = val;
  }

  get isUnblinded() {
    return this._isUnblinded;
  }
  set isUnblinded(val) {
    this._isUnblinded = val;
  }
  get is_unblinded() {
    return this._isUnblinded;
  }
  set is_unblinded(val) {
    this._isUnblinded = val;
  }

  get unblindedBy() {
    return this._unblindedBy;
  }
  set unblindedBy(val) {
    this._unblindedBy = val;
  }
  get unblinded_by() {
    return this._unblindedBy;
  }
  set unblinded_by(val) {
    this._unblindedBy = val;
  }

  get unblindedReason() {
    return this._unblindedReason;
  }
  set unblindedReason(val) {
    this._unblindedReason = val;
  }
  get unblinded_reason() {
    return this._unblindedReason;
  }
  set unblinded_reason(val) {
    this._unblindedReason = val;
  }

  get unblindedAt() {
    return this._unblindedAt;
  }
  set unblindedAt(val) {
    this._unblindedAt = val;
  }
  get unblinded_at() {
    return this._unblindedAt;
  }
  set unblinded_at(val) {
    this._unblindedAt = val;
  }

  get randomizationId() {
    return this._randomizationId;
  }
  set randomizationId(val) {
    this._randomizationId = val;
  }
  get randomization_id() {
    return this._randomizationId;
  }
  set randomization_id(val) {
    this._randomizationId = val;
  }

  get kitReference() {
    return this._kitReference;
  }
  set kitReference(val) {
    this._kitReference = val;
  }
  get kit_reference() {
    return this._kitReference;
  }
  set kit_reference(val) {
    this._kitReference = val;
  }

  get withdrawalReason() {
    return this._withdrawalReason;
  }
  set withdrawalReason(val) {
    this._withdrawalReason = val;
  }
  get withdrawal_reason() {
    return this._withdrawalReason;
  }
  set withdrawal_reason(val) {
    this._withdrawalReason = val;
  }

  get withdrawnAt() {
    return this._withdrawnAt;
  }
  set withdrawnAt(val) {
    this._withdrawnAt = val;
  }
  get withdrawn_at() {
    return this._withdrawnAt;
  }
  set withdrawn_at(val) {
    this._withdrawnAt = val;
  }

  get siteId() {
    return this._siteId;
  }
  set siteId(val) {
    this._siteId = val;
  }
  get site_id() {
    return this._siteId;
  }
  set site_id(val) {
    this._siteId = val;
  }

  get reasonForChange() {
    return this._reasonForChange;
  }
  set reasonForChange(val) {
    this._reasonForChange = val;
  }
  get reason_for_change() {
    return this._reasonForChange;
  }
  set reason_for_change(val) {
    this._reasonForChange = val;
  }

  randomize(
    randomizationId: string,
    kitReference: string,
    stratFactors?: Record<string, any>
  ): void {
    this.status = "RANDOMIZED";
    this.randomizationId = randomizationId;
    this.kitReference = kitReference;
    if (stratFactors !== undefined) {
      this.stratFactors = stratFactors;
    }
  }

  unblind(unblindedBy: string, reason: string): void {
    this.status = "UNBLINDED";
    this.isUnblinded = true;
    this.unblindedBy = unblindedBy;
    this.unblindedReason = reason;
    this.unblindedAt = new Date();
  }

  withdraw(reason: string): void {
    this.status = "WITHDRAWN";
    this.withdrawalReason = reason;
    this.withdrawnAt = new Date();
  }

  clone(): ClinicalSubject {
    return new ClinicalSubject({
      id: this.id,
      subjectId: this.subjectId,
      studyId: this.studyId,
      status: this.status,
      stratFactors: JSON.parse(JSON.stringify(this.stratFactors)),
      isUnblinded: this.isUnblinded,
      unblindedBy: this.unblindedBy,
      unblindedReason: this.unblindedReason,
      unblindedAt: this.unblindedAt,
      randomizationId: this.randomizationId,
      kitReference: this.kitReference,
      withdrawalReason: this.withdrawalReason,
      withdrawnAt: this.withdrawnAt,
      siteId: this.siteId,
      reasonForChange: this.reasonForChange,
    });
  }
}
