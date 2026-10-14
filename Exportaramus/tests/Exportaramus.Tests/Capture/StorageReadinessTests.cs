using Exportaramus.Features.CollectionData.Capture;

namespace Exportaramus.Tests.Capture;

public sealed class StorageReadinessTests
{
    [Fact]
    public void ExactStorageKeysResetOnIdentityValidationAndReadinessLossBeforeReopening()
    {
        var policy = new StorageReadinessPolicy(); var observed = new List<string>();
        (bool, ulong) Ready(string key) { observed.Add(key); return (true, 1); }
        Assert.True(policy.Poll(true, _ => true, Ready, 0, false));
        Assert.Equal(new[] { "armoire", "chocoboSaddlebag", "glamourDresser", "premiumSaddlebag" }, observed.Order());
        Assert.False(policy.Poll(false, _ => true, Ready, 1, false));
        Assert.True(policy.Poll(true, _ => true, Ready, 1, false));
        Assert.False(policy.Poll(true, _ => false, Ready, 2, false));
        Assert.True(policy.Poll(true, _ => true, Ready, 2, false));
        Assert.False(policy.Poll(true, _ => true, _ => throw new IOException("Read fixture failure."), 3, false));
        Assert.True(policy.Poll(true, _ => true, Ready, 4, false));
    }

    [Fact]
    public void UnvalidatedSourcesAndLostIdentityNeverReadNativeInputs()
    {
        var policy = new StorageReadinessPolicy();
        var forbiddenReads = 0;
        (bool, ulong) MustNotRead(string _) { forbiddenReads++; throw new IOException(); }
        Assert.False(policy.Poll(false, _ => true, MustNotRead, 0, false));
        Assert.False(policy.Poll(true, _ => false, MustNotRead, 0, false));
        var reads = 0;
        Assert.True(policy.Poll(true, _ => true, _ => { reads++; return (true, 1); }, 0, false));
        Assert.Equal(4, reads);
        Assert.False(policy.Poll(true, _ => true, MustNotRead, 0.5, false));
        policy.Reset();
        Assert.True(policy.Poll(true, _ => true, _ => (true, 1), 0.5, false));
        Assert.Equal(0, forbiddenReads);
    }

    [Fact]
    public void NativeReadFailureIsIsolatedAndGuidedWorkConsumesItsOwnEdges()
    {
        var policy = new StorageReadinessPolicy();
        Assert.True(policy.Poll(true, _ => true, key => key == "armoire" ? throw new IOException() : (true, 1ul), 0, false));
        Assert.False(policy.Poll(true, _ => true, _ => (true, 2), 1, true));
        Assert.False(policy.Poll(true, _ => true, _ => (true, 2), 2, false));
        Assert.True(policy.Poll(true, _ => true, _ => (true, 3), 3, false));
    }

    [Fact]
    public void FingerprintsUseStableOrderedUncheckedArithmetic()
    {
        Assert.Equal(12638152016183539244ul, StorageFingerprint.Add(StorageFingerprint.Initial, 1));
        Assert.NotEqual(StorageFingerprint.Add(StorageFingerprint.Add(StorageFingerprint.Initial, 1), 2),
            StorageFingerprint.Add(StorageFingerprint.Add(StorageFingerprint.Initial, 2), 1));
        Assert.Equal(0ul, StorageFingerprint.Add(ulong.MaxValue, ulong.MaxValue));
    }
}
