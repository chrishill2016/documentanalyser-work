# Document Analyser: a Foundry multi-agent hackathon starter

A small ASP.NET Core (csharp) web app. You upload a document, type some instructions, and a team of AI agents
running on a model in **Microsoft Foundry** (formerly Azure AI Foundry) processes them. Each agent
step and the evaluation scores appear in the browser.

```
            ┌──────────┐    plan    ┌──────────┐   draft   ┌──────────┐
 document ─►│ Planner  │──────────► │ Analyst  │─────────► │ Reviewer │──approved──► Evaluators ──► final answer
 + instr.   └──────────┘            └──────────┘ ◄──────── └──────────┘              (LLM-as-judge)
                                                 feedback (up to N rounds)
```

- **Planner**: turns the instructions into a short step-by-step plan.
- **Analyst**: follows the plan and writes the answer. It keeps a *session* (conversation memory), so when
  it gets feedback it revises its own earlier draft.
- **Reviewer**: checks the draft against the document and returns **structured JSON**
  (`approved`, `score`, `issues`, `feedback`). If the draft isn't approved, it goes back to the Analyst.
- **Evaluators**: after the agents finish, separate "judge" prompts score the final answer 1–5 for
  Relevance, Coherence, Fluency and Groundedness (plus Completeness and Equivalence if you give an expected answer).

Everything is set up by hand on purpose, with no Bicep, azd or scripts, so you can see each part.

---

## 0. Prerequisites

| What | Notes |
|---|---|
| **Visual Studio 2026** with the *ASP.NET and web development* workload | The app targets **.NET 10**. On Visual Studio 2022 you need .NET 10 support. If you don't have it, change `<TargetFramework>` in `src/DocumentAnalyser.Web/DocumentAnalyser.Web.csproj` to `net8.0`. |
| An **Azure subscription** where you can create resources | You need *Contributor* (or Owner) on a resource group. |
| (Optional) **Azure CLI** | Only needed if you use Entra ID sign-in instead of an API key. |

## 1. Create a Foundry resource and project (portal, by hand)

1. Go to **https://ai.azure.com** and sign in.
2. Select **Create new** (or **+ New project**) and choose a **Foundry resource** / Foundry project.
3. Fill in:
   - **Project name**: e.g. `docanalyser-hack`
   - **Subscription / Resource group**: create a new RG such as `rg-docanalyser-hack` so it's easy to delete afterwards.
   - **Region**: a region with good model availability, e.g. *Sweden Central*, *East US 2* or *UK South*.
4. Select **Create** and wait a minute or two. This creates:
   - a **Foundry resource** (an Azure AI Services account that hosts models, keys and endpoints), and
   - a **project** inside it (a workspace for agents, evaluations, tracing and so on).

> 💡 To see the same thing from the Azure side, open https://portal.azure.com, find the resource group, and
> look at the resource that was created. The keys and endpoints are also there.

## 2. Deploy a model

1. In your Foundry project, open **Models + endpoints** (or **Discover → Models**).
2. Find **`gpt-4.1-mini`**, select **Deploy**, and pick a deployment type such as **Global Standard**.
3. Keep the **deployment name** as `gpt-4.1-mini`. The app uses the *deployment name*, not the model name.
4. (Optional) Deploy a second, stronger model such as `gpt-4.1` to act as the **evaluation judge**.
   Grading with a different model from the one that generated the answer is common practice.

> ⚠ The evaluators send `temperature = 0`, which some *reasoning* models (o-series, some gpt-5 variants)
> reject. Use a standard chat model such as gpt-4.1 / gpt-4.1-mini / gpt-4o for the judge.
> The agents themselves can use any chat model.

You can test the deployment straight away in the **Playground** ("Open in playground"). This is worth doing
first because it shows what a single raw model call does before any agents are involved.

## 3. Get the endpoint and key

On the Foundry project's **Overview** page (or *Azure portal → your Foundry resource → Keys and Endpoint*):

- **Azure OpenAI endpoint**, e.g. `https://docanalyser-hack-resource.openai.azure.com/`
- **API key**: Key 1

## 4. Configure the app

Clone the repo and open **`DocumentAnalyser.sln`** in Visual Studio.

Keep secrets out of git by using **User Secrets**: in Solution Explorer, right-click **DocumentAnalyser.Web →
Manage User Secrets** and paste:

```json
{
  "Foundry": {
    "Endpoint": "https://<your-resource>.openai.azure.com/",
    "ApiKey": "<key 1>",
    "ChatDeployment": "gpt-4.1-mini",
    "EvaluatorDeployment": ""
  }
}
```

The other settings, such as `MaxReviewRounds` and `MaxDocumentChars`, are in `appsettings.json`.

### Alternative: no keys, use your Azure login (Entra ID)

Leave `ApiKey` empty and the app uses `DefaultAzureCredential`, which picks up whoever is signed in to Visual Studio
(*Tools → Options → Azure Service Authentication*) or the Azure CLI (`az login`). You need a role on the
Foundry resource. In the Azure portal, open the resource → **Access control (IAM)** → **Add role assignment** →
**Azure AI User** (or **Cognitive Services OpenAI User**) → assign it to yourself. The role can take a few minutes to apply.

