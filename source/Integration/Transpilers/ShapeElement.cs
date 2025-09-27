using CombatOverhaul.Animations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration.Transpilers;

public sealed class ExtendedShapeElement : ShapeElement
{
    public EnumAnimatedElement NameEnum { get; set; } = EnumAnimatedElement.Unknown;

    public int NameHash { get; set; } = 0;

    public ExtendedShapeElement() : base()
    {

    }

    public ExtendedShapeElement(ShapeElement previous) : base()
    {
        AttachmentPoints = (AttachmentPoint[]?)previous.AttachmentPoints?.Clone();
        FacesResolved = (ShapeElementFace[]?)previous.FacesResolved?.Clone();
        From = (double[]?)previous.From?.Clone();
        To = (double[]?)previous.To?.Clone();
        inverseModelTransform = (float[]?)previous.inverseModelTransform?.Clone();
        JointId = previous.JointId;
        RenderPass = previous.RenderPass;
        RotationX = previous.RotationX;
        RotationY = previous.RotationY;
        RotationZ = previous.RotationZ;
        RotationOrigin = (double[]?)previous.RotationOrigin?.Clone();
        SeasonColorMap = previous.SeasonColorMap;
        ClimateColorMap = previous.ClimateColorMap;
        StepParentName = previous.StepParentName;
        Shade = previous.Shade;
        DisableRandomDrawOffset = previous.DisableRandomDrawOffset;
        ZOffset = previous.ZOffset;
        GradientShade = previous.GradientShade;
        ScaleX = previous.ScaleX;
        ScaleY = previous.ScaleY;
        ScaleZ = previous.ScaleZ;
        Name = previous.Name;

        ResolveName();
    }

    public void ResolveName()
    {
        EnumAnimatedElement newElementValue;
        if (!Enum.TryParse(Name, out newElementValue))
        {
            NameEnum = EnumAnimatedElement.Unknown;
        }
        else
        {
            NameEnum = newElementValue;
        }
        NameHash = Name.GetHashCode();
    }

    public void ResolveReferences()
    {
        _shapeElement_ResolveRefernces?.Invoke(this, []);
    }

    public static bool ResolveReferncesPrefix(ShapeElement __instance)
    {
        /*if (__instance.Children != null)
        {
            for (int i = 0; i < __instance.Children.Length; i++)
            {
                if (__instance.Children[i] is not ExtendedShapeElement)
                {
                    __instance.Children[i] = new ExtendedShapeElement(__instance.Children[i]);
                }
                
                ExtendedShapeElement? child = __instance.Children[i] as ExtendedShapeElement;
                Debug.Assert(child != null, "Error on processing 'ExtendedShapeElement' in 'ResolveRefernces'");
                child.ParentElement = __instance;
                child.ResolveReferences();
            }
        }

        if (__instance.AttachmentPoints != null)
        {
            for (int i = 0; i < __instance.AttachmentPoints.Length; i++)
            {
                __instance.AttachmentPoints[i].ParentElement = __instance;
            }
        }*/

        return true;
    }

    public static bool CollectElements(ShapeElement[]? elements, IDictionary<string, ShapeElement> elementsByName)
    {
        if (elements == null) return false;

        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] is not ExtendedShapeElement)
            {
                ExtendedShapeElement newElement = new(elements[i]);
                newElement.ResolveName();
                elements[i] = newElement;
            }

            ShapeElement elem = elements[i];

            elementsByName[elem.Name] = elem;

            CollectElements(elem.Children, elementsByName);
        }

        return false;
    }

    public static void ResolveReferences(Shape __instance)
    {

    }

    private static MethodInfo? _shapeElement_ResolveRefernces = typeof(ShapeElement).GetMethod("ResolveRefernces", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
}

internal static class ShapeElementPatches
{
    /*[HarmonyPatch(typeof(ShapeElement), nameof(ShapeElement.Clone))]
    [HarmonyPatchCategory("combatoverhaul")]
    internal static class ShapeElement_Clone_Transpiler
    {
        private static readonly ConstructorInfo Ctor_Shape
            = AccessTools.Constructor(typeof(ShapeElement), Type.EmptyTypes);

        private static readonly ConstructorInfo Ctor_Ext
            = AccessTools.Constructor(typeof(ExtendedShapeElement), Type.EmptyTypes);

        // If ResolveName() is on ShapeElement, change to typeof(ShapeElement)
        private static readonly MethodInfo MI_ResolveName
            = AccessTools.Method(typeof(ExtendedShapeElement), nameof(ExtendedShapeElement.ResolveName));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            // 1) Replace "newobj ShapeElement::.ctor" with "newobj ExtendedShapeElement::.ctor"
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Newobj && code[i].operand is ConstructorInfo ci && ci == Ctor_Shape)
                {
                    code[i].operand = Ctor_Ext;
                    break;
                }
            }

            // 2) Inject "((ExtendedShapeElement)elem).ResolveName();" right before the method returns
            //    We expect pattern "ldloc.0; ret" at the end. We insert the call before ldloc.0 to keep stack balanced.
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ret)
                {
                    int idxLdloc0 = i - 1; // should be the final "ldloc.0" that loads the return value
                    code.InsertRange(idxLdloc0, new[]
                    {
                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Castclass, typeof(ExtendedShapeElement)),
                        new CodeInstruction(OpCodes.Callvirt, MI_ResolveName)
                    });
                    break;
                }
            }

            return code;
        }
    }*/

