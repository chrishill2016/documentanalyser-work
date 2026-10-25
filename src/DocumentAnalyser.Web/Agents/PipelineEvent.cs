using DocumentAnalyser.Web.Evaluation;

namespace DocumentAnalyser.Web.Agents;

/// <summary>
/// One thing that happened during a run. These are streamed to the browser as they happen
/// (Server-Sent Events) so you can watch the agents work.
/// </summary>
public sealed class PipelineEvent
{
    /// <summary>"document", "agent", "evaluation", "final" or "error".</summary>
    public required string Type { get; init; }

    public string? Message { get; init; }

    // ---- "agent" events ----
    public string? Agent { get; init; }
    public int? Round { get; init; }
    /// <summary>"running" or "done".</summary>
    public string? Status { get; init; }
    public string? Instructions { get; init; }
    public string? Prompt { get; init; }
    public string? Output { get; init; }
    public long? DurationMs { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public ReviewVerdict? Review { get; init; }

    // ---- "document" events ----
    public int? DocumentChars { get; init; }
    public bool? Truncated { get; init; }

    // ---- "evaluation" events ----
    public IReadOnlyList<EvaluationScore>? Scores { get; init; }
}

/// <summary>Structured output we ask the reviewer agent to return (JSON schema is generated from this type).</summary>
public sealed class ReviewVerdict
{
    public bool Approved { get; set; }
    /// <summary>1 (unusable) to 5 (excellent).</summary>
    public int Score { get; set; }
    public List<string> Issues { get; set; } = [];
    public string Feedback { get; set; } = "";
}
