using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Telemetry;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class LoggerMessageGeneratorTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public void TelemetryLoggerMessages_EmitValidEventIdsAndFormattedMessages()
    {
        _logger.LogTraceparentHeaderInjected("00-trace123-span456-01", "/api/v1/test");
        _logger.LogTelemetryInitialized("Cadence.Execution");
        _logger.LogRequestTracingEvent("GET", "/healthz", "act789");

        _logger.Logs.Should().HaveCount(3);

        _logger.Logs[0].EventId.Id.Should().Be(1001);
        _logger.Logs[0].Level.Should().Be(LogLevel.Debug);
        _logger.Logs[0].Message.Should().Contain("00-trace123-span456-01").And.Contain("/api/v1/test");

        _logger.Logs[1].EventId.Id.Should().Be(1002);
        _logger.Logs[1].Level.Should().Be(LogLevel.Information);
        _logger.Logs[1].Message.Should().Contain("Cadence.Execution");

        _logger.Logs[2].EventId.Id.Should().Be(1003);
        _logger.Logs[2].Level.Should().Be(LogLevel.Trace);
        _logger.Logs[2].Message.Should().Contain("GET").And.Contain("/healthz");
    }

    [Fact]
    public void EConsentConsumerLoggerMessages_EmitValidEventIdsAndFormattedMessages()
    {
        _logger.LogMalformedConsentEventDropped("SUBJ-001", "STUDY-100");
        _logger.LogConsentEventProcessing("SUBJ-002", "STUDY-100");
        _logger.LogSubjectAutoEnrolled("SUBJ-003", "STUDY-100", 5);
        _logger.LogConsentRecordBackfilled("SUBJ-004", "STUDY-100", 2);

        _logger.Logs.Should().HaveCount(4);

        _logger.Logs[0].EventId.Id.Should().Be(2001);
        _logger.Logs[0].Level.Should().Be(LogLevel.Warning);
        _logger.Logs[0].Message.Should().Contain("SUBJ-001");

        _logger.Logs[1].EventId.Id.Should().Be(2002);
        _logger.Logs[1].Level.Should().Be(LogLevel.Information);
        _logger.Logs[1].Message.Should().Contain("SUBJ-002");

        _logger.Logs[2].EventId.Id.Should().Be(2003);
        _logger.Logs[2].Level.Should().Be(LogLevel.Information);
        _logger.Logs[2].Message.Should().Contain("SUBJ-003").And.Contain("5");

        _logger.Logs[3].EventId.Id.Should().Be(2004);
        _logger.Logs[3].Level.Should().Be(LogLevel.Information);
        _logger.Logs[3].Message.Should().Contain("SUBJ-004").And.Contain("2");
    }

    [Fact]
    public void SubjectEndpointsLoggerMessages_EmitValidEventIdsAndFormattedMessages()
    {
        _logger.LogCreatingSubject("SUBJ-101", "STUDY-200");
        _logger.LogCreatedSubject("SUBJ-101", "STUDY-200");
        _logger.LogRecordingConsent("SUBJ-101", "STUDY-200", "v1.0");
        _logger.LogAdvancingSubjectState("SUBJ-101", "ENROLLED", "Consent verified");
        _logger.LogFetchingSubjectDetail("SUBJ-101", isBlindedCaller: true);
        _logger.LogUpdatingSubjectDemographics("SUBJ-101", "Updated DOB");
        _logger.LogDeletingSubjectDemographics("SUBJ-101", "Data request");

        _logger.Logs.Should().HaveCount(7);

        _logger.Logs[0].EventId.Id.Should().Be(3001);
        _logger.Logs[0].Message.Should().Contain("SUBJ-101").And.Contain("STUDY-200");

        _logger.Logs[1].EventId.Id.Should().Be(3002);
        _logger.Logs[1].Message.Should().Contain("Successfully created subject 'SUBJ-101'");

        _logger.Logs[2].EventId.Id.Should().Be(3003);
        _logger.Logs[2].Message.Should().Contain("v1.0");

        _logger.Logs[3].EventId.Id.Should().Be(3004);
        _logger.Logs[3].Message.Should().Contain("ENROLLED").And.Contain("Consent verified");

        _logger.Logs[4].EventId.Id.Should().Be(3005);
        _logger.Logs[4].Message.Should().Contain("isBlindedCaller: True");

        _logger.Logs[5].EventId.Id.Should().Be(3006);
        _logger.Logs[5].Message.Should().Contain("Updated DOB");

        _logger.Logs[6].EventId.Id.Should().Be(3007);
        _logger.Logs[6].Message.Should().Contain("Data request");
    }

    [Fact]
    public void LoggerMessages_WhenLevelDisabled_ShortCircuitWithoutLogging()
    {
        _logger.EnabledLevel = LogLevel.None;

        _logger.LogTraceparentHeaderInjected("00-trace-01", "/path");
        _logger.LogConsentEventProcessing("SUBJ-1", "STUDY-1");
        _logger.LogCreatingSubject("SUBJ-1", "STUDY-1");

        _logger.Logs.Should().BeEmpty();
    }

    private sealed class TestLogger : ILogger
    {
        public List<LogEntry> Logs { get; } = new();
        public LogLevel EnabledLevel { get; set; } = LogLevel.Trace;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= EnabledLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Logs.Add(new LogEntry(logLevel, eventId, formatter(state, exception), state));
        }

        public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, object? State);
    }
}
