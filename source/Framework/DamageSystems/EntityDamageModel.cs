using CombatOverhaul.Colliders;
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
    public float TorsoDamageMultiplier { get; set; } = 1.0f;
    public float LimbsDamageMultiplier { get; set; } = 0.5f;
    public float HeadDamageMultiplier { get; set; } = 1.5f;
    public float CriticalDamageMultiplier { get; set; } = 2.0f;
    public float ResistantDamageMultiplier { get; set; } = 0.0f;
    public Dictionary<string, float> DefaultResists { get; set; } = [];
    public Dictionary<string, Dictionary<string, float>> ResistsForColliders { get; set; } = [];
    public Dictionary<string, SoundEffectData> HitSounds { get; set; } = [];
    public Dictionary<string, string> HitParticles { get; set; } = [];
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

public delegate void OnEntityReceiveDamageDelegate(ref float damage, DamageSource damageSource, ColliderTypes damageZone, string? collider);

public sealed class EntityDamageModelBehavior : EntityBehavior, IEntityDamageModel
{
    public EntityDamageModelBehavior(Entity entity) : base(entity)
    {
        _animationsSystem = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>();
        _system = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>();
    }

    public event OnEntityReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "EntityDamageModel";
    public Dictionary<ColliderTypes, float> DamageMultipliers { get; private set; } = new Dictionary<ColliderTypes, float>()
    {
        { ColliderTypes.Torso, 1.0f },
        { ColliderTypes.Arm, 1.0f },
        { ColliderTypes.Leg, 1.0f },
        { ColliderTypes.Head, 1.0f },
        { ColliderTypes.Critical, 1.0f },
        { ColliderTypes.Resistant, 0.0f }
    };
    public DamageResistData Resists { get; set; } = new();
    public Dictionary<ColliderTypes, DamageResistData> ResistsForColliders { get; private set; } = [];
    public Dictionary<ColliderTypes, SoundEffectData> HitSounds { get; private set; } = [];
    public Dictionary<ColliderTypes, string> HitParticles { get; private set; } = [];
    public bool ScaleParticlesCountWithDamage { get; private set; } = true;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("damageModel"))
        {
            EntityDamageModelJson stats = attributes["damageModel"].AsObject<EntityDamageModelJson>();

            Resists = new(stats.DefaultResists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));

            ResistsForColliders = stats.ResistsForColliders
                .ToDictionary(entry => Enum.Parse<ColliderTypes>(entry.Key), entry => new DamageResistData(entry.Value.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value)));

            DamageMultipliers = new Dictionary<ColliderTypes, float>()
            {
                { ColliderTypes.Torso, stats.TorsoDamageMultiplier },
                { ColliderTypes.Arm, stats.LimbsDamageMultiplier },
                { ColliderTypes.Leg, stats.LimbsDamageMultiplier },
                { ColliderTypes.Head, stats.HeadDamageMultiplier },
                { ColliderTypes.Critical, stats.CriticalDamageMultiplier },
                { ColliderTypes.Resistant, stats.ResistantDamageMultiplier }
            };

            HitSounds = stats.HitSounds.ToDictionary(entry => Enum.Parse<ColliderTypes>(entry.Key), entry => entry.Value);
            HitParticles = stats.HitParticles.ToDictionary(entry => Enum.Parse<ColliderTypes>(entry.Key), entry => entry.Value);
            ScaleParticlesCountWithDamage = stats.ScaleParticlesCountWithDamage;
        }
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
        }
    }

    private CollidersEntityBehavior? _colliders;
    private readonly CombatOverhaulAnimationsSystem _animationsSystem;
    private readonly CombatOverhaulSystem _system;

    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        ColliderTypes colliderType = ColliderTypes.Torso;
        string? collider = null;
        Vector3d position = new();

        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            if (_colliders.CollidersTypes.ContainsKey(locationalDamageSource.Collider))
            {
                colliderType = _colliders.CollidersTypes[locationalDamageSource.Collider];
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

        OnReceiveDamage?.Invoke(ref damage, damageSource, colliderType, collider);

        return damage;
    }

    /*private void SpawnSecondChanceParticles()
    {
        ParticleEffectsManager? effectsManager = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>()?.ParticleEffectsManager;
        Vintagestory.API.MathTools.Vec3f position = (entity.Pos.XYZ + entity.LocalEyePos * 0.5).ToVec3f();
        effectsManager?.Spawn("combatoverhaul:second-chance", new(position.X, position.Y, position.Z), new(), 1);
    }
    private void SpawnGracePeriodPArticles()
    {
        ParticleEffectsManager? effectsManager = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>()?.ParticleEffectsManager;
        Vintagestory.API.MathTools.Vec3f position = (entity.Pos.XYZ + entity.LocalEyePos * 0.5).ToVec3f();
        effectsManager?.Spawn("combatoverhaul:grace-period", new(position.X, position.Y, position.Z), new(), 1);
    }*/
}
