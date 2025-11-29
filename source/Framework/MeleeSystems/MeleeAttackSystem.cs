using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackPacket
{
    public MeleeDamagePacket[] MeleeAttackDamagePackets { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleePushPacket
{
    public MeleeCollisionPacket[] MeleeAttackDamagePackets { get; set; }
}

public abstract class MeleeSystem
{
    public const string NetworkChannelId = "CombatOverhaul:damage-packets";
}

public readonly struct AttackId
{
    public readonly int ItemId;
    public readonly int Id;

    public AttackId(int itemId, int id)
    {
        ItemId = itemId;
        Id = id;
    }
}

public sealed class MeleeSystemClient : MeleeSystem
{
    public MeleeSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .RegisterMessageType<MeleePushPacket>();
    }

    public void SendPackets(IEnumerable<MeleeDamagePacket> packets)
    {
        _clientChannel.SendPacket(new MeleeAttackPacket
        {
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }

    public void SendPackets(IEnumerable<MeleeCollisionPacket> packets)
    {
        _clientChannel.SendPacket(new MeleePushPacket
        {
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }

    private readonly IClientNetworkChannel _clientChannel;
}

public sealed class MeleeSystemServer : MeleeSystem
{
    public delegate void MeleeDamageDelegate(Entity target, DamageSource damageSource, ItemSlot? slot, ref float damage);

    public event MeleeDamageDelegate? OnDealMeleeDamage;

    public MeleeSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .RegisterMessageType<MeleePushPacket>()
            .SetMessageHandler<MeleeAttackPacket>(HandlePacket)
            .SetMessageHandler<MeleePushPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, MeleeAttackPacket packet)
    {
        foreach (MeleeDamagePacket damagePacket in packet.MeleeAttackDamagePackets)
        {
            Attack(damagePacket);
        }
    }

    private void HandlePacket(IServerPlayer player, MeleePushPacket packet)
    {
        foreach (MeleeCollisionPacket collisionPacket in packet.MeleeAttackDamagePackets)
        {
            Push(collisionPacket);
        }
    }

    private void Attack(MeleeDamagePacket packet)
    {
        Entity? target = _api.World.GetEntityById(packet.TargetEntityId);

        if (target == null || !target.Alive) return;

        Entity attacker = _api.World.GetEntityById(packet.AttackerEntityId);
        string targetName = target.GetName();

        IServerPlayer? serverPlayer = (attacker as EntityPlayer)?.Player as IServerPlayer;
        if (serverPlayer != null && packet.DamageType != "Heal")
        {
            if (target is EntityPlayer && (!_api.Server.Config.AllowPvP || !serverPlayer.HasPrivilege("attackplayers")))
            {
                return;
            }

            if (target is EntityAgent && !serverPlayer.HasPrivilege("attackcreatures"))
            {
                return;
            }
        }

        ItemSlot? slot = (packet.MainHand ? (attacker as EntityAgent)?.RightHandItemSlot : (attacker as EntityAgent)?.LeftHandItemSlot);

        DirectionalTypedDamageSource damageSource = new()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = attacker,
            CauseEntity = attacker,
            DamageTypeData = new DamageData(Enum.Parse<EnumDamageType>(packet.DamageType), packet.Tier, packet.ArmorPiercingTier),
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Collider = packet.Collider,
            KnockbackStrength = packet.Knockback,
            DamageTier = packet.Tier,
            Type = Enum.Parse<EnumDamageType>(packet.DamageType),
            Weapon = packet.MainHand ? serverPlayer?.Entity.RightHandItemSlot.Itemstack : serverPlayer?.Entity.LeftHandItemSlot.Itemstack,
            IgnoreInvFrames = true
        };

        bool damageReceived = DealDamage(target, damageSource, slot, packet.Damage);

        if (packet.StaggerTimeMs > 0)
        {
            target.GetBehavior<StaggerBehavior>()?.TriggerStagger(TimeSpan.FromMilliseconds(packet.StaggerTimeMs), packet.StaggerTier);
        }

        DealDurabilityDamage(slot, packet, attacker);

        PrintLog(attacker, damageReceived, target, packet, targetName);
    }

    private void Push(MeleeCollisionPacket packet)
    {
        return; // Requires rewrite of vanilla entities physics system
        
        Entity? target = _api.World.GetEntityById(packet.TargetEntityId);

        if (target == null || !target.Alive) return;

        Entity attacker = _api.World.GetEntityById(packet.AttackerEntityId);

        IServerPlayer? serverPlayer = (attacker as EntityPlayer)?.Player as IServerPlayer;
        if (serverPlayer != null)
        {
            if (target is EntityPlayer && (!_api.Server.Config.AllowPvP || !serverPlayer.HasPrivilege("attackplayers")))
            {
                return;
            }

            if (target is EntityAgent && !serverPlayer.HasPrivilege("attackcreatures"))
            {
                return;
            }
        }

        if (packet.PushTier <= 0) return;

        int attackerTier = packet.PushTier;
        int targetTier = 1;

        Vector3d targetVelocity = new(target.Pos.Motion.X, target.Pos.Motion.Y, target.Pos.Motion.Z);
        Vector3d targetPosition = new(target.Pos.X, target.Pos.Y, target.Pos.Z);
        Vector3d attackerVelocity = new(attacker.Pos.Motion.X, attacker.Pos.Motion.Y, attacker.Pos.Motion.Z);
        Vector3d attackerPosition = new(attacker.Pos.X, attacker.Pos.Y, attacker.Pos.Z);

        Vector3d direction = targetPosition - attackerPosition;
        Vector3d relativeSpeed = Vector3d.Dot(direction, targetVelocity - attackerVelocity) * direction.Normalized();
        bool movingTowardsEachOther = Vector3d.Dot(direction, relativeSpeed) < 0;

        if (!movingTowardsEachOther) return;

        Vector3d recoil = relativeSpeed * targetTier / attackerTier;
        Vector3d targetVelocityDelta = relativeSpeed - recoil;

        target.Pos.Motion.X -= targetVelocityDelta.X;
        target.Pos.Motion.Y -= targetVelocityDelta.Y;
        target.Pos.Motion.Z -= targetVelocityDelta.Z;
        target.ServerPos.Motion.X -= targetVelocityDelta.X;
        target.ServerPos.Motion.Y -= targetVelocityDelta.Y;
        target.ServerPos.Motion.Z -= targetVelocityDelta.Z;
    }

    private bool DealDamage(Entity target, DamageSource damageSource, ItemSlot? slot, float damage)
    {
        OnDealMeleeDamage?.Invoke(target, damageSource, slot, ref damage);

        return target.ReceiveDamage(damageSource, damage);
    }

    private void DealDurabilityDamage(ItemSlot? slot, MeleeDamagePacket packet, Entity? attacker)
    {
        if (packet.DurabilityDamage <= 0) return;

        if (slot?.Itemstack?.Collectible != null && attacker != null)
        {
            slot.Itemstack.Collectible.DamageItem(attacker.Api.World, attacker, slot, packet.DurabilityDamage);
            slot.MarkDirty();
        }
    }

    private void PrintLog(Entity? attacker, bool damageReceived, Entity target, MeleeDamagePacket packet, string targetName)
    {
        bool printIntoChat = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings.PrintMeleeHits;

        if (printIntoChat)
        {
            float damage = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;

            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage", Lang.Get($"combatoverhaul:entity-damage-zone-{(ColliderTypes)packet.ColliderType}"), targetName, $"{damage:F2}");

            ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
        }
    }
}