namespace Orynivo.Scrobbling;

/// <summary>Describes a track submitted to Last.fm.</summary>
/// <param name="Artist">Primary track artist.</param>
/// <param name="Title">Track title.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Duration">Known duration in seconds, or <see langword="null"/>.</param>
public sealed record LastFmTrack(string Artist, string Title, string? Album, int? Duration);

/// <summary>Describes an authorized Last.fm session.</summary>
/// <param name="Username">The authenticated Last.fm username.</param>
/// <param name="SessionKey">The session key used to sign authenticated requests.</param>
public sealed record LastFmSession(string Username, string SessionKey);
