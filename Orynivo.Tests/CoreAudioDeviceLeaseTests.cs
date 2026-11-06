using Orynivo.Audio.CoreAudio;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Exercises Core Audio rate/hog ownership with isolated, device-free adapters.</summary>
public sealed class CoreAudioDeviceLeaseTests
{
    /// <summary>Confirmed startup preserves the original rate until release and cleans up once.</summary>
    /// <param name="exclusive">Whether exclusive ownership is required.</param>
    /// <returns>The asynchronous regression test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAndRelease_RestoresRateAndOwnedHogOnce(bool exclusive)
    {
        var device = new Device();
        var lease = await CoreAudioDeviceLease.OpenAsync(device, 96_000, exclusive);
        Assert.Equal(96_000, device.SampleRate);
        Assert.Equal(exclusive ? Environment.ProcessId : -1, device.HogOwner);
        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());
        Assert.Equal(48_000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
        Assert.Equal(new double[] { 96_000, 48_000 }, device.RateWrites);
        Assert.Equal(exclusive ? new[] { Environment.ProcessId, -1 } : [], device.HogWrites);
    }

    /// <summary>An already matching rate needs no rate writes or restoration.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task MatchingRate_IsUntouched()
    {
        var device = new Device();
        await using (await CoreAudioDeviceLease.OpenAsync(device, 48_000, true)) { }
        Assert.Empty(device.RateWrites);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Unsupported or invalid input cannot alter device state.</summary>
    /// <param name="rate">The rejected rate.</param>
    /// <returns>The asynchronous regression test.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(192_000)]
    public async Task RejectedRate_DoesNotMutate(double rate)
    {
        var device = new Device();
        await Assert.ThrowsAnyAsync<Exception>(() => CoreAudioDeviceLease.OpenAsync(device, rate, true));
        Assert.Empty(device.RateWrites);
        Assert.Empty(device.HogWrites);
        await using var retry = await CoreAudioDeviceLease.OpenAsync(device, 48_000, false);
    }

    /// <summary>Hog ownership cannot be adopted even when it belongs to the current process.</summary>
    /// <param name="sameProcess">Whether a prior owner is in this process.</param>
    /// <returns>The asynchronous regression test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingHog_IsNeverReleased(bool sameProcess)
    {
        var device = new Device { HogOwner = sameProcess ? Environment.ProcessId : int.MaxValue };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Empty(device.RateWrites);
        Assert.Empty(device.HogWrites);
    }

    /// <summary>Readback failure must not silently downgrade mandatory exclusive output.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task DeniedHog_RejectsBeforeRateChange()
    {
        var device = new Device { DenyHog = true };
        await Assert.ThrowsAsync<IOException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Empty(device.RateWrites);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Startup awaits observable confirmation and retains UID admission until cleanup finishes.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task HeldConfirmation_RejectsDuplicateUidAndWaitsForRestoration()
    {
        var device = new Device();
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.Wait = (rate, _) => rate == 96_000 ? confirmation.Task : restoration.Task;
        var opening = CoreAudioDeviceLease.OpenAsync(device, 96_000, true);
        Assert.False(opening.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CoreAudioDeviceLease.OpenAsync(new Device(device.Uid), 96_000, false));
        confirmation.SetResult();
        var lease = await opening;
        var release = lease.DisposeAsync().AsTask();
        Assert.False(release.IsCompleted);
        Assert.Equal(Environment.ProcessId, device.HogOwner);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CoreAudioDeviceLease.OpenAsync(new Device(device.Uid), 96_000, false));
        restoration.SetResult();
        await release;
        await using var retry = await CoreAudioDeviceLease.OpenAsync(new Device(device.Uid), 48_000, false);
    }

    /// <summary>Different UIDs do not block each other's preparation.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task DifferentDevices_CanOwnIndependentLeases()
    {
        await using var first = await CoreAudioDeviceLease.OpenAsync(new Device(), 96_000, true);
        await using var second = await CoreAudioDeviceLease.OpenAsync(new Device(), 96_000, true);
    }

    /// <summary>Cancellation after hardware mutation still restores with an uncancelled token.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task StartupCancellation_RestoresBeforeReturning()
    {
        var device = new Device();
        using var cancellation = new CancellationTokenSource();
        device.Wait = (rate, token) =>
        {
            if (rate == 96_000)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            Assert.False(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CoreAudioDeviceLease.OpenAsync(device, 96_000, true, cancellation.Token));
        Assert.Equal(48_000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
        await using var retry = await CoreAudioDeviceLease.OpenAsync(device, 48_000, false);
    }

    /// <summary>A pre-cancelled start never touches hardware.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task PreCancellation_DoesNotMutate()
    {
        var device = new Device();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CoreAudioDeviceLease.OpenAsync(
            device, 96_000, true, new CancellationToken(true)));
        Assert.Empty(device.RateWrites);
        Assert.Empty(device.HogWrites);
    }

    /// <summary>Rate confirmation failure rolls back instead of publishing unconfirmed output.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task ConfirmationTimeout_RollsBack()
    {
        var device = new Device { Wait = (rate, _) => rate == 96_000
            ? Task.FromException(new TimeoutException()) : Task.CompletedTask };
        await Assert.ThrowsAsync<TimeoutException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Equal(48_000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Device removal, replacement or another owner/rate must not be overwritten on release.</summary>
    /// <param name="change">The intervening external change.</param>
    /// <returns>The asynchronous regression test.</returns>
    [Theory]
    [InlineData("removed")]
    [InlineData("rate")]
    [InlineData("owner")]
    public async Task ExternalChanges_ArePreserved(string change)
    {
        var device = new Device();
        var lease = await CoreAudioDeviceLease.OpenAsync(device, 96_000, true);
        if (change == "removed") device.IsAvailable = false;
        if (change == "rate") device.SampleRate = 44_100;
        if (change == "owner") device.HogOwner = int.MaxValue;
        await lease.DisposeAsync();
        Assert.Equal(new double[] { 96_000 }, device.RateWrites);
        if (change == "owner") Assert.Equal(int.MaxValue, device.HogOwner);
        if (change == "rate") Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Failed restoration still attempts hog release and unblocks future admission.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task RestoreFailure_ReleasesHogAndReportsFailure()
    {
        var device = new Device();
        var lease = await CoreAudioDeviceLease.OpenAsync(device, 96_000, true);
        device.FailRestore = true;
        var first = lease.DisposeAsync().AsTask();
        var second = lease.DisposeAsync().AsTask();
        Assert.Same(first, second);
        await Assert.ThrowsAsync<AggregateException>(() => first);
        Assert.Equal(-1, device.HogOwner);
        await using var retry = await CoreAudioDeviceLease.OpenAsync(new Device(device.Uid), 48_000, false);
    }

    /// <summary>A setter that throws after changing hardware is still rolled back.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task SetterFailureAfterMutation_RollsBack()
    {
        var device = new Device { FailAfterRateChange = true };
        await Assert.ThrowsAsync<IOException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Equal(48_000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Missing hardware is rejected before any mutation.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task MissingDevice_DoesNotMutate()
    {
        var device = new Device { IsAvailable = false };
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Empty(device.RateWrites);
        Assert.Empty(device.HogWrites);
    }

    /// <summary>Partial hog acquisition is released even if its setter reports an error.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task HogSetterFailureAfterMutation_ReleasesOwnership()
    {
        var device = new Device { FailAfterHogChange = true };
        await Assert.ThrowsAsync<IOException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Equal(-1, device.HogOwner);
        Assert.Empty(device.RateWrites);
        Assert.Equal(new[] { Environment.ProcessId, -1 }, device.HogWrites);
    }

    /// <summary>A pending rate change is explicitly superseded by restoration after cancellation.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task CancelledUnconfirmedRate_ReassertsOriginalRate()
    {
        var device = new Device { DeferRateChange = true };
        using var cancellation = new CancellationTokenSource();
        device.Wait = (rate, token) =>
        {
            if (rate == 96_000)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            Assert.False(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CoreAudioDeviceLease.OpenAsync(device, 96_000, true, cancellation.Token));
        Assert.Equal(new double[] { 96_000, 48_000 }, device.RateWrites);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>A confirmation callback cannot publish a rate that readback does not confirm.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task UnchangedReadback_RejectsPublication()
    {
        var device = new Device { DeferRateChange = true };
        await Assert.ThrowsAsync<IOException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Equal(new double[] { 96_000, 48_000 }, device.RateWrites);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>Losing exclusive ownership during confirmation rejects startup and preserves its new owner.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task OwnershipLostDuringConfirmation_RejectsPublication()
    {
        var device = new Device();
        device.Wait = (_, _) =>
        {
            device.HogOwner = int.MaxValue;
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<IOException>(() => CoreAudioDeviceLease.OpenAsync(device, 96_000, true));
        Assert.Equal(int.MaxValue, device.HogOwner);
        Assert.Equal(new double[] { 96_000 }, device.RateWrites);
        Assert.Equal(new[] { Environment.ProcessId }, device.HogWrites);
    }

    /// <summary>The native adapter rejects foreign platforms before loading Apple frameworks.</summary>
    [Fact]
    public void NativeAdapter_IsInertOutsideMacOS()
    {
        if (OperatingSystem.IsMacOS()) return;
        Assert.Throws<PlatformNotSupportedException>(() => CoreAudioNativeDevice.GetOutputDevices());
        Assert.Throws<PlatformNotSupportedException>(() => CoreAudioNativeDevice.Resolve("fixture"));
    }

    /// <summary>Isolated HAL state with explicit completion/failure injection.</summary>
    /// <param name="uid">Optional identity shared by simulated competing sessions.</param>
    private sealed class Device(string? uid = null) : ICoreAudioDeviceControl
    {
        /// <inheritdoc/>
        public string Uid { get; } = uid ?? Guid.NewGuid().ToString("N");
        /// <inheritdoc/>
        public bool IsAvailable { get; set; } = true;
        /// <inheritdoc/>
        public double SampleRate { get; set; } = 48_000;
        /// <inheritdoc/>
        public int HogOwner { get; set; } = -1;
        /// <summary>Gets rate writes in invocation order.</summary>
        internal List<double> RateWrites { get; } = [];
        /// <summary>Gets hog writes in invocation order.</summary>
        internal List<int> HogWrites { get; } = [];
        /// <summary>Gets or sets whether exclusive acquisition is denied.</summary>
        internal bool DenyHog { get; set; }
        /// <summary>Gets or sets whether restoring the original rate fails.</summary>
        internal bool FailRestore { get; set; }
        /// <summary>Gets or sets whether startup mutation throws after changing the rate.</summary>
        internal bool FailAfterRateChange { get; set; }
        /// <summary>Gets or sets whether hog acquisition fails after mutation.</summary>
        internal bool FailAfterHogChange { get; set; }
        /// <summary>Gets or sets whether a nominal rate request remains unconfirmed.</summary>
        internal bool DeferRateChange { get; set; }
        /// <summary>Gets or sets the confirmation adapter.</summary>
        internal Func<double, CancellationToken, Task>? Wait { get; set; }
        /// <inheritdoc/>
        public bool SupportsRate(double rate) => rate is 44_100 or 48_000 or 96_000;
        /// <inheritdoc/>
        public void SetSampleRate(double rate)
        {
            RateWrites.Add(rate);
            if (FailRestore && rate == 48_000) throw new IOException();
            if (!DeferRateChange) SampleRate = rate;
            if (FailAfterRateChange && rate == 96_000) throw new IOException();
        }
        /// <inheritdoc/>
        public void SetHogOwner(int pid)
        {
            HogWrites.Add(pid);
            if (!DenyHog || pid == -1) HogOwner = pid;
            if (FailAfterHogChange && pid != -1) throw new IOException();
        }
        /// <inheritdoc/>
        public Task WaitForSampleRateAsync(double rate, CancellationToken cancellationToken) =>
            Wait?.Invoke(rate, cancellationToken) ?? Task.CompletedTask;
    }
}
