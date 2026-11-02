// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

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
}
