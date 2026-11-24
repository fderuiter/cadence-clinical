using Cadence.Kernel;
using Cadence.Kernel.References;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public sealed class AuditEntryNodeRefTests
{
    [Fact]
    public void AuditEntry_NodeKind_ParsesAndFormatsCanonically()
    {
        var id = new AuditEntryId(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e"));
        var nodeRef = NodeRef.For(id);

        Assert.Contains(NodeKinds.AuditEntry, NodeKinds.All);
        Assert.Equal("audit_entry", nodeRef.Kind);
        Assert.Equal("audit_entry:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", nodeRef.ToString());

        Assert.True(NodeRef.TryParse("audit_entry:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e", out var parsed));
        Assert.Equal(nodeRef, parsed);
    }
}
