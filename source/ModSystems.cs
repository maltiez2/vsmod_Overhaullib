using Cairo;
using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using ConfigLib;
using HarmonyLib;
using OpenTK.Mathematics;
using ProtoBuf;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace CombatOverhaul;

public sealed class Settings
{
    public float DirectionsCursorTransparency { get; set; } = 1.0f;
    public float DirectionsCursorScale { get; set; } = 1.0f;

    public string BowsAimingCursorType { get; set; } = "Moving";
    public float BowsAimingHorizontalLimit { get; set; } = 0.125f;
    public float BowsAimingVerticalLimit { get; set; } = 0.35f;

    public string ThrownWeaponsCursorType { get; set; } = "Fixed";
    public float ThrownWeaponsAimingHorizontalLimit { get; set; } = 0.125f;
    public float ThrownWeaponsAimingVerticalLimit { get; set; } = 0.25f;

    public string SlingsAimingCursorType { get; set; } = "Fixed";
    public float SlingsAimingHorizontalLimit { get; set; } = 0.125f;
    public float SlingsAimingVerticalLimit { get; set; } = 0.35f;

    public bool PrintRangeHits { get; set; } = false;
    public bool PrintMeleeHits { get; set; } = false;
    public bool PrintPlayerHits { get; set; } = false;

    public float DirectionsSensitivity { get; set; } = 1f;
    public bool DirectionsInvert { get; set; } = false;

    public bool HandsYawSmoothing { get; set; } = false;

    public bool VanillaActionsWhileBlocking { get; set; } = true;

    public float CollisionRadius { get; set; } = 16f;

    public float DefaultColliderPenetrationResistance { get; set; } = 5f;

    public bool DirectionsMovementControls { get; set; } = false;
    public bool DirectionsHotkeysControls { get; set; } = false;

    public bool DisableAllAnimations { get; set; } = false;
    public bool DisableThirdPersonAnimations { get; set; } = false;

    public bool MeleeWeaponStopOnTerrainHit { get; set; } = false;

    public int GlobalAttackCooldownMs { get; set; } = 1000;

    public bool SecondChanceParticles { get; set; } = true;
}

public sealed class ArmorConfig
{
    public int MaxAttackTier { get; set; } = 9;
    public int MaxArmorTier { get; set; } = 24;
    public float[][] DamageReduction { get; set; } =
    [
        [0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.12f, 0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.10f, 0.12f, 0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f],
        [0.08f, 0.10f, 0.12f, 0.15f, 0.20f, 0.30f, 0.60f, 1.00f, 1.00f],
        [0.06f, 0.08f, 0.10f, 0.12f, 0.15f, 0.25f, 0.50f, 0.75f, 1.00f],
        [0.04f, 0.06f, 0.08f, 0.10f, 0.12f, 0.20f, 0.40f, 0.60f, 0.90f],
        [0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.15f, 0.30f, 0.50f, 0.80f],
        [0.01f, 0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.20f, 0.40f, 0.70f],
        [0.01f, 0.01f, 0.02f, 0.04f, 0.06f, 0.08f, 0.16f, 0.30f, 0.60f],
        [0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.06f, 0.12f, 0.25f, 0.50f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f, 0.12f, 0.25f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f, 0.12f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f]
    ];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class TogglePacket
{
    public string HotKeyCode { get; set; } = "";
}

public sealed class CombatOverhaulSystem : ModSystem
{
    public event Action? OnDispose;
    public event Action<Settings>? SettingsLoaded;
    public event Action<Settings>? SettingsChanged;

    public Settings Settings { get; set; } = new();
    public bool Disposed { get; private set; } = false;

    public override void StartPre(ICoreAPI api)
    {
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
    }

