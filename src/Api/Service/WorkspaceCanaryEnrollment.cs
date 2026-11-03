// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Service
{
    /// <summary>
    /// Server-side canary gate for the new business/freelance solo audience.
    ///
    /// Eligibility is established ONLY from trusted operator configuration
    /// (<see cref="WorkspaceCanaryEnrollmentOptions"/>). Request bodies, headers, browser flags, and
    /// unverified identity claims never establish enrollment. This gate governs new-workspace
    /// initialization exclusively; it does not open legacy solo confirmation or controlled recovery,
    /// which remain governed by their own flags.
    /// </summary>
    public interface IWorkspaceCanaryEnrollment
    {
        /// <summary>True when new-workspace solo setup is enabled at all (the global rollout switch).</summary>
        bool SoloSetupEnabled { get; }

        /// <summary>
        /// True when the trusted subject is enrolled in the canary. Either identifier may be supplied:
        /// the normalized signup email (anonymous signup, before an account exists) or the authenticated
        /// user id. Enrollment status is independent of whether setup is enabled.
        /// </summary>
        bool IsEnrolled(string? email, string? userId);

        /// <summary>
        /// True only when <see cref="SoloSetupEnabled"/> is true AND the trusted subject is enrolled.
        /// This is the single admission decision for new-workspace initialization.
        /// </summary>
        bool IsNewBusinessSoloAudienceAdmitted(string? email, string? userId);
    }

    public sealed class WorkspaceCanaryEnrollment : IWorkspaceCanaryEnrollment
    {
        private readonly bool _soloSetupEnabled;
        private readonly HashSet<string> _emails;
        private readonly HashSet<string> _userIds;

        public WorkspaceCanaryEnrollment(IOptions<WorkspaceExperienceOptions> experienceOptions)
        {
            // Resolve from environment so deployment-time configuration is authoritative, consistent with
            // the other WorkspaceExperience flags.
            var experience = WorkspaceExperienceOptions.FromEnvironment(experienceOptions.Value);
            _soloSetupEnabled = experience.SoloSetupEnabled;

            var canary = experience.Canary ?? new WorkspaceCanaryEnrollmentOptions();

            // Emails are matched against the signup normalization: email.Trim().ToLowerInvariant().
            _emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var email in canary.Emails)
            {
                if (!string.IsNullOrWhiteSpace(email))
                    _emails.Add(email.Trim().ToLowerInvariant());
            }

            _userIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var userId in canary.UserIds)
            {
                if (!string.IsNullOrWhiteSpace(userId))
                    _userIds.Add(userId.Trim());
            }
        }

        public bool SoloSetupEnabled => _soloSetupEnabled;

        public bool IsEnrolled(string? email, string? userId)
        {
            if (!string.IsNullOrWhiteSpace(email)
                && _emails.Contains(email.Trim().ToLowerInvariant()))
                return true;

            if (!string.IsNullOrWhiteSpace(userId)
                && _userIds.Contains(userId.Trim()))
                return true;

            return false;
        }

        public bool IsNewBusinessSoloAudienceAdmitted(string? email, string? userId)
            => _soloSetupEnabled && IsEnrolled(email, userId);
    }
}
