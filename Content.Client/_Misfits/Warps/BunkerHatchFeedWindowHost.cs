// #Misfits Add - Opens and keeps up to date the bunker hatch camera window for any map UI.
using Content.Shared._Misfits.Warps;

namespace Content.Client._Misfits.Warps;

/// <summary>
/// Shared by the Enclave TacMap, the Enclave power armor HUD and the Pip-Boy map program. Each
/// only has to say how to send the "look through this hatch" and "lock/unlock" requests; this
/// handles the window itself.
/// </summary>
public sealed class BunkerHatchFeedWindowHost
{
    private readonly Action<NetEntity?> _sendView;
    private readonly Action<NetEntity, bool>? _sendLock;
    private BunkerHatchFeedWindow? _window;

    /// <param name="sendLock">Null for a view-only window with no lock button (the Pip-Boy).</param>
    public BunkerHatchFeedWindowHost(Action<NetEntity?> sendView, Action<NetEntity, bool>? sendLock)
    {
        _sendView = sendView;
        _sendLock = sendLock;
    }

    /// <summary>
    /// Opens the window on this hatch (or switches an open window to it) and asks the server to
    /// stream the area around the hatch.
    /// </summary>
    public void Open(BunkerHatchEntry hatch)
    {
        if (_window == null)
        {
            _window = new BunkerHatchFeedWindow(showLockButton: _sendLock != null);
            _window.OnLockPressed += (target, lockIt) => _sendLock?.Invoke(target, lockIt);
            _window.OnClose += () =>
            {
                _window = null;
                _sendView(null);
            };
            _window.OpenCentered();
        }

        _window.SetHatch(hatch);
        _sendView(hatch.Hatch);
    }

    /// <summary>
    /// Keeps the window's lock state current, and closes it if its hatch is gone.
    /// </summary>
    public void Refresh(BunkerHatchEntry[] hatches)
    {
        if (_window?.Hatch is not { } current)
            return;

        foreach (var hatch in hatches)
        {
            if (hatch.Hatch != current)
                continue;

            _window.SetHatch(hatch);
            return;
        }

        _window.Close();
    }

    public void Close()
    {
        _window?.Close();
    }
}
