using CombatOverhaul.Implementations;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.DamageSystems.Detailed;

public class WeaponAttackStats
{
    public float Mass;
}

public class WeaponHitBoxDamageStats
{
    public float Sharpness;
    public float Roughness;
}

public class DamageStats
{
    public float Mass;
    public float Speed;
    public float Sharpness;
    public float Roughness;
    public float ContactArea;
}

public class StatusEffectStats
{
    public string Code;
    public string Class;
    public List<string> Tags;
    public Dictionary<string, float> Stats;

    public void Apply(float intensity, BodyPart part, DetailedDamageModel model)
    {

    }
}

public interface IDamageStats
{
    public float Speed { get; set; }

    public float Mass { get; }
    public float Sharpness { get; }
    public float Roughness { get; }
    public float ContactArea { get; }
}

public interface IStatusEffectType
{
    string Code { get; }
    IEnumerable<string> Tags { get; }
    void Apply(float intensity, BodyPart part, DetailedDamageModel model);
}

public interface IStatusEffect
{
    void OnSimulationTick(BodyPart part, DetailedDamageModel model);
}

public class StatusEffect
{
    public DetailedDamageModel Model;
    public BodyPart Part;
    public StatusEffectStats Stats;
}

public class BodyPartStats
{
    public string Code;
    
    public List<string> Colliders;
    public List<string> CollateralLink;
    public List<string> BloodLossLink;
    public List<string> ConcussionLinks;

    public List<StatusEffectStats> BloodLossEffects;
    public List<StatusEffectStats> DamageEffects;
    public List<StatusEffectStats> FractureEffects;
    public List<StatusEffectStats> CutEffects;
    public List<StatusEffectStats> TearEffects;
    public List<StatusEffectStats> ConcussionEffects;

    public uint IntegrityCapacity;
    public uint BloodCapacity;
    public uint OxygenCapacity;

    public int BloodFlow;
    public float Density;
    public float ConcussionResistance;
    public float FractureResistance;
    public float CutResistance;
    public float TearResistance;
    public float BloodLossResistance;
}

public class BodyPart
{
    public bool Damaged =>
        Integrity == Stats.IntegrityCapacity &&
        Blood == Stats.BloodCapacity &&
        Oxygen == Stats.OxygenCapacity;
    
    public uint Blood
    {
        get => _blood;
        set => _blood = Math.Clamp(value, 0, Stats.BloodCapacity);
    }
    public uint Oxygen
    {
        get => _oxygen;
        set => _oxygen = Math.Clamp(value, 0, Stats.OxygenCapacity);
    }
    public uint Integrity
    {
        get => _integrity;
        set => _integrity = Math.Clamp(value, 0, Stats.IntegrityCapacity);
    }

    public BodyPartStats Stats;
    public List<StatusEffect> StatusEffects;
    public DetailedDamageModel Model;

    private uint _blood;
    private uint _oxygen;
    private uint _integrity;

    public void OnSimulationTick()
    {
        if (!Damaged && StatusEffects.Count == 0) return;
        
        if (Blood == 0 && Stats.BloodCapacity > 0)
        {
            Oxygen--;
        }
        else
        {
            Oxygen++;
        }

        if (Oxygen == 0 && Stats.OxygenCapacity > 0)
        {
            Integrity--;
        }
    }

    public void OnReceiveDamage(DamageStats stats, float distance)
    {
        uint currentHitPoints = Integrity;
        
        ApplyDamage(stats, distance, out uint totalDamage);
        ApplyDamageEffects();
        AffectDamage(stats, distance);

        uint overDamage = Math.Clamp(totalDamage - currentHitPoints, 0, totalDamage);
        AffectBodyParts(overDamage);
    }

    public void ApplyDamage(DamageStats stats, float distance, out int totalDamage)
    {
        totalDamage = 0;

        int fractureDamage = (int)Math.Ceiling(stats.Mass * stats.Speed * stats.Speed / stats.ContactArea - Stats.FractureResistance * distance);
        if (fractureDamage > 0)
        {
            totalDamage += fractureDamage;
            Integrity = GameMath.Clamp(Integrity - fractureDamage, 0, Stats.IntegrityCapacity);
            foreach (StatusEffectStats effect in Stats.FractureEffects)
            {
                effect.Apply((float)fractureDamage / Stats.IntegrityCapacity, this, Model);
            }
        }

        int cutDamage = (int)Math.Ceiling(stats.Sharpness * stats.ContactArea * distance * distance - Stats.CutResistance * distance);
        if (fractureDamage > 0)
        {
            totalDamage += cutDamage;
            Integrity = GameMath.Clamp(Integrity - cutDamage, 0, Stats.IntegrityCapacity);
            foreach (StatusEffectStats effect in Stats.CutEffects)
            {
                effect.Apply((float)cutDamage / Stats.IntegrityCapacity, this, Model);
            }
        }

        int tearDamage = (int)Math.Ceiling(stats.Rougness * stats.Mass * stats.Speed * distance - Stats.TearResistance * distance);
        if (fractureDamage > 0)
        {
            totalDamage += tearDamage;
            Integrity = GameMath.Clamp(Integrity - tearDamage, 0, Stats.IntegrityCapacity);
            foreach (StatusEffectStats effect in Stats.TearEffects)
            {
                effect.Apply((float)tearDamage / Stats.IntegrityCapacity, this, Model);
            }
        }
    }

    public void AffectDamage(DamageStats stats, float distance)
    {
        stats.Speed *= GameMath.Clamp(distance * Stats.Density / (stats.Mass + distance * Stats.Density), 0, 1);
    }

    public void ApplyDamageEffects()
    {
        if (Integrity < Stats.IntegrityCapacity)
        {
            foreach (StatusEffectStats effect in Stats.DamageEffects)
            {
                effect.Apply((float)Integrity / Stats.IntegrityCapacity, this, Model);
            }
        }

        if (Blood < Stats.BloodCapacity)
        {
            foreach (StatusEffectStats effect in Stats.BloodLossEffects)
            {
                effect.Apply((float)Blood / Stats.BloodCapacity, this, Model);
            }
        }
    }

    public void AffectBodyParts(int overDamage)
    {
        int collateralDamage = (int)Math.Ceiling((float)overDamage / Stats.CollateralLink.Select(partCode => Model.Parts[partCode]).Count());
        foreach (BodyPart part in Stats.CollateralLink.Select(partCode => Model.Parts[partCode]))
        {
            part.Integrity = GameMath.Clamp(part.Integrity - collateralDamage, 0, part.Integrity);
            part.ApplyDamageEffects();
        }

        int bloodDraw = (int)Math.Ceiling((float)overDamage / Stats.BloodLossLink.Select(partCode => Model.Parts[partCode]).Count());
        foreach (BodyPart part in Stats.BloodLossLink.Select(partCode => Model.Parts[partCode]))
        {
            part.Blood = GameMath.Clamp(part.Blood - bloodDraw, 0, part.Blood);
            part.ApplyDamageEffects();
        }
    }
}

public class DetailedDamageModel
{
    public Dictionary<string, BodyPart> Parts;
    public Dictionary<string, BodyPart> PartsByColliders;



}

public class DetailedDamageModelBehavior : EntityBehavior, IEntityDamageModel
{
    public DetailedDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public event OnEntityReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => throw new NotImplementedException();
}