    /*[HarmonyPatch(typeof(ShapeElement), "ResolveRefernces")]
    internal static class ShapeElement_ResolveReferences_Transpiler
    {
        private static readonly ConstructorInfo Ctor_ExtFromBase =
            AccessTools.Constructor(typeof(ExtendedShapeElement), new[] { typeof(ShapeElement) });

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            bool patched = false;

            // Look for the child iteration: ldelem.ref followed by dup
            // Inject: newobj ExtendedShapeElement(ShapeElement) right after ldelem.ref
            for (int i = 0; i < code.Count - 1; i++)
            {
                if (code[i].opcode == OpCodes.Ldelem_Ref && code[i + 1].opcode == OpCodes.Dup)
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Newobj, Ctor_ExtFromBase));
                    patched = true;
                    break;
                }
            }

            // Optional: you could log if !patched

            return code;
        }
    }*/

    /*[HarmonyPatch(typeof(ShapeElement), "ResolveRefernces")] // keep the exact misspelling
    internal static class ShapeElement_ResolveRefernces_Transpiler
    {
        private static readonly ConstructorInfo Ctor_ExtFromBase =
            AccessTools.Constructor(typeof(ExtendedShapeElement), new[] { typeof(ShapeElement) });

        private static readonly FieldInfo FI_ParentElement =
            AccessTools.Field(typeof(ShapeElement), "ParentElement");

        private static readonly MethodInfo MI_ResolveRefs =
            AccessTools.Method(typeof(ShapeElement), "ResolveRefernces");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instructions);
            bool patched = false;

            // Find the children loop body by this sequence:
            //   ldloc.* (children)
            //   ldloc.* (i)
            //   ldelem.ref
            //   dup
            //   ldarg.0
            //   stfld ShapeElement::ParentElement
            //   callvirt instance void ShapeElement::ResolveRefernces()
            int bodyStart = -1;
            int ldElemIndex = -1;
            for (int i = 2; i < code.Count - 5; i++)
            {
                if (code[i].opcode == OpCodes.Ldelem_Ref &&
                    code[i + 1].opcode == OpCodes.Dup &&
                    code[i + 2].opcode == OpCodes.Ldarg_0 &&
                    code[i + 3].opcode == OpCodes.Stfld && Equals(code[i + 3].operand, FI_ParentElement) &&
                    code[i + 4].opcode == OpCodes.Callvirt && Equals(code[i + 4].operand, MI_ResolveRefs))
                {
                    bodyStart = i - 2; // first load of children for children[i]
                    ldElemIndex = i;
                    break;
                }
            }
            if (bodyStart < 0)
                return code; // pattern not found; do nothing

            // Capture the load instructions for 'children' and 'i' used at the loop body start
            var ldArr = code[bodyStart];       // ldloc.* (children)
            var ldIdx = code[bodyStart + 1];   // ldloc.* (i)

            // Copy labels at loop body start (the loop head target)
            var bodyLabels = new List<Label>(code[bodyStart].labels);

            // Find the loop's blt.* that targets the loop head
            int bltIndex = -1;
            for (int i = bodyStart + 1; i < code.Count; i++)
            {
                var op = code[i].opcode;
                if ((op == OpCodes.Blt || op == OpCodes.Blt_S) && code[i].operand is Label target)
                {
                    if (bodyLabels.Contains(target))
                    {
                        bltIndex = i;
                        break;
                    }
                }
            }
            if (bltIndex < 0)
                return code; // safety

            int afterLoopIndex = bltIndex + 1; // first instruction after original loop (ldarg.0 ldfld AttachmentPoints ...)

            // Helper to create matching STLOC for a given LDLOC
            static CodeInstruction MakeStlocFromLdloc(CodeInstruction ld)
            {
                var op = ld.opcode;
                if (op == OpCodes.Ldloc_0) return new CodeInstruction(OpCodes.Stloc_0);
                if (op == OpCodes.Ldloc_1) return new CodeInstruction(OpCodes.Stloc_1);
                if (op == OpCodes.Ldloc_2) return new CodeInstruction(OpCodes.Stloc_2);
                if (op == OpCodes.Ldloc_3) return new CodeInstruction(OpCodes.Stloc_3);
                if (op == OpCodes.Ldloc_S || op == OpCodes.Ldloc)
                    return new CodeInstruction(OpCodes.Stloc_S, ld.operand);
                // Fallback (rare): try Stloc with same operand
                return new CodeInstruction(OpCodes.Stloc, ld.operand);
            }

            // 1) Insert a "break" at the start of the original loop body
            var breakLabel = il.DefineLabel();
            var breakInstr = new CodeInstruction(OpCodes.Br, breakLabel);

            // Move loop-head labels from the original first body instruction onto our break instruction
            if (code[bodyStart].labels.Count > 0)
            {
                breakInstr.labels.AddRange(code[bodyStart].labels);
                code[bodyStart].labels.Clear();
            }

            // Insert the break at the body start
            code.Insert(bodyStart, breakInstr);

            // Inserting before bodyStart shifts indices >= bodyStart by +1
            if (afterLoopIndex >= bodyStart) afterLoopIndex++;

            // 2) Inject the new loop right after the original one (but keep the original null-check semantics)
            // Guard: if (children == null) skip our loop
            var afterNewLoopLabel = il.DefineLabel();
            var loopStartLabel = il.DefineLabel();

            // Build injected block:
            var injected = new List<CodeInstruction>();

            // breakLabel (from step 1) should target the first instruction of our injected block
            // We'll attach it to injected[0] after we create it.

            // if (children == null) goto afterNewLoop;
            injected.Add(new CodeInstruction(ldArr.opcode, ldArr.operand));     // ldloc children
            // brfalse.s afterNewLoop
            injected.Add(new CodeInstruction(OpCodes.Brfalse_S, afterNewLoopLabel));

            // i = 0;
            injected.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            injected.Add(MakeStlocFromLdloc(ldIdx));                             // stloc i

            // loopStart:
            // children[i] = new ExtendedShapeElement(children[i]);
            var loopStartIndexInInjected = injected.Count; // mark where to attach loopStartLabel
            injected.Add(new CodeInstruction(ldArr.opcode, ldArr.operand));      // ldloc children
            injected.Add(new CodeInstruction(ldIdx.opcode, ldIdx.operand));      // ldloc i
            injected.Add(new CodeInstruction(ldArr.opcode, ldArr.operand));      // ldloc children
            injected.Add(new CodeInstruction(ldIdx.opcode, ldIdx.operand));      // ldloc i
            injected.Add(new CodeInstruction(OpCodes.Ldelem_Ref));               // children[i]
            injected.Add(new CodeInstruction(OpCodes.Newobj, Ctor_ExtFromBase)); // new ExtendedShapeElement(child)
            injected.Add(new CodeInstruction(OpCodes.Stelem_Ref));               // children[i] = ...

            // children[i].ParentElement = this;
            injected.Add(new CodeInstruction(ldArr.opcode, ldArr.operand));      // ldloc children
            injected.Add(new CodeInstruction(ldIdx.opcode, ldIdx.operand));      // ldloc i
            injected.Add(new CodeInstruction(OpCodes.Ldelem_Ref));               // children[i]
            injected.Add(new CodeInstruction(OpCodes.Dup));                      // dup
            injected.Add(new CodeInstruction(OpCodes.Ldarg_0));                  // this
            injected.Add(new CodeInstruction(OpCodes.Stfld, FI_ParentElement));  // .ParentElement = this

            // children[i].ResolveRefernces();
            injected.Add(new CodeInstruction(OpCodes.Callvirt, MI_ResolveRefs)); // callvirt

            // i++
            injected.Add(new CodeInstruction(ldIdx.opcode, ldIdx.operand));      // ldloc i
            injected.Add(new CodeInstruction(OpCodes.Ldc_I4_1));                 // 1
            injected.Add(new CodeInstruction(OpCodes.Add));                      // add
            injected.Add(MakeStlocFromLdloc(ldIdx));                             // stloc i

            // if (i < children.Length) goto loopStart;
            injected.Add(new CodeInstruction(ldIdx.opcode, ldIdx.operand));      // ldloc i
            injected.Add(new CodeInstruction(ldArr.opcode, ldArr.operand));      // ldloc children
            injected.Add(new CodeInstruction(OpCodes.Ldlen));                    // ldlen
            injected.Add(new CodeInstruction(OpCodes.Conv_I4));                  // conv.i4
            injected.Add(new CodeInstruction(OpCodes.Blt_S, loopStartLabel));    // blt.s loopStart

            // Attach labels
            // - Attach loopStartLabel to the first instruction of loop body (children load)
            injected[loopStartIndexInInjected].labels.Add(loopStartLabel);
            // - Attach breakLabel (the target of our inserted 'break') to the first instruction of injected block
            injected[0].labels.Add(breakLabel);

            // Insert our injected loop at 'afterLoopIndex'
            code.InsertRange(afterLoopIndex, injected);

            // We need to mark the instruction after our injected block as 'afterNewLoopLabel'
            int afterInjected = afterLoopIndex + injected.Count;
            if (afterInjected < code.Count)
            {
                code[afterInjected].labels.Add(afterNewLoopLabel);
            }
            else
            {
                // In the unlikely case injection is at end, add a no-op label target
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(afterNewLoopLabel);
                code.Add(nop);
            }

            patched = true;
            return code;
        }
    }*/
}