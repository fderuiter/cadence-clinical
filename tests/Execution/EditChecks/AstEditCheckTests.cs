using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.EditChecks;

/// <summary>
/// Unit test coverage for the AST edit check engine, Kleene 3-valued boolean logic,
/// arithmetic and comparison operators, inline reference constraints, and cross-visit batch physiology checks.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-002
/// @req:SRS-EDC-005
/// </summary>
public sealed class AstEditCheckTests
{
    /// <summary>
    /// Validates that arithmetic AST nodes evaluate with division-by-zero protection.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?>
        {
            ["weight_kg"] = 70.0,
            ["height_m"] = 1.75
        };

        // Addition: 70 + 10 = 80
        var addJson = "{\"op\": \"+\", \"args\": [{\"var\": \"weight_kg\"}, 10]}";
        var addNode = JsonSerializer.Deserialize<JsonElement>(addJson);
        engine.EvaluateAst(addNode, context).Should().BeOfType<decimal>().And.Be(80m);

        // Multiplication: 1.75 * 1.75 = 3.0625
        var mulJson = "{\"op\": \"*\", \"args\": [{\"var\": \"height_m\"}, {\"var\": \"height_m\"}]}";
        var mulNode = JsonSerializer.Deserialize<JsonElement>(mulJson);
        engine.EvaluateAst(mulNode, context).Should().BeOfType<decimal>().And.Be(3.0625m);

        // Division: 70 / 2 = 35
        var divJson = "{\"op\": \"/\", \"args\": [{\"var\": \"weight_kg\"}, 2]}";
        var divNode = JsonSerializer.Deserialize<JsonElement>(divJson);
        engine.EvaluateAst(divNode, context).Should().BeOfType<decimal>().And.Be(35m);

