using CombatOverhaul.Entities;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace CombatOverhaul.Integration.Transpilers;

[HarmonyPatch(typeof(Vintagestory.GameContent.EntityBehaviorTaskAI), "Initialize")]
[HarmonyPatchCategory("combatoverhaul")]
public static class EntityBehaviorTaskAIInitializePatch
{
    static readonly ConstructorInfo OriginalCtor = AccessTools.Constructor(
        typeof(Vintagestory.Essentials.WaypointsTraverser),
        [
                typeof(Vintagestory.API.Common.EntityAgent),
                typeof(Vintagestory.API.Common.EnumAICreatureType)
        ]);

    static readonly ConstructorInfo NewCtor = AccessTools.Constructor(
        typeof(COWaypointsTraverser),
        [
                typeof(Vintagestory.API.Common.EntityAgent),
                typeof(Vintagestory.API.Common.EnumAICreatureType)
        ]);

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instr in instructions)
        {
            if (instr.opcode == OpCodes.Newobj && instr.operand is ConstructorInfo ci)
            {
                if (ci == OriginalCtor)
                {
                    instr.operand = NewCtor;
                }
            }

            yield return instr;
        }
    }
}

