using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.Compatibility;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using PlayerModelLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

[Flags]
public enum DamageZone
{
    None = 0,
    Head = 1,
    Face = 2,
    Neck = 4,
    Torso = 8,
    Arms = 16,
    Hands = 32,
    Legs = 64,
    Feet = 128
}

[Flags]
public enum PlayerBodyPart
{
    None = 0,
    Head = 1,
    Face = 2,
    Neck = 4,
    Torso = 8,
    LeftArm = 16,
    RightArm = 32,
    LeftHand = 64,
    RightHand = 128,
    LeftLeg = 256,
    RightLeg = 512,
    LeftFoot = 1024,
    RightFoot = 2048
}

public interface IArmorPiercing
{
    int ArmorPiercingTier { get; }
}

public delegate void OnPlayerReceiveDamageDelegate(ref float damage, DamageSource damageSource, PlayerBodyPart bodyPart);

public class PlayerDamageModelConfig
{
    public PlayerDamageModelJson DamageModel { get; set; } = new();
    public Dictionary<string, PlayerBodyPart> BodyParts { get; set; } = [];
    public float SecondChanceCooldownSec { get; set; } = 300;
    public bool SecondChanceAvailable { get; set; } = true;
    public float SecondChanceGracePeriodSec { get; set; } = 8;
}

public sealed class PlayerDamageModelBehavior : EntityBehavior
{
    public PlayerDamageModelBehavior(Entity entity) : base(entity)
    {
        _settings = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;
        _player = entity as EntityPlayer ?? throw new ArgumentNullException(nameof(entity), "'PlayerDamageModelBehavior' should be attached to player");
    }

    public event OnPlayerReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "PlayerDamageModel";

    public PlayerDamageModel DamageModel { get; private set; } = new([]);
    public Dictionary<string, PlayerBodyPart> CollidersToBodyParts { get; private set; } = new()
    {
        { "LowerTorso", PlayerBodyPart.Torso },
        { "UpperTorso", PlayerBodyPart.Torso },
        { "Head", PlayerBodyPart.Head },
        { "Neck", PlayerBodyPart.Neck },
        { "UpperArmR", PlayerBodyPart.RightArm },
        { "UpperArmL", PlayerBodyPart.LeftArm },
        { "LowerArmR", PlayerBodyPart.RightHand },
        { "LowerArmL", PlayerBodyPart.LeftHand },
        { "UpperFootL", PlayerBodyPart.LeftLeg },
        { "UpperFootR", PlayerBodyPart.RightLeg },
        { "LowerFootL", PlayerBodyPart.LeftFoot },
        { "LowerFootR", PlayerBodyPart.RightFoot }
    };
    public Dictionary<PlayerBodyPart, DamageZone> BodyPartsToZones { get; private set; } = new()
    {
        { PlayerBodyPart.None, DamageZone.None },
        { PlayerBodyPart.Head, DamageZone.Head },
        { PlayerBodyPart.Face, DamageZone.Face },
        { PlayerBodyPart.Neck, DamageZone.Neck },
        { PlayerBodyPart.Torso, DamageZone.Torso },
        { PlayerBodyPart.LeftArm, DamageZone.Arms },
        { PlayerBodyPart.RightArm, DamageZone.Arms },
        { PlayerBodyPart.LeftHand, DamageZone.Hands },
        { PlayerBodyPart.RightHand, DamageZone.Hands },
        { PlayerBodyPart.LeftLeg, DamageZone.Legs },
        { PlayerBodyPart.RightLeg, DamageZone.Legs },
        { PlayerBodyPart.LeftFoot, DamageZone.Feet },
        { PlayerBodyPart.RightFoot, DamageZone.Feet }
    };
    public Dictionary<DamageZone, string> MultiplierStats { get; private set; } = new()
    {
        {DamageZone.Head, "playerHeadDamageFactor"},
        {DamageZone.Face, "playerFaceDamageFactor"},
        {DamageZone.Neck, "playerNeckDamageFactor"},
        {DamageZone.Torso, "playerTorsoDamageFactor"},
        {DamageZone.Arms, "playerArmsDamageFactor"},
        {DamageZone.Hands, "playerHandsDamageFactor"},
        {DamageZone.Legs, "playerLegsDamageFactor"},
        {DamageZone.Feet, "playerFeetDamageFactor"}
    };
    public List<EnumDamageType> DamageTypesToProcess { get; private set; } =
    [
        EnumDamageType.PiercingAttack,
        EnumDamageType.SlashingAttack,
        EnumDamageType.BluntAttack
    ];
    public TimeSpan SecondChanceDefaultCooldown { get; set; }
    public TimeSpan SecondChanceCooldown => SecondChanceDefaultCooldown * entity.Stats.GetBlended("secondChanceCooldown");
    public TimeSpan SecondChanceDefaultGracePeriod { get; set; }
    public TimeSpan SecondChanceGracePeriod => SecondChanceDefaultGracePeriod * entity.Stats.GetBlended("secondChanceGracePeriod");
    public bool SecondChanceAvailable { get; set; }

