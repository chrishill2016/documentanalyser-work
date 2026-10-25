namespace DocumentAnalyser.Web.Foundry;

/// <summary>
/// Settings bound from the "Foundry" section of appsettings.json / user secrets.
/// See README.md for where to find each value in the Azure AI Foundry portal.
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>
    /// The Azure OpenAI endpoint of your Foundry resource,
    /// e.g. https://my-foundry.openai.azure.com/ (Foundry portal → Overview → "Azure OpenAI endpoint").
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Optional API key. If empty we use your Azure login (Visual Studio / az login) via DefaultAzureCredential.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The model deployment name the agents use (Foundry portal → Models + endpoints).</summary>
    public string ChatDeployment { get; set; } = "gpt-4.1-mini";

    /// <summary>
    /// Optional separate deployment used as the "judge" for evaluation. Falls back to ChatDeployment.
    /// Using a different (often stronger) model as judge is common practice.
    /// </summary>
    public string? EvaluatorDeployment { get; set; }

    /// <summary>How many times the reviewer may send the draft back to the analyst.</summary>
    public int MaxReviewRounds { get; set; } = 2;

    /// <summary>Documents longer than this are truncated before being sent to the model.</summary>
    public int MaxDocumentChars { get; set; } = 60_000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ChatDeployment);

    public string AuthMode => string.IsNullOrWhiteSpace(ApiKey) ? "Entra ID (DefaultAzureCredential)" : "API key";
}
