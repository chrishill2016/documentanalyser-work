namespace DocumentAnalyser.Web.Agents;

/// <summary>The system instructions ("prompts") for each agent.</summary>
public sealed record AgentPrompts(string Planner, string Analyst, string Reviewer)
{
    /// <summary>
    /// Loads the defaults from the Prompts folder. They're read on every request, so you can edit
    /// the .md files while the app is running. The UI can also override them per run.
    /// </summary>
    public static AgentPrompts LoadDefaults(IWebHostEnvironment env)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(env.ContentRootPath, "Prompts", $"{name}.md"));
        return new AgentPrompts(Read("planner"), Read("analyst"), Read("reviewer"));
    }

    public AgentPrompts WithOverrides(string? planner, string? analyst, string? reviewer) => new(
        string.IsNullOrWhiteSpace(planner) ? Planner : planner,
        string.IsNullOrWhiteSpace(analyst) ? Analyst : analyst,
        string.IsNullOrWhiteSpace(reviewer) ? Reviewer : reviewer);
}
