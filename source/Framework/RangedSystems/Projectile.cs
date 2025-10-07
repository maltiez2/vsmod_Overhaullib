using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;

public sealed class ProjectileServer
{
    public ProjectileServer(ProjectileEntity projectile, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ICoreAPI api, Action<Guid> clearCallback, ItemStack projectileStack)
    {
        _stats = projectileStats;
        _spawnStats = spawnStats;
        _api = api;
        _shooter = _api.World.GetEntityById(spawnStats.ProducerEntityId);

        _system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _settings = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

        _entity = projectile;
        _entity.ClearCallback = clearCallback;
    }

    public int PacketVersion { get; set; } = 0;

    public void OnCollision(ProjectileCollisionPacket packet)
    {
        

        Entity receiver = _api.World.GetEntityById(packet.ReceiverEntity);

        if (receiver == null) return;

        float initialPenetrationStrength = _entity.PenetrationStrength;
        _entity.PenetrationStrength = Math.Max(0, _entity.PenetrationStrength - packet.PenetrationStrengthLoss);

        Vector3d collisionPoint = new(packet.CollisionPoint[0], packet.CollisionPoint[1], packet.CollisionPoint[2]);

        //receiver.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 255, 0, 255), new(collisionPoint.X, collisionPoint.Y, collisionPoint.Z), new(collisionPoint.X, collisionPoint.Y, collisionPoint.Z), new Vec3f(), new Vec3f(), 3, 0, 1, EnumParticleModel.Cube);

        if (_entity.PenetrationStrength == 0)
        {
            _entity.ServerPos.SetPos(new Vec3d(collisionPoint.X, collisionPoint.Y, collisionPoint.Z));
            _entity.ServerPos.Motion.X = receiver.ServerPos.Motion.X;
            _entity.ServerPos.Motion.Y = receiver.ServerPos.Motion.Y;
            _entity.ServerPos.Motion.Z = receiver.ServerPos.Motion.Z;
        }
        else
        {
            _entity.ServerPos.SetPos(new Vec3d(collisionPoint.X, collisionPoint.Y, collisionPoint.Z));
            float speedReduction = _entity.PenetrationStrength / initialPenetrationStrength;
            _entity.ServerPos.Motion.X *= speedReduction;
            _entity.ServerPos.Motion.Y *= speedReduction;
            _entity.ServerPos.Motion.Z *= speedReduction;
        }

        bool hit = Attack(_shooter, receiver, collisionPoint, packet.Collider, packet.RelativeSpeed);

        if (hit) PlaySound(_shooter);

        _entity.OnCollisionWithEntity(receiver, packet.Collider);

        
    }

    public void TryCollide()
    {
        _system.TryCollide(_entity);

        
    }

    private readonly ProjectileStats _stats;
    private readonly ProjectileSpawnStats _spawnStats;
    internal readonly ProjectileEntity _entity;
    private readonly Entity _shooter;
    private readonly ICoreAPI _api;
    private readonly ProjectileSystemServer _system;
    private readonly Settings _settings;

    private bool Attack(Entity attacker, Entity target, Vector3d position, string collider, double relativeSpeed)
    {
        if (relativeSpeed < _stats.SpeedThreshold) return false;
        if (!target.Alive) return false;

        string targetName = target.GetName();
        string projectileName = _entity.GetName();

        float damage = _stats.DamageStats.Damage * _spawnStats.DamageMultiplier;
        int damageTierBonus = _stats.DamageTierBonus;
        DamageData damageData = new(
            Enum.Parse<EnumDamageType>(_stats.DamageStats.DamageType),
            Math.Max(1, _spawnStats.DamageTier + damageTierBonus),
            0
            );

        if (!CheckPermissions(attacker, target) && damageData.DamageType != EnumDamageType.Heal) return false;

        DirectionalTypedDamageSource damageSource = new()
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = _entity,
            CauseEntity = attacker,
            Type = damageData.DamageType,
            Position = position,
            Collider = collider,
            DamageTypeData = damageData,
            DamageTier = damageData.Tier,
            KnockbackStrength = _stats.Knockback,
            Weapon = _entity.WeaponStack,
            IgnoreInvFrames = _entity.IgnoreInvFrames,
        };

        _system.OnDealDamage(target, damageSource, _entity.WeaponStack, ref damage);

        bool damageReceived = target.ReceiveDamage(damageSource, damage);

        bool received = damageReceived || damage <= 0;

