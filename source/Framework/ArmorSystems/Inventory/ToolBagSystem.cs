using CombatOverhaul.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CombatOverhaul.Armor;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ToolBagPacket
{
    public string ToolBagId { get; set; } = "";
    public bool MainHand { get; set; } = true;
}

public class ToolBagSystemClient
{
    public ToolBagSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>();
    }

    public void Send(string toolBagId, bool mainHand)
    {
        _clientChannel.SendPacket(new ToolBagPacket
        {
            ToolBagId = toolBagId,
            MainHand = mainHand
        });
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
}

public class ToolBagSystemServer
{
    public ToolBagSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>()
            .SetMessageHandler<ToolBagPacket>(HandlePacket);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly ICoreServerAPI _api;
    private IWorldAccessor _world => _api.World;

    private void HandlePacket(IServerPlayer player, ToolBagPacket packet)
    {
        IInventory? inventory = GetBackpackInventory(player);

        if (inventory == null) return;

        try
        {
            ProcessSlots(player, inventory, packet.ToolBagId, mainHand: true);
            ProcessSlots(player, inventory, packet.ToolBagId, mainHand: false);
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(player.Entity.Api, this, $"Error when trying to use tool bag/sheath '{packet.ToolBagId}': {exception}");
        }
    }

    private ItemSlotToolHolder? GetToolSlot(IInventory inventory, string bagId, bool mainHand)
    {
        return inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == bagId && (slot as ItemSlotToolHolder)?.MainHand == mainHand, null) as ItemSlotToolHolder;
    }

    private ItemSlotTakeOutOnly? GetTakeOutSlot(IInventory inventory, string bagId, bool mainHand)
    {
        return inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == bagId && (slot as ItemSlotTakeOutOnly)?.MainHand == mainHand, null) as ItemSlotTakeOutOnly;
    }

    private ItemSlot GetActiveSlot(IServerPlayer player, bool mainHand)
    {
        return mainHand ? player.Entity.ActiveHandItemSlot : player.Entity.LeftHandItemSlot;
    }

    private void ProcessSlots(IServerPlayer player, IInventory inventory, string bagId, bool mainHand)
    {
        ItemSlotToolHolder? toolSlot = GetToolSlot(inventory, bagId, mainHand);
        if (toolSlot == null) return;

        ItemSlot activeSlot = GetActiveSlot(player, mainHand);
        if (activeSlot.Empty && toolSlot.Empty) return;

        if (toolSlot.Empty)
        {
            PutBack(player, inventory, bagId, mainHand);
        }
        else
        {
            TakeOut(player, inventory, bagId, mainHand);
        }
    }

    private void TakeOut(IServerPlayer player, IInventory inventory, string bagId, bool mainHand)
    {
        ItemSlot activeSlot = GetActiveSlot(player, mainHand);
        ItemSlotTakeOutOnly? takeOutSlot = GetTakeOutSlot(inventory, bagId, mainHand);
        if (takeOutSlot == null) return;

        int movedQuantity = 0;
        if (activeSlot.Itemstack?.StackSize > 0)
        {
            takeOutSlot.CanHoldNow = true;
            movedQuantity = activeSlot.TryPutInto(_world, takeOutSlot, activeSlot.Itemstack.StackSize);
        }

        if (!activeSlot.Empty) return;

        ItemSlotToolHolder? toolSlot = GetToolSlot(inventory, bagId, mainHand);
        if (toolSlot == null || toolSlot.Empty) return;

        movedQuantity = toolSlot.TryPutInto(_world, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);

        if (takeOutSlot != null) takeOutSlot.CanHoldNow = false;
    }

    private void PutBack(IServerPlayer player, IInventory inventory, string bagId, bool mainHand)
    {
        ItemSlotToolHolder? toolSlot = GetToolSlot(inventory, bagId, mainHand);
        ItemSlot activeSlot = GetActiveSlot(player, mainHand);
        if (toolSlot == null || !toolSlot.Empty || activeSlot.Empty) return;

        int movedQuantity = activeSlot.TryPutInto(_world, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
    }

    private static IInventory? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
}