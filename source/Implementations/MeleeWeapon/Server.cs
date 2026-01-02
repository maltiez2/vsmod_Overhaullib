using CombatOverhaul.RangedSystems;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public class MeleeWeaponServer : RangeWeaponServer
{
    public MeleeWeaponServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        try
        {
            Stats = item.Attributes.AsObject<MeleeWeaponStats>();
        }
        catch (Exception exception)
        {
            throw new Exception($"Error while getting stats for item '{item.Code}' on server side: {exception.Message}");
        }
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (slot?.Itemstack == null || slot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(slot.Itemstack);
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (stats == null)
        {
            return false;
        }

        Vector3d playerVelocity = new(player.Entity.ServerPos.Motion.X, player.Entity.ServerPos.Motion.Y, player.Entity.ServerPos.Motion.Z);

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = 1 * stackStats.ThrownDamageMultiplier,
            DamageTier = Stats.ThrowAttack?.DamageTier ?? 0 + stackStats.ThrownDamageTierBonus,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3d.Normalize(new Vector3d(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * (Stats.ThrowAttack?.Velocity ?? 1) * stackStats.ThrownProjectileSpeedMultiplier + playerVelocity
        };

        AssetLocation projectileCode = slot.Itemstack.Item.Code.Clone();

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, slot.TakeOut(1), slot.Itemstack, shooter);

        SwapToNewProjectile(player, slot, projectileCode);

        slot.MarkDirty();

        return true;
    }

    protected readonly MeleeWeaponStats Stats;

    protected virtual void SwapToNewProjectile(IServerPlayer player, ItemSlot slot, AssetLocation projectileCode)
    {
        if (slot.Itemstack == null || slot.Itemstack.StackSize == 0)
        {
            ItemSlot? replacementSlot = null;
            WalkInventory(player.Entity, slot =>
            {
                if (slot?.Itemstack?.Item?.Code == null) return true;

                if (slot.Itemstack.Item.Code.ToString() == projectileCode.ToString())
                {
                    replacementSlot = slot;
                    return false;
                }

                return true;
            });

            if (replacementSlot == null)
            {
                string projectilePath = projectileCode.ToShortString();

                while (projectilePath.Contains('-'))
                {
                    int delimiterIndex = projectilePath.LastIndexOf('-');
                    projectilePath = projectilePath.Substring(0, delimiterIndex);
                    string wildcard = $"{projectilePath}-*";

                    WalkInventory(player.Entity, slot =>
                    {
                        if (slot?.Itemstack?.Item?.Code == null) return true;

                        if (WildcardUtil.Match(wildcard, slot.Itemstack.Item.Code.ToString()))
                        {
                            replacementSlot = slot;
                            return false;
                        }

                        return true;
                    });

                    if (replacementSlot != null) break;
                }
            }

            if (replacementSlot != null)
            {
                slot.TryFlipWith(replacementSlot);
                replacementSlot.MarkDirty();
            }
        }
    }

    protected virtual void WalkInventory(EntityPlayer player, System.Func<ItemSlot, bool> selector)
    {
        foreach (ItemSlot hotbarSlot in player.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName))
        {
            if (!selector.Invoke(hotbarSlot))
            {
                return;
            }
        }

        foreach (ItemSlot backpackSlot in player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName))
        {
            if (!selector.Invoke(backpackSlot))
            {
                return;
            }
        }
    }
}