// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Api.Filters
{
    /// <summary>
    /// Marks a non-GET action that is safe on a lifecycle read-only module (exports, PDF/CSV generation).
    /// <see cref="RequireModuleAttribute"/> treats the action as <see cref="Core.Entitlements.ModuleAccess.Read"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AllowWhenReadOnlyAttribute : Attribute
    {
    }
}
