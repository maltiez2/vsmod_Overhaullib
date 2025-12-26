using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace CombatOverhaul.Integration.Transpilers;

[HarmonyPatch]
[HarmonyPatchCategory("combatoverhaul")]
internal static class EidolonSlam_KnockbackMultiplierPatch
{
    public static float KnockbackMultiplier { get; set; } = 1.0f;

    static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(Vintagestory.GameContent.AiTaskEidolonSlam)
                .GetNestedType("<>c__DisplayClass20_0", BindingFlags.NonPublic),
            "<ContinueExecute>b__0"
        );
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = new(instructions);

        MethodInfo clampMethod = AccessTools.Method(
            typeof(Vintagestory.API.MathTools.GameMath),
            nameof(Vintagestory.API.MathTools.GameMath.Clamp),
            new[] { typeof(float), typeof(float), typeof(float) }
        );

        for (int i = 0; i < code.Count; i++)
        {
            // Look for:
            // num7 * GameMath.Clamp(...)
            // followed by stloc.s (num8)
            if (i + 1 < code.Count &&
                code[i].opcode == OpCodes.Mul &&
                code[i - 1].Calls(clampMethod) &&
                code[i + 1].IsStloc())
            {
                // original mul
                yield return code[i];

                // inject scaling
                yield return new CodeInstruction(OpCodes.Ldc_R4, KnockbackMultiplier);
                yield return new CodeInstruction(OpCodes.Mul);

                continue;
            }

            yield return code[i];
        }
    }
}



