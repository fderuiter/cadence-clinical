using System;
using System.Linq;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Econsent;

public class EconsentScoreDecimalPrecisionTests
{
    [Fact]
    public void ComprehensionResult_ScoreProperty_IsDecimalType()
    {
        var property = typeof(ComprehensionResult).GetProperty(nameof(ComprehensionResult.Score));
        property.Should().NotBeNull("Score property must exist on ComprehensionResult");
        property!.PropertyType.Should().Be(typeof(decimal), "Score property on ComprehensionResult must be of type decimal");
    }

    [Fact]
    public void EconsentDbContext_ComprehensionResultScore_HasPrecision5Scale2()
    {
        var options = new DbContextOptionsBuilder<EconsentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new EconsentDbContext(options, TestFieldProtection.CreateEncryptor());
        var model = dbContext.Model;

        var entityTypeMeta = model.FindEntityType(typeof(ComprehensionResult));
        entityTypeMeta.Should().NotBeNull("ComprehensionResult entity must exist in EF Core model");

        var propertyMeta = entityTypeMeta!.FindProperty(nameof(ComprehensionResult.Score));
        propertyMeta.Should().NotBeNull("Score property must exist on ComprehensionResult entity mapping");

        propertyMeta!.GetPrecision().Should().Be(5, "Score column precision must be configured to 5");
        propertyMeta.GetScale().Should().Be(2, "Score column scale must be configured to 2");
    }

    [Fact]
    public void ComprehensionResult_ExactBoundaryCheck_DivergesFromFloatingPointDrift()
    {
        // Near threshold boundary: 80.00% pass threshold
        decimal passingThreshold = 80.00m;

        // Exact decimal 79.99m is strictly below 80.00m threshold
        decimal scoreBelowThreshold = 79.99m;
        bool decimalPassed = scoreBelowThreshold >= passingThreshold;
        decimalPassed.Should().BeFalse();

        // Exact decimal 80.00m meets threshold
        decimal scoreAtThreshold = 80.00m;
        bool atThresholdPassed = scoreAtThreshold >= passingThreshold;
        atThresholdPassed.Should().BeTrue();

        // 5 out of 6 correct questions is 83.33m when rounded to 2 decimal places
        decimal questionsTotal = 6.0m;
        decimal questionsCorrect = 5.0m;
        decimal calculatedScore = Math.Round((questionsCorrect / questionsTotal) * 100.0m, 2, MidpointRounding.ToEven);
        calculatedScore.Should().Be(83.33m);

        // Verify that float/double binary inaccuracy (e.g. 79.99999999999999) is avoided by decimal representation
        decimal scoreFromFloatString = decimal.Parse("79.99");
        scoreFromFloatString.Should().Be(79.99m);
        (scoreFromFloatString < passingThreshold).Should().BeTrue();
    }

    [Fact]
    public void EconsentDbContext_Persistence_PreservesDecimalPrecisionWithoutTruncation()
    {
        var options = new DbContextOptionsBuilder<EconsentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new EconsentDbContext(options, TestFieldProtection.CreateEncryptor());

        var result1 = new ComprehensionResult
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = "tmpl-001",
            VersionIndex = 1,
            SubjectPseudonym = "SUBJ-101",
            Passed = false,
            Score = 79.99m,
            CreatedBy = "user-001",
            ReasonForChange = "Initial submission",
        };

        var result2 = new ComprehensionResult
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = "tmpl-001",
            VersionIndex = 1,
            SubjectPseudonym = "SUBJ-102",
            Passed = true,
            Score = 80.00m,
            CreatedBy = "user-001",
            ReasonForChange = "Initial submission",
        };

        dbContext.ComprehensionResults.AddRange(result1, result2);
        dbContext.SaveChanges();

        using var readContext = new EconsentDbContext(options, TestFieldProtection.CreateEncryptor());
        var retrieved1 = readContext.ComprehensionResults.First(r => r.Id == result1.Id);
        var retrieved2 = readContext.ComprehensionResults.First(r => r.Id == result2.Id);

        retrieved1.Score.Should().Be(79.99m);
        retrieved2.Score.Should().Be(80.00m);
    }
}
