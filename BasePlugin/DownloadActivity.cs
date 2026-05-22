using System.Threading;

namespace BasePlugins
{
    /// <summary>
    /// Tracks active media downloads so background reconnect does not abort transfers.
    /// </summary>
    public static class DownloadActivity
    {
        private static int _active;

        public static int ActiveCount => Volatile.Read(ref _active);

        public static void Enter() => Interlocked.Increment(ref _active);

        public static void Leave() => Interlocked.Decrement(ref _active);
    }
}
