#if ORYNIVO_LINUX
using System.Security.Cryptography;
using System.Text;
using Orynivo.Library;
using Tmds.DBus.Protocol;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Exposes Orynivo's transport through the Linux MPRIS D-Bus interface so
/// desktop media keys, panels, and applets can control playback and read the
/// current track, position, and capabilities.
/// </summary>
internal sealed class MprisMediaTransport : IPathMethodHandler, IDisposable
{
    /// <summary>Well-known bus name registered by the Orynivo media player.</summary>
    internal const string BusName = "org.mpris.MediaPlayer2.orynivo";

    private const string ObjectPathValue = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    private readonly object _gate = new();
    private DBusConnection? _connection;
    private WindowsMediaMetadata _metadata = new(string.Empty, string.Empty, string.Empty);
    private MediaPlaybackStatus _status = MediaPlaybackStatus.Stopped;
    private bool _canPrevious;
    private bool _canNext;
    private double _volume = 1.0;
    private long _positionMicros;
    private long _durationMicros;
    private string _trackId = "/org/orynivo/track/none";
    private DateTime _lastPositionUpdate = DateTime.MinValue;
    private bool _disposed;

    private MprisMediaTransport()
    {
    }

    /// <summary>Raised when MPRIS requests playback to start or resume.</summary>
    internal event Action? PlayRequested;

    /// <summary>Raised when MPRIS requests playback to pause.</summary>
    internal event Action? PauseRequested;

    /// <summary>Raised when MPRIS requests the previous queue item.</summary>
    internal event Action? PreviousRequested;

    /// <summary>Raised when MPRIS requests the next queue item.</summary>
    internal event Action? NextRequested;

    /// <summary>Raised when MPRIS requests playback to stop.</summary>
    internal event Action? StopRequested;

    /// <summary>Raised when MPRIS requests a new absolute playback position.</summary>
    internal event Action<TimeSpan>? PositionChangeRequested;

    /// <summary>Raised when MPRIS requests a new output volume.</summary>
    internal event Action<double>? VolumeChangeRequested;

    /// <summary>Gets the D-Bus object path served by this handler.</summary>
    public string Path => ObjectPathValue;

    /// <summary>Gets whether this handler also serves descendant paths.</summary>
    public bool HandlesChildPaths => false;

