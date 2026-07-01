using System.Diagnostics;

namespace Orynivo;

/// <summary>
/// Provides optional startup diagnostic logging hooks for shared core components.
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>Gets or sets the sink that receives startup diagnostic messages.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Writes a startup diagnostic message when a sink is configured.</summary>
    /// <param name="message">The message to write.</param>
    public static void Write(string message)
    {
        try { Log?.Invoke(message); }
        catch { }
    }

    /// <summary>Starts a timed startup diagnostic scope.</summary>
    /// <param name="name">The scope name.</param>
    /// <returns>An object that writes the elapsed time when disposed.</returns>
    public static IDisposable Time(string name)
    {
        Write($"{name} started");
        return new TimingScope(name);
    }

    private sealed class TimingScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _disposed;

        public TimingScope(string name) => _name = name;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _stopwatch.Stop();
            Write($"{_name} finished in {_stopwatch.ElapsedMilliseconds:N0} ms");
        }
    }
}