        // Division by zero: safe null
        var divZeroJson = "{\"op\": \"/\", \"args\": [{\"var\": \"weight_kg\"}, 0]}";
        var divZeroNode = JsonSerializer.Deserialize<JsonElement>(divZeroJson);
        engine.EvaluateAst(divZeroNode, context).Should().BeNull();
    }

    /// <summary>
    /// Validates Kleene 3-valued logic truth table operations.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?>
        {
            ["is_present"] = true,
            ["is_missing"] = null,
            ["is_false"] = false
        };

        // false && null => false
        var andFalseNullJson = "{\"op\": \"&&\", \"args\": [{\"var\": \"is_false\"}, {\"var\": \"is_missing\"}]}";
        var andFalseNullNode = JsonSerializer.Deserialize<JsonElement>(andFalseNullJson);
        engine.EvaluateAst(andFalseNullNode, context).Should().Be(false);

        // true && null => null
        var andTrueNullJson = "{\"op\": \"&&\", \"args\": [{\"var\": \"is_present\"}, {\"var\": \"is_missing\"}]}";
        var andTrueNullNode = JsonSerializer.Deserialize<JsonElement>(andTrueNullJson);
        engine.EvaluateAst(andTrueNullNode, context).Should().BeNull();

        // true || null => true
        var orTrueNullJson = "{\"op\": \"||\", \"args\": [{\"var\": \"is_present\"}, {\"var\": \"is_missing\"}]}";
        var orTrueNullNode = JsonSerializer.Deserialize<JsonElement>(orTrueNullJson);
        engine.EvaluateAst(orTrueNullNode, context).Should().Be(true);

        // false || null => null
        var orFalseNullJson = "{\"op\": \"||\", \"args\": [{\"var\": \"is_false\"}, {\"var\": \"is_missing\"}]}";
        var orFalseNullNode = JsonSerializer.Deserialize<JsonElement>(orFalseNullJson);
        engine.EvaluateAst(orFalseNullNode, context).Should().BeNull();
    }

    /// <summary>
    /// Validates comparison operations across numeric bounds.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_Comparisons_EvaluatesInequalities()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?> { ["val"] = 120.0 };

        var ltJson = "{\"op\": \"<\", \"args\": [{\"var\": \"val\"}, 150]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(ltJson), context).Should().Be(true);

        var gtJson = "{\"op\": \">\", \"args\": [{\"var\": \"val\"}, 150]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(gtJson), context).Should().Be(false);

        var eqJson = "{\"op\": \"==\", \"args\": [{\"var\": \"val\"}, 120]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(eqJson), context).Should().Be(true);
    }

    /// <summary>
    /// Validates that EvaluateInlineConstraints flags values falling outside specified reference range bounds.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-002
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateInlineConstraints_FlagsReferenceRangeViolations()
    {
        var engine = new AstEditCheckEngine();

        var lowObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 2.8,
            additionalProperties: new ObservationAdditionalProperties
            {
                ReferenceRangeLow = 3.5,
                ReferenceRangeHigh = 5.0
            }).Value!;

        var violations = engine.EvaluateInlineConstraints(lowObs);
        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("RULE_RANGE_LOW");
        violations[0].Severity.Should().Be("WARNING");
    }

    /// <summary>
    /// Validates that batch evaluation detects cross-test physiological impossibilities.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation()
    {
        var engine = new AstEditCheckEngine();

        var sysObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 70.0,
            visitId: "VISIT-01").Value!;

        var diaObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic Blood Pressure",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 90.0,
            visitId: "VISIT-01").Value!;

        var batchContext = new BatchEvaluationContext(
            studyId: "STUDY-001",
            subjectId: "SUBJ-001",
            observations: [sysObs, diaObs],
            formSubmissions: []);

        var violations = engine.EvaluateBatchRules(batchContext);
        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("RULE_BP_PHYSIOLOGY");
        violations[0].Severity.Should().Be("QUERY");
    }

    /// <summary>
    /// Validates indexed-repeat functionality on nested relative array references.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference()
    {
        var engine = new AstEditCheckEngine();
        var nodeJson = """
        {
            "type": "function",
            "operator": "indexed-repeat",
            "operands": [
                {"type": "field_ref", "field_ref": {"field_id": "vssbp"}},
                {"type": "field_ref", "field_ref": {"field_id": "repeating_vs"}},
                {"type": "constant", "value": 2}
            ]
        }
        """;
        var node = JsonSerializer.Deserialize<JsonElement>(nodeJson);
        var context = new Dictionary<string, object?>
        {
            ["repeating_vs[1]/vssbp"] = 110.0,
            ["repeating_vs[2]/vssbp"] = 130.0
        };

        var result = engine.EvaluateAst(node, context);
        result.Should().Be(130.0);
    }

    /// <summary>
    /// Validates relative path resolution (../field) via bare context fallback and /clinical_data/subject/ prefix.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths()
    {
        var engine = new AstEditCheckEngine();
        var nodeJson = "{\"node_type\": \"XPATH\", \"value\": \"../vssbp\"}";
        var node = JsonSerializer.Deserialize<JsonElement>(nodeJson);

        // 1. Bare fallback
        var contextBare = new Dictionary<string, object?> { ["vssbp"] = 120.0 };
        engine.EvaluateAst(node, contextBare).Should().Be(120.0);

        // 2. Prefixed resolution
        var contextPrefixed = new Dictionary<string, object?> { ["/clinical_data/subject/vssbp"] = 135.0 };
        engine.EvaluateAst(node, contextPrefixed).Should().Be(135.0);
    }

    /// <summary>
    /// Validates that comparisons across mixed numeric types and null values evaluate deterministically.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?>
        {
            ["int_val"] = 100,
            ["double_val"] = 100.0,
            ["null_val"] = null
        };

        // int == double
        var eqJson = "{\"op\": \"==\", \"args\": [{\"var\": \"int_val\"}, {\"var\": \"double_val\"}]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(eqJson), context).Should().Be(true);

        // int != double
        var neJson = "{\"op\": \"!=\", \"args\": [{\"var\": \"int_val\"}, {\"var\": \"double_val\"}]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(neJson), context).Should().Be(false);

        // null comparison null == null
        var nullEqJson = "{\"op\": \"==\", \"args\": [{\"var\": \"null_val\"}, null]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(nullEqJson), context).Should().Be(true);

        // null < number => false
        var nullLtJson = "{\"op\": \"<\", \"args\": [{\"var\": \"null_val\"}, 50]}";
        engine.EvaluateAst(JsonSerializer.Deserialize<JsonElement>(nullLtJson), context).Should().Be(false);
    }

    /// <summary>
    /// Validates that deeply nested ASTs exceeding recursion depth of 64 terminate safely without stack overflow.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow()
    {
        var engine = new AstEditCheckEngine();
        // Construct AST dictionary with depth > 70
        object current = new Dictionary<string, object?> { ["var"] = "val" };
        for (int i = 0; i < 70; i++)
        {
            current = new Dictionary<string, object?>
            {
                ["op"] = "!",
                ["args"] = new object[] { current }
            };
        }

        var context = new Dictionary<string, object?> { ["val"] = true };

        // Should return null due to depth > 64 guard
        var result = engine.EvaluateAst(current, context);
        result.Should().BeNull();
    }

    /// <summary>
    /// Validates that fixed-point decimal arithmetic avoids binary floating point representation errors and evaluates exact decimal equality.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_DecimalPrecisionAndExactEquality_EvaluatesWithoutEpsilonDrift()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?>();

        // 0.1 + 0.2 == 0.3 (in binary floating point 0.1 + 0.2 is 0.30000000000000004)
        var exactAddJson = "{\"op\": \"==\", \"args\": [{\"op\": \"+\", \"args\": [0.1, 0.2]}, 0.3]}";
        var node = JsonSerializer.Deserialize<JsonElement>(exactAddJson);
        engine.EvaluateAst(node, context).Should().Be(true);

        // Exact comparison boundary: 100.0000000001 == 100.0000000002 should be false without epsilon masking
        var boundaryJson = "{\"op\": \"==\", \"args\": [100.0000000001, 100.0000000002]}";
        var boundaryNode = JsonSerializer.Deserialize<JsonElement>(boundaryJson);
        engine.EvaluateAst(boundaryNode, context).Should().Be(false);

        // Exact comparison boundary: 100.0000000001 != 100.0000000002 should be true
        var boundaryNeJson = "{\"op\": \"!=\", \"args\": [100.0000000001, 100.0000000002]}";
        var boundaryNeNode = JsonSerializer.Deserialize<JsonElement>(boundaryNeJson);
        engine.EvaluateAst(boundaryNeNode, context).Should().Be(true);
    }

    /// <summary>
    /// Validates that values outside decimal range or invalid operations return null safely.
    /// @req:PRD-SYS-004
    /// @req:SRS-EDC-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-004")]
    public void EvaluateAst_DecimalEdgeCases_HandlesOverflowAndInvalidValuesSafely()
    {
        var engine = new AstEditCheckEngine();
        var context = new Dictionary<string, object?>
        {
            ["huge_val"] = 1e308,
            ["nan_val"] = double.NaN
        };

        // Arithmetic with value outside decimal range returns null
        var overflowJson = "{\"op\": \"+\", \"args\": [{\"var\": \"huge_val\"}, 10]}";
        var overflowNode = JsonSerializer.Deserialize<JsonElement>(overflowJson);
        engine.EvaluateAst(overflowNode, context).Should().BeNull();

        // Arithmetic with NaN returns null
        var nanJson = "{\"op\": \"+\", \"args\": [{\"var\": \"nan_val\"}, 5]}";
        var nanNode = JsonSerializer.Deserialize<JsonElement>(nanJson);
        engine.EvaluateAst(nanNode, context).Should().BeNull();
    }
}
