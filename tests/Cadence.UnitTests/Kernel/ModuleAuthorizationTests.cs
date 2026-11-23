using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Module commands are authorized by the kernel like kernel commands (ADR-2601, #5449): a registered module's declared
/// capabilities join the catalogue, and a module command without one is denied.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleAuthorizationTests
{
    private static readonly Actor Coordinator = Actor.Person("00u-coordinator");

    /// <summary>
    /// Validates that a registered module's command is authorized against its declared capability and resource, and
    /// that the same command is denied when its module is not registered.
    /// </summary>
    [Fact(DisplayName = "ModuleCommand_IsAuthorizedAgainstItsCapability_OnlyWhenItsModuleIsRegistered")]
    public async Task ModuleCommandIsAuthorizedAgainstItsCapabilityOnlyWhenItsModuleIsRegistered()
    {
        var ct = TestContext.Current.CancellationToken;
        var study = StudyId.New();
        var command = new AnnotateStudy(TenantId.New(), study, "key-1", "Unit test");
        var registered = new RecordingEvaluator();
        var unregistered = new RecordingEvaluator();

        var permitted = await Authorizer(registered, s => s.AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly))
            .AuthorizeAsync(Coordinator, command, ct);
        var denied = await Authorizer(unregistered, s => s).AuthorizeAsync(Coordinator, command, ct);

        permitted.Permitted.Should().BeTrue();
        registered.Requests.Should().ContainSingle().Which.Should().Match<AuthorizationRequest>(r =>
            r.Capability.Token == "probe_module.annotate" && r.Resource == NodeRef.For(study) && r.Actor == Coordinator);
        denied.Permitted.Should().BeFalse();
        denied.Conditions.Should().Contain(c => c.Code == KernelAuthorization.CapabilityDeclaredCondition);
        unregistered.Requests.Should().BeEmpty();
    }

    /// <summary>Validates that module names are unique and well formed, and that the kernel's assembly is not a module.</summary>
    [Fact(DisplayName = "AddCadenceModule_RefusesDuplicatesBadNamesAndTheKernel")]
    public void AddCadenceModuleRefusesDuplicatesBadNamesAndTheKernel()
    {
        var services = new ServiceCollection().AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly);

        var duplicate = () => services.AddCadenceModule("probe_module", typeof(AssertionExtensions).Assembly);
        var badName = () => new ServiceCollection().AddCadenceModule("Probe", typeof(AnnotateStudy).Assembly);
        var kernel = () => new ServiceCollection().AddCadenceModule("kernel_again", typeof(ICommand).Assembly);

        duplicate.Should().Throw<InvalidOperationException>();
        badName.Should().Throw<ArgumentException>();
        kernel.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates that each registered module declares a unique, well-formed OpenAPI document name (defaulting to its
    /// module name when omitted) and refuses reserved or duplicate OpenAPI document names.
    /// </summary>
    [Fact(DisplayName = "AddCadenceModule_RegistersOpenApiDocument_AndRefusesReservedOrDuplicateDocuments")]
    public void AddCadenceModuleRegistersOpenApiDocumentAndRefusesReservedOrDuplicateDocuments()
    {
        var services = new ServiceCollection()
            .AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly)
            .AddCadenceModule("execution", typeof(AssertionExtensions).Assembly, openApiDocument: "execution_forms");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var registrations = provider.GetServices<CadenceModuleRegistration>();
        registrations.Should().SatisfyRespectively(
            first => first.EffectiveOpenApiDocument.Should().Be("probe_module"),
            second =>
            {
                second.Name.Should().Be("execution");
                second.EffectiveOpenApiDocument.Should().Be("execution_forms");
            });

        var duplicateDoc = () => services.AddCadenceModule("another_module", typeof(NullLogger).Assembly, openApiDocument: "execution_forms");
        var reservedKernel = () => new ServiceCollection().AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly, openApiDocument: "kernel");
        var reservedDefault = () => new ServiceCollection().AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly, openApiDocument: "v1");
        var reservedAggregate = () => new ServiceCollection().AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly, openApiDocument: "cdisc");
        var malformedDoc = () => new ServiceCollection().AddCadenceModule("probe_module", typeof(AnnotateStudy).Assembly, openApiDocument: "ExecutionForms");

        duplicateDoc.Should().Throw<InvalidOperationException>();
        reservedKernel.Should().Throw<ArgumentException>();
        reservedDefault.Should().Throw<ArgumentException>();
        reservedAggregate.Should().Throw<ArgumentException>();
        malformedDoc.Should().Throw<ArgumentException>();
    }

    private static IAuthorizer Authorizer(RecordingEvaluator evaluator, Func<IServiceCollection, IServiceCollection> configure) =>
        configure(new ServiceCollection()
                .AddSingleton<IClock>(new FakeClock())
                .AddSingleton<ICurrentActor>(new NoActor())
                .AddSingleton<IAuthorizationEvaluator>(evaluator)
                .AddSingleton<ILogger<KernelAuthorization>>(NullLogger<KernelAuthorization>.Instance)
                .AddKernelAuthorization())
            .BuildServiceProvider()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<IAuthorizer>();

    /// <summary>A module command, authorized against the study it annotates.</summary>
    [KernelCapability("probe_module.annotate", VerbClass.Execute, Resource = nameof(StudyId))]
    [NoValidator("Test command.")]
    public sealed record AnnotateStudy(TenantId TenantId, StudyId StudyId, string IdempotencyKey, string ReasonForChange) : ICommand<StudyId>;

    private sealed class NoActor : ICurrentActor
    {
        public Actor? Actor => null;
    }

    private sealed class RecordingEvaluator : IAuthorizationEvaluator
    {
        public List<AuthorizationRequest> Requests { get; } = [];

        public ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(AuthorizationDecision.Permit());
        }
    }
}
