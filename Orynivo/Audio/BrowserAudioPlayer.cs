using Orynivo.Remote;
using Microsoft.AspNetCore.Http;

namespace Orynivo.Audio;

/// <summary>Device-free player whose actual progress and completion come from the owning browser.</summary>
internal sealed class BrowserAudioPlayer : IAudioPlayer
{
    private readonly object _sync = new();
    private readonly GaplessPlaybackItem _item;
    private readonly string _clientId;
    private readonly TimeProvider _clock;
    private readonly Func<GaplessPlaybackItem, TimeSpan, float, Stream, CancellationToken, Task> _encode;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _writer = new(1);
    private readonly ITimer _leaseTimer;
    private TaskCompletionSource _idle = CompletedIdle();
    private CancellationTokenSource? _connection;
    private string _streamId = Guid.NewGuid().ToString("N");
    private TimeSpan _position, _streamStart;
    private long _reportedAt, _lastSeen, _controlRevision;
    private float _volume = 1, _gain = 1;
    private bool _paused, _audible, _disposed;
    private bool _direct, _pausedByLease;
    private string? _directMime;
    private readonly bool _live;
    private TimeSpan _duration;
    private readonly BrowserAudioEncoding _encoding;
    private readonly bool _allowFallback;
    private int _connections;

    /// <summary>Creates a device-free player with optional synthetic encoder and clock adapters.</summary>
    /// <param name="clientId">Owning page instance.</param>
    /// <param name="item">Desktop-resolved source.</param>
    /// <param name="duration">Logical duration, or zero for a live stream.</param>
    /// <param name="encode">Optional isolated encoder adapter.</param>
    /// <param name="clock">Optional test clock.</param>
    /// <param name="direct">Whether to relay an already compatible source.</param>
    /// <param name="directMime">Known source media type, or null for header detection.</param>
    /// <param name="live">Whether this is a live radio source that must never seek or infer a finite duration.</param>
    /// <param name="encoding">Selected music encoding.</param>
    /// <param name="allowFallback">Whether unsupported direct media may be converted.</param>
    internal BrowserAudioPlayer(string clientId, GaplessPlaybackItem item, TimeSpan duration,
        Func<GaplessPlaybackItem, TimeSpan, float, Stream, CancellationToken, Task>? encode = null, TimeProvider? clock = null,
        bool direct = false, string? directMime = null, bool live = false, BrowserAudioEncoding? encoding = null, bool allowFallback = true)
    {
        _clientId = clientId; _item = item; _duration = duration; _live = live;
        _encoding = encoding ?? BrowserAudioEncoding.Find("mp3-192")!;
        _allowFallback = allowFallback;
        _encode = encode ?? ((source, position, gain, output, ct) => BrowserAudioTranscoder.WriteEncodedAsync(source, position, gain, output, ct, _encoding));
        _direct = direct; _directMime = directMime;
        _clock = clock ?? TimeProvider.System;
        _lastSeen = _reportedAt = _clock.GetTimestamp();
        _leaseTimer = _clock.CreateTimer(_ => CheckLease(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc/>
    public TimeSpan Duration { get { lock (_sync) return _duration; } }
    /// <inheritdoc/>
    public bool CanSeek => !_live && Duration > TimeSpan.Zero;
    /// <inheritdoc/>
    public TimeSpan Position { get { lock (_sync) return CurrentPosition(); } }
    /// <inheritdoc/>
    public bool IsPaused { get { lock (_sync) return _paused; } }
    /// <summary>Gets explicit user pause, excluding an automatic disconnected-page pause.</summary>
    internal bool UserPaused { get { lock (_sync) return _paused && !_pausedByLease; } }
    /// <summary>Gets the actual delivered audio format instead of the source decoder format.</summary>
    internal string OutputFormat { get { lock (_sync) return _direct ? BrowserAudioRelay.Format(_directMime) : _encoding.Description; } }
    /// <inheritdoc/>
    public float Volume
    {
        get { lock (_sync) return _volume; }
        set { lock (_sync) { var next = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1; if (next != _volume) { _volume = next; _controlRevision++; } } }
    }
    /// <inheritdoc/>
    public float ReplayGainFactor
    {
        get { lock (_sync) return _gain; }
        set
        {
            CancellationTokenSource? connection = null;
            lock (_sync)
            {
                var next = float.IsFinite(value) ? Math.Clamp(value, 0, 16) : 1;
                if (next != _gain) { _gain = next; if (!_direct) connection = Rotate(CurrentPosition()); }
            }
            Cancel(connection);
        }
    }

    /// <summary>Captures the safe browser control state.</summary>
    /// <returns>One consistent command and stream revision.</returns>
    internal BrowserAudioState Snapshot()
    {
        lock (_sync) return new(_clientId, _disposed ? null : _streamId, _direct ? 0 : _streamStart.TotalSeconds, _paused, _volume,
            _controlRevision, _direct, _direct ? _streamStart.TotalSeconds : 0, OutputFormat);
    }

    /// <inheritdoc/>
    public void Pause() { lock (_sync) { _pausedByLease = false; _position = CurrentPosition(); _audible = false; if (!_paused) { _paused = true; _controlRevision++; } } }
    /// <inheritdoc/>
    public void Resume() { lock (_sync) { _pausedByLease = false; if (_paused) { _paused = false; _controlRevision++; } _lastSeen = _clock.GetTimestamp(); } }
    /// <inheritdoc/>
    public Task SeekAsync(TimeSpan position)
    {
        CancellationTokenSource? connection;
        lock (_sync) connection = Rotate(CanSeek ? TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds)) : TimeSpan.Zero);
        Cancel(connection);
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public Task WaitForCompletionAsync() => _completion.Task;

    /// <summary>Accepts progress and completion only from the current page, source and command revision.</summary>
    /// <param name="report">Finite, source-bound media feedback.</param>
    /// <returns>Whether the feedback belongs to this player.</returns>
    internal bool Report(BrowserAudioReport report)
    {
        CancellationTokenSource? retry = null;
        lock (_sync)
        {
            if (_disposed || report.ClientId != _clientId || report.StreamId != _streamId || report.ControlRevision != _controlRevision ||
                !double.IsFinite(report.PositionSeconds) || report.PositionSeconds < 0) return false;
            if (_direct && !_live && report.DurationSeconds is { } duration && double.IsFinite(duration) && duration > 0 && duration < TimeSpan.MaxValue.TotalSeconds / 2)
                _duration = TimeSpan.FromSeconds(duration);
            _position = TimeSpan.FromSeconds(CanSeek ? Math.Min(report.PositionSeconds, Duration.TotalSeconds) : Math.Min(report.PositionSeconds, TimeSpan.MaxValue.TotalSeconds / 2));
            _lastSeen = _reportedAt = _clock.GetTimestamp();
            _audible = report.Playing && !_paused;
            if (report.Failed && _direct && _allowFallback) { _direct = false; retry = Rotate(_live ? TimeSpan.Zero : _position); }
            else if (report.Failed) _completion.TrySetException(new InvalidOperationException(Localization.LocalizationManager.Current.PlaybackStopped));
            else if (report.Ended) _completion.TrySetResult();
        }
        Cancel(retry);
        return true;
    }

    /// <summary>Writes the current transport with correct content type and native byte-range support.</summary>
    /// <param name="streamId">Requested source revision.</param>
    /// <param name="context">Authenticated HTTP response.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>Completion after relay or encoder cleanup.</returns>
    internal Task WriteResponseAsync(string streamId, HttpContext context, CancellationToken ct) =>
        WriteCoreAsync(streamId, context.Response.Body, ct, async (start, gain, token) =>
        {
            bool direct; string? mime;
            lock (_sync) { direct = _direct; mime = _directMime; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
            if (direct)
                await BrowserAudioRelay.WriteAsync(_item.PlaybackPath, mime, context,
                    actual => { lock (_sync) if (_streamId == streamId) _directMime = actual; }, token).ConfigureAwait(false);
            else
            {
                context.Response.ContentType = _encoding.Mime;
                await context.Response.StartAsync(token).ConfigureAwait(false);
                await _encode(_item, start, gain, context.Response.Body, token).ConfigureAwait(false);
            }
        });

    /// <summary>Streams one revision, replacing duplicate connections and draining the encoder before returning.</summary>
    /// <param name="streamId">Opaque requested revision.</param>
    /// <param name="destination">Borrowed HTTP response stream.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>Completion after the encoder is stopped and disposed.</returns>
    internal Task WriteAsync(string streamId, Stream destination, CancellationToken ct) => WriteCoreAsync(streamId, destination, ct, null);

    private async Task WriteCoreAsync(string streamId, Stream destination, CancellationToken ct,
        Func<TimeSpan, float, CancellationToken, Task>? responseWriter)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        TimeSpan start; float gain;
        lock (_sync)
        {
            if (_disposed || streamId != _streamId) throw new OperationCanceledException();
            previous = _connection;
            current = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connection = current;
            start = _streamStart; gain = _gain;
            if (_connections++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Cancel(previous);
        var entered = false;
        try
        {
            await _writer.WaitAsync(current.Token).ConfigureAwait(false); entered = true;
            lock (_sync) if (_disposed || _streamId != streamId) throw new OperationCanceledException();
            if (responseWriter is not null) await responseWriter(start, gain, current.Token).ConfigureAwait(false);
            else await _encode(_item, start, gain, destination, current.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch
        {
            lock (_sync) if (!_disposed && _streamId == streamId && ReferenceEquals(_connection, current))
                _completion.TrySetException(new InvalidOperationException(Localization.LocalizationManager.Current.PlaybackStopped));
        }
        finally
        {
            if (entered) _writer.Release();
            lock (_sync)
            {
                if (ReferenceEquals(_connection, current)) _connection = null;
                if (--_connections == 0) _idle.TrySetResult();
                _lastSeen = _clock.GetTimestamp();
            }
            current.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Task idle; CancellationTokenSource? connection;
        lock (_sync)
        {
            _disposed = true; _audible = false; connection = _connection; idle = _idle.Task;
            _completion.TrySetCanceled();
        }
        _leaseTimer.Dispose(); Cancel(connection);
        idle.GetAwaiter().GetResult();
    }

    private TimeSpan CurrentPosition()
    {
        var position = _position + (_audible && !_paused ? _clock.GetElapsedTime(_reportedAt) : TimeSpan.Zero);
        return CanSeek && position > Duration ? Duration : position;
    }
    private CancellationTokenSource? Rotate(TimeSpan position)
    {
        _position = _streamStart = position; _audible = false; _streamId = Guid.NewGuid().ToString("N"); _controlRevision++;
        return _connection;
    }
    private void CheckLease()
    {
        CancellationTokenSource? connection = null;
        lock (_sync)
        {
            if (!_disposed && _connections == 0 && _clock.GetElapsedTime(_lastSeen) > TimeSpan.FromSeconds(30))
            { _position = CurrentPosition(); _audible = false; if (!_paused) { _paused = true; _pausedByLease = true; _controlRevision++; } connection = _connection; }
        }
        Cancel(connection);
    }
    private static void Cancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException) { }
    }
    private static TaskCompletionSource CompletedIdle() { var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); result.SetResult(); return result; }
}
