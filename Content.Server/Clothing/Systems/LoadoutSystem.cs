using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Paint;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Hands.EntitySystems; // #Misfits Fix - hand fallback for failed loadout equips
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Traits.Assorted.Components;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Network; // #Cythisiax Add - player NetUserId for Patreon tier enforcement
using Robust.Shared.Player; // #Misfits Add - event roles apply loadouts straight to a session
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Clothing.Systems;

public sealed class LoadoutSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly SharedLoadoutSystem _loadout = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTimeTracking = default!;
    [Dependency] private readonly PaintSystem _paint = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!; // #Misfits Fix - hand fallback for failed loadout equips

    private ISawmill _sawmill = default!;

    // #Misfits Fix - Slot priority for placing failed loadout items into worn storage.
    // Tried in order; first slot whose equipped item has a StorageComponent that accepts the
    // loadout entity wins. Order favors larger/more-appropriate containers first.
    private static readonly string[] FallbackStorageSlots =
    {
        "back",         // backpacks, satchels, duffels
        "suitstorage",  // hardsuit storage
        "belt",         // tool belts, holsters
    };


    public override void Initialize()
    {
        _sawmill = _log.GetSawmill("loadouts");

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }


    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId == null || Deleted(ev.Mob) || !Exists(ev.Mob)
            || !HasComp<MetaDataComponent>(ev.Mob) // TODO: FIND THE STUPID RACE CONDITION THAT IS MAKING ME CHECK FOR THIS.
            || !_protoMan.TryIndex<JobPrototype>(ev.JobId, out var job)
            || !_configurationManager.GetCVar(CCVars.GameLoadoutsEnabled))
            return;

        ApplyCharacterLoadout(
            ev.Mob,
            ev.JobId,
            ev.Profile,
            _playTimeTracking.GetTrackerTimes(ev.Player),
            ev.Player.ContentData()?.Whitelisted ?? false,
            jobProto: job,
            player: ev.Player.UserId); // #Cythisiax Edited - pass player for Patreon tier enforcement
    }


    // #Misfits Add - event ghost roles are not spawned by a station job, so they hand their loadout over directly.
    public void ApplyEventLoadout(EntityUid uid, ProtoId<JobPrototype> job, HumanoidCharacterProfile profile,
        ICommonSession player)
    {
        if (!_configurationManager.GetCVar(CCVars.GameLoadoutsEnabled)
            || !_protoMan.TryIndex(job, out var jobProto))
            return;

        // Same flags a station job spawn uses: the player's own playtime and whitelist state, never assumed.
        ApplyCharacterLoadout(
            uid,
            job,
            profile,
            _playTimeTracking.GetTrackerTimes(player),
            player.ContentData()?.Whitelisted ?? false,
            jobProto: jobProto,
            player: player.UserId);
    }

    /// Equips every loadout, then puts whatever extras it can in inventories
    public void ApplyCharacterLoadout(
        EntityUid uid,
        ProtoId<JobPrototype> job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        bool deleteFailed = false,
        JobPrototype? jobProto = null,
        NetUserId? player = null) // #Cythisiax Edited - player for Patreon tier enforcement
    {
        // Spawn the loadout, get a list of items that failed to equip
        var (failedLoadouts, allLoadouts) =
            _loadout.ApplyCharacterLoadout(uid, job, profile, playTimes, whitelisted, out var heirlooms, player);

        // #Misfits Fix - Cascading fallback for loadout items that couldn't equip to their natural
        // slot (slot collision with job startingGear, or non-clothing items like weapons that have
        // no slot at all). Previously we only tried the "back" slot's storage; if that slot was
        // occupied by the job's default backpack the loadout backpack would be stuffed inside the
        // wrong container and follow-up items (e.g. a Double-Barreled Shotgun) would silently fall
        // on the spawn floor and get walked off. New order: worn storage slots in priority order →
        // any empty hand → leave on the ground (existing behavior, items already spawned at
        // player coords by SharedLoadoutSystem). deleteFailed is honored only after every fallback
        // is exhausted.
        // # #Misfits Removed - replaced with cascading fallback below.
        // if (_inventory.TryGetSlotEntity(uid, "back", out var item) &&
        //     EntityManager.TryGetComponent<StorageComponent>(item, out var inventory))
        //     foreach (var loadout in failedLoadouts)
        //         if ((!EntityManager.TryGetComponent<ItemComponent>(loadout, out var itemComp)
        //                 || !_storage.CanInsert(item.Value, loadout, out _, inventory, itemComp)
        //                 || !_storage.Insert(item.Value, loadout, out _, playSound: false))
        //             && deleteFailed)
        //             EntityManager.QueueDeleteEntity(loadout);
        foreach (var loadout in failedLoadouts)
        {
            if (!Exists(loadout) || Deleted(loadout))
                continue;

            if (TryStashInWornStorage(uid, loadout))
                continue;

            if (_hands.TryPickupAnyHand(uid, loadout, checkActionBlocker: false, animate: false))
                continue;

            // All fallbacks exhausted - the entity remains on the ground at the player's spawn
            // coordinates (where SharedLoadoutSystem spawned it). Only delete if explicitly asked.
            if (deleteFailed)
                EntityManager.QueueDeleteEntity(loadout);
        }

        foreach (var loadout in allLoadouts)
        {
            if (loadout.Item1 == EntityUid.Invalid
                || !HasComp<MetaDataComponent>(loadout.Item1)
                || Deleted(loadout.Item1))
            {
                _sawmill.Warning($"Loadout {loadout.Item2.LoadoutName} failed to load properly, deleting.");
                EntityManager.QueueDeleteEntity(loadout.Item1);

                continue;
            }

            var loadoutProto = _protoMan.Index<LoadoutPrototype>(loadout.Item2.LoadoutName);
            if (loadoutProto.CustomName && loadout.Item2.CustomName != null)
                _meta.SetEntityName(loadout.Item1, loadout.Item2.CustomName);
            if (loadoutProto.CustomDescription && loadout.Item2.CustomDescription != null)
                _meta.SetEntityDescription(loadout.Item1, loadout.Item2.CustomDescription);
            if (loadoutProto.CustomColorTint && !string.IsNullOrEmpty(loadout.Item2.CustomColorTint))
                _paint.Paint(null, null, loadout.Item1, Color.FromHex(loadout.Item2.CustomColorTint));

            foreach (var component in loadoutProto.Components.Values)
            {
                if (HasComp(loadout.Item1, component.Component.GetType()))
                    continue;

                var comp = (Component) _serialization.CreateCopy(component.Component, notNullableOverride: true);
                comp.Owner = loadout.Item1;
                EntityManager.AddComponent(loadout.Item1, comp);
            }

            foreach (var function in loadoutProto.Functions)
                function.OnPlayerSpawn(uid, loadout.Item1, _componentFactory, EntityManager, _serialization);
        }

        // Pick the heirloom
        if (heirlooms.Any())
        {
            var heirloom = _random.Pick(heirlooms);
            EnsureComp<HeirloomHaverComponent>(uid, out var haver);
            EnsureComp<HeirloomComponent>(heirloom.Item1, out var comp);
            haver.Heirloom = heirloom.Item1;
            comp.HOwner = uid;
            Dirty(uid, haver);
            Dirty(heirloom.Item1, comp);
        }

        if (jobProto != null ||
            _protoMan.TryIndex(job, out jobProto))
            foreach (var special in jobProto.AfterLoadoutSpecial)
                special.AfterEquip(uid);
    }

    // #Misfits Fix - Walk the player's worn slots in priority order and try to insert the failed
    // loadout entity into the first slot whose equipped item has a StorageComponent that accepts
    // it. Returns true if successfully stored anywhere.
    private bool TryStashInWornStorage(EntityUid wearer, EntityUid loadout)
    {
        if (!EntityManager.TryGetComponent<ItemComponent>(loadout, out var itemComp))
            return false;

        foreach (var slotName in FallbackStorageSlots)
        {
            if (!_inventory.TryGetSlotEntity(wearer, slotName, out var slotEnt))
                continue;

            if (!EntityManager.TryGetComponent<StorageComponent>(slotEnt, out var storage))
                continue;

            if (!_storage.CanInsert(slotEnt.Value, loadout, out _, storage, itemComp))
                continue;

            if (_storage.Insert(slotEnt.Value, loadout, out _, storageComp: storage, playSound: false))
                return true;
        }

        return false;
    }

    // Corvax-Change-Start
    public void InsertBack(EntityUid uid, EntityUid loadout)
    {
        if (!TryComp(loadout, out ClothingComponent? clothing) ||
            clothing.Slots != SlotFlags.BACK)
            return;

        if (!TryComp(uid, out MindContainerComponent? mind) || mind.Mind == null)
            return;

        if (!_job.MindTryGetJob(mind.Mind.Value, out _, out var jobPrototype) || jobPrototype.StartingGear == null)
            return;

        if (!_protoMan.TryIndex(jobPrototype.StartingGear, out StartingGearPrototype? gear))
            return;

        if (!TryComp(uid, out InventoryComponent? inventoryComp))
            return;

        if (gear.Storage.Count > 0)
        {
            var coords = _xform.GetMapCoordinates(uid);
            var ents = new ValueList<EntityUid>();
            foreach (var (slot, entProtos) in gear.Storage)
            {
                if (slot != "back")
                    continue;

                if (entProtos.Count == 0)
                    continue;

                foreach (var ent in entProtos)
                {
                    ents.Add(Spawn(ent, coords));
                }

                if (inventoryComp != null &&
                    _inventory.TryGetSlotEntity(uid, slot, out var slotEnt, inventoryComponent: inventoryComp) &&
                    TryComp(slotEnt, out StorageComponent? storageComp))
                {
                    foreach (var ent in ents)
                    {
                        _storage.Insert(slotEnt.Value, ent, out _, storageComp: storageComp, playSound: false);
                    }
                }
                ents.Clear();
            }
        }
    }
    
    public void DeleteHelmet(EntityUid uid)
    {
        if (!_inventory.TryGetSlotEntity(uid, "head", out var helmet))
        {
            _sawmill.Error("Helmet not found");
            return;
        }    

        EntityManager.DeleteEntity(helmet);
    }
    // Corvax-Change-End
}
