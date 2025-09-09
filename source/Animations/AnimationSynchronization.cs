using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.Animations;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimationRequestPacket
{
    public bool MainHand { get; set; }
    public string Animation { get; set; } = "";
    public float AnimationSpeed { get; set; }
    public float Weight { get; set; }
    public string Category { get; set; } = "";
    public double EaseOutDurationMs { get; set; }
    public double EaseInDurationMs { get; set; }
    public bool EaseOut { get; set; }
    public long EntityId { get; set; }
    public int ItemId { get; set; }

    public AnimationRequestPacket()
    {

    }

    public AnimationRequestPacket(AnimationRequestByCode request, bool mainHand, long entityId, int itemId)
    {
        MainHand = mainHand;
        Animation = request.Animation;
        AnimationSpeed = request.AnimationSpeed;
        Weight = request.Weight;
        Category = request.Category;
        EaseOutDurationMs = request.EaseOutDuration.TotalMilliseconds;
        EaseInDurationMs = request.EaseInDuration.TotalMilliseconds;
        EaseOut = request.EaseOut;
        EntityId = entityId;
        ItemId = itemId;
    }

    public (AnimationRequestByCode request, bool mainHand) ToRequest()
    {
        AnimationRequestByCode request = new(Animation, AnimationSpeed, Weight, Category, TimeSpan.FromMilliseconds(EaseOutDurationMs), TimeSpan.FromMilliseconds(EaseInDurationMs), EaseOut);
        return (request, MainHand);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimationStopRequestPacket
{
    public string Category { get; set; } = "";
    public long EntityId { get; set; }

    public AnimationStopRequestPacket()
    {

    }

    public AnimationStopRequestPacket(string category, long entityId)
    {
        Category = category;
        EntityId = entityId;
    }
}


public sealed class AnimationSystemClient
{
    public AnimationSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel("CO-animation-system")
            .RegisterMessageType<AnimationRequestPacket>()
            .RegisterMessageType<AnimationStopRequestPacket>()
            .SetMessageHandler<AnimationRequestPacket>(HandlePacket)
            .SetMessageHandler<AnimationStopRequestPacket>(HandlePacket);
    }

    public void SendPlayPacket(AnimationRequestByCode request, bool mainHand, long entityId, int itemId)
    {
        _clientChannel.SendPacket(new AnimationRequestPacket(request, mainHand, entityId, itemId));
    }
    public void SendStopPacket(string category, long entityId)
    {
        _clientChannel.SendPacket(new AnimationStopRequestPacket(category, entityId));
    }

    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;

    private void HandlePacket(AnimationRequestPacket packet)
    {
        if (_api.World.GetEntityById(packet.EntityId) is not EntityPlayer player) return;

        if (GetCurrentItemId(packet.MainHand, player) != packet.ItemId) return;

        (AnimationRequestByCode request, bool mainHand) = packet.ToRequest();

        player.GetBehavior<ThirdPersonAnimationsBehavior>()?.Play(request, mainHand);
    }

    private void HandlePacket(AnimationStopRequestPacket packet)
    {
        _api.World.GetEntityById(packet.EntityId)?.GetBehavior<ThirdPersonAnimationsBehavior>()?.Stop(packet.Category);
    }

    private static int GetCurrentItemId(bool mainHand, EntityPlayer player) => mainHand ? player?.RightHandItemSlot?.Itemstack?.Item?.Id ?? 0 : player?.LeftHandItemSlot?.Itemstack?.Item?.Id ?? 0;
}

public sealed class AnimationSystemServer
{
    public AnimationSystemServer(ICoreServerAPI api)
    {
        _serverChannel = api.Network.RegisterChannel("CO-animation-system")
            .RegisterMessageType<AnimationRequestPacket>()
            .RegisterMessageType<AnimationStopRequestPacket>()
            .SetMessageHandler<AnimationRequestPacket>(HandlePacket)
            .SetMessageHandler<AnimationStopRequestPacket>(HandlePacket);
    }


    private readonly IServerNetworkChannel _serverChannel;

    private void HandlePacket(IServerPlayer player, AnimationRequestPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }

    private void HandlePacket(IServerPlayer player, AnimationStopRequestPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }
}