        if (_settings.PrintRangeHits && collider != "")
        {
            CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
            ColliderTypes ColliderType = colliders?.CollidersTypes[collider] ?? ColliderTypes.Torso;

            float damageReceivedValue = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;
            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage-with-projectile", Lang.Get($"combatoverhaul:entity-damage-zone-{ColliderType}"), targetName, $"{damageReceivedValue:F2}", projectileName);
            ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
        }

        return received;
    }
    private static bool CheckPermissions(Entity attacker, Entity target)
    {
        if (attacker.Api is ICoreServerAPI serverApi && attacker is EntityPlayer playerAttacker)
        {
            if (target is EntityPlayer && (!serverApi.Server.Config.AllowPvP || !playerAttacker.Player.HasPrivilege("attackplayers"))) return false;
            if (target is not EntityPlayer && !playerAttacker.Player.HasPrivilege("attackcreatures")) return false;
        }

        return true;
    }
    private void PlaySound(Entity attacker)
    {
        ProjectileStats? stats = _entity.ProjectileStack?.Item?.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;
        if (stats == null || attacker is not EntityPlayer player || stats.HitSound == "") return;

        _api.World.PlaySoundFor(new(stats.HitSound), player.Player, false);
    }
}

public class ProjectileEntity : Entity
{
    public ProjectileServer? ServerProjectile { get; set; }
    public Guid ProjectileId { get; set; }
    public ItemStack? ProjectileStack { get; set; }
    public ItemStack? WeaponStack { get; set; }
    public int DurabilityDamageOnImpact { get; set; }
    public float DropOnImpactChance { get; set; }
    public Action<Guid>? ClearCallback { get; set; }
    public float ColliderRadius { get; set; }
    public float PenetrationDistance { get; set; }
    public float PenetrationStrength { get; set; }
    public long ShooterId { get; set; }
    public long OwnerId { get; set; }
    public Vec3d PreviousPosition { get; private set; } = new(0, 0, 0);
    public Vec3d PreviousVelocity { get; private set; } = new(0, 0, 0);
    public List<long> CollidedWith { get; set; } = new();
    public bool IgnoreInvFrames { get; set; } = true;
    public bool CanBeCollected { get; set; } = true;


    public bool Stuck
    {
        get => StuckInternal;
        set
        {
            StuckInternal = value;
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", StuckInternal);
        }
    }

    public override bool ApplyGravity => !Stuck;
    public override bool IsInteractable => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        SpawnTime = TimeSpan.FromMilliseconds(World.ElapsedMilliseconds);

        CollisionTestBox = SelectionBox.Clone();//.OmniGrowBy(0.05f);

        ProjectilePhysicsBehavior? physicsBehavior = GetBehavior<ProjectilePhysicsBehavior>();

        if (physicsBehavior == null)
        {
            LoggerUtil.Error(Api, this, $"Projectile {Code} does not have 'ProjectilePhysicsBehavior', update this mod to support latest Overhaul lib version.");
            return;
        }

        PhysicsBehavior = physicsBehavior;

        if (physicsBehavior.Config.ColliderRadius == 0)
        {
            physicsBehavior.Config.ColliderRadius = ColliderRadius;
        }
        physicsBehavior.OnPhysicsTickCallback = OnPhysicsTickCallback;

        PreviousPosition = Pos.XYZ.Clone();
        PreviousVelocity = Pos.Motion.Clone();
        StartingPos = Pos.XYZ.Clone();
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (ShouldDespawn) return;

        

        if (Api.Side == EnumAppSide.Server && Stuck && !Collided)
        {
            WatchedAttributes.SetBool("stuck", false);
        }

        Stuck = Collided || WatchedAttributes.GetBool("stuck");
        if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", Stuck);
        if (PhysicsBehavior != null) PhysicsBehavior.Stuck = Stuck;

        if (!Stuck)
        {
            SetRotation();
        }

        double impactSpeed = Math.Max(MotionBeforeCollide.Length(), SidedPos.Motion.Length());
        if (Stuck)
        {
            OnTerrainCollision(SidedPos, impactSpeed);
        }

