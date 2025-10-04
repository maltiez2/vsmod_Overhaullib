using CombatOverhaul.Armor;
using CombatOverhaul.Implementations;
using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class HarmonyPatches
{
    public static Settings ClientSettings { get; set; } = new();
    public static Settings ServerSettings { get; set; } = new();

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        _api = api;
        _reportedEntities.Clear();

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.CreateColliders)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.SmoothCameraTurning)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.OnFallToGround)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ReloadBagInventory)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetProperty("LightHsv", AccessTools.all)?.GetAccessors()[0],
                postfix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(LightHsv)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BagInventory).GetMethod("SaveSlotIntoBag", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(BagInventory_SaveSlotIntoBag)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BehaviorHealingItem).GetMethod("OnHeldInteractStart", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(BehaviorHealingItem_OnHeldInteractStart)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("OnSelfBeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetProperty("LightHsv", AccessTools.all)?.GetAccessors()[0], HarmonyPatchType.Postfix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BagInventory).GetMethod("SaveSlotIntoBag", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BehaviorHealingItem).GetMethod("OnHeldInteractStart", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        _api = null;
    }

    private static readonly FieldInfo? _entity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);


    private static bool CreateColliders(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {


        EntityPlayer? entity = (Entity?)_entity?.GetValue(__instance) as EntityPlayer;

        if (entity?.Api?.Side != EnumAppSide.Client) return true;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;

        if (animator == null) return true;

        AnimationPatches._animatorsLock.AcquireWriterLock(1000);
        if (!AnimationPatches._animators.ContainsKey(animator))
        {
            AnimationPatches._animators.Add(animator, entity);
        }
        AnimationPatches._animatorsLock.ReleaseWriterLock();



        return true;
    }

    internal static readonly HashSet<long> _reportedEntities = new();
    private static ICoreAPI? _api;


    private static readonly FieldInfo? _smoothedBodyYaw = typeof(EntityPlayerShapeRenderer).GetField("smoothedBodyYaw", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool SmoothCameraTurning(EntityPlayerShapeRenderer __instance, float bodyYaw, float mdt)
    {
        if (!ClientSettings.HandsYawSmoothing)
        {
            _smoothedBodyYaw?.SetValue(__instance, bodyYaw);
            return false;
        }
        else
        {
            return true;
        }
    }

    private const string _fallDamageThresholdMultiplierStat = "fallDamageThreshold";
    private const float _fallDamageMultiplier = 0.2f;
    private const float _fallDamageSpeedThreshold = 0.01f;
    private const double _newFallDistance = 4.5;
    
    private static bool OnFallToGround(EntityBehaviorHealth __instance, ref double withYMotion)
    {
        if ((__instance.entity as EntityAgent)?.ServerControls.Gliding == true)
        {
            return true;
        }

        if (__instance.entity is not EntityPlayer player)
        {
            return true;
        }

        Vec3d positionBeforeFalling = __instance.entity.PositionBeforeFalling;
        double lowestHeight = player.Pos.Y;//CurrentBelowBlockHeight(player);
        double fallDistance = (positionBeforeFalling.Y - lowestHeight) / Math.Max(player.Stats.GetBlended(_fallDamageThresholdMultiplierStat), 0.001);

        if (fallDistance < _newFallDistance) return false;

        if (Math.Abs(withYMotion) < _fallDamageSpeedThreshold) return false;

        double fallDamage = Math.Max(0, fallDistance - _newFallDistance) * player.Properties.FallDamageMultiplier * _fallDamageMultiplier;

        player.ReceiveDamage(new DamageSource()
        {
            Source = EnumDamageSource.Fall,
            Type = EnumDamageType.Gravity,
            IgnoreInvFrames = true,
        }, (float)fallDamage);

        return false;
    }
    private static double CurrentBelowBlockHeight(EntityAgent player)
    {
        double height = player.SidedPos.Y;
        IBlockAccessor accessor = _api.World.GetBlockAccessor(false, false, false);

        int heightDiff = 1;
        while (heightDiff < height)
        {
            BlockPos blockPos = player.SidedPos.AsBlockPos;
            blockPos.Y -= heightDiff;

            BlockPos bp0 = blockPos.Copy();
            BlockPos bp1 = blockPos.Copy();
            BlockPos bp2 = blockPos.Copy();
            BlockPos bp3 = blockPos.Copy();

            Vec3d entityPosPos = player.SidedPos.XYZ;

            float xDiff = player.CollisionBox.XSize / 2f;
            float zDiff = player.CollisionBox.ZSize / 2f;

            bp0.X = (int)(entityPosPos.X - xDiff);
            bp0.Z = (int)(entityPosPos.Z - zDiff);
            bp1.X = (int)(entityPosPos.X + xDiff);
            bp1.Z = (int)(entityPosPos.Z - zDiff);
            bp2.X = (int)(entityPosPos.X - xDiff);
            bp2.Z = (int)(entityPosPos.Z + zDiff);
            bp3.X = (int)(entityPosPos.X + xDiff);
            bp3.Z = (int)(entityPosPos.Z + zDiff);

            List<Block> blocks = [accessor.GetBlock(bp0)];
            if (bp0 != bp1) blocks.Add(accessor.GetBlock(bp1));
            if (bp0 != bp2) blocks.Add(accessor.GetBlock(bp2));
            if (bp0 != bp3) blocks.Add(accessor.GetBlock(bp3));

            if (blocks.Exists(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0))
            {
                return blockPos.Y + blocks
                    .Where(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                    .Select(block => block.CollisionBoxes
                        .Select(box => box.MaxY)
                        .Max())
                    .Max();
            }

            heightDiff++;
        }

        return 0;
    }

    private static void ReloadBagInventory(BagInventory __instance, ref InventoryBase parentinv, ref ItemSlot[] bagSlots)
    {
        if (parentinv is not InventoryBasePlayer inventory) return;

        bagSlots = AppendGearInventorySlots(bagSlots, inventory.Owner);

        if (bagSlots.Length == 4)
        {
            bagSlots = Enumerable.Range(0, ArmorInventory._totalSlotsNumber).Select(_ => new DummySlot() as ItemSlot).Concat(bagSlots).ToArray();
        }
    }
    private static ItemSlot[] AppendGearInventorySlots(ItemSlot[] backpackSlots, Entity owner)
    {
        IInventory? inventory = GetGearInventory(owner);

        if (inventory == null) return backpackSlots;

        if (backpackSlots.Any(slot => slot.Inventory == inventory)) return backpackSlots;

        ItemSlot[] gearSlots = inventory?.ToArray() ?? Array.Empty<ItemSlot>();

        return gearSlots.Concat(backpackSlots).ToArray();
    }
    private static IInventory? GetGearInventory(Entity entity)
    {
        return (entity as EntityPlayer)?.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
    }
    private static IInventory? GetBackpackInventory(EntityPlayer player)
    {
        return player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
    private static bool BagInventory_SaveSlotIntoBag(BagInventory __instance, ItemSlotBagContent slot)
    {
        ItemStack? backPackStack = __instance.BagSlots[slot.BagIndex]?.Itemstack;

        try
        {
            backPackStack?.Collectible.GetCollectibleInterface<IHeldBag>()?.Store(backPackStack, slot);
        }
        catch (Exception exception)
        {
            Debug.WriteLine("BagInventory_SaveSlotIntoBag");
            Debug.WriteLine(exception);
            return true;
        }

        return false;
    }

    private static void LightHsv(EntityPlayer __instance, ref byte[] __result)
    {
        if (__instance?.Player == null || !__instance.Alive || __instance.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator) return;

        if (__result == null) __result = new byte[3] { 0, 0, 0 };

        IInventory? gearInventory = GetGearInventory(__instance);
        if (gearInventory == null) return;

        foreach (ItemSlot slot in gearInventory.Where(slot => slot?.Empty == false).Where(slot => slot.Itemstack?.Collectible.GetCollectibleInterface<IWearableLightSource>() != null))
        {
            AddLight(ref __result, slot.Itemstack.Collectible.GetCollectibleInterface<IWearableLightSource>().GetLightHsv(__instance, slot));
        }

        foreach (ItemSlot slot in gearInventory.Where(slot => slot?.Empty == false).Where(slot => slot.Itemstack?.Collectible?.LightHsv[2] > 0))
        {
            AddLight(ref __result, slot.Itemstack.Collectible.LightHsv);
        }

        IInventory? backpackInventory = GetBackpackInventory(__instance);
        if (backpackInventory == null) return;

        for (int index = 0; index < 4; index++)
        {
            ItemSlot slot = backpackInventory[index];


            if (slot?.Empty == false && slot.Itemstack?.Collectible.GetCollectibleInterface<IWearableLightSource>() != null)
            {
                AddLight(ref __result, slot.Itemstack.Collectible.GetCollectibleInterface<IWearableLightSource>().GetLightHsv(__instance, slot));
            }

            if (slot?.Empty == false && slot.Itemstack?.Collectible?.LightHsv[2] > 0)
            {
                AddLight(ref __result, slot.Itemstack.Collectible.LightHsv);
            }
        }
    }

    private static readonly byte[] _lightHsvBuffer = new byte[3] { 0, 0, 0 };
    private static void AddLight(ref byte[] result, byte[] hsv)
    {
        float totalBrightness = result[2] + hsv[2];
        float brightnessFraction = hsv[2] / totalBrightness;

        _lightHsvBuffer[0] = result[0];
        _lightHsvBuffer[1] = result[1];
        _lightHsvBuffer[2] = result[2];

        result = _lightHsvBuffer;

        result[0] = (byte)(hsv[0] * brightnessFraction + result[0] * (1 - brightnessFraction));
        result[1] = (byte)(hsv[1] * brightnessFraction + result[1] * (1 - brightnessFraction));
        result[2] = Math.Max(hsv[2], result[2]);
    }


    private static bool BehaviorHealingItem_OnHeldInteractStart(EntityAgent byEntity)
    {
        return !((byEntity as EntityPlayer)?.LeftHandItemSlot?.Itemstack?.Item as IHasMeleeWeaponActions)?.CanBlock(false) ?? true;
    }
}