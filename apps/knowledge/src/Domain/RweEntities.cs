using System;

namespace Cadence.Apps.Knowledge.Domain;

public class RweCohortProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CohortCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TherapeuticArea { get; set; } = string.Empty;
    public string Indication { get; set; } = string.Empty;
    public string BaselineDemographicsJson { get; set; } = "{}";
    public int TotalPatientCount { get; set; } = 5000;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class RwePatientRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CohortProfileId { get; set; } = string.Empty;
    public string PatientIdentifier { get; set; } = string.Empty;
    public double Age { get; set; }
    public string Sex { get; set; } = "U";
    public double Bmi { get; set; }
    public double SystolicBp { get; set; }
    public double DiastolicBp { get; set; }
    public double BiomarkerValue { get; set; }
    public string ComorbiditiesJson { get; set; } = "[]";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
