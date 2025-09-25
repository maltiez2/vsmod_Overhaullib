using HarmonyLib;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration.Transpilers;

internal static class MotionAndCollisionPatches
{
    [HarmonyPatch(typeof(EntityBehaviorPassivePhysics), "MotionAndCollision")]
    [HarmonyPatchCategory("combatoverhaul")]
    public class EntityBehaviorPassivePhysicsMotionAndCollisionPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = [.. instructions];

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_R8 && (double)code[i].operand == -0.014999999664723873)
                {
                    code[i].operand = 0.0;
                    return code;
                }
            }

            return code;
        }
    }
}