    public DamageBlockStats? CurrentDamageBlock { get; set; } = null;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        _serverSide = entity.Api.Side == EnumAppSide.Server;
        _defaultConfig = attributes.AsObject<PlayerDamageModelConfig>();

        ApplyConfig(_defaultConfig);
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        if (_serverSide)
        {
            entity.GetBehavior<EntityBehaviorHealth>().onDamaged += OnReceiveDamageHandler;
        }

        if (entity.Api.ModLoader.IsModEnabled(CollidersEntityBehavior.PlayerModelLibId))
        {
            SubscribeOnModelChange();
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        if (!_serverSide) return;

        long currentTime = entity.World.ElapsedMilliseconds;
        if (_nextTemperatureDamageCheck < currentTime)
        {
            ApplyDamageFromHotClothes();
            _nextTemperatureDamageCheck = currentTime + _temperatureDamageCheckCooldownMs;
        }

        float secondChanceCooldown = entity.WatchedAttributes.GetFloat("secondChanceCooldown", 0);
        secondChanceCooldown = Math.Clamp(secondChanceCooldown - deltaTime, 0, secondChanceCooldown);
        entity.WatchedAttributes.SetFloat("secondChanceCooldown", secondChanceCooldown);

        float secondChanceGracePeriod = entity.WatchedAttributes.GetFloat("secondChanceGracePeriod", 0);
        secondChanceGracePeriod = Math.Clamp(secondChanceGracePeriod - deltaTime, 0, secondChanceGracePeriod);
        entity.WatchedAttributes.SetFloat("secondChanceGracePeriod", secondChanceGracePeriod);
    }

    private readonly EntityPlayer _player;
    private readonly Settings _settings;
    private CollidersEntityBehavior? _colliders;
    private readonly float _healthAfterSecondChance = 1;
    private PlayerDamageModelConfig _defaultConfig = new();
    private bool _serverSide = false;
    private const float _minTemperatureToDamage = 100;
    private const float _minTemperatureDamage = 0.1f;
    private const float _maxTemperatureToDamage = 1000;
    private const float _maxTemperatureDamage = 16f;
    private long _nextTemperatureDamageCheck = 0;
    private const long _temperatureDamageCheckCooldownMs = 2000;


    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        if (!DamageTypesToProcess.Contains(damageSource.Type)) return damage;

        (PlayerBodyPart detailedDamageZone, float multiplier) = DetermineHitZone(damageSource);

        DamageZone damageZone = BodyPartsToZones[detailedDamageZone];

        multiplier *= GetStatsMultiplier(damageZone);

        ApplyBlock(damageSource, detailedDamageZone, ref damage, out string blockDamageLogMessage);
        PrintToDamageLog(blockDamageLogMessage);

        ApplyArmorResists(damageSource, damageZone, ref damage, out string armorDamageLogMessage, out EnumDamageType damageType);
        PrintToDamageLog(armorDamageLogMessage);

        damage *= multiplier;

        if (SecondChanceAvailable) ApplySecondChance(ref damage);

        OnReceiveDamage?.Invoke(ref damage, damageSource, detailedDamageZone);

