using Content.Shared._Misfits.Radio;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.Radio;

/// <summary>
/// Lets players bring map-placed radio towers online or take them offline for the current round.
/// </summary>
public sealed class RadioTowerSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<RadioTowerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<RadioTowerComponent> tower, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(tower.Comp.Activated
                ? "n14-radio-tower-deactivate-verb"
                : "n14-radio-tower-activate-verb"),
            Act = () => SetActivated(tower, !tower.Comp.Activated),
        });
    }

    private void SetActivated(Entity<RadioTowerComponent> tower, bool activated)
    {
        if (tower.Comp.Activated == activated || Deleted(tower))
            return;

        tower.Comp.Activated = activated;
        Dirty(tower);
    }
}
