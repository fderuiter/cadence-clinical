using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Core.Domain.Rules;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class RuleChecksumTests
{
    private static RuleDefinition CreateRule(string comparisonOperator) => new()
    {
        RuleId = "RULE-001",
        StudyId = "STUDY-001",
        RuleType = "edit_check",
        CreatedAt = FakeClock.DefaultStart,
        Condition = new ExpressionNode
        {
            Type = "comparison",
            Operator = comparisonOperator,
            Operands =
            [
                new ExpressionNode { Type = "field_ref", FieldRef = new FieldReference("SYSBP", "VS") },
                new ExpressionNode { Type = "constant", Value = 180 },
            ],
        },
        Action = "raise_query",
        TargetField = "SYSBP",
        TargetForm = "VS",
        QueryMessage = "Systolic BP out of range",
        VersionIndex = 1,
    };

    /// <summary>
    /// Validates that a rule's integrity checksum covers its condition, so any change to the
    /// rule logic produces a different checksum.
    /// </summary>
    [Fact]
    public void ComputeChecksum_WhenConditionDiffers_ProducesDifferentChecksum()
    {
        var greaterThan = CreateRule(">");
        var lessThan = CreateRule("<");

        greaterThan.ComputeChecksum();
        lessThan.ComputeChecksum();

        greaterThan.Sha256Checksum.Should().NotBe(lessThan.Sha256Checksum);
    }

    /// <summary>
    /// Validates that checksums stay identical to those computed before the payload moved to
    /// JsonObject, so previously stored rule checksums still verify.
    /// </summary>
    [Fact]
    public void ComputeChecksum_MatchesOriginalPayloadLayout()
    {
        var rule = CreateRule(">");
        var original = new
        {
            rule_id = rule.RuleId,
            study_id = rule.StudyId,
            rule_type = rule.RuleType,
            condition = rule.Condition,
            action = rule.Action,
            target_field = rule.TargetField,
            target_form = rule.TargetForm,
            query_message = rule.QueryMessage,
            version_index = rule.VersionIndex,
            severity = rule.Severity,
            code = rule.Code,
        };
        string expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(original)))).ToLowerInvariant();

        rule.ComputeChecksum();

        rule.Sha256Checksum.Should().Be(expected);
    }
}
