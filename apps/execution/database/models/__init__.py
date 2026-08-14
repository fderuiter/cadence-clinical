import importlib

# Define the submodules mapping for all 49 clinical models, enums, and base classes
_submodule_mappings = {
    # audit
    "Base": "audit",
    "AuditLog": "audit",
    "AuditLedgerSeal": "audit",
    "AuditedModel": "audit",
    # subject
    "ClinicalSubject": "subject",
    "SubjectConsent": "subject",
    "InformedConsentRecord": "subject",
    "SiteStaffMember": "subject",
    "DOADelegationRecord": "subject",
    "DOAAuditLog": "subject",
    # consent
    "ConsentFormRecord": "consent",
    "ConsentSignature": "consent",
    "ComprehensionQuizResult": "consent",
    # visit
    "ClinicalVisit": "visit",
    # observation
    "ClinicalObservation": "observation",
    # query
    "ClinicalQuery": "query",
    "QueryStatus": "query",
    # sdv
    "SDVSignOff": "sdv",
    "TSDVConfig": "sdv",
    "SDVStatus": "sdv",
    # coding
    "DictionaryType": "coding",
    "ImportState": "coding",
    "CodingState": "coding",
    "RecodingState": "coding",
    "MedDRATerm": "coding",
    "MedDRAHierarchy": "coding",
    "WHODrugRecord": "coding",
    "WHODrugIngredient": "coding",
    "WHODrugATC": "coding",
    "WHODrugDrugATC": "coding",
    "WHODrugDrugIngredient": "coding",
    "DictionaryImportJob": "coding",
    "ClinicalCodingAssignment": "coding",
    "ClinicalCodingLedger": "coding",
    # lab
    "LabReferenceRange": "lab",
    "LabTestMasterLegacy": "lab",
    "LabUnitConversion": "lab",
    "LabTestMaster": "lab",
    # form
    "FormSubmission": "form",
    "FormSubmissionStatus": "form",
    # rtsm
    "RandomizationConfig": "rtsm",
    "StratumState": "rtsm",
    "SubjectRandomization": "rtsm",
    "AllocationKeyMetadata": "rtsm",
    "PendingPredecessorCheck": "rtsm",
    "IPKit": "rtsm",
    "SiteInventory": "rtsm",
    "KitDispensation": "rtsm",
    "ResupplyEvent": "rtsm",
    # biostat
    "BiostatExport": "biostat",
    "SDTMDomainRecord": "biostat",
    # designer
    "TranslationJob": "designer",
    "StudyAuthoredRule": "designer",
    # migration
    "MigrationRule": "migration",
    # compliance
    "ComplianceChangeRequest": "compliance",
    "ChangeApprovalSignature": "compliance",
    "SiteComplianceCache": "compliance",
    # sync
    "SyncedBatchIdempotencyKey": "sync",
    "ProcessedOfflineBatch": "sync",
    # outbox
    "IntegrationOutbox": "outbox",
    # lock
    "DataLock": "lock",
    "ScopeTypeEnum": "lock",
    "LockTypeEnum": "lock",
}


def discover_models():
    """Programmatically load all domain submodules to ensure they are registered in metadata."""
    for submodule in sorted(set(_submodule_mappings.values())):
        importlib.import_module(f".{submodule}", __name__)


def __getattr__(name):
    if name in _submodule_mappings:
        # If someone accesses Base, load all models so metadata discovery works seamlessly
        if name == "Base":
            discover_models()
        submodule_name = _submodule_mappings[name]
        mod = importlib.import_module(f".{submodule_name}", __name__)
        return getattr(mod, name)
    raise AttributeError(f"module {__name__} has no attribute {name}")


def __dir__():
    return list(_submodule_mappings.keys()) + ["discover_models"]
