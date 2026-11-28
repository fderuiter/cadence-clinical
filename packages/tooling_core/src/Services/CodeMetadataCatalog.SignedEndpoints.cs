using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class CodeMetadataCatalog
{
    [GeneratedRegex(@"(?:public|private|internal)\s+const\s+string\s+([A-Za-z0-9_]+)\s*=\s*(\$?""[^""]+"")\s*;")]
    private static partial Regex ConstStringRegex();

    [GeneratedRegex(@"public\s+static\s+string\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)\s*=>\s*(\$?""[^""]+"")\s*;")]
    private static partial Regex ActionTemplateMethodRegex();

    [GeneratedRegex(@"\b(?:var|RouteGroupBuilder)\s+([A-Za-z0-9_]+)\s*=\s*([A-Za-z0-9_]+)\.MapGroup\s*\(\s*([^)]+?)\s*\)")]
    private static partial Regex MapGroupRegex();

    [GeneratedRegex(@"(?:([A-Za-z0-9_]+)\.MapGroup\s*\(\s*""([^""]+)""\s*\)(?:(?![;{])[\s\S]){0,120}?|([A-Za-z0-9_]+))\s*\.Map(Get|Post|Put|Patch|Delete)\s*\(\s*(?:""([^""]*)""|(string\.Empty))")]
    private static partial Regex MapEndpointHeaderRegex();

    [GeneratedRegex(@"\.RequireElectronicSignature\s*\(")]
    private static partial Regex RequireElectronicSignatureCallRegex();

    [GeneratedRegex(@"\.RequireElectronicSignatureWhen\s*\(")]
    private static partial Regex RequireElectronicSignatureWhenCallRegex();

    [GeneratedRegex(@"new\s+SigTokenBinding\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*,")]
    private static partial Regex SigTokenBindingCtorRegex();

    [GeneratedRegex(@"private\s+static\s+void\s+([A-Za-z0-9_]+)\s*\(\s*RouteHandlerBuilder\s+\w+\s*\)\s*=>[\s\S]*?\.RequireElectronicSignature\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*\)\s*;")]
    private static partial Regex WrapperRequireSignatureRegex();

    private static IReadOnlyList<SignedEndpointMetadata> ParseSignedEndpointsFromFile(string source, string relativePath)
    {
        string host = ResolveHostFromRelativePath(relativePath);
        var constants = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KernelApiEndpointRouteBuilderExtensions.RoutePrefix"] = "/api/v1/kernel",
            ["FormEndpoints.RoutePrefix"] = "/api/v1/execution/forms",
        };

        foreach (GroupCollection constGroups in ConstStringRegex().Matches(source).Select(cm => cm.Groups))
        {
            string name = constGroups[1].Value;
            string rawValue = StripQuotesAndInterpolation(constGroups[2].Value, constants);
            constants[name] = rawValue;
        }

        var actionMethods = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GroupCollection actionGroups in ActionTemplateMethodRegex().Matches(source).Select(am => am.Groups))
        {
            string methodName = actionGroups[1].Value;
            string template = NormalizeActionTemplate(StripQuotesAndInterpolation(actionGroups[3].Value, constants));
            actionMethods[methodName] = template;
        }

        string defaultBaseGroup = ResolveDefaultGroupPrefix(relativePath);
        var groups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app"] = string.Empty,
            ["routes"] = string.Empty,
            ["endpoints"] = string.Empty,
            ["kernel"] = "/api/v1/kernel",
            ["group"] = defaultBaseGroup,
        };

        foreach (GroupCollection groupMatch in MapGroupRegex().Matches(source).Select(gm => gm.Groups))
        {
            string targetVar = groupMatch[1].Success ? groupMatch[1].Value : groupMatch[2].Value;
            string parentVar = groupMatch[2].Value;
            string argExpr = groupMatch[3].Value.Trim();
            string segment = argExpr == "string.Empty"
                ? string.Empty
                : StripQuotesAndInterpolation(argExpr, constants);
            string parentPrefix = groups.GetValueOrDefault(parentVar, defaultBaseGroup);
            groups[targetVar] = CombineRoute(parentPrefix, segment);
        }

        var list = new List<SignedEndpointMetadata>();

        // Case A: local wrapper helper like `MapApprove(group.MapPost(...))` that attaches `.RequireElectronicSignature(...)`
        var wrapperMatch = WrapperRequireSignatureRegex().Match(source);
        if (wrapperMatch.Success)
        {
            string wrapperName = wrapperMatch.Groups[1].Value;
            string semanticIdent = wrapperMatch.Groups[2].Value;
            string semanticAction = constants.GetValueOrDefault(semanticIdent, semanticIdent);
            string actionTemplate = ResolveActionFromHelper(source, wrapperMatch.Groups[3].Value, constants, actionMethods);

            var wrappedRoutes = new List<(string Method, string Route)>();
            var callRegex = new Regex(
                $@"{Regex.Escape(wrapperName)}\s*\(\s*([A-Za-z0-9_]+)\.Map(Post|Put|Patch|Delete)\s*\(\s*""([^""]+)""",
                RegexOptions.CultureInvariant);
            foreach (GroupCollection callGroups in callRegex.Matches(source).Select(call => call.Groups))
            {
                string grp = groups.GetValueOrDefault(callGroups[1].Value, defaultBaseGroup);
                string verb = callGroups[2].Value.ToUpperInvariant();
                string fullRoute = NormalizeRouteParameters(CombineRoute(grp, callGroups[3].Value));
                wrappedRoutes.Add((verb, fullRoute));
            }

            if (wrappedRoutes.Count > 0)
            {
                string? alias = wrappedRoutes.Count > 1 ? $"{wrappedRoutes[1].Method} {wrappedRoutes[1].Route}" : null;
                list.Add(new SignedEndpointMetadata(
                    host,
                    wrappedRoutes[0].Method,
                    wrappedRoutes[0].Route,
                    alias,
                    semanticAction,
                    actionTemplate.Length > 0 ? actionTemplate : wrappedRoutes[0].Route,
                    UsesBatchBinding: false,
                    relativePath));
            }
        }

        // Case B: direct fluent `.Map*(...).RequireElectronicSignature(...)` or `.RequireElectronicSignatureWhen(...)` chains
        foreach (Match stmt in MapEndpointHeaderRegex().Matches(source))
        {
            string fluentTail = ExtractFluentTailAfterMapCall(source, stmt.Index + stmt.Length);
            if (fluentTail.Length == 0)
            {
                continue;
            }

            bool isConditionalWhen = false;
            if (!TryExtractInvocationArgs(fluentTail, RequireElectronicSignatureCallRegex(), out IReadOnlyList<string> sigArgs))
            {
                if (!TryExtractInvocationArgs(fluentTail, RequireElectronicSignatureWhenCallRegex(), out sigArgs))
                {
                    continue;
                }

                isConditionalWhen = true;
            }

            string basePrefix = stmt.Groups[1].Success
                ? CombineRoute(groups.GetValueOrDefault(stmt.Groups[1].Value, defaultBaseGroup), stmt.Groups[2].Value)
                : groups.GetValueOrDefault(stmt.Groups[3].Value, defaultBaseGroup);
            string verb = stmt.Groups[4].Value.ToUpperInvariant();
            string subPath = stmt.Groups[6].Success ? string.Empty : stmt.Groups[5].Value;
            string fullRoute = NormalizeRouteParameters(CombineRoute(basePrefix, subPath));

            string semanticAction;
            string actionTemplate;
            bool usesBatch = false;

            if (isConditionalWhen)
            {
                string semArg = sigArgs.Count >= 2 ? sigArgs[1].Trim() : string.Empty;
                string? actArg = sigArgs.Count >= 3 ? sigArgs[2].Trim() : null;
                semanticAction = ResolveSemanticActionsFromHelper(source, semArg, constants);
                actionTemplate = actArg is not null
                    ? ResolveSecondArgAction(source, actArg, fullRoute, constants, actionMethods)
                    : fullRoute;
            }
            else if (sigArgs.Count == 1)
            {
                string firstArg = sigArgs[0].Trim();
                // Overload `RequireElectronicSignature(DelegationSignOffBinding)` returning `new SigTokenBinding(action, semanticAction, batchId)`
                var bindingMatch = SigTokenBindingCtorRegex().Match(source);
                if (bindingMatch.Success)
                {
                    actionTemplate = constants.GetValueOrDefault(bindingMatch.Groups[1].Value, fullRoute);
                    semanticAction = constants.GetValueOrDefault(bindingMatch.Groups[2].Value, firstArg);
                    usesBatch = true;
                }
                else
                {
                    semanticAction = StripQuotesAndInterpolation(firstArg, constants);
                    actionTemplate = fullRoute;
                }
            }
            else
            {
                string firstArg = sigArgs[0].Trim();
                string secondArg = sigArgs[1].Trim();
                semanticAction = StripQuotesAndInterpolation(firstArg, constants);
                actionTemplate = ResolveSecondArgAction(source, secondArg, fullRoute, constants, actionMethods);
            }

            list.Add(new SignedEndpointMetadata(
                host,
                verb,
                fullRoute,
                AliasRoute: null,
                semanticAction,
                NormalizeActionPathAgainstRoute(actionTemplate, fullRoute),
                usesBatch,
                relativePath));
        }

        return list;
    }

    private static string NormalizeActionTemplate(string template) =>
        Regex.Replace(template, @"\{([A-Za-z0-9_]+):[^}]+\}", "{$1}");

    private static string NormalizeRouteParameters(string route) =>
        Regex.Replace(route, @"\{([A-Za-z0-9_]+):[^}]+\}", "{$1}");

    private static string NormalizeActionPathAgainstRoute(string actionTemplate, string route)
    {
        string normalized = NormalizeActionTemplate(actionTemplate);
        string[] routeSegments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] actionSegments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (routeSegments.Length != actionSegments.Length)
        {
            return normalized;
        }

        for (int i = 0; i < actionSegments.Length; i++)
        {
            if (actionSegments[i].StartsWith('{') && actionSegments[i].EndsWith('}') &&
                routeSegments[i].StartsWith('{') && routeSegments[i].EndsWith('}'))
            {
                actionSegments[i] = routeSegments[i];
            }
        }

        return "/" + string.Join('/', actionSegments);
    }

    private static string CombineRoute(string prefix, string suffix)
    {
        string p = prefix.Trim().TrimEnd('/');
        string s = suffix.Trim();
        if (s == "/" || s.Length == 0)
        {
            return p.Length == 0 ? "/" : p;
        }

        if (s.StartsWith("/api/", StringComparison.Ordinal))
        {
            return s.TrimEnd('/');
        }

        if (!s.StartsWith('/'))
        {
            s = "/" + s;
        }

        return p + s;
    }

    private static string ResolveDefaultGroupPrefix(string relativePath)
    {
        if (relativePath.StartsWith("apps/ctms/", StringComparison.Ordinal))
        {
            return "/api/v1/ctms";
        }

        if (relativePath.StartsWith("apps/safety/", StringComparison.Ordinal))
        {
            return "/api/v1/safety";
        }

        if (relativePath.StartsWith("apps/execution/module/", StringComparison.Ordinal))
        {
            return "/api/v1/execution/forms";
        }

        if (relativePath.StartsWith("packages/kernel/", StringComparison.Ordinal))
        {
            return "/api/v1/kernel";
        }

        return string.Empty;
    }
}
