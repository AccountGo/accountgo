#nullable enable
using System.Collections.Generic;

namespace Api.Service.Import
{
    /// <summary>
    /// Pluggable COA file format adapter. Production adapters set <see cref="IsInstalled"/> true
    /// and implement <see cref="MapToCanonical"/>. Detection stubs set <see cref="IsInstalled"/> false.
    /// </summary>
    public interface ICoaFormatAdapter
    {
        string FormatKey { get; }
        string DisplayName { get; }
        bool IsInstalled { get; }

        /// <summary>Returns true when this adapter recognizes the CSV marker and/or header signature.</summary>
        bool CanHandle(IReadOnlyList<IReadOnlyList<string>> rows);

        /// <summary>
        /// Maps a recognized file into canonical rows. Only called when <see cref="IsInstalled"/> is true.
        /// </summary>
        CoaAdapterResult MapToCanonical(IReadOnlyList<IReadOnlyList<string>> rows);
    }
}
