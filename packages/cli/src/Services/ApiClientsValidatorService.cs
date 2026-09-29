namespace Cadence.Cli.Services;

public static class ApiClientsValidatorService
{
    public static bool ValidateApiClients()
    {
        return Cadence.ToolingCore.Services.ApiClientValidatorService.ValidateApiClients() == 0;
    }
}
