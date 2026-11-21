using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._Misfits.Events;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Events;

// Client half of the event pool: admin tab state and the ghost [Warp] chat line.
public sealed class MisfitsEventsClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    // Fired when the server sends an admin state snapshot.
    public event Action<MisfitsEventsStateEvent>? OnStateReceived;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MisfitsEventsStateEvent>(OnState);
        SubscribeNetworkEvent<MisfitsEventGhostAnnouncement>(OnGhostAnnouncement);
    }

    public void RequestState()
    {
        RaiseNetworkEvent(new MisfitsEventsStateRequest());
    }

    public void SendAction(MisfitsEventAdminActionRequest request)
    {
        RaiseNetworkEvent(request);
    }

    // Asks the server to warp our ghost to the event behind the [Warp] chat link.
    public void RequestWarp(NetEntity rule)
    {
        RaiseNetworkEvent(new MisfitsEventWarpRequest(rule));
    }

    private void OnState(MisfitsEventsStateEvent msg)
    {
        OnStateReceived?.Invoke(msg);
    }

    private void OnGhostAnnouncement(MisfitsEventGhostAnnouncement msg)
    {
        foreach (var box in _ui.GetUIController<ChatUIController>().Chats)
        {
            box.AddEventLine(msg.Text, msg.Rule);
        }
    }
}
