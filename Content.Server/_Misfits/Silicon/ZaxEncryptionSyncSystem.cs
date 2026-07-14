using Content.Shared._Misfits.Silicon;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// Shares the union of physically installed encryption-key channels across every
/// Z.A.X-linked chassis and the Z.A.X core.
/// </summary>
public sealed class ZaxEncryptionSyncSystem : EntitySystem
{
    private readonly HashSet<string> _sharedChannels = new();
    private bool _synchronizing;
    private bool _dirty;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EncryptionKeyHolderComponent, EncryptionChannelsChangedEvent>(OnChannelsChanged);
        SubscribeLocalEvent<ZaxLinkedUnitComponent, ComponentShutdown>(OnNodeShutdown);
        SubscribeLocalEvent<ZaxCoreComponent, ComponentShutdown>(OnNodeShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_dirty)
            return;

        _dirty = false;
        SynchronizeChannels();
    }

    private void OnChannelsChanged(Entity<EncryptionKeyHolderComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        if (_synchronizing || !IsZaxNode(ent.Owner))
            return;

        SynchronizeChannels();
    }

    private void OnNodeShutdown<T>(Entity<T> ent, ref ComponentShutdown args) where T : Component
    {
        // Rebuild next tick, after the departing holder has left entity queries.
        _dirty = true;
    }

    private void SynchronizeChannels()
    {
        if (_synchronizing)
            return;

        _synchronizing = true;
        try
        {
            _sharedChannels.Clear();
            string? defaultChannel = null;

            var sourceQuery = EntityQueryEnumerator<EncryptionKeyHolderComponent>();
            while (sourceQuery.MoveNext(out var uid, out var holder))
            {
                if (!holder.Initialized || !IsZaxNode(uid))
                    continue;

                foreach (var keyUid in holder.KeyContainer.ContainedEntities)
                {
                    if (!TryComp<EncryptionKeyComponent>(keyUid, out var key))
                        continue;

                    _sharedChannels.UnionWith(key.Channels);
                    defaultChannel ??= key.DefaultChannel;
                }
            }

            var targetQuery = EntityQueryEnumerator<EncryptionKeyHolderComponent>();
            while (targetQuery.MoveNext(out var uid, out var holder))
            {
                if (!holder.Initialized || !IsZaxNode(uid) ||
                    holder.Channels.SetEquals(_sharedChannels) && holder.DefaultChannel == defaultChannel)
                {
                    continue;
                }

                holder.Channels.Clear();
                holder.Channels.UnionWith(_sharedChannels);
                holder.DefaultChannel = defaultChannel;
                RaiseLocalEvent(uid, new EncryptionChannelsChangedEvent(holder));
            }
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private bool IsZaxNode(EntityUid uid)
    {
        return HasComp<ZaxLinkedUnitComponent>(uid) || HasComp<ZaxCoreComponent>(uid);
    }
}
