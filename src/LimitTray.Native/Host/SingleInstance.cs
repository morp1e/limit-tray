namespace LimitTray.Native.Host;

/// <summary>
/// One process per data directory: two copies would poll the same account twice and
/// double the chance of a 429, and they would race on history.json.
/// </summary>
internal static class SingleInstance
{
    /// <summary>
    /// FNV-1a over the upper-cased full path. A name needs no cryptographic hash, and
    /// SHA-256 would pull the crypto stack and bcrypt.dll into a process kept small on purpose.
    /// </summary>
    public static string MutexName(string dataDirectory)
    {
        var fullPath = Path.GetFullPath(dataDirectory).ToUpperInvariant();
        var hash = 14695981039346656037UL;
        foreach (var c in fullPath)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return "LimitTray-" + hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static IDisposable? TryAcquire(string dataDirectory)
    {
        var mutex = new Mutex(false, MutexName(dataDirectory));
        try
        {
            try
            {
                if (!mutex.WaitOne(0))
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous process exited without releasing; ownership passed to us.
            }

            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _mutex, null);
            if (owned is null) return;
            owned.ReleaseMutex();
            owned.Dispose();
        }
    }
}
