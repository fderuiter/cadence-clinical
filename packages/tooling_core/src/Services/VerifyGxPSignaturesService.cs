using System;

namespace Cadence.ToolingCore.Services;

public static class VerifyGxPSignaturesService
{
    public static int VerifySignatures(string? repoRoot = null)
    {
        Console.WriteLine("[ERROR] GxP signature verification is not implemented yet; no signatures were checked.");
        return 1;
    }
}
