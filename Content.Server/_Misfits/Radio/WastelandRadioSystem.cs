using Content.Server.Radio;
using Content.Shared._Misfits.Radio;

namespace Content.Server._Misfits.Radio;

/// <summary>
/// Provides a round-local administrator switch for the public Wasteland radio channel.
/// This deliberately affects only WastelandGlobal, leaving faction and broadcast channels alone.
/// Public radio additionally requires every map-placed radio tower to be activated.
/// </summary>
public sealed class WastelandRadioSystem : EntitySystem
{
    private const string WastelandGlobalChannel = "WastelandGlobal";

    /// <summary>
    /// True unless an administrator disables the channel for the current server process.
    /// </summary>
    public bool Enabled { get; private set; } = true;

    public override void Initialize()
    {
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent args)
    {
        if ((!Enabled || !AreAllRadioTowersActivated()) && args.Channel.ID.ToString() == WastelandGlobalChannel)
            args.Cancelled = true;
    }

    private bool AreAllRadioTowersActivated()
    {
        var foundTower = false;
        var query = EntityQueryEnumerator<RadioTowerComponent>();
        while (query.MoveNext(out _, out var tower))
        {
            foundTower = true;
            if (!tower.Activated)
                return false;
        }

        // Do not silently enable the channel on maps which have no radio tower.
        return foundTower;
    }
}
