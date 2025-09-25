using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration.Transpilers;

internal static class CalculateMatricesPatches
{
    [HarmonyPatch(typeof(ClientAnimator), "calculateMatrices", typeof(int),
        typeof(float),
        typeof(List<ElementPose>),
        typeof(ShapeElementWeights[][]),
        typeof(float[]),
        typeof(List<ElementPose>[]),
        typeof(List<ElementPose>[]),
        typeof(int))]
    [HarmonyPatchCategory("combatoverhaul")]
    public class ClientAnimatorCalculateMatricesPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = [.. instructions];
            MethodInfo onFrameInvokeMethod = AccessTools.Method(typeof(AnimationPatches), "OnFrameInvoke");
            MethodInfo getLocalTransformMatrixMethod = AccessTools.Method(typeof(ShapeElement), "GetLocalTransformMatrix");

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(getLocalTransformMatrixMethod))
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0)); // Load own class onto the stack.
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldloc, 4)); // Load shape.
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, onFrameInvokeMethod));
                    break;
                }
            }

            return code;
        }
    }
}