        if (damage != 0)
        {
            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-received-damage", $"{damage:F1}", Lang.Get($"combatoverhaul:detailed-damage-zone-{detailedDamageZone}"), Lang.Get($"combatoverhaul:damage-type-{damageType}"));
            PrintToDamageLog(damageLogMessage);
        }

        return damage;
    }
    private void PrintToDamageLog(string message)
    {
        if (_settings.PrintPlayerHits && message != "") ((entity as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, message, EnumChatType.Notification);
    }

    private float GetStatsMultiplier(DamageZone part)
    {
        if (MultiplierStats.TryGetValue(part, out string? stat))
        {
            return _player.Stats.GetBlended(stat);
        }

        return 1;
    }
    private (PlayerBodyPart zone, float multiplier) DetermineHitZone(DamageSource damageSource)
    {
        PlayerBodyPart damageZone;
        float multiplier;
        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource && locationalDamageSource.Collider != "")
        {
            damageZone = CollidersToBodyParts[locationalDamageSource.Collider];
            multiplier = DamageModel.GetMultiplier(damageZone);
        }
        else if (damageSource is IDirectionalDamage directionalDamage)
        {
            (damageZone, multiplier) = DamageModel.GetZone(directionalDamage.Direction, directionalDamage.Target, directionalDamage.WeightMultiplier);
        }
        else if (damageSource.SourceEntity != null && damageSource.SourceEntity.EntityId != entity.EntityId)
        {
            DirectionOffset direction = DirectionOffset.GetDirection(entity, damageSource.SourceEntity);

            (damageZone, multiplier) = DamageModel.GetZone(direction);
        }
        else
        {
            (damageZone, multiplier) = DamageModel.GetZone();
        }

        return (damageZone, multiplier);
    }
    private void ApplyBlock(DamageSource damageSource, PlayerBodyPart zone, ref float damage, out string damageLogMessage)
    {
        damageLogMessage = "";

        if (CurrentDamageBlock == null) return;

        if (!CurrentDamageBlock.CanBlockProjectiles && IsCausedByProjectile(damageSource))
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-projectile");
            return;
        }

        if ((zone & CurrentDamageBlock.ZoneType) == 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-zone", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"));
            return;
        }

        if (damageSource is IDirectionalDamage directionalDamage)
        {
            if (!CurrentDamageBlock.Directions.Check(directionalDamage.Direction))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-direction", directionalDamage.Direction);
                return;
            }
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset offset = DirectionOffset.GetDirectionWithRespectToCamera(entity, damageSource.SourceEntity);

            if (!CurrentDamageBlock.Directions.Check(offset))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-direction", offset);
                return;
            }
        }

        float damageTier = damageSource.DamageTier;
        float initialDamage = damage;
        EnumDamageType damageType = damageSource.Type;

        if (CurrentDamageBlock.BlockTier != null)
        {
            if (!CurrentDamageBlock.BlockTier.ContainsKey(damageType))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-damageType", Lang.Get($"damage-type-{damageType}"));
                return;
            }

            float blockTier = CurrentDamageBlock.BlockTier[damageType];
            if (blockTier < damageTier)
            {
                ApplyBlockResists(blockTier, damageTier, ref damage);
                damageSource.DamageTier = (int)(damageTier - blockTier);
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-partial-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"), $"{initialDamage - damage:F1}");
            }
            else
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-success-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"), $"{damage:F1}");
                damage = 0;
            }
        }
        else
        {
            damage = 0;
        }

        if (CurrentDamageBlock.StaggerTime > TimeSpan.Zero)
        {
            damageSource.SourceEntity?.GetBehavior<StaggerBehavior>()?.TriggerStagger(CurrentDamageBlock.StaggerTime, CurrentDamageBlock.StaggerTier);
        }

        CurrentDamageBlock.Callback.Invoke(initialDamage - damage);

        if (CurrentDamageBlock.Sound != null) entity.Api.World.PlaySoundAt(new(CurrentDamageBlock.Sound), entity);
    }
    private void ApplyArmorResists(DamageSource damageSource, DamageZone zone, ref float damage, out string damageLogMessage, out EnumDamageType damageType)
    {
        damageLogMessage = "";
        damageType = damageSource.Type;

        if ((entity as EntityPlayer)?.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) is not ArmorInventory inventory) return;

        if (zone == DamageZone.None) return;

        IEnumerable<ArmorSlot> slots = inventory.GetNotEmptyZoneSlots(zone);

        if (!slots.Any()) return;

        DamageResistData resists = DamageResistData.Combine(slots
            .Where(slot => slot?.Itemstack?.Item != null)
            .Where(slot => slot?.Itemstack?.Item.GetRemainingDurability(slot.Itemstack) > 0 || slot?.Itemstack?.Item.GetMaxDurability(slot.Itemstack) == 0)
            .Select(slot => slot.Resists));

        float previousDamage = damage;
        int durabilityDamage = 0;
        int apTier = 0;
        if (damageSource is IArmorPiercing apSource)
        {
            apTier = apSource.ArmorPiercingTier;
        }

        _ = resists.ApplyPlayerResist(new(damageSource.Type, damageSource.DamageTier, apTier), ref damage, out durabilityDamage);

        durabilityDamage = GameMath.Clamp(durabilityDamage, 1, durabilityDamage);

        DamageArmor(slots, damageType, durabilityDamage, out int totalDurabilityDamage);

        if (previousDamage - damage > 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-armor-damage-negation", $"{previousDamage - damage:F1}", Lang.Get($"combatoverhaul:damage-zone-{zone}"), totalDurabilityDamage, Lang.Get($"combatoverhaul:damage-type-{damageType}"), damageSource.DamageTier);
        }
    }
    private void ApplyBlockResists(float blockTier, float damageTier, ref float damage)
    {
        damage *= 1 - MathF.Exp((blockTier - damageTier) / 2f);
    }
    private void ApplySecondChance(ref float damage)
    {
        float currentHealth = entity.GetBehavior<EntityBehaviorHealth>()?.Health ?? 0;

        if (currentHealth > damage) return;

        float secondChanceCooldown = entity.WatchedAttributes.GetFloat("secondChanceCooldown", 0);
        float secondChanceGracePeriod = entity.WatchedAttributes.GetFloat("secondChanceGracePeriod", 0);
        if (secondChanceGracePeriod > 0)
        {
            damage *= 0;
            PrintToDamageLog(Lang.Get("combatoverhaul:damagelog-second-chance-grace-period", (int)secondChanceGracePeriod));
            SpawnGracePeriodPArticles();
            return;
        }
        if (secondChanceCooldown > 0)
        {
            PrintToDamageLog(Lang.Get("combatoverhaul:damagelog-second-chance-cooldown", (int)secondChanceCooldown));
            return;
        }

        entity.WatchedAttributes.SetFloat("secondChanceCooldown", (float)SecondChanceCooldown.TotalSeconds);
        entity.WatchedAttributes.SetFloat("secondChanceGracePeriod", (float)SecondChanceGracePeriod.TotalSeconds);
        damage = currentHealth - _healthAfterSecondChance;

        SpawnSecondChanceParticles();

        PrintToDamageLog(Lang.Get("combatoverhaul:damagelog-second-chance"));
    }
    private void SpawnSecondChanceParticles()
    {
        if (!_settings.SecondChanceParticles) return;
        ParticleEffectsManager? effectsManager = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>()?.ParticleEffectsManager;
        Vec3f position = (entity.Pos.XYZ + entity.LocalEyePos * 0.5).ToVec3f();
        effectsManager?.Spawn("combatoverhaul:second-chance", new(position.X, position.Y, position.Z), new(), 1);
    }
    private void SpawnGracePeriodPArticles()
    {
        if (!_settings.SecondChanceParticles) return;
        ParticleEffectsManager? effectsManager = entity.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>()?.ParticleEffectsManager;
        Vec3f position = (entity.Pos.XYZ + entity.LocalEyePos * 0.5).ToVec3f();
        effectsManager?.Spawn("combatoverhaul:grace-period", new(position.X, position.Y, position.Z), new(), 1);
    }
    private bool IsCausedByProjectile(DamageSource damageSource)
    {
        Entity? sourceEntity = damageSource.SourceEntity;
        Entity? causeEntity = damageSource.CauseEntity;

        return sourceEntity != null && causeEntity != null && sourceEntity != causeEntity;
    }
    private void ApplyConfig(PlayerDamageModelConfig config)
    {
        DamageModel = new(config.DamageModel.Zones);
        CollidersToBodyParts = config.BodyParts;
        SecondChanceDefaultCooldown = TimeSpan.FromSeconds(config.SecondChanceCooldownSec);
        SecondChanceAvailable = config.SecondChanceAvailable;
        SecondChanceDefaultGracePeriod = TimeSpan.FromSeconds(config.SecondChanceGracePeriodSec);
    }
    private void SubscribeOnModelChange()
    {
        PlayerSkinBehavior? skinBehavior = entity.GetBehavior<PlayerSkinBehavior>();

        if (skinBehavior != null)
        {
            skinBehavior.OnModelChanged += ReloadConfigForCustomModel;
        }
    }
    private void ReloadConfigForCustomModel(string modelCode)
    {
        PlayerModelLibCompatibilitySystem? system = entity.Api?.ModLoader.GetModSystem<PlayerModelLibCompatibilitySystem>();

        if (system == null) return;

        if (!system.CustomModelConfigs.TryGetValue(modelCode, out PlayerModelConfig? customModelConfig) || customModelConfig.DamageModel == null)
        {
            ApplyConfig(_defaultConfig);
            return;
        }

        DamageModel = new(customModelConfig.DamageModel.DamageModel.Zones);
        CollidersToBodyParts = customModelConfig.DamageModel.BodyParts;
        SecondChanceDefaultCooldown = TimeSpan.FromSeconds(customModelConfig.DamageModel.SecondChanceCooldownSec);
        SecondChanceAvailable = customModelConfig.DamageModel.SecondChanceAvailable;
        SecondChanceDefaultGracePeriod = TimeSpan.FromSeconds(customModelConfig.DamageModel.SecondChanceGracePeriodSec);
    }
    private void DamageArmor(IEnumerable<ArmorSlot> slots, EnumDamageType damageType, int durabilityDamage, out int totalDurabilityDamage)
    {
        float totalProtection = slots.Select(slot => slot.Resists.Resists[damageType]).Sum();

        if (totalProtection <= float.Epsilon * 2)
        {
            durabilityDamage /= slots.Count();
            durabilityDamage = Math.Max(durabilityDamage, 1);
            foreach (ArmorSlot slot in slots)
            {
                if (slot.Itemstack.Item.GetMaxDurability(slot.Itemstack) <= 0) continue;
                slot.Itemstack.Item.DamageItem(entity.Api.World, entity, slot, durabilityDamage);
                slot.MarkDirty();
            }
            totalDurabilityDamage = durabilityDamage * slots.Count();
            return;
        }

        totalDurabilityDamage = 0;
        foreach (ArmorSlot slot in slots)
        {
            if (slot.Itemstack.Item.GetMaxDurability(slot.Itemstack) <= 0) continue;

            float protection = slot.Resists.Resists[damageType];
            int durabilityDamagePerSlot = (int)Math.Ceiling(durabilityDamage * protection / totalProtection);
            totalDurabilityDamage += durabilityDamagePerSlot;

            slot.Itemstack.Item.DamageItem(entity.Api.World, entity, slot, durabilityDamagePerSlot);
            slot.MarkDirty();
        }
    }
    private void ApplyDamageFromHotClothes()
    {
        if ((entity as EntityPlayer)?.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) is not ArmorInventory inventory) return;

        float damage = 0;

        foreach (ItemSlot slot in inventory.Where(slot => slot.Itemstack?.Item != null))
        {
            float temperature = slot.Itemstack.Item.GetTemperature(entity.Api.World, slot.Itemstack);

            if (temperature > _minTemperatureToDamage)
            {
                float temperatureFactor = (temperature - _minTemperatureToDamage) / (_maxTemperatureToDamage - _minTemperatureToDamage);
                float newDamage = _minTemperatureDamage + (_maxTemperatureDamage - _minTemperatureDamage) * temperatureFactor;
                if (newDamage > damage)
                {
                    damage = newDamage;
                }

                if (newDamage > 6)
                {
                    entity.Api.World.SpawnItemEntity(slot.TakeOutWhole(), entity.Pos.AsBlockPos);
                }
            }
        }

        if (damage > 0)
        {
            DamageSource heatDamageSource = new()
            {
                CauseEntity = entity,
                SourceEntity = entity,
                IgnoreInvFrames = true,
                DamageTier = 0,
                Type = EnumDamageType.Heat,
                KnockbackStrength = 0,
                Source = EnumDamageSource.Internal
            };

            entity.ReceiveDamage(heatDamageSource, damage);
        }
    }
}
public sealed class PlayerDamageModel
{
    public readonly DamageZoneStats[] DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != PlayerBodyPart.None).ToArray();
        _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);

        _weights = new();
        foreach (PlayerBodyPart zone in Enum.GetValues<PlayerBodyPart>())
        {
            _weights[zone] = 0;
        }
    }

    public (PlayerBodyPart zone, float damageMultiplier) GetZone(DirectionOffset? direction = null, PlayerBodyPart target = PlayerBodyPart.None, float multiplier = 1f)
    {
        IEnumerable<DamageZoneStats> zones = direction == null ? DamageZones : DamageZones.Where(zone => zone.Directions.Check(direction.Value));

        foreach ((PlayerBodyPart zone, _) in _weights)
        {
            _weights[zone] = 0;
        }

        float sum = 0;
        foreach (DamageZoneStats zone in zones)
        {
            float zoneMultiplier = (target | zone.ZoneType) != 0 ? multiplier : 1;
            sum += zone.Coverage * zoneMultiplier;
            _weights[zone.ZoneType] += zone.Coverage * zoneMultiplier;
        }

        foreach ((PlayerBodyPart zone, _) in _weights)
        {
            _weights[zone] /= sum;
        }

        float randomValue = _random.nextFloat();

        sum = 0;
        foreach ((PlayerBodyPart zone, float weight) in _weights)
        {
            sum += weight;
            if (sum >= randomValue)
            {
                return (zone, zones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average());
            }
        }

        return (PlayerBodyPart.None, 1.0f);
    }

    public float GetMultiplier(PlayerBodyPart zone)
    {
        return DamageZones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average();
    }

    private readonly NatFloat _random;
    private readonly Dictionary<PlayerBodyPart, float> _weights;
}

