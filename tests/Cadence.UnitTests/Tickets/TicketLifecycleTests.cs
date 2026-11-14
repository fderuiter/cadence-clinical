using System;
using System.Linq;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Apps.Tickets.Infrastructure.Persistence.Configurations;
using Cadence.Database;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Tickets;

public class TicketLifecycleTests
{
    private static Ticket CreateTicket()
    {
        return new Ticket
        {
            Reference = "TCK-2026-0001",
            Title = "Investigational Product Temperature Excursion",
            Description = "Refrigeration unit at Site 101 recorded temperature of 9C for 30 minutes.",
            CreatedAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "cra.monitor@cadence.io",
            ReasonForChange = "Initial issue logging"
        };
    }

    [Fact]
    public void DefaultCreation_InitializesStatusToOpen()
    {
        var ticket = CreateTicket();

        ticket.Status.Should().Be(TicketStatus.OPEN);
        ticket.ResolutionCode.Should().BeNull();
        ticket.RootCauseSummary.Should().BeNull();
    }

    [Fact]
    public void TransitionTo_SameState_ReturnsSuccessAsSilentNoOp()
    {
        var ticket = CreateTicket();

        var result = ticket.TransitionTo(TicketStatus.OPEN);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TicketStatus.OPEN);
        ticket.Status.Should().Be(TicketStatus.OPEN);
    }

    [Fact]
    public void TransitionTo_ValidProgressions_UpdatesStatus()
    {
        var ticket = CreateTicket();

        // OPEN -> IN_PROGRESS
        var r1 = ticket.TransitionTo(TicketStatus.IN_PROGRESS);
        r1.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);

        // IN_PROGRESS -> WAITING_ON_SITE
        var r2 = ticket.TransitionTo(TicketStatus.WAITING_ON_SITE);
        r2.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.WAITING_ON_SITE);

        // WAITING_ON_SITE -> WAITING_ON_SPONSOR
        var r3 = ticket.TransitionTo(TicketStatus.WAITING_ON_SPONSOR);
        r3.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.WAITING_ON_SPONSOR);

        // WAITING_ON_SPONSOR -> IN_PROGRESS
        var r4 = ticket.TransitionTo(TicketStatus.IN_PROGRESS);
        r4.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);

        // IN_PROGRESS -> PENDING_REGULATORY_REVIEW
        var r5 = ticket.TransitionTo(TicketStatus.PENDING_REGULATORY_REVIEW);
        r5.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.PENDING_REGULATORY_REVIEW);

        // PENDING_REGULATORY_REVIEW -> IN_PROGRESS
        var r6 = ticket.TransitionTo(TicketStatus.IN_PROGRESS);
        r6.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);
    }

    [Fact]
    public void TransitionTo_IllegalTransitions_ReturnsValidationErrorAndPreservesStatus()
    {
        var ticket = CreateTicket();

        // OPEN -> REOPENED is not allowed
        var r1 = ticket.TransitionTo(TicketStatus.REOPENED);
        r1.IsFailure.Should().BeTrue();
        r1.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        ticket.Status.Should().Be(TicketStatus.OPEN);

        // Advance to IN_PROGRESS
        ticket.TransitionTo(TicketStatus.IN_PROGRESS).IsSuccess.Should().BeTrue();

        // IN_PROGRESS -> CLOSED is not in transition table
        var r2 = ticket.TransitionTo(TicketStatus.CLOSED, resolutionCode: "RESOLVED_AS_EXPECTED");
        r2.IsFailure.Should().BeTrue();
        r2.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);
    }

    [Fact]
    public void TransitionTo_Resolved_EnforcesResolutionInvariants()
    {
        var ticket = CreateTicket();
        ticket.TransitionTo(TicketStatus.IN_PROGRESS).IsSuccess.Should().BeTrue();

        // Attempting RESOLVED without resolution details fails
        var rMissingAll = ticket.TransitionTo(TicketStatus.RESOLVED);
        rMissingAll.IsFailure.Should().BeTrue();
        rMissingAll.Error!.Code.Should().Be("RESOLUTION_CODE_REQUIRED");
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);

        // Attempting with only whitespace resolution code fails
        var rWhitespaceCode = ticket.TransitionTo(
            TicketStatus.RESOLVED,
            resolutionCode: "   ",
            rootCauseSummary: "Detailed root cause");
        rWhitespaceCode.IsFailure.Should().BeTrue();
        rWhitespaceCode.Error!.Code.Should().Be("RESOLUTION_CODE_REQUIRED");

        // Attempting without root cause summary fails
        var rMissingSummary = ticket.TransitionTo(
            TicketStatus.RESOLVED,
            resolutionCode: "CAPA_INITIATED");
        rMissingSummary.IsFailure.Should().BeTrue();
        rMissingSummary.Error!.Code.Should().Be("ROOT_CAUSE_REQUIRED");

        // Attempting with whitespace root cause summary fails
        var rWhitespaceSummary = ticket.TransitionTo(
            TicketStatus.RESOLVED,
            resolutionCode: "CAPA_INITIATED",
            rootCauseSummary: "   ");
        rWhitespaceSummary.IsFailure.Should().BeTrue();
        rWhitespaceSummary.Error!.Code.Should().Be("ROOT_CAUSE_REQUIRED");

        // Supplying both succeeds
        var rSuccess = ticket.TransitionTo(
            TicketStatus.RESOLVED,
            resolutionCode: "CAPA_INITIATED",
            rootCauseSummary: "Excursion caused by faulty seal replaced by site contractor.",
            rootCauseCategory: "INVESTIGATIONAL_PRODUCT");
        rSuccess.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.RESOLVED);
        ticket.ResolutionCode.Should().Be("CAPA_INITIATED");
        ticket.RootCauseSummary.Should().Be("Excursion caused by faulty seal replaced by site contractor.");
        ticket.RootCauseCategory.Should().Be("INVESTIGATIONAL_PRODUCT");
    }

    [Fact]
    public void Resolve_Method_SetsResolvedStatusAndDetails()
    {
        var ticket = CreateTicket();
        ticket.TransitionTo(TicketStatus.IN_PROGRESS).IsSuccess.Should().BeTrue();

        var result = ticket.Resolve(
            resolutionCode: "WORKAROUND_APPLIED",
            rootCauseSummary: "Backup sensor activated and verified within threshold.",
            rootCauseCategory: "SYSTEM_TECHNICAL");

        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.RESOLVED);
        ticket.ResolutionCode.Should().Be("WORKAROUND_APPLIED");
        ticket.RootCauseSummary.Should().Be("Backup sensor activated and verified within threshold.");
        ticket.RootCauseCategory.Should().Be("SYSTEM_TECHNICAL");
    }

    [Fact]
    public void TransitionTo_TerminalStates_RequiresResolutionCode()
    {
        var ticket = CreateTicket();

        // OPEN -> CLOSED without resolution code fails
        var rClosedNoCode = ticket.TransitionTo(TicketStatus.CLOSED);
        rClosedNoCode.IsFailure.Should().BeTrue();
        rClosedNoCode.Error!.Code.Should().Be("RESOLUTION_CODE_REQUIRED");
        ticket.Status.Should().Be(TicketStatus.OPEN);

        // OPEN -> CANCELLED without resolution code fails
        var rCancelledNoCode = ticket.TransitionTo(TicketStatus.CANCELLED);
        rCancelledNoCode.IsFailure.Should().BeTrue();
        rCancelledNoCode.Error!.Code.Should().Be("RESOLUTION_CODE_REQUIRED");
        ticket.Status.Should().Be(TicketStatus.OPEN);

        // OPEN -> CANCELLED with resolution code succeeds
        var rCancelledWithCode = ticket.TransitionTo(
            TicketStatus.CANCELLED,
            resolutionCode: "FALSE_POSITIVE_VOIDED");
        rCancelledWithCode.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.CANCELLED);
        ticket.ResolutionCode.Should().Be("FALSE_POSITIVE_VOIDED");
    }

    [Fact]
    public void TransitionTo_Closed_SucceedsWhenResolutionCodeAlreadyPresent()
    {
        var ticket = CreateTicket();
        ticket.TransitionTo(TicketStatus.IN_PROGRESS).IsSuccess.Should().BeTrue();
        ticket.Resolve("RESOLVED_AS_EXPECTED", "Issue inspected and verified").IsSuccess.Should().BeTrue();

        // RESOLVED -> CLOSED inherits existing ResolutionCode
        var rClosed = ticket.TransitionTo(TicketStatus.CLOSED);
        rClosed.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.CLOSED);
        ticket.ResolutionCode.Should().Be("RESOLVED_AS_EXPECTED");
    }

    [Fact]
    public void TerminalStates_CanTransitionToReopenedOnly()
    {
        var ticket = CreateTicket();
        ticket.TransitionTo(TicketStatus.CANCELLED, resolutionCode: "FALSE_POSITIVE_VOIDED").IsSuccess.Should().BeTrue();

        // CANCELLED -> IN_PROGRESS is forbidden
        var rIllegal = ticket.TransitionTo(TicketStatus.IN_PROGRESS);
        rIllegal.IsFailure.Should().BeTrue();
        rIllegal.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");

        // CANCELLED -> REOPENED is allowed
        var rReopened = ticket.TransitionTo(TicketStatus.REOPENED);
        rReopened.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.REOPENED);

        // REOPENED -> IN_PROGRESS is allowed
        var rInProgress = ticket.TransitionTo(TicketStatus.IN_PROGRESS);
        rInProgress.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);
    }

    [Fact]
    public void TransitionTo_StringOverload_ParsesAndValidates()
    {
        var ticket = CreateTicket();

        // Case-insensitive valid string
        var rValid = ticket.TransitionTo("in_progress");
        rValid.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.IN_PROGRESS);

        // Invalid status string
        var rInvalid = ticket.TransitionTo("NOT_A_STATUS");
        rInvalid.IsFailure.Should().BeTrue();
        rInvalid.Error!.Code.Should().Be("INVALID_STATUS");
    }

    [Fact]
    public void TicketTransitionGraph_CanTransition_EvaluatesCorrectly()
    {
        TicketTransitionGraph.CanTransition(TicketStatus.OPEN, TicketStatus.OPEN).Should().BeTrue();
        TicketTransitionGraph.CanTransition(TicketStatus.OPEN, TicketStatus.IN_PROGRESS).Should().BeTrue();
        TicketTransitionGraph.CanTransition(TicketStatus.OPEN, TicketStatus.REOPENED).Should().BeFalse();
        TicketTransitionGraph.CanTransition(TicketStatus.CLOSED, TicketStatus.REOPENED).Should().BeTrue();
        TicketTransitionGraph.CanTransition(TicketStatus.CLOSED, TicketStatus.OPEN).Should().BeFalse();
    }

    [Fact]
    public void TicketConfiguration_ConfiguresStatusCheckConstraintAndConversion()
    {
        using var context = new TicketsDbContextFactory()
            .CreateDbContext(["--connection", "Host=localhost;Database=cadence_test"]);
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var entityType = designModel.FindEntityType(typeof(Ticket))!;

        var checkConstraints = entityType.GetCheckConstraints();
        var statusConstraint = checkConstraints.FirstOrDefault(c => c.Name == "ck_tickets_status");
        statusConstraint.Should().NotBeNull();
        statusConstraint!.Sql.Should().Contain("\"Status\" IN ('OPEN', 'IN_PROGRESS'");

        var runtimeEntityType = context.Model.FindEntityType(typeof(Ticket))!;
        var statusProperty = runtimeEntityType.FindProperty(nameof(Ticket.Status))!;
        statusProperty.ClrType.Should().Be(typeof(TicketStatus));
        statusProperty.GetMaxLength().Should().Be(50);
        statusProperty.IsNullable.Should().BeFalse();
    }
}
