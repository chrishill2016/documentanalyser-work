using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentAnalyser.Web.Evaluation;
using DocumentAnalyser.Web.Foundry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DocumentAnalyser.Web.Agents;

public sealed record AnalysisRequest(
    string DocumentText,
    string Instructions,
    AgentPrompts Prompts,
    int MaxReviewRounds,
    bool RunEvaluation,
    string? ExpectedAnswer);

/// <summary>
/// The multi-agent workflow. Kept as plain, sequential C# on purpose so it's easy to follow:
///
///   Planner ──► Analyst ──► Reviewer ──(not approved, rounds left)──► Analyst (revise) ──► Reviewer ...
///                                     └─(approved / out of rounds)──► Evaluators ──► final answer
///
/// Each agent is a Microsoft Agent Framework <see cref="AIAgent"/> backed by a model deployed in Foundry.
/// </summary>
public sealed class AnalysisPipeline(FoundryClientFactory foundry, AnswerEvaluator evaluator)
{
    public async IAsyncEnumerable<PipelineEvent> RunAsync(
        AnalysisRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        IChatClient chatClient = foundry.CreateAgentChatClient();

        // 1. Create the agents. An agent = model + instructions (+ optionally tools, memory...).
        AIAgent planner = CreateAgent(chatClient, "Planner", request.Prompts.Planner);
        AIAgent analyst = CreateAgent(chatClient, "Analyst", request.Prompts.Analyst);
        AIAgent reviewer = CreateAgent(chatClient, "Reviewer", request.Prompts.Reviewer);

        string documentBlock = $"<document>\n{request.DocumentText}\n</document>";
        string instructionsBlock = $"<user_instructions>\n{request.Instructions}\n</user_instructions>";

        // 2. Planner: turn the user's instructions into a plan.
        string plannerPrompt = $"{instructionsBlock}\n\n{documentBlock}";
        yield return Running("Planner", 0, request.Prompts.Planner, plannerPrompt);
        var (plan, plannerEvent) = await RunTextAgentAsync(planner, "Planner", 0, request.Prompts.Planner, plannerPrompt, session: null, ct);
        yield return plannerEvent;

        // 3. Analyst: write the first draft. The analyst keeps a *session* (conversation history),
        //    so when it revises it remembers the document and its previous draft.
        AgentSession analystSession = await analyst.CreateSessionAsync(ct);
        string analystPrompt = $"{instructionsBlock}\n\n<plan>\n{plan}\n</plan>\n\n{documentBlock}";
        yield return Running("Analyst", 1, request.Prompts.Analyst, analystPrompt);
        var (draft, analystEvent) = await RunTextAgentAsync(analyst, "Analyst", 1, request.Prompts.Analyst, analystPrompt, analystSession, ct);
        yield return analystEvent;

        // 4. Review loop.
        for (int round = 1; round <= request.MaxReviewRounds + 1; round++)
        {
            // The reviewer gets no session: it judges each draft fresh, like an independent reviewer.
            string reviewerPrompt = $"{instructionsBlock}\n\n{documentBlock}\n\n<draft_answer>\n{draft}\n</draft_answer>";
            yield return Running("Reviewer", round, request.Prompts.Reviewer, reviewerPrompt);

            var sw = Stopwatch.StartNew();
            // RunAsync<T> asks the model for JSON matching ReviewVerdict ("structured output").
            AgentResponse<ReviewVerdict> review = await reviewer.RunAsync<ReviewVerdict>(reviewerPrompt, cancellationToken: ct);
            sw.Stop();
            ReviewVerdict verdict = review.Result;

            yield return Done("Reviewer", round, request.Prompts.Reviewer, reviewerPrompt, review.Text, sw, review.Usage, verdict);

            if (verdict.Approved || round > request.MaxReviewRounds)
            {
                break;
            }

            // Send the feedback back to the analyst in the same session.
            string revisionPrompt =
                $"The REVIEWER gave your draft {verdict.Score}/5 and did not approve it.\n\n" +
                $"Issues:\n{string.Join("\n", verdict.Issues.Select(i => $"- {i}"))}\n\n" +
                $"Feedback: {verdict.Feedback}\n\nRewrite the full answer, fixing every issue.";
            yield return Running("Analyst", round + 1, request.Prompts.Analyst, revisionPrompt);
            (draft, analystEvent) = await RunTextAgentAsync(analyst, "Analyst", round + 1, request.Prompts.Analyst, revisionPrompt, analystSession, ct);
            yield return analystEvent;
        }

        // 5. Evaluation: separate LLM-as-judge evaluators score the final answer.
        if (request.RunEvaluation)
        {
            yield return new PipelineEvent { Type = "evaluation", Status = "running" };
            IReadOnlyList<EvaluationScore> scores =
                await evaluator.EvaluateAsync(request.Instructions, request.DocumentText, draft, request.ExpectedAnswer, ct);
            yield return new PipelineEvent { Type = "evaluation", Status = "done", Scores = scores };
        }

        yield return new PipelineEvent { Type = "final", Output = draft };
    }

    private static AIAgent CreateAgent(IChatClient chatClient, string name, string instructions) =>
        chatClient
            .AsAIAgent(instructions: instructions, name: name)
            .AsBuilder()
            .UseOpenTelemetry(sourceName: FoundryClientFactory.TelemetrySource)
            .Build();

    private static async Task<(string Text, PipelineEvent Event)> RunTextAgentAsync(
        AIAgent agent, string name, int round, string instructions, string prompt, AgentSession? session, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        AgentResponse response = await agent.RunAsync(prompt, session, cancellationToken: ct);
        sw.Stop();
        return (response.Text, Done(name, round, instructions, prompt, response.Text, sw, response.Usage, review: null));
    }

    private static PipelineEvent Running(string agent, int round, string instructions, string prompt) => new()
    {
        Type = "agent", Agent = agent, Round = round, Status = "running",
        Instructions = instructions, Prompt = prompt,
    };

    private static PipelineEvent Done(
        string agent, int round, string instructions, string prompt, string output,
        Stopwatch sw, UsageDetails? usage, ReviewVerdict? review) => new()
    {
        Type = "agent", Agent = agent, Round = round, Status = "done",
        Instructions = instructions, Prompt = prompt, Output = output,
        DurationMs = sw.ElapsedMilliseconds,
        InputTokens = usage?.InputTokenCount, OutputTokens = usage?.OutputTokenCount,
        Review = review,
    };
}
