using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
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
    public static event Action<Entity, float>? OnBeforeFrame;
    public static Settings ClientSettings { get; set; } = new();
    public static Settings ServerSettings { get; set; } = new();
    public static Dictionary<long, ThirdPersonAnimationsBehavior> AnimationBehaviors { get; } = [];
    public static FirstPersonAnimationsBehavior? FirstPersonAnimationBehavior { get; set; }
    public static long OwnerEntityId { get; set; } = 0;
    public static HashSet<long> ActiveEntities { get; set; } = [];
    public static ObjectCache<ClientAnimator, EntityPlayer>? Animators { get; private set; }

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        Animators = new(api, "animators to players cache", 10000, 5 * 60 * 1000, threadSafe: true);

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaquePlayer)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(BeforeRender)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);

        Animators?.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnFrameInvoke(ClientAnimator? animator, ElementPose pose)
    {
        if (ClientSettings.DisableAllAnimations || animator == null || Animators == null) return;

        if (pose is ExtendedElementPose extendedPose)
        {
            if (extendedPose.ElementNameEnum == EnumAnimatedElement.Unknown && animator is not ClientItemAnimator) return;

            if (extendedPose.Player != null)
            {
                if (!ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(extendedPose.Player.EntityId, out ThirdPersonAnimationsBehavior? behavior))
                {
                    behavior.OnFrame(extendedPose.Player, pose, animator);
                }

                if (extendedPose.Player.EntityId == OwnerEntityId)
                {
                    FirstPersonAnimationBehavior?.OnFrame(extendedPose.Player, pose, animator);
                }

                return;
            }
        }

        if (Animators.Get(animator, out EntityPlayer? entity))
        {
            if (!ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(entity.EntityId, out ThirdPersonAnimationsBehavior? behavior))
            {
                behavior.OnFrame(entity, pose, animator);
            }

            if (entity.EntityId == OwnerEntityId)
            {
                FirstPersonAnimationBehavior?.OnFrame(entity, pose, animator);
            }

            if (pose is ExtendedElementPose extendedPose2 && extendedPose2.Player == null)
            {
                extendedPose2.Player = entity;
            }
        }
    }

    private static void BeforeRender(EntityShapeRenderer __instance, float dt)
    {
        if (!ClientSettings.DisableAllAnimations)
        {
            OnBeforeFrame?.Invoke(__instance.entity, dt);
        }
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

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        //if (isShadowPass) return true;



        ItemSlot? slot;

        if (right)
        {
            slot = (__instance.entity as EntityPlayer)?.RightHandItemSlot;
        }
        else
        {
            slot = (__instance.entity as EntityPlayer)?.LeftHandItemSlot;
        }

        if (slot?.Itemstack?.Item == null) return true;

        Animatable? behavior = slot.Itemstack.Item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;

        if (behavior == null) return true;

        ItemRenderInfo renderInfo = __instance.capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.HandTp, dt);

        behavior.BeforeRender(__instance.capi, slot.Itemstack, __instance.entity, EnumItemRenderTarget.HandFp, dt);

        (string textureName, _) = slot.Itemstack.Item.Textures.First();

        TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);

        renderInfo.TextureId = atlasPos.atlasTextureId;

        Vec4f? lightrgbs = (Vec4f?)typeof(EntityShapeRenderer)
                                          .GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(__instance);



        bool result = !behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo);



        return result;
    }
}