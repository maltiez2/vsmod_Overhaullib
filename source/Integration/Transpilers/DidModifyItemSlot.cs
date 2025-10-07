using CombatOverhaul.Utils;
using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration.Transpilers;

internal static class DidModifyItemSlotPatches
{
    private static void OnInvalidSlot(InventoryBase inventory, ItemSlot slot)
    {
        LoggerUtil.Verbose(inventory.Api, typeof(DidModifyItemSlotPatches), $"Supplied slot is not part of this inventory ({inventory.InventoryID})");
        Debug.WriteLine($"Supplied slot is not part of this inventory ({inventory.InventoryID})");
    }

    [HarmonyPatch(typeof(InventoryBase), "DidModifyItemSlot")]
    [HarmonyPatchCategory("combatoverhaul")]
    public static class InventoryBase_DidModifyItemSlot_Transpiler
    {
        // Transpiler signature commonly used by Harmony
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);

            // Get reflection info we will compare against / call
            ConstructorInfo? argExceptionCtor = typeof(ArgumentException).GetConstructor([typeof(string)]);
            MethodInfo? stringFormatMethod = typeof(string).GetMethod("Format", [typeof(string), typeof(object)]);
            MethodInfo getInventoryIdMethod = AccessTools.PropertyGetter(typeof(InventoryBase), "InventoryID");
            MethodInfo helperMethod = AccessTools.Method(typeof(DidModifyItemSlotPatches), nameof(DidModifyItemSlotPatches.OnInvalidSlot));

            // The literal IL string from the method (from ILSpy)
            const string expectedLiteral = "Supplied slot is not part of this inventory ({0})!";

            // Iterate and look for the pattern:
            //   ldstr "Supplied slot is not part of this inventory ({0})!"
            //   ldarg.0
            //   callvirt get_InventoryID
            //   call string.Format(string, object)
            //   newobj instance void System.ArgumentException::.ctor(string)
            //   throw
            //
            // If found, replace that entire range with:
            //   ldarg.0
            //   ldarg.1
            //   call InventoryPatchesHelper.OnInvalidSlot(InventoryBase, ItemSlot)
            //   ret

            for (int i = 0; i < codes.Count; i++)
            {
                bool matched = false;

                // Make sure we have enough remaining instructions to match the sequence
                if (i + 5 < codes.Count)
                {
                    CodeInstruction c0 = codes[i];
                    CodeInstruction c1 = codes[i + 1];
                    CodeInstruction c2 = codes[i + 2];
                    CodeInstruction c3 = codes[i + 3];
                    CodeInstruction c4 = codes[i + 4];
                    CodeInstruction c5 = codes[i + 5];

                    bool isLdstr = c0.opcode == OpCodes.Ldstr && c0.operand is string s && s == expectedLiteral;
                    bool isLdarg0 = (c1.opcode == OpCodes.Ldarg_0) || (c1.opcode == OpCodes.Ldarg && (int)c1.operand == 0);
                    bool isCallGetInventory = (c2.opcode == OpCodes.Call || c2.opcode == OpCodes.Callvirt) && c2.operand is MethodInfo m2 && m2 == getInventoryIdMethod;
                    bool isCallStringFormat = (c3.opcode == OpCodes.Call || c3.opcode == OpCodes.Callvirt) && c3.operand is MethodInfo m3 && m3 == stringFormatMethod;
                    bool isNewobjArgEx = c4.opcode == OpCodes.Newobj && c4.operand is ConstructorInfo ctor && ctor == argExceptionCtor;
                    bool isThrow = c5.opcode == OpCodes.Throw;

                    if (isLdstr && isLdarg0 && isCallGetInventory && isCallStringFormat && isNewobjArgEx && isThrow)
                    {
                        // emit replacement instructions
                        // ldarg.0
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        // ldarg.1
                        // use short form if possible
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        // call helper
                        yield return new CodeInstruction(OpCodes.Call, helperMethod);
                        // ret
                        yield return new CodeInstruction(OpCodes.Ret);

                        // skip the matched instructions
                        i += 5; // loop will increment further
                        matched = true;
                    }
                }

                if (!matched)
                {
                    // otherwise pass-through original instruction
                    yield return codes[i];
                }
            }
        }
    }
}
