using CollidersLib;
using CollidersLib.Projectiles;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
        _collidersBehavior = projectile.GetBehavior<ProjectileColliderServerBehavior>() ?? throw new Exception();
        _physicsBehavior = projectile.GetBehavior<ProjectilePhysicsBehavior>() ?? throw new Exception();

        _entity = projectile;
        _entity.ClearCallback = clearCallback;

        _owner = ((_shooter as EntityPlayer)?.Player as IServerPlayer) ?? (_api.World.AllOnlinePlayers[0] as IServerPlayer) ?? throw new Exception(); // @TODO fix this mess
        _collidersBehavior.OnCollision += OnCollision;
        _collidersBehavior.ToggleCollisions(_owner, true);
    }

    public const string DamageTierPlayerStatPrefix = "rangedDamageTierBonus";

    private readonly ProjectileColliderServerBehavior _collidersBehavior;
    private readonly ProjectilePhysicsBehavior _physicsBehavior;
    private readonly ProjectileStats _stats;
    private readonly ProjectileSpawnStats _spawnStats;
    private readonly ProjectileEntity _entity;
    private readonly Entity _shooter;
    private readonly IServerPlayer _owner;
    private readonly ICoreAPI _api;
    private readonly ProjectileSystemServer _system;
    private readonly Settings _settings;
    private readonly HashSet<long> _entitiesHit = [];
    private bool _collisionsWithEntityActive = true;


    private void OnCollision(Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions, List<TerrainWithShpereIntersectionData> terrainCollisions)
    {
        List<Entity> entitiesToRemove = entityCollisions.Keys.Where(entity => _entitiesHit.Contains(entity.EntityId)).ToList();
        foreach (Entity entity in entitiesToRemove)
        {
            entityCollisions.Remove(entity);
        }

        if (entityCollisions.Count > 0 && terrainCollisions.Count > 0)
        {
            Entity target = GetFirstEntityHit(entityCollisions, out double entityHitTime);
            TerrainWithShpereIntersectionData terrain = GetFirstTerrainHit(terrainCollisions, out double terrainHitTime);

            if (entityHitTime <= terrainHitTime)
            {
                OnCollisionWithEntity(target, entityCollisions[target]);
            }
            else
            {
                OnCollisionWithTerrain(terrain);
            }
        }
        else if (terrainCollisions.Count > 0)
        {
            TerrainWithShpereIntersectionData terrain = GetFirstTerrainHit(terrainCollisions, out _);
            OnCollisionWithTerrain(terrain);
        }
        else if (entityCollisions.Count > 0)
        {
            Entity target = GetFirstEntityHit(entityCollisions, out _);
            OnCollisionWithEntity(target, entityCollisions[target]);
        }
    }
    private void OnCollisionWithEntity(Entity target, EntityWithSphereIntersectionData[] collisions)
    {
        if (!_collisionsWithEntityActive)
        {
            return;
        }

        EntityWithSphereIntersectionData collisionData = SelectCollision(target, collisions);

        //float initialPenetrationStrength = _entity.PenetrationStrength;
        //_entity.PenetrationStrength = Math.Max(0, _entity.PenetrationStrength - packet.PenetrationStrengthLoss);

        Vector3d collisionPoint = collisionData.IntersectionPoint;

        _entity.Pos.SetPos(new Vec3d(collisionPoint.X, collisionPoint.Y, collisionPoint.Z));
        _entity.Pos.Motion.X = target.Pos.Motion.X;
        _entity.Pos.Motion.Y = target.Pos.Motion.Y;
        _entity.Pos.Motion.Z = target.Pos.Motion.Z;

        bool hit = Attack(_shooter, target, collisionData);

        if (hit)
        {
            PlaySound(_shooter);
        }

        _entitiesHit.Add(target.EntityId);
        _entity.OnCollisionWithEntity(target, collisionData);
        _collisionsWithEntityActive = false;

        _collidersBehavior.ResetCollisionsPosition(collisionData.IntersectionPoint);
    }
    private void OnCollisionWithTerrain(TerrainWithShpereIntersectionData collision)
    {
        _physicsBehavior.OnCollisionWithTerrain(collision);
        _collidersBehavior.ResetCollisionsPosition(collision.IntersectionPoint);
        _collisionsWithEntityActive = false;

        //_entity.OnCollisionWithTerrain(collision);
    }


    private EntityWithSphereIntersectionData SelectCollision(Entity target, EntityWithSphereIntersectionData[] collisions)
    {
        return collisions[0]; // @TODO fix
    }
    private Entity GetFirstEntityHit(Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions, out double minTime)
    {
        minTime = double.MaxValue;
        Entity earliestHit = entityCollisions.Keys.First();
        foreach ((Entity traget, EntityWithSphereIntersectionData[] collisions) in entityCollisions)
        {
            double earliestCollision = collisions.Min(collision => collision.PositionInTime);
            if (earliestCollision < minTime)
            {
                minTime = earliestCollision;
                earliestHit = traget;
            }
        }

        return earliestHit;
    }
    private TerrainWithShpereIntersectionData GetFirstTerrainHit(List<TerrainWithShpereIntersectionData> terrainCollisions, out double minTime)
    {
        minTime = double.MaxValue;
        TerrainWithShpereIntersectionData earliestHit = terrainCollisions[0];
        foreach (TerrainWithShpereIntersectionData hit in terrainCollisions)
        {
            if (hit.PositionInTime < minTime)
            {
                minTime = hit.PositionInTime;
                earliestHit = hit;
            }
        }

        return earliestHit;
    }
    private bool Attack(Entity attacker, Entity target, EntityWithSphereIntersectionData collisionData)
    {
        //if (relativeSpeed < _stats.SpeedThreshold) return false;
        if (!target.Alive) return false;

        string targetName = target.GetName();
        string projectileName = _entity.GetName();

        string damageTierStat = DamageTierPlayerStatPrefix + _stats.DamageStats.DamageType.ToString();
        float statValue = attacker.Stats.GetBlended(damageTierStat) - 1;
        float damage = _stats.DamageStats.Damage * _spawnStats.DamageMultiplier;

        if (_settings.RangedWeaponsDamageSupport)
        {
            float rangedWeaponsDamageStat = Math.Max(0, attacker.Stats.GetBlended("rangedWeaponsDamage"));
            damage *= rangedWeaponsDamageStat;
        }

        int damageTierBonus = _stats.DamageTierBonus + (int)statValue;
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
            Position = collisionData.IntersectionPoint,
            Collider = collisionData.EntityCollider?.ShapeElementName ?? "",
            DamageTypeData = damageData,
            DamageTier = damageData.Tier,
            KnockbackStrength = _stats.Knockback,
            Weapon = _entity.WeaponStack,
            IgnoreInvFrames = _entity.IgnoreInvFrames,
        };

        _system.OnDealDamage(target, damageSource, _entity.WeaponStack, ref damage);

        bool damageReceived = target.ReceiveDamage(damageSource, damage);

        bool received = damageReceived || damage <= 0;

        /*if (_settings.PrintRangeHits && collisionData.EntityCollider?.ShapeElementName != "")
        {
            CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
            ColliderTypes ColliderType = colliders?.CollidersTypes[collider] ?? ColliderTypes.Torso;

            float damageReceivedValue = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;
            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage-with-projectile", Lang.Get($"combatoverhaul:entity-damage-zone-{ColliderType}"), targetName, $"{damageReceivedValue:F2}", projectileName);
            ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
        }*/

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
    public override bool IsInteractable => IsInteractableValue;
    public virtual bool IsInteractableValue { get; set; } = false;

    public static event Action<ProjectileEntity, EntityAgent, ItemSlot, Vec3d, EnumInteractMode>? OnInteracted;

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

    public virtual void OnCollisionWithEntity(Entity target, EntityWithSphereIntersectionData collisionData)
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
    public virtual void OnCollisionWithTerrain(TerrainWithShpereIntersectionData collisionData)
    {
        WatchedAttributes.MarkAllDirty();
        if (DurabilityDamageOnImpact != 0)
        {
            ProjectileStack?.Item?.DamageItem(Api.World, Api.World.GetEntityById(OwnerId), new DummySlot(ProjectileStack), DurabilityDamageOnImpact); // @TODO ownder might not be online at this point
            if (ProjectileStack?.Item?.GetRemainingDurability(ProjectileStack) <= 0)
            {
                Die();
            }
        }
        TryDestroyOnCollision();
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        OnInteracted?.Invoke(this, byEntity, itemslot, hitPosition, mode);
        base.OnInteract(byEntity, itemslot, hitPosition, mode);
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

        PreviousPosition = Pos.XYZ.Clone();
        PreviousVelocity = Pos.Motion.Clone();
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
