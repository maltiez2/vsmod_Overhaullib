using CombatOverhaul.Inputs;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public static partial class InteractionsTester
{
    public static bool PlayerTriesToInteract(EntityPlayer player, bool mainHand, ActionEventData eventData)
    {
        return eventData.AltPressed ||
            PlayerTriesToPutItemOnGround(player, mainHand, eventData) ||
            PlayerTriesToPutItemIntoRack(player, mainHand, eventData) ||
            PlayerTriesToPutItemIntoGroundStorage(player, mainHand, eventData);
    }
    public static bool PlayerTriesToPutItemIntoRack(EntityPlayer player, bool mainHand, ActionEventData eventData)
    {
        Block? selectedBlock = player.BlockSelection?.Block;
        if (selectedBlock == null) return false;

        WorldInteraction[] interactions = selectedBlock.GetPlacedBlockInteractionHelp(player.Api.World, player.BlockSelection, player.Player);

        return interactions.Any(interaction => IsWorldInteractionBlocking(interaction, player, mainHand, eventData));
    }
    public static bool PlayerTriesToPutItemOnGround(EntityPlayer player, bool mainHand, ActionEventData eventData)
    {
        if (!mainHand) return false;

        bool blockSelected = player.BlockSelection?.Block != null;
        bool modifiersPressed = eventData.Modifiers.Contains(EnumEntityAction.ShiftKey) && eventData.Modifiers.Contains(EnumEntityAction.CtrlKey);

        return blockSelected && modifiersPressed;
    }
    public static bool IsWorldInteractionBlocking(WorldInteraction interaction, EntityPlayer player, bool mainHand, ActionEventData eventData)
    {
        bool sameMouseButton =
            eventData.Action.Action == EnumEntityAction.LeftMouseDown && interaction.MouseButton == EnumMouseButton.Left ||
            eventData.Action.Action == EnumEntityAction.RightMouseDown && interaction.MouseButton == EnumMouseButton.Right;

        ItemSlot? currentSlot = mainHand ? player.RightHandItemSlot : player.LeftHandItemSlot;
        ItemStack? currentStack = currentSlot?.Itemstack;
        if (currentStack != null && interaction.Itemstacks != null && interaction.Itemstacks.Length > 0 && !interaction.Itemstacks.Any(stack => stack?.Item?.Id == currentStack.Item?.Id))
        {
            return false;
        }

        return sameMouseButton && mainHand && !interaction.RequireFreeHand;
    }
    public static bool PlayerTriesToPutItemIntoGroundStorage(EntityPlayer player, bool mainHand, ActionEventData eventData)
    {
        Block? selectedBlock = player.BlockSelection?.Block;
        if (selectedBlock is not BlockGroundStorage) return false;

        bool modifiersPressed = eventData.Modifiers.Contains(EnumEntityAction.CtrlKey);

        return modifiersPressed;
    }
}