public sealed class PlayerDamageModelJson
{
    public DamageZoneStatsJson[] Zones { get; set; } = Array.Empty<DamageZoneStatsJson>();
}

public interface IDirectionalDamage
{
    DirectionOffset Direction { get; }
    PlayerBodyPart Target { get; }
    float WeightMultiplier { get; }
}

public sealed class DamageZoneStatsJson
{
    public string Zone { get; set; } = "None";
    public float Coverage { get; set; } = 0;
    public float Top { get; set; } = 0;
    public float Bottom { get; set; } = 0;
    public float Left { get; set; } = 0;
    public float Right { get; set; } = 0;
    public float DamageMultiplier { get; set; } = 1;

    public DamageZoneStats ToStats() => new(Enum.Parse<PlayerBodyPart>(Zone), Coverage, DirectionConstrain.FromDegrees(Top, Bottom, Right, Left), DamageMultiplier);
}

public readonly struct DamageZoneStats
{
    public readonly PlayerBodyPart ZoneType;
    public readonly float Coverage;
    public readonly DirectionConstrain Directions;
    public readonly float DamageMultiplier;

    public DamageZoneStats(PlayerBodyPart type, float coverage, DirectionConstrain directions, float damageMultiplier)
    {
        ZoneType = type;
        Coverage = coverage;
        Directions = directions;
        DamageMultiplier = damageMultiplier;
    }
}

