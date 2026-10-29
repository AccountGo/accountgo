// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace Api.Data
{
    public class DataProtectionKeyEntity
    {
        public int Id { get; set; }
        public string FriendlyName { get; set; } = string.Empty;
        public string Xml { get; set; } = string.Empty;
    }
}
