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
        /// workspaces. Kept independent of <see cref="SoloSetupEnabled"/> so enabling new
        /// business/freelance solo setup never opens legacy confirmation. Default false.
        /// </summary>
        public bool LegacySoloConfirmationEnabled { get; set; }

        public static WorkspaceExperienceOptions FromEnvironment(WorkspaceExperienceOptions? configured = null)
        {
            var options = configured ?? new WorkspaceExperienceOptions();
            options.SoloSetupEnabled = ReadBool("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", options.SoloSetupEnabled);
            options.PersonalPurposeEnabled = ReadBool("WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED", options.PersonalPurposeEnabled)
                && Core.Domain.EntityEquityMapping.IsIndividualMappingApproved;
            options.ControlledRecoveryEnabled = ReadBool("WORKSPACE_EXPERIENCE_CONTROLLED_RECOVERY_ENABLED", options.ControlledRecoveryEnabled);
            options.LegacySoloConfirmationEnabled = ReadBool("WORKSPACE_EXPERIENCE_LEGACY_SOLO_CONFIRMATION_ENABLED", options.LegacySoloConfirmationEnabled);
            return options;
        }

        private static bool ReadBool(string key, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var value))
                return value;

            return fallback;
        }
    }
}
