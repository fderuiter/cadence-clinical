using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Supply;

[SuppressMessage("Naming", "S101:Types should be named in PascalCase",
    Justification = "DTP (direct-to-patient) is the supply-chain term used in the protocol and IRT specifications.")]
public static class DTPLogisticsEngine
{
    public const double GasConstantR = 0.008314472;
    public const double ActivationEnergyDh = 83.144;

    public static double CalculateMeanKineticTemperature(IReadOnlyList<TemperatureReading> readings)
    {
        if (readings == null || readings.Count == 0)
        {
            throw new ArgumentException("Cannot calculate MKT with empty temperature readings.");
        }

        int n = readings.Count;
        double sumExp = 0.0;

        foreach (double tempCelsius in readings.Select(r => r.TemperatureCelsius))
        {
            double tempKelvin = tempCelsius + 273.15;
            if (tempKelvin <= 0)
            {
                throw new ArgumentException($"Invalid temperature reading: {tempCelsius} C");
            }
            double exponent = -ActivationEnergyDh / (GasConstantR * tempKelvin);
            sumExp += Math.Exp(exponent);
        }

        double avgExp = sumExp / n;
        double tMktKelvin = (ActivationEnergyDh / GasConstantR) / (-Math.Log(avgExp));
        return Math.Round(tMktKelvin - 273.15, 2);
    }

    public static DTPDispatchAuthorization EvaluateDtpDispatch(
        string studyId,
        string siteId,
        string subjectId,
        string kitNumber,
        string lotNumber,
        int daysUntilExpiry,
        DateTimeOffset authorizedAt,
        bool isQuarantined = false,
        bool isRecalled = false,
        IReadOnlyList<TemperatureReading>? temperatureReadings = null,
        double minAllowedTemp = 2.0,
        double maxAllowedTemp = 8.0,
        int minShelfLifeDays = 14)
    {
        var rejectionReasons = new List<string>();

        if (isQuarantined)
        {
            rejectionReasons.Add($"Kit '{kitNumber}' is under active quality quarantine.");
        }

        if (isRecalled)
        {
            rejectionReasons.Add($"Lot '{lotNumber}' has an active regulatory recall notice.");
        }

        if (daysUntilExpiry < minShelfLifeDays)
        {
            rejectionReasons.Add($"Remaining shelf-life ({daysUntilExpiry} days) is less than the required minimum of {minShelfLifeDays} days for home dispensation.");
        }

        double? calculatedMkt = null;
        if (temperatureReadings != null && temperatureReadings.Count > 0)
        {
            try
            {
                calculatedMkt = CalculateMeanKineticTemperature(temperatureReadings);
                if (calculatedMkt < minAllowedTemp || calculatedMkt > maxAllowedTemp)
                {
                    rejectionReasons.Add($"Mean Kinetic Temperature excursion: calculated MKT {calculatedMkt} C is outside the permitted range [{minAllowedTemp} C, {maxAllowedTemp} C].");
                }
            }
            catch (Exception ex)
            {
                rejectionReasons.Add($"Failed to process temperature telemetry: {ex.Message}");
            }
        }

        bool isEligible = rejectionReasons.Count == 0;
        string dispatchId = $"DTP-{studyId}-{siteId}-{kitNumber}";
        string sealPayload = $"{dispatchId}:{lotNumber}:{isEligible}:{daysUntilExpiry}:{calculatedMkt}";
        byte[] csBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sealPayload));
        string? tamperSealId = isEligible ? $"SEAL-{Convert.ToHexString(csBytes)[..12].ToUpperInvariant()}" : null;

        return new DTPDispatchAuthorization(
            DispatchId: dispatchId,
            StudyId: studyId,
            SiteId: siteId,
            SubjectId: subjectId,
            KitNumber: kitNumber,
            LotNumber: lotNumber,
            IsEligibleForDispatch: isEligible,
            RejectionReasons: rejectionReasons,
            CalculatedMktCelsius: calculatedMkt,
            TamperSealId: tamperSealId,
            AuthorizedTimestamp: authorizedAt.UtcDateTime.ToString("o")
        );
    }
}
