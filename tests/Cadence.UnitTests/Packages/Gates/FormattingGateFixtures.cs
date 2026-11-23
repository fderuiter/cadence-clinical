using System;
using System.Collections.Generic;

using Cadence.Cli.Services;

namespace Cadence.UnitTests.Packages.Gates;

internal static class FormattingGateFixtures
{
    public static Dictionary<string, Func<GateContext>> Create(QualityGateFixtureWorkspace ws) => new(StringComparer.Ordinal)
    {
        ["dotnet-format"] = () =>
        {
            ws.WriteDotnetProject(
                "root = true\n[*.cs]\nindent_style = space\nindent_size = 4\n",
                "namespace Fixture;\n\npublic static class Bad\n{\n        public static int Value => 1;\n}\n");
            return ws.Context();
        },
        ["dotnet-format-style"] = () =>
        {
            ws.WriteDotnetProject(
                "root = true\n[*.cs]\nindent_style = space\nindent_size = 4\ncsharp_style_namespace_declarations = file_scoped:warning\n",
                "namespace Fixture\n{\n    public static class Bad\n    {\n        public static int Value => 1;\n    }\n}\n");
            return ws.Context();
        },
        ["prettier"] = () => ws.Context(),
    };
}
