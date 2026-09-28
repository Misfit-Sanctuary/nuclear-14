using System.Linq;
using Content.Shared.Effects;
using Robust.Shared.Player;

namespace Content.Server.Effects;

public sealed class ColorFlashEffectSystem : SharedColorFlashEffectSystem
{
    public override void RaiseEffect(Color color, List<EntityUid> entities, Filter filter, float? animationLength = null)
    {
        // misfits change: bad to hardcode filter out something like this.
        //                 this is something that should be up the the function's caller in case by case basis
        //                 Especially because it isnt only for things related to damage
        /*
            // #Misfits Change: suppress the red hit-flash on player-controlled entities — it is metagamey to broadcast a player's damage state visually.
            var nonPlayerEntities = entities.Where(e => !HasComp<ActorComponent>(e)).ToList();
            if (nonPlayerEntities.Count == 0)
                return;
        */

        RaiseNetworkEvent(new ColorFlashEffectEvent(color, GetNetEntityList(entities), animationLength), filter);
    }
}
