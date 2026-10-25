using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DocumentAnalyser.Web.Agents;
using DocumentAnalyser.Web.Documents;
using DocumentAnalyser.Web.Evaluation;
using DocumentAnalyser.Web.Foundry;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FoundryOptions>(builder.Configuration.GetSection(FoundryOptions.SectionName));
builder.Services.AddSingleton<FoundryClientFactory>();
builder.Services.AddSingleton<AnswerEvaluator>();
builder.Services.AddSingleton<AnalysisPipeline>();

// Optional: send traces to Application Insights so you can see every agent/model call
// in the Foundry portal under "Tracing". Only enabled if a connection string is configured.
string? appInsights = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(o => o.ConnectionString = appInsights)
        .WithTracing(t => t.AddSource(FoundryClientFactory.TelemetrySource));
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// What the UI shows in the "Connection" panel, plus the default agent prompts.
app.MapGet("/api/config", (IOptions<FoundryOptions> options, IWebHostEnvironment env) =>
{
    FoundryOptions o = options.Value;
    return Results.Json(new
    {
        o.IsConfigured,
        o.Endpoint,
        o.ChatDeployment,
        EvaluatorDeployment = string.IsNullOrWhiteSpace(o.EvaluatorDeployment) ? o.ChatDeployment : o.EvaluatorDeployment,
        o.AuthMode,
        o.MaxReviewRounds,
        TracingEnabled = !string.IsNullOrWhiteSpace(appInsights),
        SupportedExtensions = DocumentTextExtractor.SupportedExtensions,
        Prompts = AgentPrompts.LoadDefaults(env),
    }, json);
});

// Runs the multi-agent pipeline and streams each step back as Server-Sent Events.
app.MapPost("/api/analyse", async (
    HttpContext http,
    AnalysisPipeline pipeline,
    IOptions<FoundryOptions> options,
    IWebHostEnvironment env,
    ILogger<Program> logger) =>
{
    CancellationToken ct = http.RequestAborted;
    IFormCollection form = await http.Request.ReadFormAsync(ct);

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    async Task Send(PipelineEvent e)
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(e, json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        // --- Read the document (uploaded file wins over pasted text) ---
        IFormFile? file = form.Files.GetFile("file");
        string documentText = file is { Length: > 0 }
            ? await DocumentTextExtractor.ExtractAsync(file, ct)
            : form["documentText"].ToString();

        string instructions = form["instructions"].ToString();
        if (string.IsNullOrWhiteSpace(documentText)) throw new ArgumentException("Please upload a document or paste some text.");
        if (string.IsNullOrWhiteSpace(instructions)) throw new ArgumentException("Please enter some instructions.");

        int maxChars = options.Value.MaxDocumentChars;
        bool truncated = documentText.Length > maxChars;
        if (truncated) documentText = documentText[..maxChars];

        await Send(new PipelineEvent
        {
            Type = "document",
            Message = file?.FileName ?? "Pasted text",
            DocumentChars = documentText.Length,
            Truncated = truncated,
        });

        var request = new AnalysisRequest(
            DocumentText: documentText,
            Instructions: instructions,
            Prompts: AgentPrompts.LoadDefaults(env).WithOverrides(form["plannerPrompt"], form["analystPrompt"], form["reviewerPrompt"]),
            MaxReviewRounds: int.TryParse(form["maxReviewRounds"], out int r) ? Math.Clamp(r, 0, 5) : options.Value.MaxReviewRounds,
            RunEvaluation: form["runEvaluation"] != "false",
            ExpectedAnswer: form["expectedAnswer"]);

        await foreach (PipelineEvent e in pipeline.RunAsync(request, ct))
        {
            await Send(e);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Browser went away; nothing to do.
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Analysis failed");
        await Send(new PipelineEvent { Type = "error", Message = ex.Message });
    }
}).DisableAntiforgery();

app.Run();
