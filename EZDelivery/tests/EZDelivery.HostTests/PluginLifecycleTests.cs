using System.Reflection;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EZDelivery.Bootstrap;
using EZDelivery.Features.Interface;
using EZDelivery.Features.Shopping;
using Xunit;

namespace EZDelivery.HostTests;

/// <summary>Runs the production Plugin against service doubles, without a game process or native actions.</summary>
public sealed class PluginLifecycleTests
{
    [Fact]
    public async Task TenLifecycleCyclesHaveDistinctInstancesAndBalancedTopLevelCleanup()
    {
        // Arrange
        var instances = new HashSet<string>();
        // Act
        for (var cycle = 0; cycle < 10; cycle++)
        {
            var host = new LifecycleHost();
            await host.Plugin.LoadAsync(CancellationToken.None);
            await host.Plugin.DisposeAsync();
            await host.Plugin.DisposeAsync();
            // Assert each completed isolated lifecycle; no live game is involved.
            Assert.Equal(2, host.LifecycleTraces.Count);
            using var loaded = System.Text.Json.JsonDocument.Parse(host.LifecycleTraces[0]);
            using var disposed = System.Text.Json.JsonDocument.Parse(host.LifecycleTraces[1]);
            Assert.True(instances.Add(loaded.RootElement.GetProperty("InstanceId").GetString()!));
            var attached = loaded.RootElement.GetProperty("AttachedResources").EnumerateArray().Select(x => x.GetString()).Order().ToArray();
            var completed = disposed.RootElement.GetProperty("CleanupCompleted").EnumerateArray().Select(x => x.GetString()).Order().ToArray();
            Assert.Equal(attached, completed);
            Assert.Equal(2, host.Calls.Count(x => x == "IAddonLifecycle.RegisterListener"));
            Assert.Equal(2, host.Calls.Count(x => x == "IAddonLifecycle.UnregisterListener"));
            Assert.Single(host.Calls, x => x == "ICommandManager.AddHandler");
            Assert.Single(host.Calls, x => x == "ICommandManager.RemoveHandler");
        }
    }

    [Fact]
    public async Task QualificationInvalidAndPrematureExportCommandsDoNotFaultTransactions()
    {
        // Arrange
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        var runtime = new LifecycleShoppingRuntime();
        host.UseSafeShopping(runtime);
        var command = typeof(Plugin).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Act
        foreach (var request in new[] { "qualification start ../invalid", "qualification start safe", "qualification export", "qualification unknown", "qualification stop" })
        {
            command.Invoke(host.Plugin, ["/ezdelivery", request]);
            host.Update();
        }
        // Assert: invalid/export requests report failure without changing engine fault state.
        Assert.False(host.Field<bool>("faulted"));
        Assert.Equal(0, runtime.Dispatches);
        Assert.Equal(5, host.Calls.Count(x => x == "INotificationManager.AddNotification"));
        Assert.Single(host.Calls, x => x == "IPluginLog.Warning");
        await host.Plugin.DisposeAsync();
    }

