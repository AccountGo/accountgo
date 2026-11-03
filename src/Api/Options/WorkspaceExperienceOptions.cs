// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Options
{
    /// <summary>
    /// Rollout switch for new solo/team workspace initialization and legacy confirmation.
    /// Disabling must not strand already initialized workspaces.
    /// </summary>
    public class WorkspaceExperienceOptions
    {
        public const string SectionName = "WorkspaceExperience";

        /// <summary>When false, new About initialization and legacy confirm are rejected; persisted policies still evaluate.</summary>
        public bool SoloSetupEnabled { get; set; }

        /// <summary>
        /// Personal purpose initialization requires a finance-approved Individual equity mapping.
        /// Kept false until EntityEquityMapping.IsIndividualMappingApproved is true.
        /// </summary>
        public bool PersonalPurposeEnabled { get; set; }

        /// <summary>Controlled recovery remains unavailable until recovery authority is product-approved.</summary>
        public bool ControlledRecoveryEnabled { get; set; }

        /// <summary>
        /// Governs the legacy "Set up for just me" confirmation and return-to-solo flows for existing
        /// workspaces. Kept independent of <see cref="SoloSetupEnabled"/> so enabling the new
        /// business/freelance solo canary never opens legacy confirmation. Default false.
        /// </summary>
        public bool LegacySoloConfirmationEnabled { get; set; }

        /// <summary>
        /// Trusted, operator-controlled enrollment for the new business/freelance solo canary audience.
        /// This is the ONLY audience gate for new-workspace initialization: a caller is admitted only when
        /// <see cref="SoloSetupEnabled"/> is true AND the caller's trusted subject (signup email or
        /// authenticated user id) is enrolled here. It never opens legacy confirmation or controlled
        /// recovery, and it is never populated from request fields, headers, browser flags, or
        /// unverified identity claims — only from server configuration / environment.
        /// </summary>
        public WorkspaceCanaryEnrollmentOptions Canary { get; set; } = new();

        public static WorkspaceExperienceOptions FromEnvironment(WorkspaceExperienceOptions? configured = null)
        {
            var options = configured ?? new WorkspaceExperienceOptions();
            options.SoloSetupEnabled = ReadBool("WorkspaceExperience__SoloSetupEnabled",
                "WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", options.SoloSetupEnabled);
            options.PersonalPurposeEnabled = ReadBool("WorkspaceExperience__PersonalPurposeEnabled",
                "WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED", options.PersonalPurposeEnabled)
                && Core.Domain.EntityEquityMapping.IsIndividualMappingApproved;
            options.ControlledRecoveryEnabled = ReadBool("WorkspaceExperience__ControlledRecoveryEnabled",
                "WORKSPACE_EXPERIENCE_CONTROLLED_RECOVERY_ENABLED", options.ControlledRecoveryEnabled);
            options.LegacySoloConfirmationEnabled = ReadBool("WorkspaceExperience__LegacySoloConfirmationEnabled",
                "WORKSPACE_EXPERIENCE_LEGACY_SOLO_CONFIRMATION_ENABLED", options.LegacySoloConfirmationEnabled);
            options.Canary ??= new WorkspaceCanaryEnrollmentOptions();
            options.Canary = WorkspaceCanaryEnrollmentOptions.FromEnvironment(options.Canary);
            return options;
        }

        private static bool ReadBool(string nestedKey, string flatKey, bool fallback)
        {
            var nested = Environment.GetEnvironmentVariable(nestedKey);
            if (!string.IsNullOrWhiteSpace(nested) && bool.TryParse(nested, out var nestedValue))
                return nestedValue;

            var flat = Environment.GetEnvironmentVariable(flatKey);
            if (!string.IsNullOrWhiteSpace(flat) && bool.TryParse(flat, out var flatValue))
                return flatValue;

            return fallback;
        }
    }

    /// <summary>
    /// Operator-controlled enrollment lists for the new business/freelance solo canary audience.
    /// Both lists are trusted server configuration. Emails cover the anonymous signup path (before an
    /// account or workspace exists); user ids cover the authenticated workspace-creation path. Values
    /// are compared case-insensitively; emails are trimmed and lower-cased to match the signup
    /// normalization (<c>email.Trim().ToLowerInvariant()</c>).
    /// </summary>
    public sealed class WorkspaceCanaryEnrollmentOptions
    {
        /// <summary>Enrolled signup emails. Empty by default — nobody is admitted even when setup is enabled.</summary>
        public IReadOnlyList<string> Emails { get; set; } = Array.Empty<string>();

        /// <summary>Enrolled authenticated user ids. Empty by default.</summary>
        public IReadOnlyList<string> UserIds { get; set; } = Array.Empty<string>();

        public static WorkspaceCanaryEnrollmentOptions FromEnvironment(WorkspaceCanaryEnrollmentOptions? configured = null)
        {
            var options = configured ?? new WorkspaceCanaryEnrollmentOptions();

            var envEmails = ReadList(
                "WorkspaceExperience__Canary__Emails",
                "WORKSPACE_EXPERIENCE_CANARY_EMAILS");
            if (envEmails != null)
                options.Emails = envEmails;

            var envUserIds = ReadList(
                "WorkspaceExperience__Canary__UserIds",
                "WORKSPACE_EXPERIENCE_CANARY_USER_IDS");
            if (envUserIds != null)
                options.UserIds = envUserIds;

            return options;
        }

        private static IReadOnlyList<string>? ReadList(string nestedKey, string flatKey)
        {
            var raw = Environment.GetEnvironmentVariable(nestedKey);
            if (string.IsNullOrWhiteSpace(raw))
                raw = Environment.GetEnvironmentVariable(flatKey);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return raw
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }
    }
}
