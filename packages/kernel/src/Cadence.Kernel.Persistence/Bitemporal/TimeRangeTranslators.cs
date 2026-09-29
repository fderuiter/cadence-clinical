using System;
using System.Collections.Generic;
using System.Reflection;

using Cadence.Kernel.Domain.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Translates <see cref="TimeRange"/> members on <c>tstzrange</c> columns to PostgreSQL range operators, so the domain
/// <c>AsOf</c> query runs in the database and can use the GiST index (ADR-2246): <c>Contains</c> → <c>@&gt;</c>,
/// <c>Overlaps</c> → <c>&amp;&amp;</c>, <c>Start</c> → <c>lower()</c>, <c>End</c> → <c>upper()</c>,
/// <c>IsOpen</c> → <c>upper_inf()</c>.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class TimeRangeTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslator, IMemberTranslator
{
    private static readonly MethodInfo ContainsMethod =
        typeof(TimeRange).GetMethod(nameof(TimeRange.Contains), [typeof(DateTimeOffset)])!;

    private static readonly MethodInfo OverlapsMethod =
        typeof(TimeRange).GetMethod(nameof(TimeRange.Overlaps), [typeof(TimeRange)])!;

    private readonly NpgsqlSqlExpressionFactory _npgsql = (NpgsqlSqlExpressionFactory)sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null)
        {
            return null;
        }

        if (method.Equals(ContainsMethod))
        {
            return _npgsql.Contains(instance, _npgsql.ApplyTypeMapping(arguments[0], TimestampTz()));
        }

        if (method.Equals(OverlapsMethod))
        {
            return _npgsql.Overlaps(instance, _npgsql.ApplyTypeMapping(arguments[0], instance.TypeMapping));
        }

        return null;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || member.DeclaringType != typeof(TimeRange))
        {
            return null;
        }

        return member.Name switch
        {
            nameof(TimeRange.Start) => Function("lower", instance, returnType, TimestampTz()),
            nameof(TimeRange.End) => Function("upper", instance, returnType, TimestampTz()),
            nameof(TimeRange.IsOpen) => Function("upper_inf", instance, returnType, typeMappingSource.FindMapping(typeof(bool))),
            _ => null,
        };
    }

    private RelationalTypeMapping TimestampTz() => typeMappingSource.FindMapping(typeof(DateTimeOffset))!;

    private SqlExpression Function(string name, SqlExpression range, Type returnType, RelationalTypeMapping? mapping) =>
        _npgsql.Function(name, [range], nullable: true, argumentsPropagateNullability: [true], returnType, mapping);
}

/// <summary>Registers <see cref="TimeRangeTranslator"/> as a method-call translator.</summary>
internal sealed class TimeRangeMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new TimeRangeTranslator(sqlExpressionFactory, typeMappingSource)];
}

/// <summary>Registers <see cref="TimeRangeTranslator"/> as a member translator.</summary>
internal sealed class TimeRangeMemberTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    : IMemberTranslatorPlugin
{
    public IEnumerable<IMemberTranslator> Translators { get; } = [new TimeRangeTranslator(sqlExpressionFactory, typeMappingSource)];
}
