using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ReadmeValidatorServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "readme-validator-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ValidateReadmes_IgnoresADirectoryHoldingOnlyBuildOutput()
    {
        Write("packages/documented/README.md", "# Documented\n");
        Write("packages/deleted/src/bin/Debug/net10.0/Old.dll", "");
        Write("packages/deleted/src/obj/project.assets.json", "{}");

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(0);
    }

    [Fact]
    public void ValidateReadmes_FailsForAProjectWithoutAReadme()
    {
        Write("packages/documented/README.md", "# Documented\n");
        Write("packages/undocumented/src/Thing.cs", "namespace X;");
        Write("packages/undocumented/src/bin/Debug/Thing.dll", "");

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(1);
    }

    [Fact]
    public void ValidateReadmes_VerifiesServiceRunLocallyStartupExports()
    {
        Write("apps/payments/src/Cadence.Apps.Payments.csproj", "<Project />");
        Write(
            "apps/payments/src/Program.cs",
            """
            builder.Services.AddCadenceSecurity(builder.Configuration);
            builder.Services.AddCadenceFieldEncryption(builder.Configuration);
            builder.AddCadencePostgresDbContext<PaymentsDbContext>();
            """);
        Write(
            "apps/payments/README.md",
            """
            # Payments service

            ## Run locally

            ```bash
            export GATEWAY_SECRET="<at least 32 characters>"
            export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
            dotnet run --project apps/payments/src/Cadence.Apps.Payments.csproj
            ```
            """);

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(1);

        Write(
            "apps/payments/README.md",
            """
            # Payments service

            ## Run locally

            ```bash
            export GATEWAY_SECRET="<at least 32 characters>"
            export FIELD_ENCRYPTION_KEY="$(openssl rand -base64 32)"
            export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
            dotnet run --project apps/payments/src/Cadence.Apps.Payments.csproj
            ```
            """);

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(0);
    }

    [Fact]
    public void ValidateReadmes_OnCurrentRepository_ReturnsZero()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Cadence.sln")))
        {
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        ReadmeValidatorService.ValidateReadmes(current).Should().Be(0);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
