namespace Cadence.Cli.Services;

public static class ContractsVerifierService
{
    public static bool VerifyContracts()
    {
        return Cadence.ToolingCore.Services.ContractVerifierService.VerifyContracts() == 0;
    }
}