        //BeforeCollided = false;
        MotionBeforeCollide.Set(SidedPos.Motion.X, SidedPos.Motion.Y, SidedPos.Motion.Z);

        
    }
    public override bool CanCollect(Entity byEntity)
    {
        return CanBeCollected && Alive && TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime > CollisionDelay && ServerPos.Motion.Length() < 0.01;
    }
    public override ItemStack? OnCollected(Entity byEntity)
    {
        ClearCallback?.Invoke(ProjectileId);
        ProjectileStack?.ResolveBlockOrItem(World);
        return CanBeCollected ? ProjectileStack : null;
    }
    public override void OnCollided()
    {
        EntityPos sidedPos = SidedPos;
        OnTerrainCollision(SidedPos, Math.Max(MotionBeforeCollide.Length(), sidedPos.Motion.Length()));
        MotionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
    }
    public override void ToBytes(BinaryWriter writer, bool forClient)
    {
        base.ToBytes(writer, forClient);
        writer.Write(ShooterId);
        writer.Write(ProjectileId.ToString());
        writer.Write(ProjectileStack != null);
        ProjectileStack?.ToBytes(writer);
        writer.Write(WeaponStack != null);
        WeaponStack?.ToBytes(writer);
        writer.Write(OwnerId);
        writer.Write(IgnoreInvFrames);
        writer.Write(CanBeCollected);
    }
    public override void FromBytes(BinaryReader reader, bool fromServer)
    {
        base.FromBytes(reader, fromServer);
        try
        {
            ShooterId = reader.ReadInt64();
            ProjectileId = Guid.Parse(reader.ReadString());
            if (reader.ReadBoolean()) ProjectileStack = new ItemStack(reader);
            if (reader.ReadBoolean()) WeaponStack = new ItemStack(reader);
            OwnerId = reader.ReadInt64();
            IgnoreInvFrames = reader.ReadBoolean();
            CanBeCollected = reader.ReadBoolean();
        }
        catch (Exception exception)
        {
#if DEBUG
            Debug.WriteLine($"Error on restoring projectile {Code} from bytes:\n{exception}");
#endif
        }
    }
    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        ClearCallback?.Invoke(ProjectileId);
    }
    public void SetRotation()
    {
        EntityPos pos = (World is IServerWorldAccessor) ? ServerPos : Pos;

        double speed = pos.Motion.Length();

        if (speed > 0.01)
        {
            pos.Pitch = 0;
            pos.Yaw =
                GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                + GameMath.Cos((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
            pos.Roll =
                -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                + GameMath.Sin((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
        }
    }

    public void OnCollisionWithEntity(Entity target, string collider)
    {
        WatchedAttributes.MarkAllDirty();
        if (DurabilityDamageOnImpact != 0)
        {
            ProjectileStack?.Item?.DamageItem(Api.World, target, new DummySlot(ProjectileStack), DurabilityDamageOnImpact);
            if (ProjectileStack?.Item?.GetRemainingDurability(ProjectileStack) <= 0)
            {
                Die();
            }
        }
        TryDestroyOnCollision();
    }

    protected readonly TimeSpan CollisionDelay = TimeSpan.FromMilliseconds(500);
    protected TimeSpan SpawnTime = TimeSpan.Zero;
    protected bool StuckInternal;
    protected readonly CollisionTester CollTester = new();
    protected Cuboidf? CollisionTestBox;
    protected Vec3d MotionBeforeCollide = new();
    protected bool BeforeCollided = false;
    protected long MsCollide = 0;
    protected Random Rand = new();
    protected Vector3d NewPosition = new();
    protected bool SetPosition = false;
    protected Vec3d StartingPos = new();
    protected ProjectilePhysicsBehavior? PhysicsBehavior;

    protected void OnPhysicsTickCallback(float dtFac)
    {
        if (ShouldDespawn || !Alive) return;

        if (!Stuck && ServerProjectile != null)
        {
            ServerProjectile.TryCollide();
        }

        PreviousPosition = SidedPos.XYZ.Clone();
        PreviousVelocity = SidedPos.Motion.Clone();
    }
    protected void OnTerrainCollision(EntityPos pos, double impactSpeed)
    {
        pos.Motion.Set(0.0, 0.0, 0.0);
        if (BeforeCollided || !(World is IServerWorldAccessor) || World.ElapsedMilliseconds <= MsCollide + 500)
        {
            return;
        }

        if (impactSpeed >= 0.07)
        {
            World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, randomizePitch: false);
            WatchedAttributes.MarkAllDirty();
        }

        MsCollide = World.ElapsedMilliseconds;
        BeforeCollided = true;
    }
    protected virtual void TryDestroyOnCollision()
    {
        float random = (float)Rand.NextDouble();
        if (DropOnImpactChance <= random)
        {
            World.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), this, null, randomizePitch: true, volume: 0.5f);
            Die();
        }
    }
}

