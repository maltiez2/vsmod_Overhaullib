using CollidersLib;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

public sealed class EntityDamageModelJson
{
    public float? TorsoDamageMultiplier { get; set; } = null;
    public float? LimbsDamageMultiplier { get; set; } = null;
    public float? HeadDamageMultiplier { get; set; } = null;
    public float? CriticalDamageMultiplier { get; set; } = null;
    public float? ResistantDamageMultiplier { get; set; } = null;


    public Dictionary<string, float> Multipliers { get; set; } = [];
    public Dictionary<string, float> DefaultResists { get; set; } = [];
    public Dictionary<string, Dictionary<string, float>> ResistsForColliders { get; set; } = [];
    public Dictionary<string, SoundEffectData> HitSounds { get; set; } = [];
    public Dictionary<string, string> HitParticles { get; set; } = [];
    public List<string> ResistantColliders { get; set; } = [];
    public bool ScaleParticlesCountWithDamage { get; set; } = true;
}

public sealed class SoundEffectData
{
    public string Code { get; set; } = "";
    public bool RandomizePitch { get; set; } = false;
    public float Range { get; set; } = 32;
    public float Volume { get; set; } = 1;
}

public interface IEntityDamageModel
{
    event OnEntityReceiveDamageDelegate? OnReceiveDamage;
}

public delegate void OnEntityReceiveDamageDelegate(ref float damage, DamageSource damageSource, int colliderType, string colliderTypeName, string? collider);

public sealed class EntityDamageModelBehavior : EntityBehavior, IEntityDamageModel
{
    public EntityDamageModelBehavior(Entity entity) : base(entity)
    {
        _animationsSystem = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>();
        _system = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>();
    }

