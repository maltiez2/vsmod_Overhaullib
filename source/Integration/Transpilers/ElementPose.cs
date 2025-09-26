using CombatOverhaul.Animations;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration.Transpilers;

public class ExtendedElementPose : ElementPose
{
    public ExtendedElementPose() : base() { }

    public EnumAnimatedElement ElementNameEnum { get; set; } = EnumAnimatedElement.Unknown;

    public int ElementNameHash { get; set; } = 0;

    public string ElementName
    {
        get => ElementNameEnum.ToString();

        set
        {
            EnumAnimatedElement newElementValue;
            if (!Enum.TryParse(value, out newElementValue))
            {
                ElementNameEnum = EnumAnimatedElement.Unknown;
            }
            else
            {
                ElementNameEnum = newElementValue;
            }
            ElementNameHash = value.GetHashCode();
        }
    }

    public EntityPlayer? Player { get; set; }
}

internal static class ElementPosePatches
{
    [HarmonyPatch(typeof(Vintagestory.API.Common.Animation), "GenerateFrame")]
    [HarmonyPatchCategory("combatoverhaul")]
    public static class Animation_GenerateFrame_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            ConstructorInfo elementPoseCtor = AccessTools.Constructor(typeof(ElementPose));
            ConstructorInfo extendedPoseCtor = AccessTools.Constructor(typeof(ExtendedElementPose));
            FieldInfo shapeElementName = AccessTools.Field(typeof(ShapeElement), nameof(ShapeElement.Name));
            MethodInfo setElementName = AccessTools.PropertySetter(typeof(ExtendedElementPose), nameof(ExtendedElementPose.ElementName));

            List<CodeInstruction> codes = new(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instr = codes[i];

                // Replace ElementPose constructor with ExtendedElementPose constructor
                if (instr.opcode == OpCodes.Newobj && instr.operand == elementPoseCtor)
                {
                    instr.operand = extendedPoseCtor;

                    // After storing into loc.3, inject code to set ElementName
                    // The sequence is: newobj -> stloc.3
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Stloc_3)
                    {
                        // Inject after stloc.3
                        List<CodeInstruction> injected = new()
                        {
                            new CodeInstruction(OpCodes.Ldloc_3),              // load ExtendedElementPose
                            new CodeInstruction(OpCodes.Ldloc_2),              // load ShapeElement
                            new CodeInstruction(OpCodes.Ldfld, shapeElementName), // ShapeElement.Name
                            new CodeInstruction(OpCodes.Callvirt, setElementName) // set_ElementName
                        };

                        codes.InsertRange(i + 2, injected);
                        i += injected.Count; // skip over inserted code
                    }
                }
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(ClientAnimator), "LoadPosesAndAttachmentPoints")]
    [HarmonyPatchCategory("combatoverhaul")]
    public static class ClientAnimator_LoadPosesAndAttachmentPoints_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);

            ConstructorInfo elementPoseCtor = AccessTools.Constructor(typeof(ElementPose));
            ConstructorInfo extendedPoseCtor = AccessTools.Constructor(typeof(ExtendedElementPose));
            FieldInfo shapeElementNameField = AccessTools.Field(typeof(ShapeElement), nameof(ShapeElement.Name));
            MethodInfo setElementName = AccessTools.PropertySetter(typeof(ExtendedElementPose), nameof(ExtendedElementPose.ElementName));

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instr = codes[i];

                // Replace "newobj ElementPose::.ctor()" with "newobj ExtendedElementPose::.ctor()"
                if (instr.opcode == OpCodes.Newobj && instr.operand == elementPoseCtor)
                {
                    instr.operand = extendedPoseCtor;

                    // We expect the IL shape: newobj, dup, stloc.0, callvirt List`1::Add
                    // We'll insert the element-name setter AFTER the Add call to avoid disturbing the evaluation stack.
                    // Find the index of the next callvirt Add instruction.
                    int insertIndex = -1;
                    for (int j = i + 1; j < codes.Count; j++)
                    {
                        CodeInstruction c = codes[j];
                        if (c.opcode == OpCodes.Callvirt && c.operand is System.Reflection.MethodInfo mi &&
                            mi.Name == "Add" && mi.DeclaringType.IsGenericType &&
                            mi.DeclaringType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            insertIndex = j + 1; // insert after this instruction
                            break;
                        }
                        // Safety: stop searching if we hit an unrelated newobj/new or ret etc. (optional)
                    }

                    if (insertIndex != -1)
                    {
                        List<CodeInstruction> injected = new()
                        {
                            // load the cached pose local (local index 0)
                            new CodeInstruction(OpCodes.Ldloc_0),
                            // load the current element local (local index 2)
                            new CodeInstruction(OpCodes.Ldloc_2),
                            // load ShapeElement.Name
                            new CodeInstruction(OpCodes.Ldfld, shapeElementNameField),
                            // callvirt ExtendedElementPose.set_ElementName(string)
                            new CodeInstruction(OpCodes.Callvirt, setElementName)
                        };

                        codes.InsertRange(insertIndex, injected);
                        i = insertIndex + injected.Count - 1; // move i past injected region
                    }
                }
            }

            return codes;
        }
    }
}