public class ProjectileBehavior : CollectibleBehavior
{
    public ProjectileStats? Stats { get; private set; }

    public ProjectileBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Stats = properties["stats"].AsObject<ProjectileStats>();
    }

    public ProjectileStats GetStats(ItemStack stack)
    {
        ItemStackProjectileStats stackStats = ItemStackProjectileStats.FromItemStack(stack);

        ProjectileStats stats = Stats.Clone();
        stats.DamageStats.Damage *= stackStats.DamageMultiplier;
        stats.DamageTierBonus += stackStats.DamageTierBonus;
        stats.DropChance = Math.Max(0, Math.Min(1, stats.DropChance * stackStats.DropChanceMultiplier));
        stats.Knockback *= stackStats.KnockbackMultiplier;
        stats.PenetrationBonus = Math.Max(0, stackStats.PenetrationBonus + stats.PenetrationBonus);
        stats.AdditionalDurabilityCost = Math.Max(0, stackStats.AdditionalDurabilityCost + stats.AdditionalDurabilityCost);

        return stats;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null)
        {
            ItemStackMeleeWeaponStats weaponStackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);
            ItemStackProjectileStats projectileStackStats = ItemStackProjectileStats.FromItemStack(inSlot.Itemstack);

            dsc.AppendLine(Lang.Get(
                "combatoverhaul:iteminfo-projectile",
                $"{Stats.DamageStats.Damage * weaponStackStats.DamageMultiplier * projectileStackStats.DamageMultiplier:F1}",
                Lang.Get($"combatoverhaul:damage-type-{Stats.DamageStats.DamageType}"),
                $"{(1 - Stats.DropChance * projectileStackStats.DropChanceMultiplier) * 100:F1}"));

            if (Stats.DamageTierBonus != 0)
            {
                dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-projectile-bonus-damagetier", Stats.DamageTierBonus + weaponStackStats.DamageTierBonus + projectileStackStats.DamageTierBonus));
            }

            if (Stats.AdditionalDurabilityCost != 0)
            {
                dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-projectile-durability-cost", Stats.AdditionalDurabilityCost));
            }
        }

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }
}

public class ProjectilePhysicsBehaviorConfig
{
    public double ColliderRadius { get; set; } = 0;
    public bool CanRicochet { get; set; } = true;
    public float MinSpeedToRicochet { get; set; } = 0.5f;
    public float RicochetSpeedFactor { get; set; } = 0.5f;
    public float RicochetNormalSpeedFactor { get; set; } = 0.5f;
    public float MaxRicochetAngleDeg { get; set; } = 5;
}

public class ProjectilePhysicsBehavior : EntityBehaviorPassivePhysics
{
    public ProjectilePhysicsBehavior(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        Config = attributes.AsObject<ProjectilePhysicsBehaviorConfig>();

        EntityBehaviorPassivePhysics_airDragValue ??= typeof(EntityBehaviorPassivePhysics).GetField("airDragValue", BindingFlags.NonPublic | BindingFlags.Instance);

        EntityBehaviorPassivePhysics_airDragValue?.SetValue(this, 1);
    }

    public bool Stuck { get; set; } = false;

    public ProjectilePhysicsBehaviorConfig Config { get; set; } = new();

    protected BlockPos MinPos = new(0);
    protected BlockPos MaxPos = new(0);
    protected BlockPos PosBuffer = new(0);
    protected Cuboidd EntityBox = new();

    protected static FieldInfo? EntityBehaviorPassivePhysics_airDragValue = typeof(EntityBehaviorPassivePhysics).GetField("airDragValue", BindingFlags.NonPublic | BindingFlags.Instance);

    protected override void applyCollision(EntityPos pos, float dtFactor)
    {
        Vector3d CurrentPosition = new(pos.X, pos.Y, pos.Z);

        if (CurrentPosition.LengthSquared == 0) return;

        Vector3d PositionDelta = new(pos.Motion.X * dtFactor, pos.Motion.Y * dtFactor, pos.Motion.Z * dtFactor);
        Vector3d NextPosition = CurrentPosition + PositionDelta;

#if DEBUG
        CuboidAABBCollider._api = entity.Api as ICoreServerAPI;
        if (pos.Motion.Length() > 0.1)
        {
            CuboidAABBCollider._api?.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 100, 100, 125), new(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z), new(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z), new Vec3f(), new Vec3f(), 0.1f, 0, 0.7f, EnumParticleModel.Cube);
        }

        //CuboidAABBCollider._api?.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 100, 100, 125), new(NextPosition.X, NextPosition.Y, NextPosition.Z), new(NextPosition.X, NextPosition.Y, NextPosition.Z), new Vec3f(), new Vec3f(), 3, 0, 0.7f, EnumParticleModel.Cube);
