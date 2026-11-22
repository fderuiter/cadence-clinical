using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;
using Cadence.Testing.Commands;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The kernel's errors are a closed set of registered codes, and every kernel endpoint that can answer with one
/// declares it in its OpenAPI contract (K-ERR-1, #5366).
/// </summary>
public sealed class KernelErrorContractTests
{
    /// <summary>
    /// POST endpoints that run no kernel service and so answer with no kernel error: they parse their body and compute
    /// in memory, and a bad body is a plain 400.
    /// </summary>
    private static readonly HashSet<string> ComputationOnly = new(StringComparer.Ordinal)
    {
        "AiVerifyLiveness",
        "KernelGenerateProtocolProposal",
        "KernelMatrixGapCheck",
        "RouteAiIntent",
        "AiVerifyLiveness",
    };

    private static KernelErrorCatalog KernelCatalog() => KernelErrorCatalog.From(
    [
        typeof(KernelErrors).Assembly,
        typeof(RuleErrors).Assembly,
        typeof(KernelApplicationServiceCollectionExtensions).Assembly,

        // Modules composed into the kernel host publish their routes in the kernel contract (ADR-2601).
        typeof(Cadence.Modules.Intelligence.Domain.IntelligenceErrors).Assembly,
        typeof(Cadence.Modules.Execution.Application.FormErrors).Assembly,
    ]);

    /// <summary>
    /// Validates that every error registered in the kernel's assemblies, the rules engine's included, has a unique
    /// well-formed code and a 4xx or 5xx status.
    /// </summary>
    [Fact(DisplayName = "KernelErrorCatalog_KernelAssemblies_AreUniqueAndWellFormed")]
    public void KernelErrorCatalogKernelAssembliesAreUniqueAndWellFormed()
    {
        var catalog = KernelCatalog();

        catalog.Violations.Should().BeEmpty();
        catalog.Errors.Should().Contain([KernelErrors.CommandInvalid, ObservationErrors.Locked, RuleErrors.PackUnknown, IndicatorErrors.SetUnknown]);
    }

    /// <summary>Validates that the catalog reports a malformed code, a non-error status and a code registered twice.</summary>
    [Fact(DisplayName = "KernelErrorCatalog_ReportsMalformedAndDuplicateRegistrations")]
    public void KernelErrorCatalogReportsMalformedAndDuplicateRegistrations()
    {
        var catalog = KernelErrorCatalog.FromTypes([typeof(BadErrors), typeof(ClashingErrors)]);

        catalog.Violations.Should().HaveCount(3);
        catalog.Violations.Should().Contain(v => v.Contains("'lowercase.code'", StringComparison.Ordinal));
        catalog.Violations.Should().Contain(v => v.Contains("maps to 200", StringComparison.Ordinal));
        catalog.Violations.Should().Contain(v => v.Contains("Probe.Errors.Twice is registered more than once", StringComparison.Ordinal));
        catalog.Contains(BadErrors.Fine).Should().BeTrue();
        catalog.Contains(BadErrors.Fine with { HttpStatus = 409 }).Should().BeFalse("a code is registered with one status");
    }

