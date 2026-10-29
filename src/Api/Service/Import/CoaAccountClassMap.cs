#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Service.Import
{
    public static class CoaAccountClassMap
    {
        public static readonly Dictionary<string, int> NameToId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Assets"] = 1,
            ["Asset"] = 1,
            ["Liabilities"] = 2,
            ["Liability"] = 2,
            ["Equity"] = 3,
            ["Equities"] = 3,
            ["Revenue"] = 4,
            ["Revenues"] = 4,
            ["Income"] = 4,
            ["Expenses"] = 5,
            ["Expense"] = 5,
            ["Closing accounts"] = 6,
            ["Closing"] = 6,
            ["Temporary"] = 6,
        };

        public static readonly Dictionary<int, string> IdToName = new()
        {
            [1] = "Assets",
            [2] = "Liabilities",
            [3] = "Equity",
            [4] = "Revenue",
            [5] = "Expenses",
            [6] = "Closing accounts",
        };

        public static bool TryResolve(string classVal, out int classId, out string className)
        {
            classId = 0;
            className = "Unknown";
            if (int.TryParse(classVal, out int parsedClassId) && IdToName.TryGetValue(parsedClassId, out var mappedName))
            {
                classId = parsedClassId;
                className = mappedName;
                return true;
            }
            if (NameToId.TryGetValue(classVal, out int idFromName))
            {
                classId = idFromName;
                className = IdToName[idFromName];
                return true;
            }
            return false;
        }
    }
}
