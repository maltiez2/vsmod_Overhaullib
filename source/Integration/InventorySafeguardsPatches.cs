using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace CombatOverhaul.Integration;

internal static class InventorySafeguardsPatches
{
    public static void Patch(string harmonyId)
    {
        new Harmony(harmonyId).Patch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public),
            prefix: new HarmonyMethod(InventorySafeguardsPatches.UpdateFromPacket)
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public), HarmonyPatchType.Prefix);
    }

    [ThreadStatic] private static bool _skipUpdateFromPacket;
    private static bool UpdateFromPacket(PlayerInventoryNetworkUtil __instance, IWorldAccessor world, Packet_InventoryUpdate packet)
    {
        if (_skipUpdateFromPacket) return true;

        _skipUpdateFromPacket = true;

        try
        {
            __instance.UpdateFromPacket(world, packet);
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(__instance.Api, typeof(InventorySafeguardsPatches), $"Error in 'UpdateFromPacket'. Packet info:\n- InventoryId = {packet.InventoryId}\n- ItemId = {packet.ItemStack?.ItemId}\n- StackSize = {packet.ItemStack?.StackSize}\nException: {exception}");
        }

        _skipUpdateFromPacket = false;

        return false;
    }
}