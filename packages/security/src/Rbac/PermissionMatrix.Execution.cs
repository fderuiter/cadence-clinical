namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The role the eTMF service's signed caller carries when it posts completeness updates to Execution.</summary>
    public const string EtmfServiceRole = "etmf_service";

    /// <summary>The role of the clinical endpoint committee chair, who signs final adjudications.</summary>
    public const string CecChairRole = "cec_chair";

    /// <summary>
    /// The Execution module's route permissions (ADR-2510, ADR-2516). Route permissions are a coarse gate: the
    /// application services keep their own role checks (<c>SubjectAuthority</c>, SDV, e-signature signer
    /// roles), and no permission here admits a role those checks refuse for every request. Separation of duties
    /// (21 CFR 11.10(g), ICH E6(R3)): site staff enter and sign data, monitors verify it, data managers manage coding and
    /// imports, auditors only read, and only the pharmacist touches blinded supply.
    /// </summary>
    private static partial void AddExecutionGrants()
    {
        AddEdcGrants();
        AddRandomizationGrants();
        AddLaboratoryGrants();
        AddSupplyGrants();
        AddSiteComplianceGrants();
        AddAdjudicationGrants();
    }

    /// <summary>Subjects, eCRF forms, observations, SDV and medical-coding synonyms.</summary>
    private static void AddEdcGrants()
    {
        // Subject and eCRF reads beyond the sponsor, site and CRA roles that already hold them: monitors need them for
        // SDV, auditors and inspectors read everything, the unblinded pharmacist dispenses against a subject, and the
        // unblinded statistician inspects study participant enrollment and unblinded allocation details (ADR-2640).
        Grant("monitor", "subject_enrollment", "read");
        Grant("monitor", "ecrf_data_entry", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "subject_enrollment", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ecrf_data_entry", "read");
        Grant(RoleNormalization.ROLE_PHARMACIST, "subject_enrollment", "read");
        Grant(RoleNormalization.ROLE_UNBLINDED_STATISTICIAN, "subject_enrollment", "read");

        // Signing a form (PI approval, batch sign-off) is the investigator's act; the PI roles inherit it. Unlocking a
        // signed form for correction is a data-management action.
        Grant(RoleNormalization.ROLE_INVESTIGATOR, "ecrf_form", "sign");
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "ecrf_form", "unlock");

        // Source data verification: the service admits CRAs, monitors, external monitors and data managers.
        foreach (string role in new[] { RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_EXTERNAL_MONITOR })
        {
            Grant(role, "sdv", "read", "update", "flag");
        }

        // Synonym dictionaries: imported and deactivated by data managers and study designers (the roles the
        // endpoint's own check admits); read by everyone who reads coding.
        Grant(RoleNormalization.ROLE_SPONSOR_DESIGNER, "medical_coding", "read");
        foreach (string role in new[] { RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_DESIGNER })
        {
            Grant(role, "medical_coding", "manage_synonyms");
        }
    }

    /// <summary>
    /// Randomisation (#5607, ADR-2640). The unblinded statistician drafts and approves scheme versions (the service also
    /// requires the approver to be someone other than the author); data managers and auditors read the configuration,
    /// which holds no list or allocation. Site staff randomise a participant; the response names the allocation and kit,
    /// never the arm.
    /// </summary>
    private static void AddRandomizationGrants()
    {
        Grant(RoleNormalization.ROLE_UNBLINDED_STATISTICIAN, "randomization_scheme", "read", "draft", "approve");
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "randomization_scheme", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "randomization_scheme", "read");
        Grant(RoleNormalization.ROLE_INVESTIGATOR, "subject_enrollment", "randomize");
        Grant(RoleNormalization.ROLE_CRC, "subject_enrollment", "randomize");
        Grant(RoleNormalization.ROLE_TRIAL_COORDINATOR, "subject_enrollment", "randomize");
    }

    /// <summary>Lab reference ranges, bulk range imports and toxicity review.</summary>
    private static void AddLaboratoryGrants()
    {
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "lab_range", "read");

        // Bulk reference-range imports replace reference data for a whole study, so previewing and committing one is a
        // sponsor data-management operation; auditors read the import history.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "lab_range_import", "read", "preview", "commit");
        }

        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "lab_range_import", "read");

        // Grade 3/4 toxicity triggers are staged for the investigator, who confirms, dismisses or escalates them.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_MM,
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_CRA_CANONICAL, "monitor",
            RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "lab_toxicity_review", "read");
        }

        Grant(RoleNormalization.ROLE_INVESTIGATOR, "lab_toxicity_review", "review");
    }

    /// <summary>IP kits, investigational devices and cell and gene therapy chain of custody.</summary>
    private static void AddSupplyGrants()
    {
        // IP kit reconciliation reads and writes blinded supply through rtsm_supply (the pharmacist only). Seeding a
        // site's kit inventory is a sponsor operation.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "ip_kit_inventory", "seed");
        }

        // Investigational device accountability.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_MM,
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_CRA_CANONICAL, "monitor",
            RoleNormalization.ROLE_PHARMACIST, RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "device_accountability", "read");
        }

        // Registering serialized inventory is supply management.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_PHARMACIST })
        {
            Grant(role, "device_accountability", "register");
        }

        // Dispensing to a participant is site work.
        foreach (string role in new[] { RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_PHARMACIST })
        {
            Grant(role, "device_accountability", "dispense");
        }

        // Anyone who handles or oversees a device may report a deficiency.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_PHARMACIST,
            RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_SPONSOR_MM,
        })
        {
            Grant(role, "device_accountability", "report_deficiency");
        }

        // Placing a device in quarantine: the pharmacy, sponsor safety and operations. Releasing one back to use is a
        // sponsor safety decision, never the site's.
        foreach (string role in new[] { RoleNormalization.ROLE_PHARMACIST, RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_SYSADMIN })
        {
            Grant(role, "device_accountability", "quarantine");
        }

        foreach (string role in new[] { RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_SYSADMIN })
        {
            Grant(role, "device_accountability", "release");
        }

        // Cell and gene therapy chain of custody: collection, bedside verification, infusion, returns and destruction
        // are recorded by the site and the pharmacy; sponsor, monitoring and audit roles read the trace.
        foreach (string role in new[] { RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_PHARMACIST })
        {
            Grant(role, "cgt_chain_of_custody", "read", "write");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_MM,
            RoleNormalization.ROLE_TRIAL_COORDINATOR, RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "cgt_chain_of_custody", "read");
        }
    }

    /// <summary>Site activation and the eTMF completeness feed that gates it.</summary>
    private static void AddSiteComplianceGrants()
    {
        // Activating a site is the sponsor's certification, as the CTMS greenlight is.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "site_compliance", "read", "activate");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_TRIAL_COORDINATOR, RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "site_compliance", "read");
        }

        // The eTMF completeness webhook is called service to service. Only the eTMF service's signed identity holds
        // the role, so no person can mark a site's TMF complete and unblock its activation.
        Grant(EtmfServiceRole, "site_compliance", "etmf_sync");
    }

    /// <summary>Clinical endpoint adjudication (CEC).</summary>
    private static void AddAdjudicationGrants()
    {
        // The CEC chair's binding ruling is signed as CEC_CHAIR; nobody else may finalize a dossier.
        Grant(CecChairRole, "cec_adjudication", "read", "adjudicate");
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_MM,
            RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "cec_adjudication", "read");
        }
    }
}