public sealed class DamageBlockStats
{
    public readonly PlayerBodyPart ZoneType;
    public readonly DirectionConstrain Directions;
    public readonly Action<float> Callback;
    public readonly string? Sound;
    public readonly Dictionary<EnumDamageType, float>? BlockTier;
    public readonly bool CanBlockProjectiles;
    public readonly TimeSpan StaggerTime;
    public readonly int StaggerTier;

    public DamageBlockStats(PlayerBodyPart type, DirectionConstrain directions, Action<float> callback, string? sound, Dictionary<EnumDamageType, float>? blockTier, bool canBlockProjectiles, TimeSpan staggerTime, int staggerTier)
    {
        ZoneType = type;
        Directions = directions;
        Callback = callback;
        Sound = sound;
        BlockTier = blockTier;
        CanBlockProjectiles = canBlockProjectiles;
        StaggerTime = staggerTime;
        StaggerTier = staggerTier;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockPacket
{
    public int Zones { get; set; }
    public float[] Directions { get; set; } = Array.Empty<float>();
    public bool MainHand { get; set; }
    public string? Sound { get; set; } = null;
    public Dictionary<EnumDamageType, float>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; }
    public int StaggerTimeMs { get; set; }
    public int StaggerTier { get; set; }

    public DamageBlockStats ToBlockStats(Action<float> callback)
    {
        return new((PlayerBodyPart)Zones, DirectionConstrain.FromArray(Directions), callback, Sound, BlockTier, CanBlockProjectiles, TimeSpan.FromMilliseconds(StaggerTimeMs), StaggerTier);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageStopBlockPacket
{
    public bool MainHand { get; set; }
}

public sealed class DamageBlockJson
{
    public string[] Zones { get; set; } = Array.Empty<string>();
    public float[] Directions { get; set; } = Array.Empty<float>();
    public string? Sound { get; set; } = null;
    public Dictionary<string, float>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; } = true;
    public int StaggerTimeMs { get; set; } = 0;
    public int StaggerTier { get; set; } = 1;

    public DamageBlockPacket ToPacket()
    {
        return new()
        {
            Zones = (int)Zones.Select(Enum.Parse<PlayerBodyPart>).Aggregate((first, second) => first | second),
            Directions = Directions,
            Sound = Sound,
            BlockTier = BlockTier?.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value),
            CanBlockProjectiles = CanBlockProjectiles,
            StaggerTimeMs = StaggerTimeMs,
            StaggerTier = StaggerTier
        };
    }

    public DamageBlockJson Clone()
    {
        return new()
        {
            Zones = Zones,
            Directions = Directions,
            Sound = Sound,
            BlockTier = BlockTier?.ToDictionary(entry => entry.Key, entry => entry.Value),
            CanBlockProjectiles = CanBlockProjectiles,
            StaggerTimeMs = StaggerTimeMs,
            StaggerTier = StaggerTier
        };
    }
}

public sealed class MeleeBlockSystemClient : MeleeSystem
{
    public MeleeBlockSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>();
    }

