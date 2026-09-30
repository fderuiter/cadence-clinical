using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Persistence;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Interop;

/// <summary>
/// #4770: interop stores device tokens only as keyed HMAC lookup hashes; the raw token never reaches the database.
/// </summary>
public sealed class DeviceTokenAtRestTests : IDisposable
{
    private const string RawDeviceToken = "cgm-device-token-7f3a9c21e4b8";

    private static readonly string[] DeviceTokenTables =
    [
        "device_registrations",
        "sensor_telemetry_streams",
        "sensor_daily_summaries",
        "sensor_safety_alerts",
    ];

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DeviceTokenHasher _hasher = new(TestFieldProtection.CreateLookupHasher());

    public DeviceTokenAtRestTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Every device-token column holds the 64-character HMAC of the token (raw SQL read), no column of those rows
    /// contains the raw token, and the hash finds the device's rows in all four tables.
    /// </summary>
    [Fact]
    public async Task DeviceToken_IsStoredOnlyAsKeyedHash_AndIsFoundByHash()
    {
        var ct = TestContext.Current.CancellationToken;
        string tokenHash = _hasher.Hash(RawDeviceToken);
        await using (var write = CreateContext())
        {
            write.DeviceRegistrationRecords.Add(new DeviceRegistrationRecord
            {
                StudyId = "STUDY-1",
                SubjectId = "SUBJ-101",
                DeviceType = "CGM",
                Vendor = "Vendor",
                ModelName = "G7",
                UdiDi = "00312345678901",
                DeviceTokenHash = tokenHash,
                CreatedBy = "interop-test",
                CreatedAt = FakeClock.DefaultStart,
            });
            write.SensorTelemetryStreamRecords.Add(new SensorTelemetryStreamRecord
            {
                StudyId = "STUDY-1",
                SubjectId = "SUBJ-101",
                DeviceType = "CGM",
                BatchId = "B-1",
                ReceivedAt = FakeClock.DefaultStart,
                CreatedAt = FakeClock.DefaultStart,
                RawDataHash = new string('a', 64),
                DeviceTokenHash = tokenHash,
            });
            write.SensorDailySummaryRecords.Add(new SensorDailySummaryRecord
            {
                StudyId = "STUDY-1",
                SubjectId = "SUBJ-101",
                DeviceType = "CGM",
                SummaryDate = FakeClock.DefaultStart.UtcDateTime.Date,
                Sha256Hash = new string('b', 64),
                CreatedAt = FakeClock.DefaultStart,
                DeviceTokenHash = tokenHash,
            });
            write.SensorSafetyAlertRecords.Add(new SensorSafetyAlertRecord
            {
                AlertId = "ALERT-1",
                StudyId = "STUDY-1",
                SubjectId = "SUBJ-101",
                DeviceType = "CGM",
                AlertType = "HYPO",
                Severity = "HIGH",
                ParameterName = "glucose",
                Unit = "mg/dL",
                DeviceTokenHash = tokenHash,
                CreatedAt = FakeClock.DefaultStart,
            });
            await write.SaveChangesAsync(ct);
        }

        foreach (string table in DeviceTokenTables)
        {
            var rows = await StoredRowReader.ReadAllAsync(_connection, table, ct);
            rows.Should().ContainSingle($"{table} should hold the seeded row");

            var columns = rows[0];
            columns.Should().NotContainKey("DeviceToken", $"{table} must not have a raw token column");
            columns["DeviceTokenHash"].Should().Be(tokenHash).And.MatchRegex("^[0-9a-f]{64}$");
            columns.Values.Should().NotContain(value => value != null && value.Contains(RawDeviceToken), $"{table} must not store the raw token");
        }

        await using var read = CreateContext();
        string presented = _hasher.Hash(RawDeviceToken);
        (await read.DeviceRegistrationRecords.AsNoTracking().CountAsync(x => x.DeviceTokenHash == presented, ct)).Should().Be(1);
        (await read.SensorTelemetryStreamRecords.AsNoTracking().CountAsync(x => x.DeviceTokenHash == presented, ct)).Should().Be(1);
        (await read.SensorDailySummaryRecords.AsNoTracking().CountAsync(x => x.DeviceTokenHash == presented, ct)).Should().Be(1);
        (await read.SensorSafetyAlertRecords.AsNoTracking().CountAsync(x => x.DeviceTokenHash == presented, ct)).Should().Be(1);
        (await read.DeviceRegistrationRecords.AsNoTracking().CountAsync(x => x.DeviceTokenHash == _hasher.Hash("another-token"), ct)).Should().Be(0);
    }

    /// <summary>
    /// No interop entity keeps a raw device-token property, and each hash column is indexed and sized for a hash.
    /// </summary>
    [Fact]
    public void InteropModel_HasOnlyIndexedDeviceTokenHashColumns()
    {
        using var context = CreateContext();

        foreach (var entity in context.Model.GetEntityTypes())
        {
            entity.GetProperties().Select(p => p.Name).Should().NotContain("DeviceToken", $"{entity.DisplayName()} must not store raw device tokens");
        }

        foreach (var clrType in new[] { typeof(DeviceRegistrationRecord), typeof(SensorTelemetryStreamRecord), typeof(SensorDailySummaryRecord), typeof(SensorSafetyAlertRecord) })
        {
            var property = context.Model.FindEntityType(clrType)!.FindProperty("DeviceTokenHash")!;
            property.GetMaxLength().Should().Be(ILookupHasher.HashLength);
            property.IsNullable.Should().BeFalse();
            property.GetContainingIndexes().Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// The hasher rejects blank tokens rather than hashing them into a shared lookup value.
    /// </summary>
    [Fact]
    public void DeviceTokenHasher_RejectsBlankTokens()
    {
        FluentActions.Invoking(() => _hasher.Hash(" ")).Should().Throw<ArgumentException>();
        _hasher.Hash(RawDeviceToken).Should().NotContain(RawDeviceToken).And.HaveLength(ILookupHasher.HashLength);
    }

    private InteropDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InteropDbContext>().UseSqlite(_connection).Options);
}