    public event OnEntityReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "EntityDamageModel";
    public Dictionary<int, float> DamageMultipliers { get; private set; } = [];
    public DamageResistData Resists { get; set; } = new();
    public Dictionary<int, DamageResistData> ResistsForColliders { get; private set; } = [];
    public Dictionary<int, SoundEffectData> HitSounds { get; private set; } = [];
    public Dictionary<int, string> HitParticles { get; private set; } = [];
    public HashSet<int> ResistantColliders { get; private set; } = [];
    public List<string> ColliderTypeNames { get; private set; } = [];
    public bool ScaleParticlesCountWithDamage { get; private set; } = true;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        _stats = attributes["damageModel"]?.AsObject<EntityDamageModelJson>() ?? attributes.AsObject<EntityDamageModelJson>() ?? new();
    }
    public override void GetInfoText(StringBuilder infotext)
    {
        if (!Resists.Resists.Values.Any(x => x > 0)) return;

        if (_system.Settings.ShortEntityInfo)
        {
            int piercing = 0;
            int slashing = 0;
            int blunt = 0;
            if (Resists.Resists.TryGetValue(EnumDamageType.PiercingAttack, out float piercingValue))
            {
                piercing = (int)piercingValue;
            }
            if (Resists.Resists.TryGetValue(EnumDamageType.SlashingAttack, out float slashingValue))
            {
                slashing = (int)slashingValue;
            }
            if (Resists.Resists.TryGetValue(EnumDamageType.BluntAttack, out float bluntValue))
            {
                blunt = (int)bluntValue;
            }
            infotext.AppendLine(Lang.Get($"combatoverhaul:damage-short-protection-info", piercing, slashing, blunt));
        }
        else
        {
            infotext.AppendLine(Lang.Get($"combatoverhaul:damage-protection-info"));
            foreach ((EnumDamageType type, float value) in Resists.Resists)
            {
                if (value <= 0) continue;

                string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
                infotext.AppendLine($"  {damageType}: {value}");
            }
        }
    }
    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        EntityBehaviorHealth? healthBehavior = entity.GetBehavior<EntityBehaviorHealth>();
        if (healthBehavior != null) healthBehavior.onDamaged += OnReceiveDamageHandler;

        if (_colliders == null)
        {
            LoggerUtil.Warn(entity.Api, this, $"Entity '{entity.Code}' does not have colliders behavior");
            return;
        }

        Dictionary<string, int> colliderTypeNamesToIndex = _colliders.ColliderTypeNames.ToDictionary(name => name, name => _colliders.ColliderTypeNames.IndexOf(name));

        Resists = new(_stats.DefaultResists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));

        ResistsForColliders = _stats.ResistsForColliders
            .ToDictionary(entry => colliderTypeNamesToIndex[entry.Key], entry => new DamageResistData(entry.Value.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value)));

        DamageMultipliers = _stats.Multipliers.ToDictionary(entry => colliderTypeNamesToIndex[entry.Key], entry => entry.Value);

        if (_stats.TorsoDamageMultiplier.HasValue)
        {
            DamageMultipliers[colliderTypeNamesToIndex["Torso"]] = _stats.TorsoDamageMultiplier.Value;
        }
        if (_stats.LimbsDamageMultiplier.HasValue)
        {
            DamageMultipliers[colliderTypeNamesToIndex["Limbs"]] = _stats.LimbsDamageMultiplier.Value;
        }
        if (_stats.HeadDamageMultiplier.HasValue)
        {
            DamageMultipliers[colliderTypeNamesToIndex["Head"]] = _stats.HeadDamageMultiplier.Value;
        }
        if (_stats.CriticalDamageMultiplier.HasValue)
        {
            DamageMultipliers[colliderTypeNamesToIndex["Critical"]] = _stats.CriticalDamageMultiplier.Value;
        }
        if (_stats.ResistantDamageMultiplier.HasValue)
        {
            DamageMultipliers[colliderTypeNamesToIndex["Resistant"]] = _stats.ResistantDamageMultiplier.Value;
            _stats.ResistantColliders.Add("Resistant");
        }

        HitSounds = _stats.HitSounds.ToDictionary(entry => colliderTypeNamesToIndex[entry.Key], entry => entry.Value);
        HitParticles = _stats.HitParticles.ToDictionary(entry => colliderTypeNamesToIndex[entry.Key], entry => entry.Value);
        ScaleParticlesCountWithDamage = _stats.ScaleParticlesCountWithDamage;
        ResistantColliders = _stats.ResistantColliders.Select(name => colliderTypeNamesToIndex[name]).ToHashSet();
    }

    private CollidersEntityBehavior? _colliders;
    private readonly CombatOverhaulAnimationsSystem _animationsSystem;
    private readonly CombatOverhaulSystem _system;
    private EntityDamageModelJson _stats = new();

    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        int colliderType = 0;
        string? collider = null;
        Vector3d position = new();

        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            ShapeElementCollider? colliderElement = _colliders.Colliders.Find(collider => collider.ShapeElementName == locationalDamageSource.Collider);
            if (colliderElement != null)
            {
                colliderType = colliderElement.ColliderType;
            }
            collider = locationalDamageSource.Collider;
            float multiplier = DamageMultipliers[colliderType];
            damage *= multiplier;
            position = locationalDamageSource.Position;
        }

        if (damageSource is ITypedDamage typedDamage)
        {
            if (ResistsForColliders.ContainsKey(colliderType))
            {
                typedDamage.DamageTypeData = ResistsForColliders[colliderType].ApplyNonPlayerResist(typedDamage.DamageTypeData, ref damage);
            }
            else
            {
                typedDamage.DamageTypeData = Resists.ApplyNonPlayerResist(typedDamage.DamageTypeData, ref damage);
            }
        }
        else
        {
            DamageData damageData = new(damageSource.Type, damageSource.DamageTier, 0);
            Resists.ApplyNonPlayerResist(damageData, ref damage);
        }

        if (HitSounds.TryGetValue(colliderType, out SoundEffectData? value))
        {
            entity.Api.World.PlaySoundAt(new AssetLocation(value.Code), entity, randomizePitch: value.RandomizePitch, range: value.Range, volume: value.Volume);
        }

        if (HitParticles.TryGetValue(colliderType, out string? particlesEffect))
        {
            float intensity = ScaleParticlesCountWithDamage ? MathF.Sqrt(damage) : 1;
            if (damage <= 0)
            {
                intensity = 1;
            }
            _animationsSystem.ParticleEffectsManager?.Spawn(particlesEffect, position, Vector3.Zero, intensity);
        }

        OnReceiveDamage?.Invoke(ref damage, damageSource, colliderType, ColliderTypeNames[colliderType], collider);

        return damage;
    }
}
