using DocumentAnalyser.Web.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace DocumentAnalyser.Web.Evaluation;

public sealed record EvaluationScore(
    string Name,
    double? Value,
    string? Rating,
    bool Failed,
    string? Reason,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Scores the final answer with the Microsoft.Extensions.AI.Evaluation "quality" evaluators.
/// Each evaluator is an LLM-as-judge: it sends the question + answer to the evaluator model
/// with a scoring rubric and gets back a 1-5 score and a reason.
/// These are the same metrics Foundry's built-in evaluators use (relevance, coherence, groundedness...).
/// </summary>
public sealed class AnswerEvaluator(FoundryClientFactory foundry)
{
    public async Task<IReadOnlyList<EvaluationScore>> EvaluateAsync(
        string instructions, string documentText, string answer, string? expectedAnswer, CancellationToken ct)
    {
        var evaluators = new List<IEvaluator>
        {
            new RelevanceEvaluator(),     // Does the answer address the request?
            new CoherenceEvaluator(),     // Is it logically structured and easy to follow?
            new FluencyEvaluator(),       // Is the language well written?
            new GroundednessEvaluator(),  // Is every claim supported by the document?
        };

        var context = new List<EvaluationContext> { new GroundednessEvaluatorContext(documentText) };

        // If you supply an expected ("ground truth") answer in the UI, we can also check it against that.
        if (!string.IsNullOrWhiteSpace(expectedAnswer))
        {
            evaluators.Add(new CompletenessEvaluator());   // Does it cover everything in the expected answer?
            evaluators.Add(new EquivalenceEvaluator());    // Does it mean the same as the expected answer?
            context.Add(new CompletenessEvaluatorContext(expectedAnswer));
            context.Add(new EquivalenceEvaluatorContext(expectedAnswer));
        }

        IEvaluator composite = new CompositeEvaluator(evaluators);
        var chatConfiguration = new ChatConfiguration(foundry.CreateEvaluatorChatClient());

        // The "conversation" being judged: the user's request and the model's final response.
        var messages = new[] { new ChatMessage(ChatRole.User, instructions) };
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

        EvaluationResult result = await composite.EvaluateAsync(messages, response, chatConfiguration, context, ct);

        return result.Metrics.Values
            .Select(m => new EvaluationScore(
                m.Name,
                (m as NumericMetric)?.Value,
                m.Interpretation?.Rating.ToString(),
                m.Interpretation?.Failed ?? false,
                m.Reason,
                m.Diagnostics?.Select(d => $"{d.Severity}: {d.Message}").ToList() ?? []))
            .ToList();
    }
}
