namespace Cadence.Cli.Services;

public static class GxpRtmValidatorService
{
    public static bool ValidateGxpRtm()
    {
        return Cadence.Compliance.RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true) == 0;
    }
}
