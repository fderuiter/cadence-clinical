using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Providers;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>Supplier records (ADR-2636 §4.5) and the in-deployment endpoint rule (§4.1).</summary>
public sealed class SupplierAndEndpointTests
{
    /// <summary>Every provider's supplier record ships, as a draft with its assessment questions open.</summary>
    [Fact]
    public void ShippedSupplierRecords_AreDraftsWithOpenQuestions()
    {
        var catalog = Catalog([]);

        catalog.All.Select(r => r.Id).Should().BeEquivalentTo(
            SupplierIds.Anthropic, SupplierIds.AnthropicOnAws, SupplierIds.AwsBedrock,
            SupplierIds.GoogleVertex, SupplierIds.MicrosoftFoundry, SupplierIds.OperatorHosted);
        catalog.All.Should().OnlyContain(r => r.Status == SupplierStatus.Draft && r.AssessmentQuestions.Count > 0 && r.StatedCertifications.Count == 0);
    }

    /// <summary>A deployment approves a supplier only with who assessed it, when, and the next review.</summary>
    [Fact]
    public void ApprovalOverlay_NeedsItsAssessment()
    {
        var approved = Catalog(new()
        {
            ["Intelligence:Inference:Suppliers:aws-bedrock:Status"] = "Approved",
            ["Intelligence:Inference:Suppliers:aws-bedrock:AssessedBy"] = "QA",
            ["Intelligence:Inference:Suppliers:aws-bedrock:AssessedOn"] = "2026-10-01",
            ["Intelligence:Inference:Suppliers:aws-bedrock:NextReviewOn"] = "2027-04-01",
        }).Find(SupplierIds.AwsBedrock)!;

        approved.Status.Should().Be(SupplierStatus.Approved);
        approved.IsReviewOverdue(new DateOnly(2027, 4, 2)).Should().BeTrue();
        var unassessed = () => Catalog(new() { ["Intelligence:Inference:Suppliers:aws-bedrock:Status"] = "Approved" });
        unassessed.Should().Throw<InvalidOperationException>().WithMessage("*AssessedBy*");
        var unknown = () => Catalog(new() { ["Intelligence:Inference:Suppliers:acme:Status"] = "Approved" });
        unknown.Should().Throw<InvalidOperationException>().WithMessage("*acme*");
    }

    /// <summary>Only loopback, private and cluster-internal endpoints count as inside the deployment.</summary>
    [Theory]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://127.0.0.1:8000/v1", true)]
    [InlineData("http://[::1]:8000/v1", true)]
    [InlineData("http://10.4.0.12:8000/v1", true)]
    [InlineData("http://172.20.1.1/v1", true)]
    [InlineData("http://192.168.10.5/v1", true)]
    [InlineData("http://[fd12:3456::1]/v1", true)]
    [InlineData("http://vllm:8000/v1", true)]
    [InlineData("http://vllm.ai.svc.cluster.local:8000/v1", true)]
    [InlineData("https://llm.corp.internal/v1", true)]
    [InlineData("https://models.example.com/v1", false)]
    [InlineData("http://8.8.8.8/v1", false)]
    [InlineData("http://172.32.0.1/v1", false)]
    [InlineData("http://[2001:db8::1]/v1", false)]
    [InlineData("https://user:secret@localhost/v1", false)] // pragma: allowlist secret
    [InlineData("ftp://localhost/v1", false)]
    public void InDeploymentEndpoint_AcceptsOnlyInternalAddresses(string endpoint, bool inDeployment) =>
        (InDeploymentEndpoint.Problem(new Uri(endpoint)) is null).Should().Be(inDeployment);

    private static ISupplierCatalog Catalog(Dictionary<string, string?> config)
    {
        var services = new ServiceCollection();
        services.AddIntelligenceInference(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), new HostingEnvironment { EnvironmentName = Environments.Production });
        return services.BuildServiceProvider().GetRequiredService<ISupplierCatalog>();
    }
}
