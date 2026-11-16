#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Financial
{
    /// <summary>Who prepared and who posted a manual journal, for members who may see record history.</summary>
    public class JournalAttribution
    {
        /// <summary>Everyone who saved the journal as a draft, earliest first.</summary>
        public List<JournalActor> PreparedBy { get; set; } = new();
        public string? PostedByUserId { get; set; }
        public string? PostedByName { get; set; }
        /// <summary>When the journal was posted (UTC). Null for a draft, or when it was posted before history was kept.</summary>
        public DateTime? PostedAt { get; set; }
        /// <summary>True when the person who posted also prepared it. Null when who posted is not known.</summary>
        public bool? PostedByAPreparer { get; set; }
    }

    public class JournalActor
    {
        public string UserId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTime FirstAt { get; set; }
        public DateTime LastAt { get; set; }
    }
}
