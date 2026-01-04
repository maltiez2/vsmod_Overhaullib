using CombatOverhaul.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Vanity;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class VanityPacket
{
    public long PlayerEntityId { get; set; }
    public byte[] HiddenSlots { get; set; } = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class BackpackVanityPacket
{
    public bool Hide { get; set; }
    public long PlayerEntityId { get; set; }
}

public class VanitySystemClient
{
    public VanitySystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<VanityPacket>()
            .RegisterMessageType<BackpackVanityPacket>()
            .SetMessageHandler<VanityPacket>(HandlePacket)
            .SetMessageHandler<BackpackVanityPacket>(HandlePacket);
    }

    public void HideBackpack(bool hide)
    {
        _clientChannel.SendPacket(new BackpackVanityPacket() { Hide = hide, PlayerEntityId = _api.World.Player?.Entity?.EntityId ?? 0 });
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;

    private void HandlePacket(BackpackVanityPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.PlayerEntityId) as EntityPlayer;
        if (player == null)
        {
            return;
        }

        player.MarkShapeModified();

        bool previous = player.WatchedAttributes?.GetBool(VanitySystemServer.HideBackpackAttribute) ?? false;
        if (previous != packet.Hide)
        {
            player.WatchedAttributes?.SetBool(VanitySystemServer.HideBackpackAttribute, packet.Hide);
        }

        player.MarkShapeModified();
    }

    private void HandlePacket(VanityPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.PlayerEntityId) as EntityPlayer;
        if (player == null)
        {
            return;
        }

        VanityInventory? inventory = GeneralUtils.GetVanityInventory(player.Player);
        if (inventory == null)
        {
            return;
        }

        bool retesselate = false;

        foreach (ISlotContentCanHide slot in inventory.OfType<ISlotContentCanHide>())
        {
            if (slot.Hide)
            {
                byte index = (byte)(slot as ItemSlot).Inventory.GetSlotId(slot as ItemSlot);
                if (!packet.HiddenSlots.Contains(index))
                {
                    retesselate = true;
                    slot.Hide = false;
                }
            }
            else
            {
                byte index = (byte)(slot as ItemSlot).Inventory.GetSlotId(slot as ItemSlot);
                if (packet.HiddenSlots.Contains(index))
                {
                    retesselate = true;
                    slot.Hide = true;
                }
            }

        }

        inventory.RecolorSlots();

        if (retesselate)
        {
            player.MarkShapeModified();
        }
    }
}

public class VanitySystemServer
{
    public VanitySystemServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<VanityPacket>()
            .RegisterMessageType<BackpackVanityPacket>()
            .SetMessageHandler<BackpackVanityPacket>(HandlePacket);

        _listener = _api.World.RegisterGameTickListener(_ => Resync(), 1000 * 60 * 1, 3 * 1000);

        _api.Event.PlayerNowPlaying += _ => Resync();
    }

    public const string HideBackpackAttribute = "combatoverhaul:hide-backpack";

    public void SendUpdate(IPlayer player, byte[] slots)
    {
        _serverChannel.BroadcastPacket(new VanityPacket()
        {
            PlayerEntityId = player.Entity?.EntityId ?? 0,
            HiddenSlots = slots
        });
    }

    public void Dispose()
    {
        _api.World.UnregisterCallback(_listener);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IServerNetworkChannel _serverChannel;
    private readonly ICoreServerAPI _api;
    private readonly long _listener;

    private void HandlePacket(IServerPlayer player, BackpackVanityPacket packet)
    {
        player.Entity?.WatchedAttributes?.SetBool(HideBackpackAttribute, packet.Hide);

        _serverChannel.BroadcastPacket(packet);
    }

    private void Resync()
    {
        _api.World.AllOnlinePlayers
            .Where(player => player.Entity != null)
            .Select(GeneralUtils.GetVanityInventory)
            .Foreach(inventory => inventory?.Synchronize());
    }
}