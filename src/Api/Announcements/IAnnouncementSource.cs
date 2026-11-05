// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Announcements;

/// <summary>Optional announcement providers. The public host registers none; self-host returns an empty list.</summary>
public interface IAnnouncementSource
{
    IReadOnlyList<AnnouncementDto> GetAnnouncements(AnnouncementContext ctx);
}

public sealed record AnnouncementContext(string UserId, int CompanyId, string Role);

public sealed record AnnouncementActionDto(string Label, string Href);

public sealed record AnnouncementDto(
    string Id,
    string Severity,
    string Message,
    AnnouncementActionDto? Action,
    bool Dismissible,
    string Audience,
    DateTimeOffset? ExpiresAt);
