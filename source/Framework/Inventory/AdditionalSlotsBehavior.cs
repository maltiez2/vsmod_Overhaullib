using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Armor;

public interface IEnableAdditionalSlots
{
    bool GetIfEnabled(ItemStack stack, IInventory inventory, string slotType);
    string? GetIcon(ItemStack stack, IInventory inventory, string slotType);
    SlotConfig? GetConfig(ItemStack stack, IInventory inventory, string slotType);
}

public class AdditionalSlotsConfig
{
    public string[] EnabledSlots { get; set; } = [];
    public Dictionary<string, string> Icons { get; set; } = [];
    public Dictionary<string, SlotConfigJson> Configs { get; set; } = [];
}

public class AdditionalSlotsBehavior : CollectibleBehavior, IEnableAdditionalSlots
{
    public AdditionalSlotsConfig Config { get; set; } = new();

    public Dictionary<string, SlotConfig> SlotConfigs { get; set; } = new();

    public AdditionalSlotsBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Config = properties.AsObject<AdditionalSlotsConfig>();
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        foreach ((string code, SlotConfigJson configFromJson) in Config.Configs)
        {
            SlotConfig config = configFromJson.ToConfig();
            config.Resolve(api);
            SlotConfigs.Add(code, config);
        }

        Config.Configs = [];
    }

    public SlotConfig? GetConfig(ItemStack stack, IInventory inventory, string slotType) => SlotConfigs.ContainsKey(slotType) ? SlotConfigs[slotType] : null;
    public string? GetIcon(ItemStack stack, IInventory inventory, string slotType) => Config.Icons.ContainsKey(slotType) ? Config.Icons[slotType] : null;
    public bool GetIfEnabled(ItemStack stack, IInventory inventory, string slotType) => Config.EnabledSlots.Contains(slotType);
}
