using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Armor;

public interface IHasSlotBackpackCategory
{
    string BackpackCategoryCode { get; }
    float OrderPriority { get; }
}

public class SlotConfigJson
{
    public string[] CanHoldItemTags { get; set; } = [];
    public string[] CanHoldBlockTags { get; set; } = [];
    public string[][] CanHoldItemTagsCondition { get; set; } = [];
    public string[][] CanHoldBlockTagsCondition { get; set; } = [];
    public string[] CanHoldWildcards { get; set; } = [];
    public string? SlotColor { get; set; } = null;
    public string? SlotsIcon { get; set; } = null;
    public string SlotBackpackCategory { get; set; } = "";
    public float CategoryOrderPriority { get; set; } = 1f;
    public int SlotsNumber { get; set; } = 0;

    public string SlotVariant { get; set; } = "bag_slot";
    public string SlotStateVariant { get; set; } = "bag_slot_state";
    public string EmptyStateCode { get; set; } = "empty";
    public string FullStateCode { get; set; } = "full";
    public string SlotMetalVariant { get; set; } = "bag_slot_metal";
    public string SlotLeatherVariant { get; set; } = "bag_slot_leather";
    public string SlotWoodVariant { get; set; } = "bag_slot_wood";

    public bool SetVariants { get; set; } = false;
    public bool SetMaterialVariants { get; set; } = false;

    public SlotConfig ToConfig()
    {
        if (CanHoldItemTagsCondition.Length == 0 && CanHoldItemTags.Length != 0)
        {
            CanHoldItemTagsCondition = [CanHoldItemTags];
        }

        if (CanHoldBlockTagsCondition.Length == 0 && CanHoldBlockTags.Length != 0)
        {
            CanHoldBlockTagsCondition = [CanHoldBlockTags];
        }

        return new SlotConfig(CanHoldItemTagsCondition, CanHoldBlockTagsCondition)
        {
            CanHoldWildcards = CanHoldWildcards,
            SlotColor = SlotColor,
            SlotsIcon = SlotsIcon,
            BackpackCategoryCode = SlotBackpackCategory,
            OrderPriority = CategoryOrderPriority,
            SlotsNumber = SlotsNumber,
            SlotVariant = SlotVariant,
            SlotStateVariant = SlotStateVariant,
            EmptyStateCode = EmptyStateCode,
            FullStateCode = FullStateCode,
            SlotMetalVariant = SlotMetalVariant,
            SlotLeatherVariant = SlotLeatherVariant,
            SlotWoodVariant = SlotWoodVariant,
            SetVariants = SetVariants,
            SetMaterialVariants = SetMaterialVariants
        };
    }
}

public class SlotConfig : IHasSlotBackpackCategory
{
    public ItemTagRule[] CanHoldItemTags { get; set; } = [];
    public BlockTagRule[] CanHoldBlockTags { get; set; } = [];
    public string[] CanHoldWildcards { get; set; } = [];
    public string? SlotColor { get; set; } = null;
    public string? SlotsIcon { get; set; } = null;
    public string BackpackCategoryCode { get; set; } = "";
    public float OrderPriority { get; set; } = 1f;
    public int SlotsNumber { get; set; } = 0;

    public string SlotVariant { get; set; } = "bag_slot";
    public string SlotStateVariant { get; set; } = "bag_slot_state";
    public string EmptyStateCode { get; set; } = "empty";
    public string FullStateCode { get; set; } = "full";
    public string SlotMetalVariant { get; set; } = "bag_slot_metal";
    public string SlotLeatherVariant { get; set; } = "bag_slot_leather";
    public string SlotWoodVariant { get; set; } = "bag_slot_wood";

    public bool SetVariants { get; set; } = false;
    public bool SetMaterialVariants { get; set; } = false;

    protected string[][] CanHoldItemTagsNames { get; set; }
    protected string[][] CanHoldBlockTagsNames { get; set; }
    protected bool Resolved { get; set; } = false;

    public SlotConfig(string[][] itemTags, string[][] blockTags)
    {
        CanHoldItemTagsNames = itemTags;
        CanHoldBlockTagsNames = blockTags;
    }

    public void Resolve(ICoreAPI api)
    {
        if (Resolved) return;
        Resolved = true;

        CanHoldItemTags = CanHoldItemTagsNames
            .Select(tags => new ItemTagRule(api, tags))
            .Where(tags => tags != ItemTagRule.Empty)
            .ToArray();
        CanHoldBlockTags = CanHoldBlockTagsNames
            .Select(tags => new BlockTagRule(api, tags))
            .Where(tags => tags != BlockTagRule.Empty)
            .ToArray();

        CanHoldItemTagsNames = [];
        CanHoldBlockTagsNames = [];
    }
}
