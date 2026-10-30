// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Text;

namespace Services.Banking
{
    /// <summary>
    /// Decode statement file bytes: BOM (UTF-8 / UTF-16 LE/BE), else UTF-8, else Windows-1252 (#447).
    /// </summary>
    public static class StatementFileEncoding
    {
        static StatementFileEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static string Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes[3..]);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes[2..]); // UTF-16 LE

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes[2..]); // UTF-16 BE

            if (IsValidUtf8(bytes))
                return Encoding.UTF8.GetString(bytes);

            return Encoding.GetEncoding(1252).GetString(bytes);
        }

        /// <summary>True when every byte sequence is well-formed UTF-8 (including pure ASCII).</summary>
        public static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
        {
            var i = 0;
            while (i < bytes.Length)
            {
                var b = bytes[i];
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                int need;
                if ((b & 0xE0) == 0xC0) need = 1;
                else if ((b & 0xF0) == 0xE0) need = 2;
                else if ((b & 0xF8) == 0xF0) need = 3;
                else return false;

                if (i + need >= bytes.Length)
                    return false;
                // Overlong / invalid lead
                if (need == 1 && b < 0xC2)
                    return false;
                for (var j = 1; j <= need; j++)
                {
                    if ((bytes[i + j] & 0xC0) != 0x80)
                        return false;
                }
                i += 1 + need;
            }
            return true;
        }
    }
}
