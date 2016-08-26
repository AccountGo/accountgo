namespace Dto
{
    public sealed class Singleton
    {
        Singleton()
        {
        }
        public string UserName { get; set; }
        private static readonly object padlock = new object();
        private static Singleton instance = null;
        public static Singleton Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (padlock)
                    {
                        if (instance == null)
                        {
                            instance = new Singleton();
                        }
                    }
                }
                return instance;
            }
        }
    }
}
