﻿using CombatOverhaul.Compatibility;
using CombatOverhaul.Integration;
using OpenTK.Mathematics;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace CombatOverhaul.Colliders;


public enum ColliderTypes
{
    /// <summary>
    /// Normal damage received<br/>
    /// No special effects
    /// </summary>
    Torso,
    /// <summary>
    /// High damage received<br/>
    /// Affects: sight, bite attacks
    /// </summary>
    Head,
    /// <summary>
    /// Low damage received<br/>
    /// Affects: punch, throw and weapon attacks
    /// </summary>
    Arm,
    /// <summary>
    /// Low damage received<br/>
    /// Affects: movement, kick attacks
    /// </summary>
    Leg,
    /// <summary>
    /// Very high damage received<br/>
    /// No special effects
    /// </summary>
    Critical,
    /// <summary>
    /// No damage received<br/>
    /// No special effects
    /// </summary>
    Resistant
}

public sealed class ColliderTypesJson
{
    public string[] Torso { get; set; } = Array.Empty<string>();
    public string[] Head { get; set; } = Array.Empty<string>();
    public string[] Arm { get; set; } = Array.Empty<string>();
    public string[] Leg { get; set; } = Array.Empty<string>();
    public string[] Critical { get; set; } = Array.Empty<string>();
    public string[] Resistant { get; set; } = Array.Empty<string>();
}

public class CollidersConfig
{
    public ColliderTypesJson Elements { get; set; } = new();
    public float DefaultPenetrationResistance { get; set; } = 5f;
    public Dictionary<string, float> PenetrationResistances { get; set; } = [];
    public bool ResistantCollidersStopProjectiles { get; set; } = true;
}

