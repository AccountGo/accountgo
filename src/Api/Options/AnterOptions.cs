#nullable enable

namespace Api.Options
{
    /// <summary>
    /// Configuration for the Anter external agent-runner used by AccountGo Assistant.
    /// </summary>
    public class AnterOptions
    {
        public const string SectionName = "Anter";

        /// <summary>Anter agents host (default https://agents.anter.ai).</summary>
        public string BaseUrl { get; set; } = "https://agents.anter.ai";

        /// <summary>Project API key. Never expose to the browser.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Optional project id header (x-project-id).</summary>
        public string? ProjectId { get; set; }

        /// <summary>Agent slug (default accountgo-assistant).</summary>
        public string AgentSlug { get; set; } = "accountgo-assistant";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(AgentSlug);
    }
}