    [Fact]
    public async Task QualificationCommandsRunOnFrameworkAndSampleFaultReconciliation()
    {
        // Arrange: real plugin coordinator, isolated native observations only.
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        host.UseSafeShopping(new LifecycleShoppingRuntime { FailObservation = true });
        var command = typeof(Plugin).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var capture = host.Field<object>("qualification");
        var seen = capture.GetType().GetField("updateSeen", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Act
        command.Invoke(host.Plugin, ["/ezdelivery", "qualification start fault_test"]);
        Assert.Equal(0, (int)seen.GetValue(capture)!);
        host.Update();
        host.Update();
        command.Invoke(host.Plugin, ["/ezdelivery", "qualification stop"]);
        host.Update();
        // Assert: both normal fault entry and subsequent reconciliation were measured.
        Assert.Equal(2, (int)seen.GetValue(capture)!);
        Assert.True(host.Field<bool>("faulted"));
        Assert.Equal(2, host.Calls.Count(x => x == "INotificationManager.AddNotification"));
        await host.Plugin.DisposeAsync();
    }

    [Fact]
    public async Task LifecycleTraceRecordsBalancedCleanupAndNamesFailedRelease()
    {
        // Arrange
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        host.ThrowOn = "IUiBuilder.remove_Draw";
        // Act
        await Assert.ThrowsAnyAsync<Exception>(async () => await host.Plugin.DisposeAsync());
        // Assert
        Assert.Equal(2, host.LifecycleTraces.Count);
        using var loaded = System.Text.Json.JsonDocument.Parse(host.LifecycleTraces[0]);
        using var disposed = System.Text.Json.JsonDocument.Parse(host.LifecycleTraces[1]);
        Assert.Equal("Loaded", loaded.RootElement.GetProperty("Phase").GetString());
        Assert.Equal(10, loaded.RootElement.GetProperty("AttachedResources").GetArrayLength());
        Assert.Equal("Disposed", disposed.RootElement.GetProperty("Phase").GetString());
        Assert.Equal(loaded.RootElement.GetProperty("InstanceId").GetString(), disposed.RootElement.GetProperty("InstanceId").GetString());
        var attempted = disposed.RootElement.GetProperty("CleanupAttempted").EnumerateArray().Select(x => x.GetString()).ToArray();
        var completed = disposed.RootElement.GetProperty("CleanupCompleted").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(10, attempted.Length);
        Assert.Equal(9, completed.Length);
        Assert.Contains("Draw", attempted);
        Assert.DoesNotContain("Draw", completed);
        Assert.Contains("Provider", completed);
    }
    [Fact]
    public async Task LoggedOutUpdatePreparesDisabledStateAndRetainsThemeOnReadFailure()
    {
        // Arrange: real coordinator and delivery unsupported-build guard, fake shopping observations.
        var host = new LifecycleHost { Theme = 4 };
        await host.Plugin.LoadAsync(CancellationToken.None);
        host.UseSafeShopping(new LifecycleShoppingRuntime());

        // Act
        host.Update();
        var observer = host.Field<object>("themeObserver");
        observer.GetType().GetField("nextRead", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(observer, 0L);
        host.ThrowOn = "IGameConfig.TryGet";
        host.Update();

        // Assert
        var state = host.Field<InterfaceFacade>("ui").State;
        Assert.False(state.LoggedIn);
        Assert.False(state.Delivery.CanStart);
        Assert.Empty(state.Shopping.Rows);
        Assert.Equal(4u, observer.GetType().GetProperty("Current")!.GetValue(observer));
        Assert.False(host.Field<bool>("faulted"));
        Assert.Equal(2L, host.Field<long>("frameworkSamples"));
        await host.Plugin.DisposeAsync();
    }

    [Fact]
    public async Task QueuedStaleCancellationStopsPurchaseBeforeFrameworkTickCanDispatch()
    {
        // Arrange: real shopping engine prepared with a fake offer/runtime, never game memory.
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        var runtime = new LifecycleShoppingRuntime { Ready = true };
        var shopping = host.UseSafeShopping(runtime);
        shopping.Tick();
        Assert.True(shopping.BuyOne("offer"));
        host.Field<InterfaceFacade>("ui").Enqueue(new StopPurchasesIntent(-100));

        // Act
        host.Update();

        // Assert
        Assert.False(shopping.IsRunning);
        Assert.Equal(0, runtime.Dispatches);
        Assert.False(host.Field<bool>("faulted"));
        await host.Plugin.DisposeAsync();
    }

    [Fact]
    public async Task FrameworkObservationFaultDisablesActionsAndLaterUpdatesDoNotFloodLogs()
    {
        // Arrange
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        var runtime = new LifecycleShoppingRuntime { FailObservation = true };
        host.UseSafeShopping(runtime);

        // Act
        host.Update();
        host.Update();
        host.Update();

        // Assert
        Assert.True(host.Field<bool>("faulted"));
        Assert.False(host.Field<InterfaceFacade>("ui").State.Delivery.CanStart);
        Assert.Empty(host.Field<InterfaceFacade>("ui").State.Shopping.Rows);
        Assert.Single(host.Calls, call => call == "IPluginLog.Error");
        Assert.Equal(1, runtime.Observations);
        await host.Plugin.DisposeAsync();
    }

    [Fact]
    public async Task CancelledLoadNeverInitializesServicesAndDisposesOnce()
    {
        // Arrange
        var host = new LifecycleHost();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Plugin.LoadAsync(cancellation.Token));
        await host.Plugin.DisposeAsync();

        // Assert
        Assert.DoesNotContain(host.Calls, call => call == "IDalamudPluginInterface.GetPluginConfig");
        Assert.DoesNotContain(host.Calls, call => call == "ICommandManager.AddHandler");
        Assert.Equal(1, host.Calls.Count(call => call == "IFramework.RunOnFrameworkThread"));
    }

    [Fact]
    public async Task CommandConflictCleansConstructedResourcesWithoutRemovingExistingCommand()
    {
        // Arrange
        var host = new LifecycleHost { RegisterCommand = false };

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Plugin.LoadAsync(CancellationToken.None));

        // Assert
        Assert.Contains("already registered", error.Message);
        Assert.Equal(2, host.Calls.Count(call => call == "IAddonLifecycle.RegisterListener"));
        Assert.Equal(2, host.Calls.Count(call => call == "IAddonLifecycle.UnregisterListener"));
        Assert.DoesNotContain("ICommandManager.RemoveHandler", host.Calls);
        Assert.Equal(2, host.Calls.Count(call => call.EndsWith(".Unsubscribe", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PartialUiSubscriptionFailureAttemptsEveryDetachAndProviderDisposal()
    {
        // Arrange
        var host = new LifecycleHost { ThrowOn = "IUiBuilder.add_OpenMainUi" };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Plugin.LoadAsync(CancellationToken.None));

        // Assert
        Assert.Contains("IFramework.remove_Update", host.Calls);
        Assert.Contains("IUiBuilder.remove_Draw", host.Calls);
        Assert.Contains("IUiBuilder.remove_OpenConfigUi", host.Calls);
        Assert.Contains("IUiBuilder.remove_OpenMainUi", host.Calls);
        Assert.Contains("ICommandManager.RemoveHandler", host.Calls);
        Assert.Equal(2, host.Calls.Count(call => call == "IAddonLifecycle.UnregisterListener"));
        Assert.Equal(2, host.Calls.Count(call => call.EndsWith(".Unsubscribe", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DisposalFailureStillAttemptsRemainingResourcesAndProviderFinally()
    {
        // Arrange
        var host = new LifecycleHost();
        await host.Plugin.LoadAsync(CancellationToken.None);
        host.Calls.Clear();
        host.ThrowOn = "IUiBuilder.remove_Draw";

        // Act
        await Assert.ThrowsAnyAsync<Exception>(async () => await host.Plugin.DisposeAsync());
        var callCount = host.Calls.Count;
        await host.Plugin.DisposeAsync();

        // Assert
        Assert.Contains("IUiBuilder.remove_OpenMainUi", host.Calls);
        Assert.Contains("ICommandManager.RemoveHandler", host.Calls);
        Assert.Equal(2, host.Calls.Count(call => call == "IAddonLifecycle.UnregisterListener"));
        Assert.Equal(2, host.Calls.Count(call => call.EndsWith(".Unsubscribe", StringComparison.Ordinal)));
        Assert.Equal(callCount, host.Calls.Count);
    }

    [Fact]
    public void ReconciliationFailuresRunIndependentlyButLogOnlyOnce()
    {
        // Arrange
        var host = new LifecycleHost();
        var reconcile = typeof(Plugin).GetMethod("ReconcileAfterFailure", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var attempts = 0;
        Action fail = () => { attempts++; throw new IOException("Failed observation"); };

        // Act
        reconcile.Invoke(host.Plugin, ["Delivery", fail]);
        reconcile.Invoke(host.Plugin, ["Shopping", fail]);

        // Assert
        Assert.Equal(2, attempts);
        Assert.Single(host.Calls, call => call == "IPluginLog.Error");
        var failure = (string)typeof(Plugin).GetField("failure", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host.Plugin)!;
        Assert.Contains("Delivery receipt reconciliation failed: IOException", failure);
    }
}

internal sealed class LifecycleHost
{
    public List<string> Calls { get; } = [];
    public List<string> LifecycleTraces { get; } = [];
    public bool RegisterCommand { get; init; } = true;
    public string? ThrowOn { get; set; }
    public uint Theme { get; init; }
    public Plugin Plugin { get; }
    private readonly IUiBuilder ui;

    public T Field<T>(string name) => (T)typeof(Plugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Plugin)!;

    public void Update() => typeof(Plugin).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Plugin, [null]);

    public ShoppingFacade UseSafeShopping(LifecycleShoppingRuntime runtime)
    {
        var shopping = new ShoppingFacade(new LifecycleCatalog(), runtime,
            _ => new(PurchasePresence.Missing), () => DateTimeOffset.UnixEpoch);
        typeof(Plugin).GetField("shopping", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Plugin, shopping);
        return shopping;
    }

    public LifecycleHost()
    {
        ui = Service<IUiBuilder>();
        Plugin = new(Service<IDalamudPluginInterface>(), Service<IFramework>(), Service<IGameGui>(),
            Service<IClientState>(), Service<IPlayerState>(), Service<IDataManager>(), Service<ITextureProvider>(),
            Service<IGameConfig>(), Service<ICommandManager>(), Service<IPluginLog>(), Service<INotificationManager>(), Service<IAddonLifecycle>());
    }

    private T Service<T>() where T : class => (T)Service(typeof(T));

    private object Service(Type contract)
    {
        var proxy = DispatchProxy.Create(contract, typeof(LifecycleServiceProxy));
        ((LifecycleServiceProxy)proxy).Handler = (method, arguments) => Invoke(contract, method, arguments);
        return proxy;
    }

    private object? Invoke(Type contract, MethodInfo method, object?[] arguments)
    {
        var call = contract.Name + "." + method.Name;
        Calls.Add(call);
        if (contract == typeof(IPluginLog) && method.Name == "Information" && arguments.Length == 2 &&
            arguments[0] is string template && template == "EZ Delivery lifecycle {Trace}" && arguments[1] is object[] values)
            LifecycleTraces.Add((string)values[0]);
        if (call == ThrowOn) throw new InvalidOperationException("Injected service failure: " + call);
        if (method.Name == "RunOnFrameworkThread" && arguments.FirstOrDefault() is Action action)
        {
            try { action(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
        if (method.Name == "get_UiBuilder") return ui;
        if (contract == typeof(IGameConfig) && method.Name == "TryGet" && arguments.Length == 2 && method.GetParameters()[1].ParameterType == typeof(uint).MakeByRefType())
        {
            arguments[1] = Theme;
            return true;
        }
        if (method.Name == "GetPluginConfig") return new Configuration();
        if (method.Name == "GetPluginConfigDirectory") return Path.GetTempPath();
        if (method.Name == "AddHandler") return RegisterCommand;
        if (method.Name == "GetExcelSheet") throw new InvalidOperationException("No game sheets in test host");
        var result = method.ReturnType;
        if (result == typeof(void)) return null;
        if (result.IsInterface) return Service(result);
        if (result == typeof(Task)) return Task.CompletedTask;
        return result.IsValueType ? Activator.CreateInstance(result) : null;
    }
}

internal sealed class LifecycleCatalog : IShopCatalog
{
    public IReadOnlyList<ShopOffer> Offers { get; } = [new("offer", 1, 1, "Item", EZDelivery.Features.Shopping.ShopCategory.Weapons, 1, 1, 0, 0, 100, 0, 1, 0, 0)];
    public string Status => "Catalog ready";
}

internal sealed class LifecycleShoppingRuntime : IShoppingRuntime
{
    public bool Ready { get; init; }
    public bool FailObservation { get; init; }
    public int Observations { get; private set; }
    public int Dispatches { get; private set; }
    public string LastDialogCheck => "Test observation";
    public ShopAnchor? Anchor => null;
    public PurchaseObservation Observe(ShopOffer? pending)
    {
        Observations++;
        if (FailObservation) throw new IOException("Injected observation failure");
        return new(Ready, "Test observation", Ready ? new(1, 10, 1, 8, 20) : default, 0, 0, 0, 1, 100, 500, 0, true);
    }
    public ShopDispatchResult Dispatch(ShopCommand command)
    {
        Dispatches++;
        return new(false, false, "Test runtime does not dispatch");
    }
}

public class LifecycleServiceProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
}
