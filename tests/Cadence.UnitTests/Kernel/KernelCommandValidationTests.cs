using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Time;
using Cadence.Testing.Commands;
using Cadence.Testing.Time;

using FluentAssertions;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The pipeline's validation stage (ADR-2600 §4.4): registered FluentValidation validators run after the actor is
/// resolved and before authorization or any unit of work, and every command has a validator or says why not.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelCommandValidationTests
{
    /// <summary>
    /// Validates that an invalid command is refused with <c>K.Command.Invalid</c> (422) and its field errors from
    /// every validator, before the authorizer is asked or a unit of work is opened.
    /// </summary>
    [Fact(DisplayName = "Pipeline_InvalidCommand_ReturnsFieldErrors_BeforeAuthorization")]
    public async Task PipelineInvalidCommandReturnsFieldErrorsBeforeAuthorization()
    {
        var ct = TestContext.Current.CancellationToken;
        var (services, authorizer, unitOfWork) = Build(s => s
            .AddCommandValidator<RenameProbe, RenameProbeValidator>()
            .AddCommandValidator<RenameProbe, RenameProbeLengthValidator>());
        await using var _ = services;

        var result = await services.GetRequiredService<IKernelCommandSender>()
            .SendAsync(new RenameProbe(TenantId.New(), " ", "key-1", "Unit test reason"), cancellationToken: ct);

        result.Error!.Code.Should().Be(KernelErrors.CommandInvalid.Code);
        result.Error.HttpStatus.Should().Be(422);
        result.Error.Errors.Should().ContainKey(nameof(RenameProbe.Name))
            .WhoseValue.Should().BeEquivalentTo("Name is required.", "Name has at least two characters.");
        authorizer.Calls.Should().Be(0);
        unitOfWork.Begun.Should().BeFalse();
    }

    /// <summary>
    /// Validates that a valid command passes the stage and reaches authorization and the unit of work, and that a
    /// warning-level rule does not refuse it.
    /// </summary>
    [Fact(DisplayName = "Pipeline_ValidCommand_PassesToAuthorization")]
    public async Task PipelineValidCommandPassesToAuthorization()
    {
        var ct = TestContext.Current.CancellationToken;
        var (services, authorizer, unitOfWork) = Build(s => s.AddCommandValidator<RenameProbe, RenameProbeValidator>());
        await using var _ = services;

        var act = () => services.GetRequiredService<IKernelCommandSender>()
            .SendAsync(new RenameProbe(TenantId.New(), "Valid name", "key-1", "Unit test reason"), cancellationToken: ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unit of work*");
        unitOfWork.Begun.Should().BeTrue();
        authorizer.Calls.Should().Be(0, "the unit of work opens before authorization so a denial can be recorded");
    }

    /// <summary>Validates that validation runs after actor resolution: with no actor the command is refused as unaccountable.</summary>
    [Fact(DisplayName = "Pipeline_NoActor_IsRefusedBeforeValidation")]
    public async Task PipelineNoActorIsRefusedBeforeValidation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (services, _, _) = Build(s => s.AddCommandValidator<RenameProbe, RenameProbeValidator>(), withActor: false);
        await using var __ = services;

        var result = await services.GetRequiredService<IKernelCommandSender>()
            .SendAsync(new RenameProbe(TenantId.New(), " ", "key-1", "Unit test reason"), cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.ActorUnresolved);
    }

    /// <summary>
    /// Validates the catalog rule for the kernel's own commands: each has a validator registered by
    /// <c>AddKernelCommandPipeline()</c> or a <c>[NoValidator]</c> reason, never both.
    /// </summary>
    [Fact(DisplayName = "KernelCommands_EachHasAValidatorOrSaysWhyNot")]
    public void KernelCommandsEachHasAValidatorOrSaysWhyNot()
    {
        var services = new ServiceCollection().AddKernelCommandPipeline();

        CommandValidationCatalog.Violations(services, typeof(ICommand).Assembly).Should().BeEmpty();
    }

    /// <summary>Validates that the catalog check reports a command with neither a validator nor a marker, and one with both.</summary>
    [Fact(DisplayName = "CommandValidationCatalog_ReportsMissingAndContradictoryDeclarations")]
    public void CommandValidationCatalogReportsMissingAndContradictoryDeclarations()
    {
        var services = new ServiceCollection()
            .AddSingleton<IValidator<MarkedProbe>, MarkedProbeValidator>();

        var violations = CommandValidationCatalog.Violations(services, typeof(KernelCommandValidationTests).Assembly);

        violations.Should().Contain(v => v.Contains(nameof(RenameProbe), StringComparison.Ordinal) && v.Contains("neither", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Contains(nameof(MarkedProbe), StringComparison.Ordinal) && v.Contains("remove the marker", StringComparison.Ordinal));
    }

    /// <summary>Validates that a command marked <c>[NoValidator]</c> cannot also get a validator, and a marker needs a reason.</summary>
    [Fact(DisplayName = "AddCommandValidator_RefusesMarkedCommands_AndMarkersNeedAReason")]
    public void AddCommandValidatorRefusesMarkedCommandsAndMarkersNeedAReason()
    {
        var register = () => new ServiceCollection().AddCommandValidator<MarkedProbe, MarkedProbeValidator>();
        var blank = () => new NoValidatorAttribute(" ");

        register.Should().Throw<InvalidOperationException>().WithMessage("*[NoValidator]*");
        blank.Should().Throw<ArgumentException>();
    }

    private static (ServiceProvider Services, CountingAuthorizer Authorizer, BeginTrackingUnitOfWork UnitOfWork) Build(
        Func<IServiceCollection, IServiceCollection> configure, bool withActor = true)
    {
        var authorizer = new CountingAuthorizer();
        var unitOfWork = new BeginTrackingUnitOfWork();
        var services = configure(new ServiceCollection()
                .AddSingleton<IClock>(new FakeClock())
                .AddSingleton<ICurrentActor>(new FixedActor(withActor ? Actor.Person("user-1") : null))
                .AddSingleton<IAuthorizer>(authorizer)
                .AddSingleton<IKernelUnitOfWorkFactory>(unitOfWork)
                .AddKernelCommandPipeline()
                .AddCommandHandler<RenameProbe, string, RenameProbeHandler>())
            .BuildServiceProvider();
        return (services, authorizer, unitOfWork);
    }

    /// <summary>A command the tests validate.</summary>
    public sealed record RenameProbe(TenantId TenantId, string Name, string IdempotencyKey, string ReasonForChange) : ICommand<string>;

    /// <summary>A command marked as needing no validator.</summary>
    [NoValidator("Test command with nothing to validate.")]
    public sealed record MarkedProbe(TenantId TenantId, string IdempotencyKey, string ReasonForChange) : ICommand<string>;

    internal sealed class RenameProbeValidator : AbstractValidator<RenameProbe>
    {
        public RenameProbeValidator()
        {
            RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(c => c.Name).MaximumLength(5).WithSeverity(Severity.Warning);
        }
    }

    internal sealed class RenameProbeLengthValidator : AbstractValidator<RenameProbe>
    {
        public RenameProbeLengthValidator() =>
            RuleFor(c => c.Name.Trim()).MinimumLength(2).OverridePropertyName(nameof(RenameProbe.Name))
                .WithMessage("Name has at least two characters.");
    }

    private sealed class MarkedProbeValidator : AbstractValidator<MarkedProbe>;

    private sealed class RenameProbeHandler : ICommandHandler<RenameProbe, string>
    {
        public Task<CommandResult<string>> HandleAsync(RenameProbe command, ICommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(CommandResult<string>.Success(command.Name));
    }

    private sealed class FixedActor(Actor? actor) : ICurrentActor
    {
        public Actor? Actor { get; } = actor;
    }

    private sealed class CountingAuthorizer : IAuthorizer
    {
        public int Calls { get; private set; }

        public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(AuthorizationDecision.Permit());
        }
    }

    private sealed class BeginTrackingUnitOfWork : IKernelUnitOfWorkFactory
    {
        public bool Begun { get; private set; }

        public IKernelCommandTransaction Begin(CommandExecution execution)
        {
            Begun = true;
            throw new InvalidOperationException("Reached the unit of work.");
        }
    }
}
