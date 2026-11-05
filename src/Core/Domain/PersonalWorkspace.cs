#nullable enable
using System;

namespace Core.Domain;

/// <summary>Registers the one personal-books workspace owned by a person.</summary>
public sealed class PersonalWorkspace : BaseEntity
{
    public string OwnerUserId { get; set; } = "";
    public int CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>An owner's link from work books to their personal books.</summary>
public sealed class WorkspaceLink : BaseEntity
{
    public string OwnerUserId { get; set; } = "";
    public int WorkCompanyId { get; set; }
    public int PersonalCompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
