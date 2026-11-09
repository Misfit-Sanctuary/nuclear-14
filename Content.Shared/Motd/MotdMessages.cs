// #Misfits Add - Network messages for the Message of the Day window, its poll and its editor.
using System;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Motd;

/// Sent by the server to keep the Message of the Day window on a client up to date. Contains the message, the pictures and the current state of the poll.
public sealed class MotdUpdateMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    /// Whether the client should open the window, as opposed to only refreshing it if it is already open.
    public bool Open;

    public string Text = string.Empty;

    public string[] Images = Array.Empty<string>();

    public bool HasPoll;

    public string Question = string.Empty;

    public string[] Options = Array.Empty<string>();

    /// The amount of votes for each entry of <see cref="Options"/>.
    public int[] Votes = Array.Empty<int>();

    public int TotalVotes;

    /// The 1-based option this client voted for, or 0 if it has not voted.
    public int YourVote;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Open = buffer.ReadBoolean();
        buffer.ReadPadBits();

        Text = buffer.ReadString();

        Images = new string[buffer.ReadByte()];
        for (var i = 0; i < Images.Length; i++)
            Images[i] = buffer.ReadString();

        HasPoll = buffer.ReadBoolean();
        buffer.ReadPadBits();

        if (!HasPoll)
            return;

        Question = buffer.ReadString();

        var count = buffer.ReadByte();
        Options = new string[count];
        Votes = new int[count];
        for (var i = 0; i < count; i++)
        {
            Options[i] = buffer.ReadString();
            Votes[i] = buffer.ReadVariableInt32();
        }

        TotalVotes = buffer.ReadVariableInt32();
        YourVote = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Open);
        buffer.WritePadBits();

        buffer.Write(Text);

        var imageCount = Math.Min(Images.Length, MotdLimits.MaxEntries);
        buffer.Write((byte) imageCount);
        for (var i = 0; i < imageCount; i++)
            buffer.Write(Images[i]);

        var hasPoll = HasPoll && Options.Length > 0;
        buffer.Write(hasPoll);
        buffer.WritePadBits();

        if (!hasPoll)
            return;

        buffer.Write(Question);

        var optionCount = Math.Min(Options.Length, MotdLimits.MaxEntries);
        buffer.Write((byte) optionCount);
        for (var i = 0; i < optionCount; i++)
        {
            buffer.Write(Options[i]);
            buffer.WriteVariableInt32(i < Votes.Length ? Votes[i] : 0);
        }

        buffer.WriteVariableInt32(TotalVotes);
        buffer.WriteVariableInt32(YourVote);
    }

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
}

/// Sent by the client when it votes in the Message of the Day poll.
public sealed class MotdVoteMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    /// The 1-based option the client is voting for.
    public int Option;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Option = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Option);
    }

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
}

/// What an admin is asking the server to do with the Message of the Day from the window.
public enum MotdEditAction : byte
{
    /// Asks the server to send the current message, pictures and poll so the editor can show them.
    RequestState,

    /// Replaces the message, the pictures and the poll with the sent values. An empty question removes the poll.
    Save,

    /// Removes the poll, leaving the message and pictures untouched.
    ClearPoll,
}

/// Sent by a client editing the Message of the Day from the window. The server checks the admin flag itself and ignores anything it cannot validate.
public sealed class MotdEditMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public MotdEditAction Action;

    public string Text = string.Empty;

    public string[] Images = Array.Empty<string>();

    public string Question = string.Empty;

    public string[] Options = Array.Empty<string>();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Action = (MotdEditAction) buffer.ReadByte();

        Text = buffer.ReadString();

        Images = new string[buffer.ReadByte()];
        for (var i = 0; i < Images.Length; i++)
            Images[i] = buffer.ReadString();

        Question = buffer.ReadString();

        Options = new string[buffer.ReadByte()];
        for (var i = 0; i < Options.Length; i++)
            Options[i] = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Action);
        buffer.Write(Text);

        var imageCount = Math.Min(Images.Length, MotdLimits.MaxEntries);
        buffer.Write((byte) imageCount);
        for (var i = 0; i < imageCount; i++)
            buffer.Write(Images[i]);

        buffer.Write(Question);

        var optionCount = Math.Min(Options.Length, MotdLimits.MaxEntries);
        buffer.Write((byte) optionCount);
        for (var i = 0; i < optionCount; i++)
            buffer.Write(Options[i]);
    }

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
}

/// Limits shared by the Message of the Day messages. The real limits are enforced by the server, these only stop a malformed message from claiming an absurd number of entries.
internal static class MotdLimits
{
    public const int MaxEntries = 64;
}