    public void StartBlock(DamageBlockJson block, bool mainHand)
    {
        DamageBlockPacket packet = block.ToPacket();
        packet.MainHand = mainHand;
        _clientChannel.SendPacket(packet);
    }
    public void StopBlock(bool mainHand)
    {
        _clientChannel.SendPacket(new DamageStopBlockPacket() { MainHand = mainHand });
    }

    private readonly IClientNetworkChannel _clientChannel;
}

public interface IHasServerBlockCallback
{
    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked);
}

public sealed class MeleeBlockSystemServer : MeleeSystem
{
    public MeleeBlockSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>()
            .SetMessageHandler<DamageBlockPacket>(HandlePacket)
            .SetMessageHandler<DamageStopBlockPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, DamageBlockPacket packet)
    {
        PlayerDamageModelBehavior behavior = player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null)
        {
            behavior.CurrentDamageBlock = packet.ToBlockStats(damageBlocked => BlockCallback(player, packet.MainHand, damageBlocked));
        }
    }

    private void HandlePacket(IServerPlayer player, DamageStopBlockPacket packet)
    {
        PlayerDamageModelBehavior behavior = player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null)
        {
            behavior.CurrentDamageBlock = null;
        }
    }

    private static void BlockCallback(IServerPlayer player, bool mainHand, float damageBlocked)
    {
        ItemSlot slot = mainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;

        if (slot?.Itemstack?.Item is not IHasServerBlockCallback item) return;

        item.BlockCallback(player, slot, mainHand, damageBlocked);
    }
}