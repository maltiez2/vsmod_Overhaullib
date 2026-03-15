using AnimationsLib;
using CollidersLib;
using CombatOverhaul.Integration.Transpilers;
using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class AnimationPatches
{
    public static void Patch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaquePlayer)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
    }

    private static void DoRender3DOpaque(EntityShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }

    }

    private static void DoRender3DOpaquePlayer(EntityPlayerShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }
    }
}