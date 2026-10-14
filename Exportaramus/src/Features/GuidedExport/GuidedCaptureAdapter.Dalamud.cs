using System.Collections.Immutable;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Exportaramus.Features.GuidedExport;

/// <summary>Admits explicit development compatibility separately from supervised proof. Reacquires native objects for each action.</summary>
public sealed unsafe class GuidedCaptureAdapter
{
    private readonly IPlayerState player;
    private readonly IObjectTable objects;
    private readonly ITargetManager target;
    private readonly ICondition conditions;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly GuidedRuntimeValidation validation;
    private readonly Func<string> gameDataVersion;
    private readonly Func<GuidedSource, bool> managerReady;
    private readonly Action<GuidedEffect> capture;
    private readonly Action<GuidedEffect> save;
    private ulong menuTarget;
    private long menuGeneration;
    private bool menuPending;
    public GuidedCaptureAdapter(IPlayerState player, IObjectTable objects, ITargetManager target, ICondition conditions,
        IGameGui gui, IFramework framework, GuidedRuntimeValidation validation, Func<string> gameDataVersion,
        Func<GuidedSource, bool> managerReady, Action<GuidedEffect> capture, Action<GuidedEffect> save)
    {
        this.player = player; this.objects = objects; this.target = target; this.conditions = conditions;
        this.gui = gui; this.framework = framework; this.validation = validation; this.gameDataVersion = gameDataVersion;
        this.managerReady = managerReady; this.capture = capture; this.save = save;
    }
    private GuidedValidationFacts Facts => new(validation.SupervisedValidationComplete, validation.GameDataVersion,
        gameDataVersion(), validation.ObjectKind != default, validation.DresserBaseIds.Count, validation.ArmoireBaseIds.Count,
        validation.DresserAddon, validation.ArmoireAddon, validation.UnsafeConditions.Count, validation.InteractionRange);
    public bool Validated => GuidedAdapterPolicy.IsValidated(Facts);
    public bool Available => Validated || validation.DevelopmentCompatibility && GuidedAdapterPolicy.IsCompatible(Facts);
    public GuidedFrame Observe(GuidedSource source, TimeSpan now, bool stopping)
    {
        var safe = framework.IsInFrameworkUpdateThread && player.IsLoaded && player.ContentId != 0 &&
            !validation.UnsafeConditions.Any(flag => conditions[flag]);
        if (!Available || !safe)
            return new(now, player.ContentId, player.IsLoaded, stopping, safe, Available, 0, [], 0, false, false, false);
        var addon = GetAddon(source);
        var present = addon != null && addon->IsVisible;
        var ready = present && addon->IsReady;
        var origin = objects.LocalPlayer?.Position ?? new Vector3(float.PositiveInfinity);
        var furnishings = objects.Take(1024).Select(obj => new GuidedFurnishing(obj.GameObjectId,
            validation.DresserBaseIds.Contains(obj.BaseId) ? GuidedSource.Dresser : GuidedSource.Armoire,
            obj.IsValid(), validation.DresserBaseIds.Contains(obj.BaseId) || validation.ArmoireBaseIds.Contains(obj.BaseId),
            obj.ObjectKind == validation.ObjectKind, obj.IsTargetable, Vector3.Distance(origin, obj.Position), validation.InteractionRange)).ToImmutableArray();
        var menu = gui.GetAddonByName<AtkUnitBase>("SelectString", 1);
        var menuIdentity = source == GuidedSource.Armoire && menuPending && target.Target?.GameObjectId == menuTarget &&
            menu != null && menu->IsReady && menu->IsVisible ? (ulong)menu : 0;
        return new(now, player.ContentId, player.IsLoaded, stopping, safe, true, target.Target?.GameObjectId ?? 0,
            furnishings, present ? (ulong)addon : 0, present, ready, ready && managerReady(source), ArmoireMenuIdentity: menuIdentity);
    }
    public bool Execute(GuidedEffect effect, GuidedExportController controller, ulong expectedContentId, TimeSpan now)
    {
        var executed = TryExecute(effect, controller, expectedContentId, now);
        if (!executed) controller.RejectEffect(effect, now);
        return executed;
    }
    private bool TryExecute(GuidedEffect effect, GuidedExportController controller, ulong expectedContentId, TimeSpan now)
    {
        if (!GuidedAdapterPolicy.AuthorizeEffect(controller.OwnsWindowEdges, effect.Generation, controller.Generation,
            effect.Kind, framework.IsInFrameworkUpdateThread, player.IsLoaded, player.ContentId, expectedContentId)) return false;
        if (effect.Kind is GuidedEffectKind.SaveFull or GuidedEffectKind.SavePartial) { save(effect); return true; }
        var frame = Observe(effect.Source, now, false);
        if (!frame.RuntimeValidated || !frame.SafeConditions) return false;
        if (effect.Kind == GuidedEffectKind.SelectArmoireMenu)
        {
            if (!menuPending || menuGeneration != effect.Generation || effect.Source != GuidedSource.Armoire ||
                effect.TargetIdentity != menuTarget || frame.ArmoireMenuIdentity != effect.AddonIdentity || effect.AddonIdentity == 0 ||
                !frame.Furnishings.Any(obj => obj.Identity == menuTarget && FurnishingPolicy.IsEligible(obj, GuidedSource.Armoire))) return false;
            var menu = gui.GetAddonByName<AtkUnitBase>("SelectString", 1);
            if (menu == null || (ulong)menu != effect.AddonIdentity || !menu->IsReady || !menu->IsVisible) return false;
            menuPending = false;
            return menu->FireCallbackInt(0);
        }
        if (effect.Kind is GuidedEffectKind.Capture or GuidedEffectKind.CloseAddon)
        {
            if (!frame.AddonReady || !frame.ManagerReady || frame.AddonIdentity != effect.AddonIdentity) return false;
            if (effect.Kind == GuidedEffectKind.Capture) { capture(effect); return true; }
            var exact = GetAddon(effect.Source);
            if (exact == null || (ulong)exact != effect.AddonIdentity || !exact->IsReady || !exact->IsVisible) return false;
            if (effect.Source == GuidedSource.Dresser)
            {
                var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismPrismBox.Instance();
                if (agent == null || !agent->IsAgentActive()) return false;
                agent->Hide();
            }
            else _ = exact->Close(true);
            return true;
        }
        var furnishing = objects.FirstOrDefault(obj => obj.GameObjectId == effect.TargetIdentity);
        if (furnishing is null || !furnishing.IsValid()) return false;
        if (!frame.Furnishings.Any(obj => obj.Identity == effect.TargetIdentity && (effect.Kind == GuidedEffectKind.SelectTarget ? FurnishingPolicy.IsDiscoverable(obj, effect.Source) : FurnishingPolicy.IsEligible(obj, effect.Source)))) return false;
        if (effect.Kind == GuidedEffectKind.SelectTarget) { target.Target = furnishing; return true; }
        if (target.Target?.GameObjectId != effect.TargetIdentity || frame.ExpectedAddonPresent) return false;
        var system = TargetSystem.Instance();
        if (system == null) return false;
        var existingMenu = gui.GetAddonByName<AtkUnitBase>("SelectString", 1);
        if (existingMenu != null && existingMenu->IsVisible) return false;
        menuPending = effect.Source == GuidedSource.Armoire;
        menuTarget = effect.TargetIdentity; menuGeneration = effect.Generation;
        _ = system->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)furnishing.Address, true);
        return true;
    }
    private AtkUnitBase* GetAddon(GuidedSource source) => gui.GetAddonByName<AtkUnitBase>(source == GuidedSource.Dresser ? validation.DresserAddon : validation.ArmoireAddon, 1);
    public static bool ReadManagerReady(GuidedSource source)
    {
        if (source == GuidedSource.Dresser)
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.MirageManager.Instance();
            return manager != null && manager->PrismBoxLoaded;
        }
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null && ui->Cabinet.IsCabinetLoaded();
    }
}
