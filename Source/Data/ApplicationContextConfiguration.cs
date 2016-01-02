using System.Data.Entity;
using System.Data.Entity.Core.Common;

using EFCache;

namespace Data
{
    //support second level caching
    public class Configuration : DbConfiguration
    {
        //possible to use Framework.Cache.AspNetCache
        // First-Level Caching Happens Inside a Transactional Context(Session) and Second-Level Caching Is External(Session Factory)
        //https://msdn.microsoft.com/en-us/magazine/hh394143.aspx
        private readonly InMemoryCache Cache = new InMemoryCache();
        //private readonly ICache Cache = new AspNetCache();
        
        public Configuration()
        {
            var transactionHandler = new CacheTransactionHandler(Cache);

            AddInterceptor(transactionHandler);

            Loaded +=
              (sender, args) => args.ReplaceService<DbProviderServices>(
                (s, _) => new CachingProviderServices(s, transactionHandler));
        }
    }
}
