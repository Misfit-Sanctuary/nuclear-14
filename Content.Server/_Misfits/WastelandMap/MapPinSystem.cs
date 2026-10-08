using Content.Shared._Misfits.WastelandMap;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.WastelandMap;

/// <summary>Creates named, global, round-scoped tactical-map GPS landmarks.</summary>
public sealed class MapPinSystem : EntitySystem
{
    private const int MaxLabelLength = 64;

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MapPinManageComponent, MapPinNameMessage>(OnNameSubmitted);
        SubscribeLocalEvent<MapPinManageComponent, MapPinRemoveMessage>(OnPinRemoved);
        SubscribeLocalEvent<MapPinManageComponent, BoundUIClosedEvent>(OnManagerClosed);
    }

    public bool TryOpenManager(EntityUid actor)
    {
        if (Deleted(actor))
            return false;

        var query = EntityQueryEnumerator<MapPinManageComponent>();
        while (query.MoveNext(out var existing, out var pinManager))
        {
            if (pinManager.Creator != actor || Deleted(existing) || EntityManager.IsQueuedForDeletion(existing))
                continue;

            _ui.OpenUi(existing, MapPinManageUiKey.Key, actor);
            UpdateManager(existing);
            return true;
        }

        var manager = Spawn("MisfitsMapPinManager", _transform.GetMapCoordinates(actor));
        Comp<MapPinManageComponent>(manager).Creator = actor;
        _ui.OpenUi(manager, MapPinManageUiKey.Key, actor);
        UpdateManager(manager);
        return true;
    }

    private void OnNameSubmitted(Entity<MapPinManageComponent> ent, ref MapPinNameMessage args)
    {
        if (args.Actor is not { Valid: true } actor || actor != ent.Comp.Creator)
            return;

        var label = args.Name.Trim();
        if (label.Length == 0)
            return;

        if (label.Length > MaxLabelLength)
            label = label[..MaxLabelLength].TrimEnd();

        var pin = EntityManager.SpawnEntity(null, _transform.GetMapCoordinates(actor));
        var marker = EnsureComp<GlobalMapPinComponent>(pin);
        marker.Label = label;
        Dirty(pin, marker);

        UpdateManager(ent);
    }

    private void OnPinRemoved(Entity<MapPinManageComponent> ent, ref MapPinRemoveMessage args)
    {
        if (args.Actor is not { Valid: true } actor || actor != ent.Comp.Creator ||
            !TryGetEntity(args.Pin, out var pin) || !HasComp<GlobalMapPinComponent>(pin))
        {
            return;
        }

        Del(pin);
        UpdateManager(ent);
    }

    private void OnManagerClosed(Entity<MapPinManageComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey.Equals(MapPinManageUiKey.Key))
            QueueDel(ent);
    }

    private void UpdateManager(EntityUid manager)
    {
        var pins = new List<GlobalMapPinEntry>();
        var query = EntityQueryEnumerator<GlobalMapPinComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pin, out var transform))
        {
            if (Deleted(uid))
                continue;

            var coords = _transform.GetMapCoordinates(uid, transform);
            pins.Add(new GlobalMapPinEntry(GetNetEntity(uid), pin.Label, coords.Position.X, coords.Position.Y));
        }

        pins.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
        _ui.SetUiState(manager, MapPinManageUiKey.Key, new MapPinManageState(pins.ToArray()));
    }
}

/// <summary>Round-scoped GPS landmark rendered on every WastelandMap tactical feed.</summary>
[RegisterComponent]
public sealed partial class GlobalMapPinComponent : Component
{
    public string Label = string.Empty;
}

/// <summary>Short-lived BUI host for placing and removing pins.</summary>
[RegisterComponent]
public sealed partial class MapPinManageComponent : Component
{
    public EntityUid Creator;
}
