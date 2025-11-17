using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace CombatOverhaul.Integration;

internal static class InventorySafeguardsPatches
{
    public static void Patch(string harmonyId)
    {
        new Harmony(harmonyId).Patch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public, [typeof(IWorldAccessor), typeof(Packet_InventoryUpdate)]),
            prefix: new HarmonyMethod(InventorySafeguardsPatches.UpdateFromPacket)
            );
        new Harmony(harmonyId).Patch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public, [typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate)]),
            prefix: new HarmonyMethod(InventorySafeguardsPatches.UpdateFromPacketDouble)
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public, [typeof(IWorldAccessor), typeof(Packet_InventoryUpdate)]), HarmonyPatchType.Prefix);
        new Harmony(harmonyId).Unpatch(typeof(PlayerInventoryNetworkUtil).GetMethod("UpdateFromPacket", BindingFlags.Instance | BindingFlags.Public, [typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate)]), HarmonyPatchType.Prefix);
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
            LoggerUtil.Verbose(__instance.Api, typeof(InventorySafeguardsPatches), $"Error in 'UpdateFromPacket' (single).\nException: {exception}");
        }

        _skipUpdateFromPacket = false;

        return false;
    }

    [ThreadStatic] private static bool _skipUpdateFromDoublePacket;
    private static bool UpdateFromPacketDouble(PlayerInventoryNetworkUtil __instance, IWorldAccessor world, Packet_InventoryDoubleUpdate packet)
    {
        if (_skipUpdateFromDoublePacket) return true;

        _skipUpdateFromDoublePacket = true;

        try
        {
            __instance.UpdateFromPacket(world, packet);
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(__instance.Api, typeof(InventorySafeguardsPatches), $"Error in 'UpdateFromPacket' (double).\nException: {exception}");
        }

        _skipUpdateFromDoublePacket = false;

        return false;
    }
}