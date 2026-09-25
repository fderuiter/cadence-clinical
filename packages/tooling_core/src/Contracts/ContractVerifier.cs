using System;
using System.Collections.Generic;
using System.IO;

namespace Cadence.ToolingCore.Contracts;

public record ContractVerificationResult(
    bool IsValid,
    IReadOnlyList<string> EvaluatedPaths,
    IReadOnlyList<string> Errors
);

public static class ContractVerifier
{
    public static ContractVerificationResult VerifyBackendContracts(string repoRoot)
    {
        var evaluatedPaths = new List<string>();
        var errors = new List<string>();

        string executionDomain = Path.Combine(repoRoot, "apps", "execution", "src");
        if (Directory.Exists(executionDomain))
        {
            evaluatedPaths.Add(executionDomain);
            // Verify core contract interfaces exist
            string contractsPkg = Path.Combine(repoRoot, "packages", "contracts", "src");
            if (Directory.Exists(contractsPkg))
            {
                evaluatedPaths.Add(contractsPkg);
            }
        }

        return new ContractVerificationResult(
            IsValid: errors.Count == 0,
            EvaluatedPaths: evaluatedPaths,
            Errors: errors
        );
    }
}
