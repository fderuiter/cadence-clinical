using System;
using System.Collections.Generic;

using Cadence.Core.Domain.Supply;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/supply/hierarchy.py</c> (#5046). The error messages are
/// the ones the Python reference raised for the same networks.
/// </summary>
public class MultiEchelonHierarchyEngineTests
{
    private static MultiEchelonNode Node(
        string id,
        SupplyNodeType type,
        string? parent = null,
        int onHand = 0,
        int reserved = 0,
        int inTransit = 0,
        int capacity = 1000) =>
        new(id, "STUDY-001", type, id, ParentNodeId: parent, StorageCapacity: capacity, OnHandQty: onHand, ReservedQty: reserved, InTransitQty: inTransit);

    private static List<MultiEchelonNode> Network() =>
    [
        Node("MFG", SupplyNodeType.PRIMARY_MANUFACTURER, onHand: 5000),
        Node("CD-US", SupplyNodeType.CENTRAL_DEPOT, "MFG", onHand: 1200, reserved: 100, inTransit: 300),
        Node("RD-EAST", SupplyNodeType.REGIONAL_DEPOT, "CD-US", onHand: 400, reserved: 50, inTransit: 60),
        Node("SITE-01", SupplyNodeType.CLINICAL_SITE, "RD-EAST", onHand: 24, reserved: 4, inTransit: 10),
        Node("SITE-02", SupplyNodeType.CLINICAL_SITE, "CD-US", onHand: 16, reserved: 2),
    ];

    [Fact]
    public void ComputeRollup_TotalsEveryNode_AndReportsEachTier()
    {
        var nodes = Network();

        var rollup = MultiEchelonHierarchyEngine.ComputeRollup("STUDY-001", nodes);

        rollup.StudyId.Should().Be("STUDY-001");
        rollup.TotalNodes.Should().Be(5);
        rollup.TotalOnHand.Should().Be(6640);
        rollup.TotalReserved.Should().Be(156);
        rollup.TotalInTransit.Should().Be(370);
        rollup.TierCounts.Should().Equal(new Dictionary<string, int>
        {
            ["PRIMARY_MANUFACTURER"] = 1,
            ["CENTRAL_DEPOT"] = 1,
            ["REGIONAL_DEPOT"] = 1,
            ["CLINICAL_SITE"] = 2,
        });
        rollup.TierOnHand.Should().Equal(new Dictionary<string, int>
        {
            ["PRIMARY_MANUFACTURER"] = 5000,
            ["CENTRAL_DEPOT"] = 1200,
            ["REGIONAL_DEPOT"] = 400,
            ["CLINICAL_SITE"] = 40,
        });
        rollup.Nodes.Should().Equal(nodes);
    }

    [Fact]
    public void ComputeRollup_ReportsZeroForTiersWithNoNodes_AndAcceptsAnEmptyNetwork()
    {
        var rollup = MultiEchelonHierarchyEngine.ComputeRollup("STUDY-001", []);

        rollup.TotalNodes.Should().Be(0);
        rollup.TotalOnHand.Should().Be(0);
        rollup.TierCounts.Should().HaveCount(4).And.OnlyContain(t => t.Value == 0);
        rollup.TierOnHand.Should().HaveCount(4).And.OnlyContain(t => t.Value == 0);
    }

    [Fact]
    public void ValidateHierarchy_AcceptsAParentInTheSameTier_AndSeveralRoots()
    {
        var nodes = new List<MultiEchelonNode>
        {
            Node("MFG-A", SupplyNodeType.PRIMARY_MANUFACTURER),
            Node("MFG-B", SupplyNodeType.PRIMARY_MANUFACTURER, "MFG-A"),
            Node("CD-EU", SupplyNodeType.CENTRAL_DEPOT),
            Node("SITE-01", SupplyNodeType.CLINICAL_SITE, "MFG-B"),
            Node("SITE-02", SupplyNodeType.CLINICAL_SITE, "SITE-01"),
        };

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().NotThrow();
    }

    [Fact]
    public void ValidateHierarchy_RejectsADuplicateNodeId()
    {
        var nodes = Network();
        nodes.Add(Node("SITE-01", SupplyNodeType.CLINICAL_SITE, "CD-US"));

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().Throw<InvalidHierarchyException>().WithMessage("Duplicate supply node_id: 'SITE-01'");
    }

    [Fact]
    public void ValidateHierarchy_RejectsAParentThatDoesNotExist()
    {
        var nodes = Network();
        nodes.Add(Node("SITE-03", SupplyNodeType.CLINICAL_SITE, "RD-WEST"));

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().Throw<InvalidHierarchyException>()
            .WithMessage("Supply node 'SITE-03' references non-existent parent 'RD-WEST'.");
    }

    [Theory]
    [InlineData(SupplyNodeType.PRIMARY_MANUFACTURER, SupplyNodeType.CENTRAL_DEPOT)]
    [InlineData(SupplyNodeType.CENTRAL_DEPOT, SupplyNodeType.REGIONAL_DEPOT)]
    [InlineData(SupplyNodeType.REGIONAL_DEPOT, SupplyNodeType.CLINICAL_SITE)]
    [InlineData(SupplyNodeType.PRIMARY_MANUFACTURER, SupplyNodeType.CLINICAL_SITE)]
    public void ValidateHierarchy_RejectsAParentInALowerTier(SupplyNodeType child, SupplyNodeType parent)
    {
        var nodes = new List<MultiEchelonNode> { Node("P", parent), Node("C", child, "P") };

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().Throw<InvalidHierarchyException>()
            .WithMessage($"Invalid echelon relationship: '{child}' node 'C' cannot have parent '{parent}' node 'P'.");
    }

    [Fact]
    public void ValidateHierarchy_RejectsACycle_NamingTheNodeWhoseParentClosesIt()
    {
        var nodes = new List<MultiEchelonNode>
        {
            Node("CD-A", SupplyNodeType.CENTRAL_DEPOT, "CD-C"),
            Node("CD-B", SupplyNodeType.CENTRAL_DEPOT, "CD-A"),
            Node("CD-C", SupplyNodeType.CENTRAL_DEPOT, "CD-B"),
        };

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().Throw<InvalidHierarchyException>()
            .WithMessage("Cycle detected in supply hierarchy involving node 'CD-B' and parent 'CD-A'.");
    }

    [Fact]
    public void ValidateHierarchy_RejectsANodeThatIsItsOwnParent()
    {
        var nodes = new List<MultiEchelonNode> { Node("SITE-01", SupplyNodeType.CLINICAL_SITE, "SITE-01") };

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().Throw<InvalidHierarchyException>()
            .WithMessage("Cycle detected in supply hierarchy involving node 'SITE-01' and parent 'SITE-01'.");
    }

    [Fact]
    public void ValidateHierarchy_DoesNotReportACycle_WhenTwoBranchesShareAnAncestor()
    {
        var nodes = new List<MultiEchelonNode>
        {
            Node("SITE-01", SupplyNodeType.CLINICAL_SITE, "RD"),
            Node("SITE-02", SupplyNodeType.CLINICAL_SITE, "RD"),
            Node("RD", SupplyNodeType.REGIONAL_DEPOT, "CD"),
            Node("CD", SupplyNodeType.CENTRAL_DEPOT),
        };

        var validate = () => MultiEchelonHierarchyEngine.ValidateHierarchy(nodes);

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, -1, 0, 0)]
    [InlineData(10, 0, -1, 0)]
    [InlineData(10, 0, 0, -1)]
    public void ComputeRollup_RejectsTheQuantitiesThePythonModelRejected(int capacity, int onHand, int reserved, int inTransit)
    {
        var nodes = new List<MultiEchelonNode>
        {
            Node("SITE-01", SupplyNodeType.CLINICAL_SITE, capacity: capacity, onHand: onHand, reserved: reserved, inTransit: inTransit),
        };

        var rollup = () => MultiEchelonHierarchyEngine.ComputeRollup("STUDY-001", nodes);

        rollup.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*SITE-01*");
    }
}
