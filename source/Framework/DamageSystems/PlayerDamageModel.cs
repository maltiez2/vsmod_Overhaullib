using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.Compatibility;
using CombatOverhaul.Implementations;
using CombatOverhaul.Integration;
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

    public static float GetDamageReductionFactor(IPlayer player, DamageZone zone, EnumDamageType damageType, int damageTier, DamageReceivedCalculationType calculationType)
    {
        if (player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) is not ArmorInventory inventory) return 0;

        if (zone == DamageZone.None) return 0;

        float damage = 1;

        PlayerDamageModelBehavior? damageModelBehavior = player.Entity.GetBehavior<PlayerDamageModelBehavior>();

        if (calculationType == DamageReceivedCalculationType.WithBodyParts)
        {
            float zoneMultiplier = damageModelBehavior.BodyPartsToZones
                .Where(entry => entry.Value == zone)
                .Select(entry => entry.Key)
                .Select(damageModelBehavior.DamageModel.GetMultiplier)
                .Average();

            damage *= zoneMultiplier * damageModelBehavior.GetStatsMultiplier(zone);
        }

        if (calculationType == DamageReceivedCalculationType.HitChance)
        {
            DirectionOffset direction = new(Angle.FromDegrees(-1), Angle.FromDegrees(1));

            float totalWeight = damageModelBehavior.DamageModel.DamageZones
                .Where(value => value.Directions.Check(direction))
                .Select(value => value.Coverage)
                .Sum();

            IEnumerable<PlayerBodyPart> bodyParts = damageModelBehavior.BodyPartsToZones
                .Where(entry => entry.Value == zone)
                .Select(entry => entry.Key);

            float zoneWieght = damageModelBehavior.DamageModel.DamageZones
                .Where(value => bodyParts.Contains(value.ZoneType))
                .Where(value => value.Directions.Check(direction))
                .Select(value => value.Coverage)
                .Sum();

            damage *= zoneWieght / totalWeight;

            return damage;
        }

        if (calculationType == DamageReceivedCalculationType.Average)
        {
            DirectionOffset direction = new(Angle.Zero, Angle.FromDegrees(1));

            float totalWeight = damageModelBehavior.DamageModel.DamageZones
                .Where(value => value.Directions.Check(direction))
                .Select(value => value.Coverage * value.DamageMultiplier * damageModelBehavior.GetStatsMultiplier(damageModelBehavior.BodyPartsToZones[value.ZoneType]))
                .Sum();

            IEnumerable<PlayerBodyPart> bodyParts = damageModelBehavior.BodyPartsToZones
                .Where(entry => entry.Value == zone)
                .Select(entry => entry.Key);

            float zoneWieght = damageModelBehavior.DamageModel.DamageZones
                .Where(value => bodyParts.Contains(value.ZoneType))
                .Where(value => value.Directions.Check(direction))
                .Select(value => value.Coverage * value.DamageMultiplier * damageModelBehavior.GetStatsMultiplier(damageModelBehavior.BodyPartsToZones[value.ZoneType]))
                .Sum();

            damage *= zoneWieght / totalWeight;
        }

        IEnumerable<ArmorSlot> slots = inventory.GetNotEmptyZoneSlots(zone);

        if (!slots.Any()) return damage;

        DamageResistData resists = DamageResistData.Combine(slots
            .Where(slot => slot?.Itemstack?.Item != null)
            .Where(slot => slot?.Itemstack?.Item.GetRemainingDurability(slot.Itemstack) > 0 || slot?.Itemstack?.Item.GetMaxDurability(slot.Itemstack) == 0)
            .Select(slot => slot.GetResists(zone)));


        int durabilityDamage = 0;

        _ = resists.ApplyPlayerResist(new(damageType, damageTier, 0), ref damage, out durabilityDamage);

        return damage;
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

        int damageTier = damageSource.DamageTier;
        int blockTier = 0;
        float initialDamage = damage;
        EnumDamageType damageType = damageSource.Type;

        if (CurrentDamageBlock.BlockTier != null)
        {
            if (!CurrentDamageBlock.BlockTier.ContainsKey(damageType))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-damageType", Lang.Get($"damage-type-{damageType}"));
                return;
            }

            blockTier = CurrentDamageBlock.BlockTier[damageType];
            if (blockTier < damageTier)
            {
                ApplyBlockResists(blockTier, damageTier, ref damage);
                damageSource.DamageTier = Math.Clamp(damageTier - blockTier, 0, damageTier);
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-partial-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"), $"{damageSource.DamageTier:F0}");
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

        CurrentDamageBlock.Callback.Invoke(initialDamage - damage, damageTier, blockTier);

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
            .Select(slot => slot.GetResists(zone)));

        float previousDamage = damage;
        int durabilityDamage = 0;
        int apTier = 0;
        if (damageSource is IArmorPiercing apSource)
        {
            apTier = apSource.ArmorPiercingTier;
        }

        _ = resists.ApplyPlayerResist(new(damageSource.Type, damageSource.DamageTier, apTier), ref damage, out durabilityDamage);

        durabilityDamage = GameMath.Clamp(durabilityDamage, 1, durabilityDamage);

        DamageArmor(slots, zone, damageType, durabilityDamage, out int totalDurabilityDamage);

        if (previousDamage - damage > 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-armor-damage-negation", $"{previousDamage - damage:F1}", Lang.Get($"combatoverhaul:damage-zone-{zone}"), totalDurabilityDamage, Lang.Get($"combatoverhaul:damage-type-{damageType}"), damageSource.DamageTier);
        }
    }
    private void ApplyBlockResists(float blockTier, float damageTier, ref float damage)
    {
        float multiplier = damageTier > blockTier ? 1 : 0;
        damage *= multiplier;
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
    private void DamageArmor(IEnumerable<ArmorSlot> slots, DamageZone zone, EnumDamageType damageType, int durabilityDamage, out int totalDurabilityDamage)
    {
        float totalProtection = slots.Select(slot => slot.GetResists(zone).Resists[damageType]).Sum();

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

            float protection = slot.GetResists(zone).Resists[damageType];
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
    public DamageZoneStats[] DamageZones { get; }

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
