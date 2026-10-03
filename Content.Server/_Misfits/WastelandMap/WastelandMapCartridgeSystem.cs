// #Misfits Change - Pip-Boy tactical map cartridge system
using System.Collections.Generic;
using Content.Server._Misfits.PipBoy;
using Content.Server.CartridgeLoader;
using Content.Server._Misfits.Warps; // #Misfits Add - bunker hatch camera and lock
using Content.Shared._Misfits.PipBoy;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeltaV.NanoChat;
using Content.Shared.PDA;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Misfits.WastelandMap;

public sealed class WastelandMapCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly WastelandMapSystem _wastelandMap = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly PipBoyNetworkSystem _pipBoy = default!;
    [Dependency] private readonly BunkerHatchViewSystem _hatchView = default!; // #Misfits Add
    [Dependency] private readonly BunkerTeleporterSystem _bunkerTeleporter = default!; // #Misfits Add
    [Dependency] private readonly Content.Server.Popups.PopupSystem _popup = default!; // #Misfits Add

    // Track which (cartridge, loader) pairs have had their UIFragment set up by the client.
    // We must NOT send WastelandMapBoundUserInterfaceState until after CartridgeUiReadyEvent
    // fires, because UpdateCartridgeUiState uses the same PDA UI key as CartridgeLoaderUiState.
    // If both are sent in the same server tick, the BUI system only transmits the last one,
    // causing the client to never receive CartridgeLoaderUiState and never attach the fragment.
    private readonly HashSet<(EntityUid Cartridge, EntityUid Loader)> _readyPairs = new();

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WastelandMapComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<WastelandMapComponent, CartridgeMessageEvent>(OnCartridgeMessage);
        SubscribeLocalEvent<WastelandMapComponent, CartridgeDeactivatedEvent>(OnDeactivated);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        foreach (var (cartridge, loader) in _readyPairs)
        {
            if (!TryComp<WastelandMapComponent>(cartridge, out var map))
                continue;

            if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) ||
                loaderComp.ActiveProgram != cartridge)
            {
                continue;
            }

            var feed = GetLoaderFeed(loader, map);
            var mapId = GetViewerMapId(loader);
            var state = _wastelandMap.BuildState(map, mapId, feed, includeBunkerHatches: HasEnclaveId(loader)); // #Misfits Change

            // #Misfits Add - Inject PipBoy contact/group blips into the tactical map
            state = AppendPipBoyBlips(state, loader);

            _cartridgeLoader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
        }
    }

    private void OnUiReady(EntityUid uid, WastelandMapComponent component, CartridgeUiReadyEvent args)
    {
        // Mark this pair as ready so the Update loop can safely push state from here on.
        _readyPairs.Add((uid, args.Loader));

        var feed = GetLoaderFeed(args.Loader, component);
        var state = _wastelandMap.BuildState(component, GetViewerMapId(args.Loader), feed, includeBunkerHatches: HasEnclaveId(args.Loader)); // #Misfits Change

        // #Misfits Add - Inject PipBoy contact/group blips into the tactical map
        state = AppendPipBoyBlips(state, args.Loader);

        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, state);
    }

    private void OnDeactivated(EntityUid uid, WastelandMapComponent component, CartridgeDeactivatedEvent args)
    {
        // Clean up so we don't update a no-longer-active program.
        _readyPairs.RemoveWhere(p => p.Cartridge == uid);
    }

    private void OnCartridgeMessage(EntityUid uid, WastelandMapComponent component, CartridgeMessageEvent args)
    {
        var loaderUid = GetEntity(args.LoaderUid);
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var actor = args.Actor;

        // #Misfits Add - bunker hatch camera and lock, only with an Enclave ID in the Pip-Boy.
        if (args is WastelandMapCartridgeHatchViewMessageEvent)
        {
            HandleHatchMessage(actor, loaderUid, args);
            return;
        }

        var feed = GetLoaderFeed(loaderUid, component);
        var mapId = GetViewerMapId(loaderUid);
        var changed = args switch
        {
            WastelandMapCartridgeAddAnnotationMessageEvent add => _wastelandMap.TryAddAnnotation(actor, component, mapId, add.Annotation, feed),
            WastelandMapCartridgeRemoveAnnotationMessageEvent remove => _wastelandMap.TryRemoveAnnotation(actor, component, mapId, remove.Index, feed),
            WastelandMapCartridgeClearAnnotationsMessageEvent => _wastelandMap.TryClearAnnotations(actor, component, mapId, feed),
            _ => false,
        };

        if (!changed)
            return;

        var state = _wastelandMap.BuildState(component, mapId, feed, includeBunkerHatches: HasEnclaveId(loaderUid)); // #Misfits Change
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state, loader: loader);
    }

    private MapId GetViewerMapId(EntityUid loaderUid)
    {
        if (TryComp<UserInterfaceComponent>(loaderUid, out var ui))
        {
            foreach (var actor in _ui.GetActors((loaderUid, ui), PdaUiKey.Key))
            {
                return Transform(actor).MapID;
            }
        }

        return Transform(loaderUid).MapID;
    }

    private WastelandMapTacticalFeedKind GetLoaderFeed(EntityUid loaderUid, WastelandMapComponent component)
    {
        if (!TryComp<PdaComponent>(loaderUid, out var pda) || pda.ContainedId is not { } containedId)
            return _wastelandMap.GetEffectiveFeed(component);

        if (_tag.HasTag(containedId, "IdCardBrotherhood"))
            return WastelandMapTacticalFeedKind.Brotherhood;

        if (_tag.HasTag(containedId, "IdCardVault"))
            return WastelandMapTacticalFeedKind.Vault;

        if (_tag.HasTag(containedId, "IdCardNCR"))
            return WastelandMapTacticalFeedKind.NCR;

        if (_tag.HasTag(containedId, "IdCardEnclave")) // #Misfits Change
            return WastelandMapTacticalFeedKind.Enclave;

        return _wastelandMap.GetEffectiveFeed(component);
    }

    // #Misfits Add - The view ends by itself when the Pip-Boy UI closes (see BunkerHatchViewSystem).
    private void HandleHatchMessage(EntityUid actor, EntityUid loaderUid, CartridgeMessageEvent args)
    {
        // The Pip-Boy can only LOOK through hatches (locking is at the hatch, TacMap or HUD). The camera
        // needs an Enclave ID in the Pip-Boy AND an Enclave job: a stolen ID is not enough. (The hatch
        // icons alone only need the ID.)
        if (!HasEnclaveId(loaderUid) || !_bunkerTeleporter.IsEnclave(actor))
        {
            _hatchView.StopViewing(actor);
            // No message when the window is just being closed.
            if (args is not WastelandMapCartridgeHatchViewMessageEvent { Hatch: null })
                _popup.PopupCursor(Loc.GetString("bunker-hatch-pipboy-denied"), actor);
            return;
        }

        switch (args)
        {
            case WastelandMapCartridgeHatchViewMessageEvent { Hatch: { } netHatch }:
                _hatchView.StartViewing(actor, GetEntity(netHatch), loaderUid, PdaUiKey.Key);
                return;
            case WastelandMapCartridgeHatchViewMessageEvent:
                _hatchView.StopViewing(actor);
                return;
        }
    }

    /// <summary>
    /// #Misfits Add - Only a Pip-Boy with an Enclave ID in it shows the surface bunker hatches.
    /// </summary>
    private bool HasEnclaveId(EntityUid loaderUid)
    {
        return TryComp<PdaComponent>(loaderUid, out var pda)
            && pda.ContainedId is { } containedId
            && _tag.HasTag(containedId, "IdCardEnclave");
    }

    /// <summary>
    /// #Misfits Add - Appends PipBoy contact and group location blips to the tactical map state.
    /// If the PDA has a NanoChatCard with PipBoyNetworkComponent, tracked contacts and group members
    /// appear as blips on the wasteland map.
    /// </summary>
    private WastelandMapBoundUserInterfaceState AppendPipBoyBlips(WastelandMapBoundUserInterfaceState state, EntityUid loaderUid)
    {
        if (!TryComp<PdaComponent>(loaderUid, out var pda) ||
            pda.ContainedId is not { } containedId ||
            !TryComp<PipBoyNetworkComponent>(containedId, out var net) ||
            !TryComp<NanoChatCardComponent>(containedId, out var card) ||
            card.Number == null ||
            net.IsLocked)
        {
            return state;
        }

        var extraBlips = new List<WastelandMapTrackedBlip>();

        // Contact locations
        var contactLocs = _pipBoy.GetContactLocations((containedId, net));
        foreach (var loc in contactLocs)
        {
            extraBlips.Add(new WastelandMapTrackedBlip(loc.X, loc.Y,
                $"{loc.Name} #{loc.Number:D4}", WastelandMapTrackedBlipKind.PipBoyContact));
        }

        // Group member locations (for all groups the user has map tracking enabled)
        foreach (var (groupId, membership) in net.Groups)
        {
            if (!membership.MapTrackingEnabled)
                continue;

            var groupLocs = _pipBoy.GetGroupLocations(groupId, card.Number.Value);
            foreach (var loc in groupLocs)
            {
                extraBlips.Add(new WastelandMapTrackedBlip(loc.X, loc.Y,
                    loc.Name, WastelandMapTrackedBlipKind.PipBoyGroupMember));
            }
        }

        if (extraBlips.Count == 0)
            return state;

        // Merge existing blips with PipBoy blips
        var merged = new WastelandMapTrackedBlip[state.TrackedBlips.Length + extraBlips.Count];
        state.TrackedBlips.CopyTo(merged, 0);
        extraBlips.CopyTo(merged, state.TrackedBlips.Length);

        return new WastelandMapBoundUserInterfaceState(
            state.MapTitle,
            state.MapTexturePath,
            state.CompactHud,
            state.BoundsLeft,
            state.BoundsBottom,
            state.BoundsRight,
            state.BoundsTop,
            merged,
            state.SharedAnnotations,
            state.Overwatch,
            state.Communications,
            state.BunkerHatches); // #Misfits Add - keep bunker hatches
    }
}