    /// <summary>
    /// Validates that a command endpoint declares the pipeline's errors and those its command declares, with a problem
    /// response for each of their statuses, and that one code declared with two statuses is refused.
    /// </summary>
    [Fact(DisplayName = "ProducesKernelCommand_DeclaresPipelineAndCommandErrors")]
    public void ProducesKernelCommandDeclaresPipelineAndCommandErrors()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapPost("/probe", () => TypedResults.Ok()).ProducesKernelCommand<CorrectObservation>();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).Single();
        var declared = endpoint.Metadata.GetRequiredMetadata<KernelErrorsMetadata>().Errors;

        declared.Should().Contain(KernelErrors.CommandPipeline).And.Contain([ObservationErrors.Locked, ObservationErrors.VersionStale, KernelErrors.NotFound]);
        declared.Should().NotContain(AssertionErrors.AlreadyLocked, "only ApplyLock raises it");
        declared.Select(e => e.HttpStatus).Should().BeInAscendingOrder();
        endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode)
            .Should().Contain([401, 403, 404, 409, 422, 423]);

        var undeclared = () => app.MapPost("/undeclared", () => TypedResults.Ok()).ProducesKernelCommand<ProvisionTenant>();
        undeclared.Should().Throw<InvalidOperationException>().WithMessage("ProvisionTenant declares no errors*");
        var clash = () => app.MapPost("/clash", () => TypedResults.Ok())
            .ProducesKernelErrors(ObservationErrors.Locked, ObservationErrors.Locked with { HttpStatus = 409 });
        clash.Should().Throw<ArgumentException>().WithMessage("*K.Observation.Locked is declared with two statuses*");
    }

    /// <summary>
    /// Validates that every kernel command's error declarations name registered errors, and that the observation
    /// commands, which the kernel API maps, declare theirs.
    /// </summary>
    [Fact(DisplayName = "RaisesKernelErrors_KernelCommands_NameRegisteredErrors")]
    public void RaisesKernelErrorsKernelCommandsNameRegisteredErrors()
    {
        var catalog = KernelCatalog();
        var commands = typeof(KernelErrors).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ICommand).IsAssignableFrom(t))
            .ToList();

        foreach (var command in commands)
        {
            RaisesKernelErrorsAttribute.Of(command)?.Should().OnlyContain(e => catalog.Contains(e), $"{command.Name} declares registered errors only");
        }

        RaisesKernelErrorsAttribute.Of(typeof(RecordObservation)).Should().NotBeNull();
        RaisesKernelErrorsAttribute.Of(typeof(CorrectObservation)).Should().NotBeNull();
        RaisesKernelErrorsAttribute.Of(typeof(ApplyLock)).Should().Equal(
            KernelErrors.AlreadyExists,
            AssertionErrors.ScopeInvalid,
            AssertionErrors.ScopeUnknown,
            AssertionErrors.AlreadyLocked,
            AssertionErrors.AttestationRejected,
            TrustErrors.PersonRequired,
            TrustErrors.MethodRequired,
            TrustErrors.AuthenticationStale);
        var misnamed = () => RaisesKernelErrorsAttribute.Of(typeof(MisdeclaredCommand));
        misnamed.Should().Throw<InvalidOperationException>().WithMessage("*ObservationErrors.NoSuchError*");
    }

    /// <summary>
    /// Validates the test sender's rule: a command that declares its errors may return those and the pipeline's own,
    /// nothing else; a command without declarations is not checked.
    /// </summary>
    [Fact(DisplayName = "DeclaredKernelErrors_RefusesAnUndeclaredError")]
    public void DeclaredKernelErrorsRefusesAnUndeclaredError()
    {
        DeclaredKernelErrors.Undeclared(typeof(ApplyLock), AssertionErrors.AlreadyLocked).Should().BeNull();
        DeclaredKernelErrors.Undeclared(typeof(ApplyLock), KernelErrors.AuthorizationDenied).Should().BeNull();
        DeclaredKernelErrors.Undeclared(typeof(ApplyLock), KernelErrors.HandlerMissing).Should().BeNull();
        DeclaredKernelErrors.Undeclared(typeof(ApplyLock), ObservationErrors.VersionStale.WithMessage("Stale."))
            .Should().Be("ApplyLock returned K.Observation.VersionStale (409), which it does not declare with [RaisesKernelErrors].");
        DeclaredKernelErrors.Undeclared(typeof(ProvisionTenant), ObservationErrors.VersionStale).Should().BeNull();
    }

    /// <summary>Validates that a kernel error becomes problem details with its status, its code and its field errors.</summary>
    [Fact(DisplayName = "KernelErrorEndpoints_Problem_CarriesCodeStatusAndFieldErrors")]
    public void KernelErrorEndpointsProblemCarriesCodeStatusAndFieldErrors()
    {
        var invalid = KernelErrors.CommandInvalid with { Errors = new Dictionary<string, string[]> { ["Value"] = ["Required."] } };

        ProblemHttpResult problem = KernelErrorEndpoints.Problem(invalid);
        ProblemHttpResult locked = KernelErrorEndpoints.Problem(ObservationErrors.Locked);

        problem.StatusCode.Should().Be(422);
        problem.ProblemDetails.Title.Should().Be("K.Command.Invalid");
        problem.ProblemDetails.Extensions["code"].Should().Be("K.Command.Invalid");
        problem.ProblemDetails.Extensions["errors"].Should().BeSameAs(invalid.Errors);
        locked.StatusCode.Should().Be(423);
        locked.ProblemDetails.Extensions.Should().NotContainKey("errors");
    }

    /// <summary>
    /// Validates the published kernel contract (<c>docs/openapi/kernel_openapi.json</c>, kept current by the
    /// <c>openapi</c> gate): every kernel endpoint that takes a body lists its kernel errors, each one registered with the
    /// same status, and each status has a problem response that names its codes.
    /// </summary>
    [Fact(DisplayName = "KernelOpenApi_EveryMutatingEndpoint_DeclaresRegisteredErrors")]
    public void KernelOpenApiEveryMutatingEndpointDeclaresRegisteredErrors()
    {
        var catalog = KernelCatalog();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "openapi", "kernel_openapi.json")));
        var checkedOperations = 0;

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(o => o.Name is "post" or "put" or "patch" or "delete"))
            {
                var id = operation.Value.GetProperty("operationId").GetString()!;
                if (ComputationOnly.Contains(id))
                {
                    continue;
                }

                operation.Value.TryGetProperty(KernelErrorEndpoints.OpenApiExtension, out var errors)
                    .Should().BeTrue($"{id} ({path.Name}) must declare its kernel errors");
                var responses = operation.Value.GetProperty("responses");
                foreach (var error in errors.EnumerateArray())
                {
                    var declared = new KernelError(error.GetProperty("code").GetString()!, error.GetProperty("status").GetInt32(), string.Empty);
                    catalog.Contains(declared).Should().BeTrue($"{id} declares {declared.Code} ({declared.HttpStatus}), which must be registered");
                    responses.TryGetProperty(declared.HttpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture), out var response)
                        .Should().BeTrue($"{id} declares a {declared.HttpStatus} error");
                    response.GetProperty("description").GetString().Should().Contain(declared.Code);
                }

                checkedOperations++;
            }
        }

        checkedOperations.Should().BeGreaterThanOrEqualTo(3, "record, correct and the simulation are kernel endpoints with kernel errors");
    }

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Cadence.sln not found above the test output directory.");
    }

    [RaisesKernelErrors(typeof(ObservationErrors), "NoSuchError")]
    internal sealed class MisdeclaredCommand;

    internal static class BadErrors
    {
        public static readonly KernelError Fine = new("Probe.Errors.Fine", 404, "Fine.");
        public static readonly KernelError Lowercase = new("lowercase.code", 422, "Malformed.");
        public static readonly KernelError NotAnError = new("Probe.Errors.Ok", 200, "Not an error status.");
        public static readonly KernelError Twice = new("Probe.Errors.Twice", 409, "Registered twice.");
    }

    internal static class ClashingErrors
    {
        public static readonly KernelError Twice = new("Probe.Errors.Twice", 409, "Registered twice.");
    }
}