public sealed class CollidersEntityBehavior : EntityBehavior, IDisposable
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
    }

    public CuboidAABBCollider BoundingBox { get; private set; }
    public bool HasOBBCollider { get; private set; } = false;
    public bool UnprocessedElementsLeft { get; set; } = false;
    public bool UnprocessedElementsLeftCustom { get; set; } = false;
    public HashSet<string> ShapeElementsToProcess { get; private set; } = new();
    public Dictionary<string, ColliderTypes> CollidersTypes { get; private set; } = new();
    public Dictionary<string, ShapeElementCollider> Colliders { get; private set; } = new();
    public override string PropertyName() => "CombatOverhaul:EntityColliders";
    internal ClientAnimator? Animator { get; set; }
    static public bool RenderColliders { get; set; } = false;
    public float DefaultPenetrationResistance { get; set; } = 5f;
    public Dictionary<string, float> PenetrationResistances { get; set; } = new();
    public bool ResistantCollidersStopProjectiles { get; set; } = true;

    public const string PlayerModelLibId = "playermodellib";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        try
        {
            if (!attributes.KeyExists("elements"))
            {
                Utils.LoggerUtil.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. 'elements' attribute was not found.");
                return;
            }

            _defaultConfig = attributes.AsObject<CollidersConfig>();

            ApplyConfig(_defaultConfig);
        }
        catch (Exception exception)
        {
            Utils.LoggerUtil.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. Exception:\n{exception}");
            UnprocessedElementsLeft = false;
            HasOBBCollider = false;
            return;
        }

        _listenerId = entity.Api.World.RegisterGameTickListener(Update, _updateTimeMs, entity.Api.World.Rand.Next(_updateTimeMs));
    }
    public override void AfterInitialized(bool onFirstSpawn)
    {
        if (Api == null) return;

        if (Api.ModLoader.IsModEnabled(PlayerModelLibId))
        {
            SubscribeOnModelChange();
        }
    }

    public void Update(float deltaTime)
    {
        if (entity.State == EnumEntityState.Despawned)
        {
            Dispose();
            return;
        }

        if (entity.Api is not ICoreClientAPI clientApi || !HasOBBCollider || !entity.Alive) return;

        Utils.LoggerUtil.Mark(entity.Api, "col-ogt-0");

        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (Animator == null) return;

        if (UnprocessedElementsLeft)
        {
            try
            {
                foreach (ElementPose pose in Animator.RootPoses)
                {
                    AddPoseShapeElements(pose);
                }

                if (ShapeElementsToProcess.Any() && !_reportedMissingColliders)
                {
                    string missingColliders = ShapeElementsToProcess.Aggregate((first, second) => $"{first}, {second}");
                    Utils.LoggerUtil.Warn(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                    _reportedMissingColliders = true;
                }
            }
            catch (Exception exception)
            {
                if (_reportedMissingColliders)
                {
                    Utils.LoggerUtil.Error(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Error during creating colliders: \n{exception}");
                    _reportedMissingColliders = true;
                }
            }
        }

        ProcessCollidersForCustomModel();

        Utils.LoggerUtil.Mark(entity.Api, "col-ogt-1");

        if (entity.IsRendered)
        {
            RecalculateColliders(Animator, clientApi);
        }

        Utils.LoggerUtil.Mark(entity.Api, "col-ogt-2");
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };
        if (api.World.Player.Entity.EntityId == entityPlayer.EntityId && firstPerson) return;
        if (!HasOBBCollider || !entity.Alive) return;

        IShaderProgram? currentShader = api.Render.CurrentActiveShader;
        currentShader?.Stop();

        foreach ((string id, ShapeElementCollider collider) in Colliders)
        {
            if (!collider.HasRenderer)
            {
                collider.Renderer ??= renderer;
                collider.HasRenderer = true;
            }

            if (RenderColliders && CollidersTypes.TryGetValue(id, out ColliderTypes value))
            {
                collider.Render(api, entityPlayer, _colliderColors[value]);
            }
        }

        currentShader?.Use();
    }
    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out string collider, out double parameter, out Vector3d intersection)
    {

        parameter = float.MaxValue;
        bool foundIntersection = false;
        collider = "";
        intersection = Vector3d.Zero;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            bool collided = AABBCollider.Collide(segmentStart, segmentDirection, out parameter);
            intersection = segmentStart + parameter * segmentDirection;
            return collided;
        }

        if (!BoundingBox.Collide(segmentStart, segmentDirection, out _))
        {
            return false;
        }

        foreach ((string key, ShapeElementCollider shapeElementCollider) in Colliders)
        {
            if (shapeElementCollider.Collide(segmentStart, segmentDirection, out double currentParameter, out Vector3d currentIntersection) && currentParameter < parameter)
            {
                parameter = currentParameter;
                collider = key;
                intersection = currentIntersection;
                foundIntersection = true;
            }
        }

        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, double radius, out string collider, out double distance, out Vector3d intersection)
    {
        distance = double.MaxValue;
        bool foundIntersection = false;
        collider = "";

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            return AABBCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out intersection);
        }

        if (!BoundingBox.Collide(thisTickOrigin, previousTickOrigin, radius, out intersection))
        {
            return false;
        }

        foreach ((string key, ShapeElementCollider shapeElementCollider) in Colliders)
        {
            if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out double currentDistance, out Vector3d currentIntersection) && currentDistance < distance)
            {
                distance = currentDistance;
                collider = key;
                intersection = currentIntersection;
                foundIntersection = true;
            }
        }

        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, float radius, float penetrationDistance, out List<(string, double, Vector3d)> intersections)
    {
        intersections = new();
        bool foundIntersection = false;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            return AABBCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out Vector3d intersection);
        }

        if (!BoundingBox.Collide(thisTickOrigin, previousTickOrigin, radius, out _))
        {
            return false;
        }

        Vector3d firstIntersection = previousTickOrigin;
        double lowestParameter = 1;

        foreach ((string key, ShapeElementCollider shapeElementCollider) in Colliders)
        {
            if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out _, out _, out Vector3d segmentClosestPoint))
            {
                Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                double parameter = segmentPoint.Length / (thisTickOrigin - previousTickOrigin).Length;

                if (lowestParameter >= parameter)
                {
                    firstIntersection = segmentClosestPoint;
                    lowestParameter = parameter;
                }

                foundIntersection = true;
            }
        }

        if (foundIntersection)
        {
            Vector3d thisTickOriginAdjustedForPenetration = firstIntersection + Vector3d.Normalize(thisTickOrigin - previousTickOrigin) * penetrationDistance;

            foundIntersection = false;
            foreach ((string key, ShapeElementCollider shapeElementCollider) in Colliders)
            {
                if (shapeElementCollider.Collide(thisTickOriginAdjustedForPenetration, previousTickOrigin, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
                {
                    Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                    double parameter = (segmentPoint.Length + currentDistance) / (thisTickOrigin - previousTickOrigin).Length;

                    intersections.Add((key, parameter, currentIntersection));
                    foundIntersection = true;
                }
            }
        }

        intersections.Sort((first, second) => (int)(first.Item2 - second.Item2));

        return foundIntersection;
    }

    private readonly Dictionary<ColliderTypes, int> _colliderColors = new()
    {
        { ColliderTypes.Torso, ColorUtil.WhiteArgb },
        { ColliderTypes.Head, ColorUtil.ColorFromRgba(255, 0, 0, 255 ) }, // Red
        { ColliderTypes.Arm, ColorUtil.ColorFromRgba(0, 255, 0, 255 ) }, // Green
        { ColliderTypes.Leg, ColorUtil.ColorFromRgba(0, 0, 255, 255 ) }, // Blue
        { ColliderTypes.Critical, ColorUtil.ColorFromRgba(255, 255, 0, 255 ) }, // Yellow
        { ColliderTypes.Resistant, ColorUtil.ColorFromRgba(255, 0, 255, 255 ) } // Magenta
    };
    private bool _reportedMissingColliders = false;
    private ICoreAPI? Api => entity?.Api;
    private CollidersConfig _defaultConfig = new();
    private const int _updateFps = 30;
    private const int _updateTimeMs = 1000 / _updateFps;
    private long _listenerId = 0;

    private void SetColliderElement(ShapeElement element)
    {
        if (element?.Name == null || element.From == null || element.To == null) return;

        if (UnprocessedElementsLeft && ShapeElementsToProcess.Contains(element.Name))
        {
            Colliders[element.Name] = new ShapeElementCollider(element);
            ShapeElementsToProcess.Remove(element.Name);
            UnprocessedElementsLeft = ShapeElementsToProcess.Count > 0;
        }
    }
    private void AddPoseShapeElements(ElementPose pose)
    {
        SetColliderElement(pose.ForElement);

        foreach (ElementPose childPose in pose.ChildElementPoses)
        {
            AddPoseShapeElements(childPose);
        }
    }
    private void RecalculateColliders(ClientAnimator animator, ICoreClientAPI clientApi)
    {
        foreach ((_, ShapeElementCollider collider) in Colliders)
        {
            collider.Transform(animator.TransformationMatrices, clientApi);
        }
        CalculateBoundingBox();
    }
    private void CalculateBoundingBox()
    {
        Vector3d min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3d max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            for (int vertex = 0; vertex < ShapeElementCollider.VertexCount; vertex++)
            {
                Vector4d inworldVertex = collider.InworldVertices[vertex];
                min.X = Math.Min(min.X, inworldVertex.X);
                min.Y = Math.Min(min.Y, inworldVertex.Y);
                min.Z = Math.Min(min.Z, inworldVertex.Z);
                max.X = Math.Max(max.X, inworldVertex.X);
                max.Y = Math.Max(max.Y, inworldVertex.Y);
                max.Z = Math.Max(max.Z, inworldVertex.Z);
            }
        }

        BoundingBox = new CuboidAABBCollider(min, max);
    }
    private void ReloadCollidersForCustomModel(string modelCode)
    {
        PlayerModelLibCompatibilitySystem? system = Api?.ModLoader.GetModSystem<PlayerModelLibCompatibilitySystem>();

        if (system == null) return;

        if (!system.CustomModelConfigs.TryGetValue(modelCode, out PlayerModelConfig? customModelConfig) || customModelConfig.Colliders == null)
        {
            ApplyConfig(_defaultConfig);
            return;
        }

        CollidersTypes.Clear();
        ShapeElementsToProcess.Clear();

        ColliderTypesJson types = customModelConfig.Colliders.Elements;
        foreach (string collider in types.Torso)
        {
            CollidersTypes.Add(collider, ColliderTypes.Torso);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Head)
        {
            CollidersTypes.Add(collider, ColliderTypes.Head);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Arm)
        {
            CollidersTypes.Add(collider, ColliderTypes.Arm);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Leg)
        {
            CollidersTypes.Add(collider, ColliderTypes.Leg);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Critical)
        {
            CollidersTypes.Add(collider, ColliderTypes.Critical);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Resistant)
        {
            CollidersTypes.Add(collider, ColliderTypes.Resistant);
            ShapeElementsToProcess.Add(collider);
        }

        DefaultPenetrationResistance = customModelConfig.Colliders.DefaultPenetrationResistance;
        PenetrationResistances = customModelConfig.Colliders.PenetrationResistances;
        ResistantCollidersStopProjectiles = customModelConfig.Colliders.ResistantCollidersStopProjectiles;

        UnprocessedElementsLeftCustom = true;
        HasOBBCollider = true;
    }
    private void ApplyConfig(CollidersConfig config)
    {
        CollidersTypes.Clear();
        ShapeElementsToProcess.Clear();

        ColliderTypesJson types = config.Elements;
        foreach (string collider in types.Torso)
        {
            CollidersTypes.Add(collider, ColliderTypes.Torso);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Head)
        {
            CollidersTypes.Add(collider, ColliderTypes.Head);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Arm)
        {
            CollidersTypes.Add(collider, ColliderTypes.Arm);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Leg)
        {
            CollidersTypes.Add(collider, ColliderTypes.Leg);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Critical)
        {
            CollidersTypes.Add(collider, ColliderTypes.Critical);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Resistant)
        {
            CollidersTypes.Add(collider, ColliderTypes.Resistant);
            ShapeElementsToProcess.Add(collider);
        }

        DefaultPenetrationResistance = config.DefaultPenetrationResistance;
        PenetrationResistances = config.PenetrationResistances;
        ResistantCollidersStopProjectiles = config.ResistantCollidersStopProjectiles;

        UnprocessedElementsLeft = true;
        HasOBBCollider = true;
    }
    private void SubscribeOnModelChange()
    {
        PlayerSkinBehavior? skinBehavior = entity.GetBehavior<PlayerSkinBehavior>();

        if (skinBehavior != null)
        {
            skinBehavior.OnModelChanged += ReloadCollidersForCustomModel;
        }
    }
    private void ProcessCollidersForCustomModel()
    {
        if (!UnprocessedElementsLeftCustom) return;

        //entity.AnimManager.LoadAnimator(entity.World.Api, entity, customShape, entity.AnimManager.Animator?.Animations, true, ["head"]);

        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (Animator == null) return;

        UnprocessedElementsLeft = UnprocessedElementsLeftCustom;
        Colliders.Clear();

        try
        {
            foreach (ElementPose pose in Animator.RootPoses)
            {
                AddPoseShapeElements(pose);
            }

            if (ShapeElementsToProcess.Any() && !_reportedMissingColliders)
            {
                string missingColliders = ShapeElementsToProcess.Aggregate((first, second) => $"{first}, {second}");
                Utils.LoggerUtil.Warn(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                _reportedMissingColliders = true;
            }
        }
        catch (Exception exception)
        {
            if (_reportedMissingColliders)
            {
                Utils.LoggerUtil.Error(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Error during creating colliders: \n{exception}");
                _reportedMissingColliders = true;
            }
        }

        UnprocessedElementsLeftCustom = false;
    }

    public void Dispose()
    {
        Api?.World?.UnregisterGameTickListener(_listenerId);
        _listenerId = 0;
    }
}