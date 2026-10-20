using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using FluentAssertions;

using Xunit;

[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Scope = "namespace",
    Target = "~N:Cadence.UnitTests.Execution.Module",
    Justification = "The namespace mirrors the Cadence.Modules.Execution module it tests; a test assembly has no Visual Basic consumers.")]

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// The Execution module's form rules that need no database (ADR-2622): the layout a form opens with, how a form read
/// derives its order and completeness from the kernel record, and the application role grant.
/// </summary>
public sealed class ExecutionFormModuleTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A layout that repeats an item, or names an empty or overlong one, is not a form layout.</summary>
    [Theory(DisplayName = "FormInstance_Open_RefusesALayoutThatIsNotOne")]
    [InlineData("sbp", "sbp")]
    [InlineData("sbp", " ")]
    public void FormInstanceOpenRefusesALayoutThatIsNotOne(string first, string second)
    {
        var open = () => Open([first, second]);
        var overlong = () => Open([new string('x', FormInstance.ElementIdMaxLength + 1)]);

        open.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
        Open(["sbp", "dbp"]).ItemOrder.Should().Equal("sbp", "dbp");
    }

    /// <summary>
    /// A form read lists the items in the module's layout, then any item the layout does not name; a required item is
    /// missing until it has a value, and a reasoned missing value counts as one.
    /// </summary>
    [Fact(DisplayName = "FormResponse_FollowsTheLayout_AndDerivesCompleteness")]
    public void FormResponseFollowsTheLayoutAndDerivesCompleteness()
    {
        var record = Record(
            [Item("sbp", required: true), Item("dbp", required: true), Item("comment"), Item("pulse", required: true)],
            Value("dbp", new MissingValue(NullFlavor.NotDone, "Cuff unavailable")));

        var response = FormResponse.From(record, ["comment", "dbp", "sbp"]);

        response.Items.Select(i => i.ItemId).Should().Equal("comment", "dbp", "sbp", "pulse");
        response.MissingRequired.Should().Equal("sbp", "pulse");
        response.Complete.Should().BeFalse();
        response.Items.Single(i => i.ItemId == "dbp").Revision.Should().Be(1);
        response.Items.Single(i => i.ItemId == "sbp").Revision.Should().Be(0);

        var complete = FormResponse.From(Record([Item("dbp", required: true)], Value("dbp", new MissingValue(NullFlavor.NotDone))), null);
        complete.Complete.Should().BeTrue();
        complete.MissingRequired.Should().BeEmpty();
    }

    /// <summary>
    /// The grant gives the role read, insert and update on the module's schema, delete only on the used signature tokens,
    /// and refuses a name that is not a role.
    /// </summary>
    [Fact(DisplayName = "ExecutionModuleDatabase_GrantAppRoleSql_GrantsTheModuleSchema_AndRefusesBadNames")]
    public void ExecutionModuleDatabaseGrantAppRoleSqlGrantsTheModuleSchemaAndRefusesBadNames()
    {
        var sql = ExecutionModuleDatabase.GrantAppRoleSql("cadence_app");
        var injected = () => ExecutionModuleDatabase.GrantAppRoleSql("app\"; DROP SCHEMA execution; --");

        sql.Should().Contain("GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA \"execution\" TO \"cadence_app\"")
            .And.Contain("REVOKE ALL ON \"execution\".\"__ef_migrations_history\" FROM \"cadence_app\"")
            .And.Contain("GRANT DELETE ON \"execution\".\"consumed_signature_tokens\" TO \"cadence_app\"");
        sql.Split('\n').Where(line => line.Contains("DELETE", StringComparison.Ordinal)).Should().ContainSingle();
        injected.Should().Throw<ArgumentException>();
    }

    private static FormInstance Open(string[] layout) =>
        FormInstance.Open(
            CollectionInstanceId.New(), TenantId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, layout);

    private static CollectionItem Item(string id, bool required = false) => new(id, ConceptRef.New(), Required: required);

    private static CollectedValueView Value(string item, ObservationValue value) =>
        new(item, ObservationId.New(), ConceptRef.New(), value, At, 1, "crc@example.test", At);

    private static CollectionInstanceRecord Record(CollectionItem[] items, params CollectedValueView[] values) =>
        new(
            CollectionInstanceId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, "crc@example.test", At, 1 + values.Length, items, values, [], [], [], null);
}
