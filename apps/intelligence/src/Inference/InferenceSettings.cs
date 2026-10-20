namespace Cadence.Modules.Intelligence.Inference;

/// <summary>Configuration paths the inference layer reads, for error messages and documentation.</summary>
internal static class InferenceSettings
{
    public const string Root = Providers.InferenceOptions.Section;

    public const string Suppliers = Root + ":Suppliers";
}
