namespace WebBlazor.Enums
{
    public class PageRoutes
    {
        public static class Financials
        {
            const string baseUrl = "financials";

            public  const string Accounts = $"{baseUrl}/accounts";
        }

        public static class Inventory
        {
            const string baseUrl = "iventory";
            public const string Items = $"{baseUrl}/items";
            public const string CreateItem = $"{baseUrl}/createitem";
        }

        public static class Vendor
        {
            const string baseUrl = "vendor";
            public const string CreatePO = $"{baseUrl}/createpo";
        }
    }
}