    public override void Start(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Client)
        {
            HarmonyPatches.ClientSettings = Settings;
            AnimationPatches.ClientSettings = Settings;
        }
        else
        {
            HarmonyPatches.ServerSettings = Settings;
            AnimationPatches.ServerSettings = Settings;
        }

        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ThirdPersonAnimations", typeof(ThirdPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:WearableStats", typeof(WearableStatsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:InInventory", typeof(InInventoryPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ArmorStandInventory", typeof(EntityBehaviorCOArmorStandInventory));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ProjectilePhysics", typeof(ProjectilePhysicsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:Stagger", typeof(StaggerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PositionBeforeFalling", typeof(PositionBeforeFallingBehavior));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Armor", typeof(ArmorBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:WearableWithStats", typeof(WearableWithStatsBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:GearEquipableBag", typeof(GearEquipableBag));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:ToolBag", typeof(ToolBag));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TextureFromAttributes", typeof(TextureFromAttributes));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TexturesFromAttributes", typeof(TexturesFromAttributes));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AdditionalSlots", typeof(AdditionalSlotsBehavior));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:Sling", typeof(SlingItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:StanceBasedMeleeWeapon", typeof(StanceBasedMeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:VanillaShield", typeof(VanillaShield));
        api.RegisterItemClass("CombatOverhaul:WearableArmor", typeof(ItemWearableArmor));
        api.RegisterItemClass("CombatOverhaul:WearableFueledLightSource", typeof(WearableFueledLightSource));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));

        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(GenericDisplayBlockEntity));
        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(GenericDisplayBlock));

        AiTaskRegistry.Register<AiTaskCOTurretMode>("CombatOverhaul:TurretMode");
        AiTaskRegistry.Register<StaggerAiTask>("CombatOverhaul:Stagger");

        new Harmony("CombatOverhaulAuto").PatchAll();

        InInventoryPlayerBehavior._reportedEntities.Clear();

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            SubscribeToConfigChange(api);
        }
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
        ServerSoundsSynchronizer = new(api);
        ServerMeleeSystem = new(api);
        ServerBlockSystem = new(api);
        ServerStatsSystem = new(api);
        ServerAttachmentSystem = new(api);
        ServerToolBagSystem = new(api);

        _serverToggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>()
            .SetMessageHandler<TogglePacket>(ToggleWearableItem);

    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;

        ClientProjectileSystem = new(api, api.ModLoader.GetModSystem<EntityPartitioning>());
        ActionListener = new(api);
        DirectionCursorRenderer = new(api, Settings);
        ReticleRenderer = new(api);
        DirectionController = new(api, DirectionCursorRenderer, Settings);
        ClientRangedWeaponSystem = new(api);
        ClientSoundsSynchronizer = new(api);
        AimingSystem = new(api, ReticleRenderer);
        ClientMeleeSystem = new(api);
        ClientBlockSystem = new(api);
        ClientStatsSystem = new(api);
        ClientAttachmentSystem = new(api);
        ClientToolBagSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        AimingPatches.Patch("CombatOverhaulAiming");
        MouseWheelPatch.Patch("CombatOverhaul", api);
        GuiDialogPatches.Patch("ovhlib", api);

        _clientToggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>();

        api.Input.RegisterHotKey("toggleWearableLight", "Toggle wearable light source", GlKeys.L);
        api.Input.SetHotKeyHandler("toggleWearableLight", _ => ToggleWearableItem(api.World.Player, "toggleWearableLight"));

        api.Input.RegisterHotKey("toggleTpAnimations", "Toggle CO third person animations", GlKeys.PageDown, ctrlPressed: true);
        api.Input.RegisterHotKey("toggleAllAnimations", "Toggle all CO animations", GlKeys.PageUp, ctrlPressed: true);

        api.Input.SetHotKeyHandler("toggleAllAnimations", _ =>
        {
            Settings.DisableAllAnimations = !Settings.DisableAllAnimations;
            if (Settings.DisableAllAnimations)
            {
                LoggerUtil.Notify(api, this, $"Animations disabled");
                api.TriggerIngameError(this, "animationsDisabled", "Overhaul lib animations are DISABLED");
            }
            else
            {
                LoggerUtil.Notify(api, this, $"Animations enabled");
                api.TriggerIngameError(this, "animationsDisabled", "Overhaul lib animations are ENABLED");
            }
            return true;
        });
        api.Input.SetHotKeyHandler("toggleTpAnimations", _ =>
        {
            Settings.DisableThirdPersonAnimations = !Settings.DisableThirdPersonAnimations;
            if (Settings.DisableThirdPersonAnimations)
            {
                LoggerUtil.Notify(api, this, $"Third person animations disabled");
                api.TriggerIngameError(this, "animationsDisabled", "Third person Overhaul lib animations are DISABLED");
            }
            else
            {
                Settings.DisableAllAnimations = false;
                LoggerUtil.Notify(api, this, $"All animations enabled");
                api.TriggerIngameError(this, "animationsDisabled", "All Overhaul lib animations are ENABLED");
            }
            return true;
        });
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;

        foreach (ArmorLayers layer in Enum.GetValues<ArmorLayers>())
        {
            foreach (DamageZone zone in Enum.GetValues<DamageZone>())
            {
                string iconPath = $"combatoverhaul:textures/gui/icons/armor-{layer}-{zone}.svg";
                string iconCode = $"combatoverhaul-armor-{layer}-{zone}";

                if (!clientApi.Assets.Exists(new AssetLocation(iconPath))) continue;

                RegisterCustomIcon(clientApi, iconCode, iconPath);
            }
        }

        List<IAsset> icons = clientApi.Assets.GetManyInCategory("textures", _iconsFolder, loadAsset: false);
        foreach (IAsset icon in icons)
        {
            string iconPath = icon.Location.ToString();
            string iconCode = icon.Location.Domain + ":" + icon.Location.Path[_iconsPath.Length..^4].ToLowerInvariant();

            if (!iconPath.ToLowerInvariant().EndsWith(".svg"))
            {
                LoggerUtil.Verbose(clientApi, this, $"Icon should have '.svg' format, skipping. Path: {iconPath}");
                return;
            }

            RegisterCustomIcon(clientApi, iconCode, iconPath);
        }
    }
    public override void AssetsFinalize(ICoreAPI api)
    {
        IAsset armorConfigAsset = api.Assets.Get("combatoverhaul:config/armor-config.json");
        JsonObject armorConfig = JsonObject.FromJson(armorConfigAsset.ToText());
        ArmorConfig armorConfigObj = armorConfig.AsObject<ArmorConfig>();

        DamageResistData.MaxAttackTier = armorConfigObj.MaxAttackTier;
        DamageResistData.MaxArmorTier = armorConfigObj.MaxArmorTier;
        DamageResistData.DamageReduction = armorConfigObj.DamageReduction;

        if (api is ICoreClientAPI clientApi)
        {
            DetermineSlotsStatus(clientApi);
        }
    }
    public override void Dispose()
    {
        if (Disposed) return;

        new Harmony("CombatOverhaulAuto").UnpatchAll();

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        _clientApi?.Event.UnregisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        AimingPatches.Unpatch("CombatOverhaulAiming");
        MouseWheelPatch.Unpatch("CombatOverhaul");
        GuiDialogPatches.Unpatch("ovhlib");

        OnDispose?.Invoke();

        _clientApi?.World.UnregisterGameTickListener(_cacheMissesReportedListener);

        Disposed = true;
    }

    public bool ToggleWearableItem(IPlayer player, string hotkeyCode)
    {
        IInventory? gearInventory = player.Entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;

        if (gearInventory == null) return false;

        bool toggled = false;
        foreach (ItemSlot slot in gearInventory)
        {
            if (slot?.Itemstack?.Collectible?.GetCollectibleInterface<ITogglableItem>() is ITogglableItem togglableItem && togglableItem.HotKeyCode == hotkeyCode)
            {
                togglableItem.Toggle(player, slot);
                toggled = true;
            }
        }

        if (player is IClientPlayer)
        {
            _clientToggleChannel?.SendPacket(new TogglePacket() { HotKeyCode = hotkeyCode });
        }

        return toggled;
    }
    public void ToggleWearableItem(IServerPlayer player, TogglePacket packet) => ToggleWearableItem(player, packet.HotKeyCode);

    public ProjectileSystemClient? ClientProjectileSystem { get; private set; }
    public ProjectileSystemServer? ServerProjectileSystem { get; private set; }
    public ActionListener? ActionListener { get; private set; }
    public DirectionCursorRenderer? DirectionCursorRenderer { get; private set; }
    public ReticleRenderer? ReticleRenderer { get; private set; }
    public ClientAimingSystem? AimingSystem { get; private set; }
    public DirectionController? DirectionController { get; private set; }
    public RangedWeaponSystemClient? ClientRangedWeaponSystem { get; private set; }
    public RangedWeaponSystemServer? ServerRangedWeaponSystem { get; private set; }
    public SoundsSynchronizerClient? ClientSoundsSynchronizer { get; private set; }
    public SoundsSynchronizerServer? ServerSoundsSynchronizer { get; private set; }
    public MeleeSystemClient? ClientMeleeSystem { get; private set; }
    public MeleeSystemServer? ServerMeleeSystem { get; private set; }
    public MeleeBlockSystemClient? ClientBlockSystem { get; private set; }
    public MeleeBlockSystemServer? ServerBlockSystem { get; private set; }
    public StatsSystemClient? ClientStatsSystem { get; private set; }
    public StatsSystemServer? ServerStatsSystem { get; private set; }
    public AttachableSystemClient? ClientAttachmentSystem { get; private set; }
    public AttachableSystemServer? ServerAttachmentSystem { get; private set; }
    public ToolBagSystemClient? ClientToolBagSystem { get; private set; }
    public ToolBagSystemServer? ServerToolBagSystem { get; private set; }

    private ICoreClientAPI? _clientApi;
    private readonly Vector4 _iconScale = new(-0.1f, -0.1f, 1.2f, 1.2f);
    private IClientNetworkChannel? _clientToggleChannel;
    private IServerNetworkChannel? _serverToggleChannel;
    private const string _iconsFolder = "sloticons";
    private const string _iconsPath = $"textures/{_iconsFolder}/";
    private long _cacheMissesReportedListener = 0;

    private void RegisterCustomIcon(ICoreClientAPI api, string key, string path)
    {
        api.Gui.Icons.CustomIcons[key] = delegate (Context ctx, int x, int y, float w, float h, double[] rgba)
        {
            AssetLocation location = new(path);
            IAsset svgAsset = api.Assets.TryGet(location);
            int value = ColorUtil.ColorFromRgba(75, 75, 75, 125);
            Surface target = ctx.GetTarget();

            int xNew = x + (int)(w * _iconScale.X);
            int yNew = y + (int)(h * _iconScale.Y);
            int wNew = (int)(w * _iconScale.W);
            int hNew = (int)(h * _iconScale.Z);

            api.Gui.DrawSvg(svgAsset, (ImageSurface)(object)((target is ImageSurface) ? target : null), xNew, yNew, wNew, hNew, value);
        };
    }

    private void SubscribeToConfigChange(ICoreAPI api)
    {
        ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, config, setting) =>
        {
            if (domain != "combatoverhaul" && domain != "bullseyecontinued") return;

            setting.AssignSettingValue(Settings);
            SettingsChanged?.Invoke(Settings);
        };

        system.ConfigsLoaded += () =>
        {
            system.GetConfig("combatoverhaul")?.AssignSettingsValues(Settings);
            system.GetConfig("bullseyecontinued")?.AssignSettingsValues(Settings);
            SettingsLoaded?.Invoke(Settings);
        };
    }

    private void DetermineSlotsStatus(ICoreClientAPI api)
    {
        foreach (Item? item in api.World.Items)
        {
            string? stackDressType = item?.Attributes?["clothescategory"].AsString() ?? item?.Attributes?["attachableToEntity"]["categoryCode"].AsString();
            string[]? stackDressTypes = item?.Attributes?["clothescategories"].AsObject<string[]>() ?? item?.Attributes?["attachableToEntity"]["categoryCodes"].AsObject<string[]>();

            if (stackDressType != null)
            {
                SetSlotsStatus(stackDressType);
            }

            if (stackDressTypes != null)
            {
                foreach (string gearType in stackDressTypes)
                {
                    SetSlotsStatus(gearType);
                }
            }
        }
    }

    private static void SetSlotsStatus(string gearType)
    {
        switch (gearType)
        {
            case "miscgear": GuiDialogPatches.SlotsStatus.Misc = true; break;
            case "headgear": GuiDialogPatches.SlotsStatus.Headgear = true; break;
            case "frontgear": GuiDialogPatches.SlotsStatus.FrontGear = true; break;
            case "backgear": GuiDialogPatches.SlotsStatus.BackGear = true; break;
            case "rightshouldergear": GuiDialogPatches.SlotsStatus.RightShoulderGear = true; break;
            case "leftshouldergear": GuiDialogPatches.SlotsStatus.LeftShoulderGear = true; break;
            case "waistgear": GuiDialogPatches.SlotsStatus.WaistHear = true; break;
            case "addBeltLeft": GuiDialogPatches.SlotsStatus.Belt = true; break;
            case "addBeltRight": GuiDialogPatches.SlotsStatus.Belt = true; break;
            case "addBeltBack": GuiDialogPatches.SlotsStatus.Belt = true; break;
            case "addBeltFront": GuiDialogPatches.SlotsStatus.Belt = true; break;
            case "addBackpack1": GuiDialogPatches.SlotsStatus.Backpack = true; break;
            case "addBackpack2": GuiDialogPatches.SlotsStatus.Backpack = true; break;
            case "addBackpack3": GuiDialogPatches.SlotsStatus.Backpack = true; break;
            case "addBackpack4": GuiDialogPatches.SlotsStatus.Backpack = true; break;
        }
    }
}

