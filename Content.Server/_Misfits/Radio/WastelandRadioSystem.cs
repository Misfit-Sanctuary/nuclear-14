using Content.Server.Radio;

namespace Content.Server._Misfits.Radio;

/// <summary>
/// Provides a round-local switch for the public Wasteland radio channel.
/// This deliberately affects only WastelandGlobal, leaving faction and broadcast channels alone.
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
        if (!Enabled && args.Channel.ID.ToString() == WastelandGlobalChannel)
            args.Cancelled = true;
    }
}