## 5. Run it

Press **F5**. A browser opens, and the green bar at the top shows which endpoint and deployments you're connected to.

1. Upload `samples/project-update.md`, or paste any text.
2. Keep the default instructions or write your own, then select **Run agents**.
3. Watch the timeline. Expand **System instructions** and **Prompt sent** on each card to see exactly what
   went to the model. Press F12 to see the raw events in the browser console.

Command line alternative: `dotnet run --project src/DocumentAnalyser.Web`

## 6. (Optional) See the traces in Foundry

The app emits OpenTelemetry traces for every agent run and model call. To see them in the portal:

1. In your Foundry project, open **Tracing** (under *Observability*). Connect or create an **Application Insights** resource.
2. Copy its **connection string** (Application Insights resource → Overview).
3. Add it to User Secrets:
   ```json
   "ApplicationInsights": { "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..." }
   ```
4. Restart, run the agents, and refresh **Tracing** after a minute or two. You'll see each agent's spans with
   prompts, responses, token counts and timings. Sensitive data (prompt text) capture is turned **on** for the
   hackathon in `FoundryClientFactory.cs`. Turn it off for anything real.

---

## Where things live

```
src/DocumentAnalyser.Web/
├── Program.cs                     ← web app setup + the two API endpoints (/api/config, /api/analyse)
├── appsettings.json               ← non-secret settings
├── Foundry/
│   ├── FoundryOptions.cs          ← the settings above
│   └── FoundryClientFactory.cs    ← the ONLY place that connects to Foundry (endpoint, key / Entra, deployment)
├── Agents/
│   ├── AnalysisPipeline.cs        ← ⭐ the multi-agent workflow: read this first
│   ├── AgentPrompts.cs            ← loads the agent instructions
│   └── PipelineEvent.cs           ← events streamed to the browser + the Reviewer's JSON shape
├── Evaluation/
│   └── AnswerEvaluator.cs         ← LLM-as-judge evaluators (Microsoft.Extensions.AI.Evaluation)
├── Documents/
│   └── DocumentTextExtractor.cs   ← .txt/.md/.pdf/.docx → plain text
├── Prompts/
│   ├── planner.md  analyst.md  reviewer.md   ← agent instructions, re-read on every run (edit while running!)
└── wwwroot/
    ├── index.html  app.js  site.css          ← plain HTML/JS UI, no build step
samples/project-update.md          ← a test document
```

### Libraries used

| Package | Why |
|---|---|
| `Azure.AI.OpenAI` + `Azure.Identity` | Talk to the model deployment in your Foundry resource (key or Entra ID). |
| `Microsoft.Agents.AI` (Microsoft Agent Framework) | The `AIAgent` abstraction: instructions, sessions (memory), structured output, telemetry. |
| `Microsoft.Extensions.AI.Evaluation.Quality` | Built-in LLM-as-judge evaluators (relevance, groundedness and so on). |
| `PdfPig`, `DocumentFormat.OpenXml` | Read PDF / Word files. |
| `Azure.Monitor.OpenTelemetry.AspNetCore` | Optional: send traces to Application Insights / Foundry Tracing. |

---

## Hackathon ideas

Start small and work up:

1. **Prompt engineering**: change the Reviewer to be much stricter (or more lenient) in the UI. How do the review
   rounds and the evaluation scores change?
2. **Break it on purpose**: ask about something that *isn't* in the document. Does the Reviewer catch the
   hallucination? Does Groundedness drop?
3. **Ground truth**: fill in *Expected answer* under Options and compare Completeness / Equivalence across prompts or models.
4. **Swap models**: deploy another model in Foundry and change `ChatDeployment`. Compare speed, tokens and scores.
   Or use a different judge with `EvaluatorDeployment`.
5. **Add an agent**: for example a *Fact Checker* that runs in parallel with the Reviewer, or a *Formatter* at the end.
   Copy the pattern in `AnalysisPipeline.cs`.
6. **Give an agent a tool**: pass `tools: [AIFunctionFactory.Create(MyMethod)]` to `AsAIAgent(...)`, e.g. a
   "look up page N" function so the Analyst doesn't need the whole document in its prompt.
7. **Use Foundry Agent Service**: create the agents *in Foundry* (portal → **Agents**) instead of in code, so they
   are stored server-side and visible in the portal. See the `Microsoft.Agents.AI.Foundry` / `Azure.AI.Projects` packages.
8. **Agent Framework Workflows**: rewrite `AnalysisPipeline` with `Microsoft.Agents.AI.Workflows`
   (graph-based orchestration: sequential, concurrent, hand-off, group chat).
9. **Evaluate in Foundry**: export a few runs (question, document, answer) to JSONL and run a **Foundry evaluation**
   from the portal (*Evaluation → New evaluation*). Compare its scores with the in-app ones.
10. **Content safety**: in Foundry, look at the content filter on your deployment and try to trigger it.

## Clean up

Delete the resource group (e.g. `rg-docanalyser-hack`) in the Azure portal when you're done.
That removes the Foundry resource, the model deployments and any Application Insights resource.