public sealed class CombatOverhaulAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }
    public DebugWindowManager? DebugManager { get; private set; }
    public ParticleEffectsManager? ParticleEffectsManager { get; private set; }
    public VanillaAnimationsSystemClient? ClientVanillaAnimations { get; private set; }
    public VanillaAnimationsSystemServer? ServerVanillaAnimations { get; private set; }
    public AnimationSystemClient? ClientTpAnimationSystem { get; private set; }
    public AnimationSystemServer? ServerTpAnimationSystem { get; private set; }

    public IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    public IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;

        HarmonyPatches.Patch("Overhaul lib", api);
        AnimationPatches.Patch("IgnoreThisPatchItHasNothingToDoWithYourCrash", api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        _ = LoadAnimatedItemShaders();
        ParticleEffectsManager = new(api);
        PlayerAnimationsManager = new(api, ParticleEffectsManager);
        DebugManager = new(api, ParticleEffectsManager);
        ClientVanillaAnimations = new(api);
        ClientTpAnimationSystem = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ParticleEffectsManager = new(api);
        ServerVanillaAnimations = new(api);
        ServerTpAnimationSystem = new(api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        PlayerAnimationsManager?.Load();
        if (api is ICoreClientAPI) DebugManager?.Load(api as ICoreClientAPI);
    }

    public override void Dispose()
    {
        HarmonyPatches.Unpatch("Overhaul lib", _api);
        AnimationPatches.Unpatch("IgnoreThisPatchItHasNothingToDoWithYourCrash", _api);

        if (_api is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
        }
    }


    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private ICoreAPI? _api;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

        _shaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        _shaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (_shaderProgram == null || _shaderProgramFirstPerson == null) return false;

        _shaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        _shaderProgram.Compile();

        _shaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        _shaderProgramFirstPerson.Compile();

        return true;
    }
}
