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

        /*ItemSlotToolHolder? mainHandToolSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotToolHolder)?.MainHand == true, null) as ItemSlotToolHolder;
        ItemSlotToolHolder? offHandToolSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotToolHolder)?.MainHand == false, null) as ItemSlotToolHolder;
        ItemSlotTakeOutOnly? mainHandSinkSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotTakeOutOnly)?.MainHand == true, null) as ItemSlotTakeOutOnly;
        ItemSlotTakeOutOnly? offHandHandSinkSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotTakeOutOnly)?.MainHand == false, null) as ItemSlotTakeOutOnly;
        ItemSlot? mainHandActiveSlot = player.Entity.ActiveHandItemSlot;
        ItemSlot? offHandActiveSlot = player.Entity.LeftHandItemSlot;

        if (mainHandToolSlot != null && mainHandSinkSlot != null && mainHandActiveSlot != null)
        {
            ProcessSlots(mainHandToolSlot, mainHandSinkSlot, mainHandActiveSlot, player);
        }

        if (offHandToolSlot != null && offHandHandSinkSlot != null && offHandActiveSlot != null)
        {
            ProcessSlots(offHandToolSlot, offHandHandSinkSlot, offHandActiveSlot, player);
        }*/

        try
        {
            /*mainHandToolSlot?.MarkDirty();
            offHandToolSlot?.MarkDirty();
            mainHandSinkSlot?.MarkDirty();
            offHandHandSinkSlot?.MarkDirty();
            mainHandActiveSlot?.MarkDirty();
            offHandActiveSlot?.MarkDirty();*/

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
            /*if (movedQuantity > 0)
            {
                activeSlot.MarkDirty();
                takeOutSlot = GetTakeOutSlot(inventory, bagId, mainHand);
                takeOutSlot?.MarkDirty();
            }*/
        }

        if (!activeSlot.Empty) return;

        ItemSlotToolHolder? toolSlot = GetToolSlot(inventory, bagId, mainHand);
        if (toolSlot == null || toolSlot.Empty) return;

        movedQuantity = toolSlot.TryPutInto(_world, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);
        /*if (movedQuantity > 0)
        {
            activeSlot.MarkDirty();
            toolSlot = GetToolSlot(inventory, bagId, mainHand);
            toolSlot?.MarkDirty();
        }*/

        if (takeOutSlot != null) takeOutSlot.CanHoldNow = false;
    }

    private void PutBack(IServerPlayer player, IInventory inventory, string bagId, bool mainHand)
    {
        ItemSlotToolHolder? toolSlot = GetToolSlot(inventory, bagId, mainHand);
        ItemSlot activeSlot = GetActiveSlot(player, mainHand);
        if (toolSlot == null || !toolSlot.Empty || activeSlot.Empty) return;

        int movedQuantity = activeSlot.TryPutInto(_world, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
        /*if (movedQuantity > 0)
        {
            activeSlot.MarkDirty();
            toolSlot = GetToolSlot(inventory, bagId, mainHand);
            toolSlot?.MarkDirty();
        }*/
    }

    private void ProcessSlots(ItemSlotToolHolder toolSlot, ItemSlotTakeOutOnly sinkSlot, ItemSlot activeSlot, IServerPlayer player)
    {
        try
        {
            if (toolSlot.Empty && !activeSlot.Empty && toolSlot.CanHold(activeSlot))
            {
                Flip(activeSlot, toolSlot);
            }
            else if (!toolSlot.Empty && !activeSlot.Empty && toolSlot.CanHold(activeSlot))
            {
                Flip(activeSlot, toolSlot);
            }
            else if (!toolSlot.Empty)
            {
                TakeOut(activeSlot, toolSlot, sinkSlot, player);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(player.Entity.Api, this, $"(player: {player.PlayerName}) Exception when trying to interact with sheath/quiver:\n{exception}");
        }
    }

    private void Flip(ItemSlot activeSlot, ItemSlotToolHolder toolSlot)
    {
        bool canTakeActive = toolSlot.CanTakeFrom(activeSlot) || activeSlot.Itemstack == null;
        bool canTakeTool = activeSlot.CanTakeFrom(toolSlot) || toolSlot.Itemstack == null;
        if (!canTakeActive || !canTakeTool) return;

        ItemStack? toolSlotStack = toolSlot.Itemstack;
        ItemStack? activeSlotStack = activeSlot.Itemstack;

        toolSlot.Itemstack = activeSlotStack;
        activeSlot.Itemstack = toolSlotStack;
    }

    private void TakeOut(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, ItemSlotTakeOutOnly sinkSlot, IServerPlayer player)
    {
        if (!sinkSlot.Empty && !activeSlot.Empty) return;

        bool canTakeActive = sinkSlot.CanTakeFrom(activeSlot) || activeSlot.Itemstack == null;
        bool canTakeTool = activeSlot.CanTakeFrom(toolSlot) || toolSlot.Itemstack == null;
        if (!canTakeActive || !canTakeTool) return;

        ItemStack? toolSlotStack = toolSlot.Itemstack;
        ItemStack? activeSlotStack = activeSlot.Itemstack;

        if (activeSlot.Empty)
        {
            activeSlot.Itemstack = toolSlotStack;
            toolSlot.Itemstack = null;
        }

        if (!activeSlot.Empty && sinkSlot.Empty)
        {
            activeSlot.Itemstack = toolSlotStack;
            sinkSlot.Itemstack = activeSlotStack;
            toolSlot.Itemstack = null;
        }
    }

    private void PutBack(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, IServerPlayer player)
    {
        activeSlot.TryPutInto(player.Entity.World, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
    }

    private static IInventory? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
}