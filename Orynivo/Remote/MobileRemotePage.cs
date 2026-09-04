namespace Orynivo.Remote;

/// <summary>Provides the self-contained responsive mobile remote from embedded resources.</summary>
internal static class MobileRemotePage
{
    /// <summary>Gets the remote document with offline script and four-language resources.</summary>
    internal static string Html { get; } = Read("Remote.MobileRemote.html")
        .Replace("/*REMOTE_WORDS*/", "const words = " + Read("Localization.MobileRemote.json") + ";", StringComparison.Ordinal)
        .Replace("/*REMOTE_SCRIPT*/", Read("Remote.MobileRemote.js"), StringComparison.Ordinal);

    private static string Read(string name)
    {
        using var stream = typeof(MobileRemotePage).Assembly.GetManifestResourceStream("Orynivo." + name)
            ?? throw new InvalidOperationException("Missing embedded mobile remote resource.");
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}