#endif

        bool collided = CuboidAABBCollider.CollideWithTerrain(entity.Api.World.BlockAccessor, NextPosition, CurrentPosition, Config.ColliderRadius, out Vector3d intersection, out Vector3d normal, out BlockFacing? facing, out Block? block, out BlockPos? blockPosition);

        

        if (collided)
        {
            Angle angle = Angle.BetweenVectors(PositionDelta, normal);
            float angleDeg = Math.Abs(angle.Degrees);

            if ((angleDeg > 90 - Config.MaxRicochetAngleDeg) && (angleDeg < 90 + Config.MaxRicochetAngleDeg) && (pos.Motion.Length() > Config.MinSpeedToRicochet))
            {
                switch (facing.Index)
                {
                    case 0: // North / South
                    case 2:
                        pos.Motion.Z *= -Config.RicochetNormalSpeedFactor;
                        break;

                    case 1: // East / West
                    case 3:
                        pos.Motion.X *= -Config.RicochetNormalSpeedFactor;
                        break;

                    case 4: // Up / Down
                    case 5:
                        pos.Motion.Y *= -Config.RicochetNormalSpeedFactor;
                        break;
                }

                PositionDelta = new(pos.Motion.X * dtFactor, pos.Motion.Y * dtFactor, pos.Motion.Z * dtFactor);
                NextPosition = intersection + PositionDelta * (1 - (CurrentPosition - intersection).Length / (CurrentPosition - NextPosition).Length);

                switch (facing.Index)
                {
                    case 2: // North / South
                        NextPosition.Z = Math.Max(intersection.Z + Config.ColliderRadius, NextPosition.Z);
                        break;
                    case 0:
                        NextPosition.Z = Math.Min(intersection.Z - Config.ColliderRadius, NextPosition.Z);
                        break;

                    case 1: // East / West
                        NextPosition.X = Math.Max(intersection.X + Config.ColliderRadius, NextPosition.X);
                        break;
                    case 3:
                        NextPosition.X = Math.Min(intersection.X - Config.ColliderRadius, NextPosition.X);
                        break;

                    case 4: // Up / Down
                        NextPosition.Y = Math.Max(intersection.Y + Config.ColliderRadius, NextPosition.Y);
                        break;
                    case 5:
                        NextPosition.Y = Math.Min(intersection.Y - Config.ColliderRadius, NextPosition.Y);
                        break;
                }

                newPos.Set(NextPosition.X, NextPosition.Y, NextPosition.Z);
                entity.CollidedHorizontally = false;
                entity.CollidedVertically = false;
                (entity as ProjectileEntity)?.SetRotation();
                pos.Motion *= Config.RicochetSpeedFactor;

                entity.Api.World.PlaySoundAt(block?.Sounds?.Hit ?? block?.Sounds?.ByTool?.Values?.FirstOrDefault()?.Hit ?? block?.Sounds?.Break ?? new AssetLocation("game:sounds/player/destruct"), intersection.X, intersection.Y, intersection.Z);

                return;
            }

            newPos.Set(intersection.X, intersection.Y, intersection.Z);
            entity.WatchedAttributes.SetBool("stuck", true);
            entity.CollidedHorizontally = true;
            entity.CollidedVertically = true;

            block?.OnEntityCollide(entity.Api.World, entity, blockPosition, facing, pos.Motion, true);

            pos.Motion *= 0;
#if DEBUG
            //entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 255, 255, 50), new(newPos.X, newPos.Y, newPos.Z), new(newPos.X, newPos.Y, newPos.Z), new Vec3f(), new Vec3f(), 3, 0, 1.5f, EnumParticleModel.Cube);
#endif
        }
        else
        {
            entity.CollidedHorizontally = false;
            entity.CollidedVertically = false;

            newPos.Set(NextPosition.X, NextPosition.Y, NextPosition.Z);
        }

        
    }
}