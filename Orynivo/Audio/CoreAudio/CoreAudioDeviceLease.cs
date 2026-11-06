namespace Orynivo.Audio.CoreAudio;

/// <summary>
/// Owns a temporary device-rate change and optional hog mode. Callers must stop
/// native rendering before disposal and await disposal before another output starts.
/// This control-plane helper does not establish bit-perfect PCM rendering.
/// </summary>
internal sealed class CoreAudioDeviceLease : IAsyncDisposable
{
    private static readonly HashSet<string> ActiveDevices = new(StringComparer.Ordinal);
    private static readonly object AdmissionLock = new();
    private readonly ICoreAudioDeviceControl _device;
    private readonly string _uid;
    private readonly double _requestedRate;
    private readonly int _processId;
    private readonly object _disposeLock = new();
    private double _originalRate;
    private bool _rateRequested;
    private bool _hogRequested;
    private bool _restoreRate = true;
    private Task? _disposeTask;

    private CoreAudioDeviceLease(ICoreAudioDeviceControl device, double rate)
    {
        _device = device;
        _uid = device.Uid;
        _requestedRate = rate;
        _processId = Environment.ProcessId;
    }

    /// <summary>Acquires an exact advertised device rate and optional exclusive access.</summary>
    /// <param name="device">UID-bound native control or an isolated test adapter.</param>
    /// <param name="rate">Positive finite PCM rate in hertz.</param>
    /// <param name="exclusive">Whether hog mode is mandatory rather than best effort.</param>
    /// <param name="cancellationToken">Cancellation of startup, including rate confirmation.</param>
    /// <param name="restoreSampleRate">Whether successful playback restores the original rate on release.</param>
    /// <returns>A confirmed lease which must be asynchronously disposed.</returns>
    internal static async Task<CoreAudioDeviceLease> OpenAsync(
        ICoreAudioDeviceControl device,
        double rate,
        bool exclusive,
        CancellationToken cancellationToken = default,
        bool restoreSampleRate = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        ArgumentException.ThrowIfNullOrEmpty(device.Uid);
        cancellationToken.ThrowIfCancellationRequested();
        lock (AdmissionLock)
        {
            if (!ActiveDevices.Add(device.Uid))
                throw new InvalidOperationException();
        }

        var lease = new CoreAudioDeviceLease(device, rate);
        try
        {
            if (!device.IsAvailable || !device.SupportsRate(rate))
                throw new NotSupportedException();
            lease._originalRate = device.SampleRate;
            if (!double.IsFinite(lease._originalRate) || lease._originalRate <= 0)
                throw new IOException();
            // Never adopt or release another session's existing hog ownership,
            // including one obtained by other code in this same process.
            if (device.HogOwner != -1)
                throw new InvalidOperationException();
            cancellationToken.ThrowIfCancellationRequested();
            if (exclusive)
            {
                lease._hogRequested = true;
                device.SetHogOwner(lease._processId);
                if (device.HogOwner != lease._processId)
                    throw new IOException();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (device.SampleRate != rate)
            {
                lease._rateRequested = true;
                device.SetSampleRate(rate);
                await device.WaitForSampleRateAsync(rate, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!device.IsAvailable || device.SampleRate != rate ||
                (exclusive && device.HogOwner != lease._processId))
                throw new IOException();
            lease._restoreRate = restoreSampleRate;
            return lease;
        }
        catch (Exception startupFailure)
        {
            try { await lease.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    /// <summary>Restores owned state once, sharing completion across concurrent disposals.</summary>
    /// <returns>The complete restoration and hog-release operation.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            return new ValueTask(_disposeTask ??= ReleaseAsync());
    }

    private async Task ReleaseAsync()
    {
        List<Exception>? failures = null;
        try
        {
            try
            {
                if (_restoreRate && _rateRequested && _device.IsAvailable &&
                    (_device.HogOwner == -1 || _device.HogOwner == _processId) &&
                    (_device.SampleRate == _requestedRate || _device.SampleRate == _originalRate))
                {
                    _device.SetSampleRate(_originalRate);
                    // Startup cancellation must never cancel restoration.
                    await _device.WaitForSampleRateAsync(_originalRate, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception failure) { (failures ??= []).Add(failure); }
            try
            {
                if (_hogRequested && _device.IsAvailable && _device.HogOwner == _processId)
                {
                    _device.SetHogOwner(-1);
                    if (_device.HogOwner == _processId)
                        throw new IOException();
                }
            }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        finally
        {
            lock (AdmissionLock)
                ActiveDevices.Remove(_uid);
        }
        if (failures is not null)
            throw new AggregateException(failures);
    }
}
