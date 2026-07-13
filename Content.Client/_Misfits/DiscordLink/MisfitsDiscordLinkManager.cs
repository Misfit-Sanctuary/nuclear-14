using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Misfits.DiscordLink;
using Robust.Client.UserInterface;
using Robust.Shared.Network;

namespace Content.Client._Misfits.DiscordLink;

public sealed class MisfitsDiscordLinkManager
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IUriOpener _uri = default!;

    private CancellationTokenSource? _pollCancel;

    public event Action<bool, string?>? LinkStatusChanged;

    public bool IsLinked { get; private set; }

    public void Initialize()
    {
        IoCManager.InjectDependencies(this);

        _net.RegisterNetMessage<MsgMisfitsDiscordLinkStatus>(OnStatus);
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkStatusRequest>();
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkBegin>();
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkCheck>();
    }

    public void RequestStatus()
    {
        if (!_net.IsConnected)
            return;

        _net.ClientSendMessage(new MsgMisfitsDiscordLinkStatusRequest());
    }

    public void BeginLink()
    {
        if (IsLinked)
            return;

        if (!_net.IsConnected)
        {
            LinkStatusChanged?.Invoke(false, "Not connected.");
            return;
        }

        LinkStatusChanged?.Invoke(false, "Opening Discord...");
        _net.ClientSendMessage(new MsgMisfitsDiscordLinkBegin());
    }

    private void OnStatus(MsgMisfitsDiscordLinkStatus msg)
    {
        IsLinked = msg.IsLinked;
        LinkStatusChanged?.Invoke(IsLinked, string.IsNullOrWhiteSpace(msg.Error) ? null : msg.Error);

        if (IsLinked)
        {
            _pollCancel?.Cancel();
            return;
        }

        if (!string.IsNullOrWhiteSpace(msg.Link))
        {
            _uri.OpenUri(msg.Link);
            LinkStatusChanged?.Invoke(false, "Waiting for Discord...");
            StartPolling();
        }
    }

    private void StartPolling()
    {
        _pollCancel?.Cancel();
        _pollCancel = new CancellationTokenSource();
        _ = PollStatus(_pollCancel.Token);
    }

    private async Task PollStatus(CancellationToken cancel)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancel);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cancel.IsCancellationRequested || !_net.IsConnected || IsLinked)
                return;

            _net.ClientSendMessage(new MsgMisfitsDiscordLinkCheck());
        }
    }
}
