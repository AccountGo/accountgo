#nullable enable

namespace Api.Service.Import
{
    public interface ICoaFormatAdapterRegistry
    {
        /// <summary>
        /// Detects format (Snapshot / AccountGo / Xero / QBO / unknown), then maps to canonical rows
        /// when an installed adapter is available.
        /// </summary>
        CoaAdapterResult Adapt(string csvContent);
    }
}
