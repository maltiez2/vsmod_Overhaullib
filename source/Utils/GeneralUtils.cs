using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace CombatOverhaul.Utils;

public static class GeneralUtils
{
    public static InventoryPlayerBackPacks? GetBackpackInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }

    public static InventoryCharacter? GetCharacterInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) as InventoryCharacter;
    }
}