    /// <summary>
    /// Connects to the session bus and registers the MPRIS media player when a
    /// session bus is available; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <returns>The connected transport, or <see langword="null"/> without a session bus.</returns>
    internal static MprisMediaTransport? TryCreate()
    {
        if (DBusAddress.Session is null)
            return null;
        var transport = new MprisMediaTransport();
        _ = transport.InitializeAsync();
        return transport;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var address = DBusAddress.Session;
            if (address is null)
                return;
            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            connection.AddMethodHandler(this);
            var acquired = await connection.TryRequestNameAsync(BusName, RequestNameOptions.ReplaceExisting);
            if (!acquired)
            {
                connection.Dispose();
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    return;
                }
                _connection = connection;
            }
            _ = MonitorConnectionAsync(connection);
        }
        catch
        {
            // MPRIS is optional desktop integration and must never affect playback.
        }
    }

    private async Task MonitorConnectionAsync(DBusConnection connection)
    {
        try
        {
            await connection.DisconnectedAsync();
        }
        catch
        {
        }
        lock (_gate)
        {
            if (ReferenceEquals(_connection, connection))
                _connection = null;
        }
    }

    /// <summary>Publishes the metadata MPRIS should display for the audible item.</summary>
    /// <param name="metadata">Title, artist, album, and optional artwork.</param>
    internal void UpdateMetadata(WindowsMediaMetadata metadata)
    {
        lock (_gate)
        {
            _metadata = metadata;
            _trackId = BuildTrackId(metadata);
        }
        EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
        {
            ["Metadata"] = BuildMetadata()
        });
    }

    /// <summary>Updates which queue navigation commands MPRIS may expose.</summary>
    /// <param name="canGoPrevious">Whether a previous queue item is available.</param>
    /// <param name="canGoNext">Whether a next queue item is available.</param>
    internal void SetNavigationCapabilities(bool canGoPrevious, bool canGoNext)
    {
        lock (_gate)
        {
            _canPrevious = canGoPrevious;
            _canNext = canGoNext;
        }
        EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
        {
            ["CanGoPrevious"] = VariantValue.Bool(canGoPrevious),
            ["CanGoNext"] = VariantValue.Bool(canGoNext)
        });
    }

    /// <summary>Updates the playback status displayed by MPRIS.</summary>
    /// <param name="status">Current playback status.</param>
    internal void SetPlaybackStatus(MediaPlaybackStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }
        EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
        {
            ["PlaybackStatus"] = VariantValue.String(StatusText(status))
        });
    }

    /// <summary>Publishes the current output volume to MPRIS.</summary>
    /// <param name="volume">Linear volume from zero through one.</param>
    internal void SetVolume(double volume)
    {
        var bounded = Math.Clamp(volume, 0.0, 1.0);
        lock (_gate)
        {
            _volume = bounded;
        }
        EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
        {
            ["Volume"] = VariantValue.Double(bounded)
        });
    }

    /// <summary>
    /// Stores the current audible position and duration, throttled unless forced.
    /// </summary>
    /// <param name="position">Current audible playback position.</param>
    /// <param name="duration">Current item duration.</param>
    /// <param name="force">Whether to bypass the normal five-second throttle.</param>
    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastPositionUpdate < TimeSpan.FromSeconds(5))
            return;
        _lastPositionUpdate = now;
        lock (_gate)
        {
            _positionMicros = (long)Math.Max(0, position.TotalMicroseconds);
            _durationMicros = (long)Math.Max(0, duration.TotalMicroseconds);
        }
    }

    /// <summary>Marks the transport as stopped and clears the reported position.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _status = MediaPlaybackStatus.Stopped;
            _positionMicros = 0;
            _durationMicros = 0;
        }
        EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
        {
            ["PlaybackStatus"] = VariantValue.String("Stopped")
        });
    }

    /// <inheritdoc/>
    public ValueTask HandleMethodAsync(MethodContext context)
    {
        try
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml(new ReadOnlyMemory<byte>[]
                {
                    Encoding.UTF8.GetBytes(RootInterfaceXml),
                    Encoding.UTF8.GetBytes(PlayerInterfaceXml)
                });
                return default;
            }

            var iface = context.Request.InterfaceAsString ?? string.Empty;
            var member = context.Request.MemberAsString ?? string.Empty;

            if (iface == PropertiesInterface)
                HandleProperties(context, member);
            else if (iface == RootInterface)
                HandleRoot(context, member);
            else if (iface == PlayerInterface)
                HandlePlayer(context, member);
            else
                context.ReplyUnknownMethodError();
        }
        catch (Exception ex)
        {
            if (!context.ReplySent)
                context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message);
        }
        return default;
    }

    private void HandleRoot(MethodContext context, string member)
    {
        switch (member)
        {
            case "Raise":
            case "Quit":
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandlePlayer(MethodContext context, string member)
    {
        switch (member)
        {
            case "Next":
                NextRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "Previous":
                PreviousRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "Pause":
                PauseRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "Play":
                PlayRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "PlayPause":
                if (Status() == MediaPlaybackStatus.Playing)
                    PauseRequested?.Invoke();
                else
                    PlayRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "Stop":
                StopRequested?.Invoke();
                ReplyEmpty(context);
                break;
            case "Seek":
            {
                var offset = context.Request.GetBodyReader().ReadInt64();
                long basePosition;
                lock (_gate)
                {
                    basePosition = _positionMicros;
                }
                var target = Math.Max(0, basePosition + offset);
                PositionChangeRequested?.Invoke(TimeSpan.FromMicroseconds(target));
                EmitSeeked(target);
                ReplyEmpty(context);
                break;
            }
            case "SetPosition":
            {
                var reader = context.Request.GetBodyReader();
                var trackId = reader.ReadObjectPathAsString();
                var position = reader.ReadInt64();
                var matches = false;
                lock (_gate)
                {
                    matches = trackId == _trackId;
                    if (matches)
                        _positionMicros = Math.Max(0, position);
                }
                if (matches)
                {
                    PositionChangeRequested?.Invoke(TimeSpan.FromMicroseconds(Math.Max(0, position)));
                    EmitSeeked(Math.Max(0, position));
                }
                ReplyEmpty(context);
                break;
            }
            case "OpenUri":
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandleProperties(MethodContext context, string member)
    {
        var reader = context.Request.GetBodyReader();
        switch (member)
        {
            case "Get":
            {
                var iface = reader.ReadString();
                var property = reader.ReadString();
                var value = GetProperty(iface, property);
                if (value is null)
                {
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", property);
                    return;
                }
                using var writer = context.CreateReplyWriter("v");
                writer.WriteVariant(value.Value);
                context.Reply(writer.CreateMessage());
                break;
            }
            case "GetAll":
            {
                var iface = reader.ReadString();
                using var writer = context.CreateReplyWriter("a{sv}");
                var start = writer.WriteDictionaryStart();
                foreach (var entry in GetAll(iface))
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(entry.Key);
                    writer.WriteVariant(entry.Value);
                }
                writer.WriteDictionaryEnd(start);
                context.Reply(writer.CreateMessage());
                break;
            }
            case "Set":
            {
                var iface = reader.ReadString();
                var property = reader.ReadString();
                var value = reader.ReadVariantValue();
                if (!SetProperty(iface, property, value))
                {
                    context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", property);
                    return;
                }
                ReplyEmpty(context);
                break;
            }
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private VariantValue? GetProperty(string iface, string property)
    {
        var all = GetAll(iface);
        if (all.TryGetValue(property, out var value))
            return value;
        return null;
    }

    private Dict<string, VariantValue> GetAll(string iface)
    {
        if (iface == RootInterface)
        {
            return new Dict<string, VariantValue>
            {
                ["CanQuit"] = VariantValue.Bool(false),
                ["Fullscreen"] = VariantValue.Bool(false),
                ["CanSetFullscreen"] = VariantValue.Bool(false),
                ["CanRaise"] = VariantValue.Bool(false),
                ["HasTrackList"] = VariantValue.Bool(false),
                ["Identity"] = VariantValue.String("Orynivo"),
                ["DesktopEntry"] = VariantValue.String("orynivo"),
                ["SupportedUriSchemes"] = VariantValue.Array(Array.Empty<string>()),
                ["SupportedMimeTypes"] = VariantValue.Array(Array.Empty<string>())
            };
        }

        if (iface == PlayerInterface)
        {
            bool canPrevious;
            bool canNext;
            double volume;
            MediaPlaybackStatus status;
            lock (_gate)
            {
                canPrevious = _canPrevious;
                canNext = _canNext;
                volume = _volume;
                status = _status;
            }
            return new Dict<string, VariantValue>
            {
                ["PlaybackStatus"] = VariantValue.String(StatusText(status)),
                ["LoopStatus"] = VariantValue.String("None"),
                ["Rate"] = VariantValue.Double(1.0),
                ["Shuffle"] = VariantValue.Bool(false),
                ["Metadata"] = BuildMetadata(),
                ["Volume"] = VariantValue.Double(volume),
                ["Position"] = VariantValue.Int64(PositionMicros()),
                ["MinimumRate"] = VariantValue.Double(1.0),
                ["MaximumRate"] = VariantValue.Double(1.0),
                ["CanGoNext"] = VariantValue.Bool(canNext),
                ["CanGoPrevious"] = VariantValue.Bool(canPrevious),
                ["CanPlay"] = VariantValue.Bool(true),
                ["CanPause"] = VariantValue.Bool(true),
                ["CanSeek"] = VariantValue.Bool(true),
                ["CanControl"] = VariantValue.Bool(true)
            };
        }

        return new Dict<string, VariantValue>();
    }

    private bool SetProperty(string iface, string property, VariantValue value)
    {
        if (iface != PlayerInterface)
            return false;
        switch (property)
        {
            case "Volume":
                if (value.Type != VariantValueType.Double)
                    return false;
                var volume = Math.Clamp(value.GetDouble(), 0.0, 1.0);
                lock (_gate)
                {
                    _volume = volume;
                }
                VolumeChangeRequested?.Invoke(volume);
                EmitPropertiesChanged(PlayerInterface, new Dict<string, VariantValue>
                {
                    ["Volume"] = VariantValue.Double(volume)
                });
                return true;
            case "LoopStatus":
            case "Rate":
            case "Shuffle":
                return true;
            default:
                return false;
        }
    }

    private MediaPlaybackStatus Status()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    private long PositionMicros()
    {
        lock (_gate)
        {
            return _positionMicros;
        }
    }

    private static string StatusText(MediaPlaybackStatus status) => status switch
    {
        MediaPlaybackStatus.Playing => "Playing",
        MediaPlaybackStatus.Paused => "Paused",
        _ => "Stopped"
    };

    private Dict<string, VariantValue> BuildMetadata()
    {
        WindowsMediaMetadata metadata;
        long durationMicros;
        string trackId;
        lock (_gate)
        {
            metadata = _metadata;
            durationMicros = _durationMicros;
            trackId = _trackId;
        }

        var dict = new Dict<string, VariantValue>
        {
            ["mpris:trackid"] = VariantValue.ObjectPath(trackId),
            ["xesam:title"] = VariantValue.String(metadata.Title ?? string.Empty),
            ["xesam:artist"] = VariantValue.Array(new[] { metadata.Artist ?? string.Empty }),
            ["xesam:album"] = VariantValue.String(metadata.Album ?? string.Empty)
        };
        if (durationMicros > 0)
            dict["mpris:length"] = VariantValue.Int64(durationMicros);
        var artUrl = ResolveArtUrl(metadata);
        if (artUrl is not null)
            dict["mpris:artUrl"] = VariantValue.String(artUrl);
        return dict;
    }

    private static string? ResolveArtUrl(WindowsMediaMetadata metadata)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(metadata.ArtworkPath) && File.Exists(metadata.ArtworkPath))
                return new Uri(metadata.ArtworkPath).AbsoluteUri;
            if (metadata.ArtworkUri is not null &&
                QueuePathPolicy.CanPersist(metadata.ArtworkUri.AbsoluteUri))
            {
                return metadata.ArtworkUri.AbsoluteUri;
            }
        }
        catch
        {
        }
        return null;
    }

    private static string BuildTrackId(WindowsMediaMetadata metadata)
    {
        var key = $"{metadata.Title}\n{metadata.Artist}\n{metadata.Album}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return "/org/orynivo/track/" + hash.ToLowerInvariant();
    }

    private void EmitPropertiesChanged(string iface, Dict<string, VariantValue> changed)
    {
        DBusConnection? connection;
        lock (_gate)
        {
            connection = _connection;
        }
        if (connection is null)
            return;
        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null!, ObjectPathValue, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
            writer.WriteString(iface);
            var start = writer.WriteDictionaryStart();
            foreach (var entry in changed)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString(entry.Key);
                writer.WriteVariant(entry.Value);
            }
            writer.WriteDictionaryEnd(start);
            writer.WriteArray(Array.Empty<string>());
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch
        {
            // Property notifications are optional desktop UI state.
        }
    }

    private void EmitSeeked(long positionMicros)
    {
        DBusConnection? connection;
        lock (_gate)
        {
            connection = _connection;
        }
        if (connection is null)
            return;
        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null!, ObjectPathValue, PlayerInterface, "Seeked", "x");
            writer.WriteInt64(positionMicros);
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch
        {
            // Seek notifications are optional desktop UI state.
        }
    }

    private static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(string.Empty);
        context.Reply(writer.CreateMessage());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DBusConnection? connection;
        lock (_gate)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
        }
        try
        {
            connection?.Dispose();
        }
        catch
        {
            // Disposal remains best effort during application shutdown.
        }
    }

    private const string RootInterfaceXml = """
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/>
          <method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/>
          <property name="Fullscreen" type="b" access="readwrite"/>
          <property name="CanSetFullscreen" type="b" access="read"/>
          <property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/>
          <property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/>
          <property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        """;

    private const string PlayerInterfaceXml = """
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/>
          <method name="Previous"/>
          <method name="Pause"/>
          <method name="PlayPause"/>
          <method name="Stop"/>
          <method name="Play"/>
          <method name="Seek">
            <arg direction="in" name="Offset" type="x"/>
          </method>
          <method name="SetPosition">
            <arg direction="in" name="TrackId" type="o"/>
            <arg direction="in" name="Position" type="x"/>
          </method>
          <method name="OpenUri">
            <arg direction="in" name="Uri" type="s"/>
          </method>
          <signal name="Seeked">
            <arg name="Position" type="x"/>
          </signal>
          <property name="PlaybackStatus" type="s" access="read"/>
          <property name="LoopStatus" type="s" access="readwrite"/>
          <property name="Rate" type="d" access="readwrite"/>
          <property name="Shuffle" type="b" access="readwrite"/>
          <property name="Metadata" type="a{sv}" access="read"/>
          <property name="Volume" type="d" access="readwrite"/>
          <property name="Position" type="x" access="read"/>
          <property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/>
          <property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/>
          <property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/>
          <property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read"/>
        </interface>
        """;
}
#endif
