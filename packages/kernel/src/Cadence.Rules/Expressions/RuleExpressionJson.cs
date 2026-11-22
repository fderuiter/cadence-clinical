using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cadence.Rules.Expressions;

/// <summary>
/// Reads and writes the JSON form of <see cref="RuleExpression"/>.
/// <para>Canonical form (what <see cref="Write"/> produces):</para>
/// <list type="bullet">
/// <item>a JSON string, number, boolean or null is a literal;</item>
/// <item><c>{"var": "NAME"}</c> reads a variable of the record;</item>
/// <item><c>{"op": "…", "args": [ … ]}</c> applies an operator or function: <c>and</c>, <c>or</c>, <c>not</c>,
/// <c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>+</c>, <c>-</c>, <c>*</c>,
/// <c>/</c>, <c>is_empty</c>, <c>is_not_empty</c>, <c>in</c>, <c>not_in</c>, <c>length</c>.</item>
/// </list>
/// <para>
/// The EDC <c>AstEditCheckEngine</c> shapes it grew from also parse: <c>operator</c> for <c>op</c>,
/// <c>operands</c> or <c>children</c> for <c>args</c>, <c>{"node_type": "LITERAL", "value": …}</c>,
/// <c>{"node_type": "FUNCTION", "operator": "is_empty", …}</c>, and <c>&amp;&amp;</c>, <c>||</c>, <c>!</c> and
/// <c>empty</c>. ODM path references (<c>XPATH</c>, <c>field_ref</c>, <c>indexed-repeat</c>) are EDC form concerns and
/// are rejected; a rule reads variables by name.
/// </para>
/// Parsing is strict: an unknown operator, a wrong number of arguments, an unknown property, a tree deeper than
/// <see cref="MaxDepth"/> or larger than <see cref="MaxNodes"/> nodes is an error, so a pack is checked once when it is
/// drafted rather than failing during evaluation.
/// </summary>
public static partial class RuleExpressionJson
{
    // Canonical output escapes only what JSON requires, so operators such as < and + stay readable.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The deepest nesting an expression may have.</summary>
    public const int MaxDepth = 64;
    /// <summary>The most nodes an expression may have.</summary>
    public const int MaxNodes = 4096;
    /// <summary>The longest text literal or variable reference, in characters.</summary>
    public const int MaxTextLength = 4000;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["&&"] = "and",
        ["||"] = "or",
        ["!"] = "not",
        ["empty"] = "is_empty",
    };

    /// <summary>Parses an expression, or returns the first problem found.</summary>
    public static bool TryParse(JsonElement json, out RuleExpression? expression, out string? error)
    {
        try
        {
            var nodes = 0;
            expression = Parse(json, "$", 0, ref nodes);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            expression = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Parses an expression.</summary>
    /// <exception cref="FormatException">The JSON is not a valid expression.</exception>
    public static RuleExpression Parse(JsonElement json) =>
        TryParse(json, out var expression, out var error) ? expression! : throw new FormatException(error);

    /// <summary>Writes the canonical JSON form.</summary>
    public static void Write(Utf8JsonWriter writer, RuleExpression expression)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(expression);
        switch (expression)
        {
            case LiteralExpression literal:
                WriteValue(writer, literal.Value);
                break;
            case VariableExpression variable:
                writer.WriteStartObject();
                writer.WriteString("var", variable.Name);
                writer.WriteEndObject();
                break;
            case NotExpression not:
                WriteOperation(writer, "not", [not.Operand]);
                break;
            case LogicalExpression logical:
                WriteOperation(writer, logical.Operator == LogicalOperator.And ? "and" : "or", logical.Operands);
                break;
            case ComparisonExpression comparison:
                WriteOperation(writer, Symbol(comparison.Operator), [comparison.Left, comparison.Right]);
                break;
            case ArithmeticExpression arithmetic:
                WriteOperation(writer, Symbol(arithmetic.Operator), [arithmetic.Left, arithmetic.Right]);
                break;
            case FunctionExpression function:
                WriteOperation(writer, Name(function.Function), function.Arguments);
                break;
            default:
                throw new ArgumentException($"Unknown expression type {expression.GetType().Name}.", nameof(expression));
        }
    }

    /// <summary>The canonical JSON form as a detached element.</summary>
    public static JsonElement ToJson(RuleExpression expression)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            Write(writer, expression);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>The distinct variables an expression reads, in first-use order.</summary>
    public static IReadOnlyList<string> Variables(RuleExpression expression)
    {
        var names = new List<string>();
        Collect(expression, names);
        return names;
    }

    private static void Collect(RuleExpression expression, List<string> names)
    {
        switch (expression)
        {
            case VariableExpression variable when !names.Contains(variable.Name, StringComparer.Ordinal):
                names.Add(variable.Name);
                break;
            case NotExpression not:
                Collect(not.Operand, names);
                break;
            case LogicalExpression logical:
                logical.Operands.ToList().ForEach(o => Collect(o, names));
                break;
            case ComparisonExpression comparison:
                Collect(comparison.Left, names);
                Collect(comparison.Right, names);
                break;
            case ArithmeticExpression arithmetic:
                Collect(arithmetic.Left, names);
                Collect(arithmetic.Right, names);
                break;
            case FunctionExpression function:
                function.Arguments.ToList().ForEach(a => Collect(a, names));
                break;
        }
    }

    private static RuleExpression Parse(JsonElement json, string path, int depth, ref int nodes)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException($"{path}: the expression is nested more than {MaxDepth} levels.");
        }

        if (++nodes > MaxNodes)
        {
            throw new FormatException($"{path}: the expression has more than {MaxNodes} nodes.");
        }

        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                return ParseObject(json, path, depth, ref nodes);
            case JsonValueKind.Array:
                throw new FormatException($"{path}: an array is not an expression; put operands in \"args\".");
            default:
                return new LiteralExpression(Literal(json, path));
        }
    }

    private static RuleExpression ParseObject(JsonElement json, string path, int depth, ref int nodes)
    {
        string? variable = null;
        string? op = null;
        string? nodeType = null;
        JsonElement? args = null;
        JsonElement? value = null;
        foreach (var property in json.EnumerateObject())
        {
            switch (property.Name)
            {
                case "var":
                    variable = String(property.Value, $"{path}.var");
                    break;
                case "op" or "operator":
                    op = op is null ? String(property.Value, $"{path}.{property.Name}") : throw Duplicate(path, "op");
                    break;
                case "args" or "operands" or "children":
                    args = args is null ? property.Value : throw Duplicate(path, "args");
                    break;
                case "node_type" or "type":
                    nodeType = String(property.Value, $"{path}.{property.Name}");
                    break;
                case "value":
                    value = property.Value;
                    break;
                default:
                    throw new FormatException($"{path}: unknown property \"{property.Name}\".");
            }
        }

        if (variable is not null)
        {
            if (op is not null || args is not null || value is not null || nodeType is not null)
            {
                throw new FormatException($"{path}: a variable reference has only \"var\".");
            }

            return VariableName().IsMatch(variable)
                ? new VariableExpression(variable)
                : throw new FormatException($"{path}: \"{variable}\" is not a variable name (letters, digits, '_', '.', ':' and '-', starting with a letter or '_', at most 64 characters).");
        }

        if (nodeType is not null && (nodeType.Equals("LITERAL", StringComparison.OrdinalIgnoreCase) || nodeType.Equals("constant", StringComparison.OrdinalIgnoreCase)))
        {
            if (op is not null || args is not null)
            {
                throw new FormatException($"{path}: a literal has only \"value\".");
            }

            return new LiteralExpression(value is { } v ? Literal(v, $"{path}.value") : RuleValue.Null);
        }

        if (nodeType is not null && !nodeType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase) && !nodeType.Equals("OPERATOR", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"{path}: node type \"{nodeType}\" is not supported; rules read variables with {{\"var\": \"NAME\"}}.");
        }

        // The EDC grammar also carried an operator in "value".
        if (op is null && value is { ValueKind: JsonValueKind.String } opValue)
        {
            op = opValue.GetString();
        }
        else if (value is not null)
        {
            throw new FormatException($"{path}: an operation has no \"value\".");
        }

        if (string.IsNullOrEmpty(op))
        {
            throw new FormatException($"{path}: an object must be {{\"var\": …}} or {{\"op\": …, \"args\": […]}}.");
        }

        var name = op.ToLowerInvariant();
        name = Aliases.GetValueOrDefault(name, name);
        var operands = new List<RuleExpression>();
        if (args is { } list)
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"{path}.args: must be an array.");
            }

            var i = 0;
            foreach (var item in list.EnumerateArray())
            {
                operands.Add(Parse(item, $"{path}.args[{i++}]", depth + 1, ref nodes));
            }
        }

        return Build(name, operands, path);
    }

    private static RuleExpression Build(string op, List<RuleExpression> args, string path) => op switch
    {
        "and" => new LogicalExpression(LogicalOperator.And, AtLeast(args, 1, op, path)),
        "or" => new LogicalExpression(LogicalOperator.Or, AtLeast(args, 1, op, path)),
        "not" => new NotExpression(Exactly(args, 1, op, path)[0]),
        "==" => Compare(ComparisonOperator.Equal, args, op, path),
        "!=" => Compare(ComparisonOperator.NotEqual, args, op, path),
        "<" => Compare(ComparisonOperator.Less, args, op, path),
        "<=" => Compare(ComparisonOperator.LessOrEqual, args, op, path),
        ">" => Compare(ComparisonOperator.Greater, args, op, path),
        ">=" => Compare(ComparisonOperator.GreaterOrEqual, args, op, path),
        "+" => Arithmetic(ArithmeticOperator.Add, args, op, path),
        "-" => Arithmetic(ArithmeticOperator.Subtract, args, op, path),
        "*" => Arithmetic(ArithmeticOperator.Multiply, args, op, path),
        "/" => Arithmetic(ArithmeticOperator.Divide, args, op, path),
        "is_empty" => new FunctionExpression(RuleFunction.IsEmpty, Exactly(args, 1, op, path)),
        "is_not_empty" => new FunctionExpression(RuleFunction.IsNotEmpty, Exactly(args, 1, op, path)),
        "in" => new FunctionExpression(RuleFunction.In, AtLeast(args, 2, op, path)),
        "not_in" => new FunctionExpression(RuleFunction.NotIn, AtLeast(args, 2, op, path)),
        "length" => new FunctionExpression(RuleFunction.Length, Exactly(args, 1, op, path)),
        _ => throw new FormatException($"{path}: unknown operator \"{op}\"."),
    };

    private static ComparisonExpression Compare(ComparisonOperator op, List<RuleExpression> args, string name, string path)
    {
        var operands = Exactly(args, 2, name, path);
        return new ComparisonExpression(op, operands[0], operands[1]);
    }

    private static ArithmeticExpression Arithmetic(ArithmeticOperator op, List<RuleExpression> args, string name, string path)
    {
        var operands = Exactly(args, 2, name, path);
        return new ArithmeticExpression(op, operands[0], operands[1]);
    }

    private static List<RuleExpression> Exactly(List<RuleExpression> args, int count, string op, string path) =>
        args.Count == count ? args : throw new FormatException($"{path}: \"{op}\" takes {count} argument(s), not {args.Count}.");

    private static List<RuleExpression> AtLeast(List<RuleExpression> args, int count, string op, string path) =>
        args.Count >= count ? args : throw new FormatException($"{path}: \"{op}\" takes at least {count} argument(s), not {args.Count}.");

    private static RuleValue Literal(JsonElement json, string path) => json.ValueKind switch
    {
        JsonValueKind.Null => RuleValue.Null,
        JsonValueKind.True => RuleValue.True,
        JsonValueKind.False => RuleValue.False,
        JsonValueKind.Number => json.TryGetDecimal(out var number)
            ? RuleValue.FromNumber(number)
            : throw new FormatException($"{path}: {json.GetRawText()} is outside the decimal range."),
        JsonValueKind.String => json.GetString()!.Length <= MaxTextLength
            ? RuleValue.FromText(json.GetString())
            : throw new FormatException($"{path}: text literals are at most {MaxTextLength} characters."),
        _ => throw new FormatException($"{path}: {json.ValueKind} is not a literal."),
    };

    private static string String(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.String ? json.GetString()! : throw new FormatException($"{path}: must be a string.");

    private static FormatException Duplicate(string path, string property) =>
        new($"{path}: \"{property}\" is given twice.");

    private static void WriteOperation(Utf8JsonWriter writer, string op, IReadOnlyList<RuleExpression> args)
    {
        writer.WriteStartObject();
        writer.WriteString("op", op);
        writer.WriteStartArray("args");
        foreach (var arg in args)
        {
            Write(writer, arg);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, RuleValue value)
    {
        switch (value.Kind)
        {
            case RuleValueKind.Null:
                writer.WriteNullValue();
                break;
            case RuleValueKind.Boolean:
                writer.WriteBooleanValue(value.IsTrue);
                break;
            case RuleValueKind.Number:
                value.TryGetNumber(out var number);
                writer.WriteNumberValue(number);
                break;
            default:
                value.TryGetText(out var text);
                writer.WriteStringValue(text);
                break;
        }
    }

    private static string Symbol(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "==",
        ComparisonOperator.NotEqual => "!=",
        ComparisonOperator.Less => "<",
        ComparisonOperator.LessOrEqual => "<=",
        ComparisonOperator.Greater => ">",
        _ => ">=",
    };

    private static string Symbol(ArithmeticOperator op) => op switch
    {
        ArithmeticOperator.Add => "+",
        ArithmeticOperator.Subtract => "-",
        ArithmeticOperator.Multiply => "*",
        _ => "/",
    };

    private static string Name(RuleFunction function) => function switch
    {
        RuleFunction.IsEmpty => "is_empty",
        RuleFunction.IsNotEmpty => "is_not_empty",
        RuleFunction.In => "in",
        RuleFunction.NotIn => "not_in",
        _ => "length",
    };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.:-]{0,63}$")]
    private static partial Regex VariableName();
}
