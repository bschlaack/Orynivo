using System.Text.Json;
using Orynivo.Library;
using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the compact remote Library Doctor wire contract.</summary>
public sealed class OrynivoLibraryDoctorContractTests
{
    /// <summary>Round-trips typed findings without embedding tracks or credentials.</summary>
    [Fact]
    public void Candidate_RoundTripsAsCompactJson()
    {
        var candidate = new OrynivoLibraryDoctorCandidate(
            "/music/album",
            12,
            LibraryDoctorSeverity.Warning,
            [new OrynivoLibraryDoctorFinding(
                "replaygain",
                LibraryDoctorSeverity.Warning,
                3,
                LibraryDoctorRepairCapability.MaintenanceAction)]);

        var json = JsonSerializer.Serialize(candidate);
        var restored = JsonSerializer.Deserialize<OrynivoLibraryDoctorCandidate>(json);

        Assert.NotNull(restored);
        Assert.Equal(12, restored.TrackCount);
        Assert.Equal("replaygain", Assert.Single(restored.Findings).Code);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tracks", json, StringComparison.OrdinalIgnoreCase);
    }
}
