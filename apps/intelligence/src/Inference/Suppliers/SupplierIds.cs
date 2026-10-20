namespace Cadence.Modules.Intelligence.Inference;

/// <summary>The ids of the supplier records shipped in <c>Inference/Suppliers/</c> (ADR-2636 §4.5).</summary>
public static class SupplierIds
{
    /// <summary>Anthropic, for the Claude API.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>Anthropic, for Claude Platform on AWS (Anthropic-operated on AWS infrastructure).</summary>
    public const string AnthropicOnAws = "anthropic-aws";

    /// <summary>Amazon Web Services, for Claude on Amazon Bedrock.</summary>
    public const string AwsBedrock = "aws-bedrock";

    /// <summary>Google, for Claude on Vertex AI.</summary>
    public const string GoogleVertex = "google-vertex";

    /// <summary>Microsoft, for Claude on Microsoft Foundry.</summary>
    public const string MicrosoftFoundry = "microsoft-foundry";

    /// <summary>The operator itself, for a model it runs inside the deployment.</summary>
    public const string OperatorHosted = "operator-hosted";
}
