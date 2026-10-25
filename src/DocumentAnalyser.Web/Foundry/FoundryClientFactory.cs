using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAnalyser.Web.Foundry;

/// <summary>
/// The one place that talks to Azure AI Foundry. Everything else in the app works with
/// <see cref="IChatClient"/> (Microsoft.Extensions.AI), so you could swap the model provider here.
/// </summary>
public sealed class FoundryClientFactory(IOptions<FoundryOptions> options)
{
    public const string TelemetrySource = "DocumentAnalyser";

    private readonly FoundryOptions _options = options.Value;
    private AzureOpenAIClient? _client;

    /// <summary>Chat client used by the agents.</summary>
    public IChatClient CreateAgentChatClient() => Create(_options.ChatDeployment);

    /// <summary>Chat client used by the LLM-as-judge evaluators.</summary>
    public IChatClient CreateEvaluatorChatClient() =>
        Create(string.IsNullOrWhiteSpace(_options.EvaluatorDeployment) ? _options.ChatDeployment : _options.EvaluatorDeployment);

    private IChatClient Create(string deployment)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Foundry is not configured. Set Foundry:Endpoint and Foundry:ChatDeployment (see README.md, step 4).");
        }

        _client ??= string.IsNullOrWhiteSpace(_options.ApiKey)
            // Uses whoever is signed in to Visual Studio / Azure CLI. Needs the
            // "Azure AI User" (or "Cognitive Services OpenAI User") role on the Foundry resource.
            ? new AzureOpenAIClient(new Uri(_options.Endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(_options.Endpoint), new ApiKeyCredential(_options.ApiKey));

        return _client
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            // Emits OpenTelemetry spans for every model call (shows up in Foundry → Tracing if
            // Application Insights is connected, see README "Tracing").
            .UseOpenTelemetry(sourceName: TelemetrySource, configure: c => c.EnableSensitiveData = true)
            .Build();
    }
}
