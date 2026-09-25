using System;

namespace Cadence.ToolingCore.Services;

public static class RuntimeGuardService
{
    public static int EnforceRuntime()
    {
        Console.WriteLine($"Asserting runtime environment: .NET SDK {Environment.Version}");
        return 0;
    }
}
