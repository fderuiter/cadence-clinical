using System;
using System.Collections.Generic;

using Cadence.Ddf;
using Cadence.Kernel.Protocol.Design;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ddf;

public class PatientMigrationEngineTests
{
    [Fact]
    public void ExecutePatientMigration_RetrospectiveAddWithMissingValue_RaisesBackfillQuery()
    {
        var matrix = new PatientMigrationMatrix(
            ProtocolId: "STUDY-001",
            FromVersionTag: "1.0.0",
            ToVersionTag: "2.0.0",
            Directives:
            [
                new MigrationDirective(
                    VariableId: "VS_TEMP",
                    Action: "ADD",
                    MigrationMode: MigrationMode.Retrospective,
                    BackfillQueryMessage: "Retrospective temperature required for protocol amendment 2.0.0"
                )
            ]
        );

        var patientData = new Dictionary<string, object?>
        {
            ["VS_SBP"] = 120,
            ["VS_DBP"] = 80
        };

        var result = PatientMigrationEngine.ExecutePatientMigration(matrix, patientData, isRetrospective: true);

        result.Should().NotBeNull();
        result.IsCompliant.Should().BeFalse();
        result.QueriesRaised.Should().ContainSingle();
        result.QueriesRaised[0].Should().Contain("VS_TEMP");
        result.QueriesRaised[0].Should().Contain("Retrospective temperature required");
    }

    [Fact]
    public void ExecutePatientMigration_RemoveDirective_ArchivesFieldForPart11Audit()
    {
        var matrix = new PatientMigrationMatrix(
            ProtocolId: "STUDY-001",
            FromVersionTag: "1.0.0",
            ToVersionTag: "2.0.0",
            Directives:
            [
                new MigrationDirective(
                    VariableId: "RETIRED_FIELD",
                    Action: "REMOVE",
                    MigrationMode: MigrationMode.Prospective
                )
            ]
        );

        var patientData = new Dictionary<string, object?>
        {
            ["VS_SBP"] = 120,
            ["RETIRED_FIELD"] = "Legacy Clinical Data"
        };

        var result = PatientMigrationEngine.ExecutePatientMigration(matrix, patientData);

        result.IsCompliant.Should().BeTrue();
        result.PatientData.Should().NotContainKey("RETIRED_FIELD");
        result.PatientData.Should().ContainKey("_archived_RETIRED_FIELD");
        result.PatientData["_archived_RETIRED_FIELD"].Should().Be("Legacy Clinical Data");
    }

    [Fact]
    public void BuildMigrationMatrix_FromSoaDiff_GeneratesDirectives()
    {
        var diff = new SoaAmendmentDiff(
            Directives:
            [
                new SoaDirective(SoaDirectiveKind.ActivityAdded, "ACT-NEW", ["ACT-NEW"], []),
                new SoaDirective(SoaDirectiveKind.ActivityRemoved, "ACT-OLD", ["ACT-OLD"], []),
                new SoaDirective(SoaDirectiveKind.VisitMoved, "VISIT-02", ["VISIT-02"], [])
            ],
            ImpactedElements: ["ACT-NEW", "ACT-OLD", "VISIT-02"],
            ImpactedConcepts: [],
            ChangesSchedule: true,
            ChangesEligibility: false
        );

        var matrix = PatientMigrationEngine.BuildMigrationMatrix(
            protocolId: "STUDY-001",
            fromVersionTag: "1.0.0",
            toVersionTag: "2.0.0",
            diff: diff
        );

        matrix.Should().NotBeNull();
        matrix.Directives.Should().HaveCount(3);
        matrix.Directives[0].Action.Should().Be("ADD");
        matrix.Directives[1].Action.Should().Be("REMOVE");
        matrix.Directives[2].Action.Should().Be("MODIFY_CONSTRAINT");
    }
}
