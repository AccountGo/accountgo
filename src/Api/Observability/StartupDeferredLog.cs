// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Api.Observability
{
    /// <summary>
    /// Collects log messages emitted during service registration (before the host is built)
    /// and flushes them through <see cref="ILogger"/> once logging is available.
    /// </summary>
    internal static class StartupDeferredLog
    {
        private static readonly object Gate = new();
        private static readonly List<Entry> Entries = new();

        private readonly record struct Entry(LogLevel Level, string Message, object?[] Args);

        public static void Add(LogLevel level, string message, params object?[] args)
        {
            lock (Gate)
            {
                Entries.Add(new Entry(level, message, args));
            }
        }

        public static void Flush(ILogger logger)
        {
            List<Entry> snapshot;
            lock (Gate)
            {
                snapshot = new List<Entry>(Entries);
                Entries.Clear();
            }

            foreach (var entry in snapshot)
            {
                logger.Log(entry.Level, entry.Message, entry.Args);
            }
        }
    }
}
