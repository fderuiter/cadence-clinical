using System;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Models;
using Cadence.Apps.Designer.Persistence;
using Cadence.Apps.Designer.Services;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Designer;

public class EcrfCollaborationSessionTests
{
    private readonly EcrfSessionManager _sessionManager;
    private readonly FakeClock _clock;

    public EcrfCollaborationSessionTests()
    {
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        _sessionManager = new EcrfSessionManager(NullLogger<EcrfSessionManager>.Instance, _clock);
    }

    [Fact]
    public void ApplyUpdate_AppendsBinaryUpdateVectorsAndCombinesState()
    {
        // Arrange
        const string formId = "FORM-DEMO-1";
        byte[] chunk1 = Encoding.UTF8.GetBytes("CRDT_CHUNK_1");
        byte[] chunk2 = Encoding.UTF8.GetBytes("CRDT_CHUNK_2");

        // Act
        _sessionManager.ApplyUpdate(formId, chunk1);
        _sessionManager.ApplyUpdate(formId, chunk2);

        // Assert
        var chunks = _sessionManager.GetUpdateChunks(formId);
        chunks.Should().HaveCount(2);

        byte[] combined = _sessionManager.GetCombinedStateVector(formId);
        string combinedStr = Encoding.UTF8.GetString(combined);
        combinedStr.Should().Be("CRDT_CHUNK_1CRDT_CHUNK_2");
    }

    [Fact]
    public void UpdateAwareness_TracksActiveUserPresenceAndRoleBadges()
    {
        // Arrange
        const string formId = "FORM-DEMO-2";
        var user1 = new AwarenessState("conn-1", "Alice", "Data Manager", "#ff0000", 120, 240, "sec-1", "field-1", 1000);
        var user2 = new AwarenessState("conn-2", "Bob", "Biostatistician", "#00ff00", 200, 300, "sec-1", "field-2", 1005);

        // Act
        _sessionManager.UpdateAwareness(formId, "conn-1", user1);
        _sessionManager.UpdateAwareness(formId, "conn-2", user2);

        // Assert
        var activeUsers = _sessionManager.GetActiveUsers(formId);
        activeUsers.Should().HaveCount(2);
        activeUsers.Should().Contain(u => u.UserName == "Alice" && u.UserRole == "Data Manager");
        activeUsers.Should().Contain(u => u.UserName == "Bob" && u.UserRole == "Biostatistician");

        // Remove user
        _sessionManager.RemoveUser(formId, "conn-1");
        _sessionManager.GetActiveUsers(formId).Should().HaveCount(1);
    }

    [Fact]
    public async Task CompileUsdmAsync_ExecutesAsynchronouslyAndValidatesSchema()
    {
        // Arrange
        const string formId = "FORM-COMPILE-1";
        const string validUsdm = """
            {"study":{"id":"S-101","name":"Valid Study"},"items":[{"id":"AGE","name":"Age","type":"integer"}]}
            """;

        // Act
        var result = await _sessionManager.CompileUsdmAsync(formId, validUsdm);

        // Assert
        result.Should().NotBeNull();
        result!.StudyId.Should().Be("S-101");
    }

    [Fact]
    public async Task SaveSnapshotAsync_SavesCrdtVectorAndJsonSchemaToDatabase()
    {
        // Arrange
        const string formId = "FORM-SNAPSHOT-1";
        const string usdm = """
            {"study":{"id":"S-102","name":"Snapshot Study"},"items":[{"id":"HEIGHT","name":"Height","type":"decimal"}]}
            """;

        byte[] updateData = Encoding.UTF8.GetBytes("BINARY_CRDT_STATE_VECTOR");
        _sessionManager.ApplyUpdate(formId, updateData);

        var options = new DbContextOptionsBuilder<DesignerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new DesignerDbContext(options);

        // Act
        var saveResult = await _sessionManager.SaveSnapshotAsync(formId, usdm, dbContext, _clock);

        // Assert
        saveResult.Success.Should().BeTrue();
        saveResult.FormId.Should().Be(formId);

        var savedEntity = await dbContext.FormSnapshots.FindAsync([formId], TestContext.Current.CancellationToken);
        savedEntity.Should().NotBeNull();
        savedEntity!.UsdmJson.Should().Be(usdm);
        savedEntity.CrdtStateVector.Should().Equal(updateData);
        savedEntity.CompilationStatus.Should().Be("Valid");
    }
}
