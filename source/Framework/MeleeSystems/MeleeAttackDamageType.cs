using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MeleeDamagePacket
{
    public string DamageType { get; set; } = "";
    public int Tier { get; set; }
    public int ArmorPiercingTier { get; set; }
    public float Damage { get; set; }
    public float Knockback { get; set; }
    public double[] Position { get; set; } = [];
    public string Collider { get; set; } = "";
    public int ColliderType { get; set; }
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public int DurabilityDamage { get; set; }
    public bool MainHand { get; set; }
    public int StaggerTimeMs { get; set; }
    public int StaggerTier { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MeleeCollisionPacket
{
    public int PushTier { get; set; }
    public string Collider { get; set; } = "";
    public int ColliderType { get; set; }
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public bool MainHand { get; set; }
}

public class MeleeDamageTypeJson
{
    public DamageDataJson Damage { get; set; } = new();
    public float Knockback { get; set; } = 0;
    public int DurabilityDamage { get; set; } = 1;
    public int Collider { get; set; } = 0;
    public int StaggerTimeMs { get; set; } = 0;
    public int StaggerTier { get; set; } = 1;
    public int PushTier { get; set; } = 0;

    public MeleeDamageType ToDamageType() => new(this);
}

public class MeleeDamageType
{
    public readonly float Damage;
    public readonly DamageData DamageTypeData;
    public readonly float Knockback;
    public readonly int DurabilityDamage;
    public readonly int StaggerTimeMs;
    public readonly int StaggerTier;
    public readonly int PushTier;
    public readonly int Collider;

    public const string DamageTierPlayerStatPrefix = "meleeDamageTierBonus";

    public MeleeDamageType(MeleeDamageTypeJson stats)
    {
        Damage = stats.Damage.Damage;
        DamageTypeData = new(Enum.Parse<EnumDamageType>(stats.Damage.DamageType), stats.Damage.Tier, stats.Damage.ArmorPiercingTier);
        Knockback = stats.Knockback;
        DurabilityDamage = stats.DurabilityDamage;
        StaggerTimeMs = stats.StaggerTimeMs;
        StaggerTier = stats.StaggerTier;
        PushTier = stats.PushTier;
        Collider = stats.Collider;
    }

    public bool Attack(Entity attacker, Entity target, Vector3d position, string collider, out MeleeDamagePacket packet, bool mainHand, ItemStackMeleeWeaponStats stats)
    {
        packet = new();

        if (attacker.Api is ICoreServerAPI serverApi && attacker is EntityPlayer playerAttacker)
        {
            if (target is EntityPlayer && (!serverApi.Server.Config.AllowPvP || !playerAttacker.Player.HasPrivilege("attackplayers"))) return false;
            if (target is not EntityPlayer && !playerAttacker.Player.HasPrivilege("attackcreatures")) return false;
        }

        float damage = Damage * attacker.Stats.GetBlended("meleeWeaponsDamage");
        if (target.Properties.Attributes?["isMechanical"].AsBool() == true)
        {
            damage *= attacker.Stats.GetBlended("mechanicalsDamage");
        }
        damage += stats.DamageBonus;
        damage *= stats.DamageMultiplier;

        string damageTierStat = DamageTierPlayerStatPrefix + DamageTypeData.DamageType.ToString();
        float statValue = attacker.Stats.GetBlended(damageTierStat) - 1;
        int damageTier = DamageTypeData.Tier + stats.DamageTierBonus + (int)statValue;
        damageTier = GameMath.Max(damageTier, 0);

        DamageData damageTypeData = new(DamageTypeData.DamageType, damageTier, DamageTypeData.ArmorPiercingTier);

        bool damageReceived = target.ReceiveDamage(new DirectionalTypedDamageSource()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = attacker,
            CauseEntity = attacker,
            DamageTypeData = damageTypeData,
            Position = position,
            Collider = collider,
            KnockbackStrength = Knockback * stats.KnockbackMultiplier,
            IgnoreInvFrames = true,
            Type = DamageTypeData.DamageType
        }, damage);

        bool received = damageReceived || Damage > 0;

        packet = new()
        {
            DamageType = damageTypeData.DamageType.ToString(),
            Tier = damageTypeData.Tier,
            ArmorPiercingTier = damageTypeData.ArmorPiercingTier + stats.ArmorPiercingBonus,
            Damage = damage,
            Knockback = Knockback * stats.KnockbackMultiplier,
            Position = [position.X, position.Y, position.Z],
            Collider = collider,
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = target.EntityId,
            DurabilityDamage = DurabilityDamage,
            MainHand = mainHand,
            StaggerTimeMs = StaggerTimeMs,
            StaggerTier = StaggerTier
        };

        return received;
    }
}