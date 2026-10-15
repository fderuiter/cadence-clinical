using System;
using System.Collections.Generic;

namespace Cadence.Core.Synthetic;

public static class MonteCarloCohortSampler
{
    public static List<SyntheticPatientRecord> GenerateCohort(
        int sampleSize,
        double meanAge = 58.5,
        double sdAge = 12.0,
        double maleRatio = 0.52,
        double meanBmi = 27.4,
        double sdBmi = 4.5,
        double meanSystolicBp = 128.0,
        double sdSystolicBp = 14.0,
        double meanBiomarker = 4.2,
        double sdBiomarker = 1.8,
        int seed = 42)
    {
        var random = new Random(seed);
        var records = new List<SyntheticPatientRecord>(sampleSize);

        for (int i = 1; i <= sampleSize; i++)
        {
            double age = Math.Max(18.0, Math.Min(90.0, SampleNormal(random, meanAge, sdAge)));
            string sex = random.NextDouble() < maleRatio ? "M" : "F";
            double bmi = Math.Max(15.0, Math.Min(50.0, SampleNormal(random, meanBmi, sdBmi)));
            double sysBp = Math.Max(80.0, Math.Min(200.0, SampleNormal(random, meanSystolicBp, sdSystolicBp)));
            double diaBp = Math.Max(50.0, Math.Min(120.0, sysBp * 0.65));
            double biomarker = Math.Max(0.1, SampleNormal(random, meanBiomarker, sdBiomarker));

            records.Add(new SyntheticPatientRecord(
                PatientId: $"SYNTH-PAT-{i:D5}",
                Age: Math.Round(age, 1),
                Sex: sex,
                Bmi: Math.Round(bmi, 1),
                SystolicBp: Math.Round(sysBp, 1),
                DiastolicBp: Math.Round(diaBp, 1),
                BiomarkerValue: Math.Round(biomarker, 2),
                PropensityScore: 0.0,
                IsMatched: false,
                MatchedControlId: null,
                AdditionalAttributes: new Dictionary<string, string>
                {
                    ["EHR_Source"] = "RWE-SIMULATED-EHR",
                    ["Deidentified"] = "true"
                }));
        }

        return records;
    }

    private static double SampleNormal(Random rng, double mean, double stdDev)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }
}
