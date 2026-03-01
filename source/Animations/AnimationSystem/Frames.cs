using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.Utils;
using ImGuiNET;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

public enum EnumAnimatedElement
{
    Unknown = 0,
    Custom = -1,
    HeldItem = -2,
    DetachedAnchor = 1,
    UpperTorso,
    LowerTorso,
    Neck,
    Head,
    UpperFootR,
    UpperFootL,
    LowerFootR,
    LowerFootL,
    ItemAnchor,
    LowerArmR,
    UpperArmR,
    ItemAnchorL,
    LowerArmL,
    UpperArmL,
}

public readonly struct PlayerItemFrame
{
    public readonly PlayerFrame Player;
    public readonly ItemFrame? Item;
    public readonly bool DetachedAnchor;
    public readonly bool SwitchArms;

    public PlayerItemFrame(PlayerFrame player, ItemFrame? item)
    {
        Player = player;
        Item = item;
        DetachedAnchor = player.DetachedAnchor;
        SwitchArms = player.SwitchArms;
    }

    public static readonly PlayerItemFrame Zero = new(PlayerFrame.Zero, null);
    public static readonly PlayerItemFrame Empty = new(PlayerFrame.Empty, null);

    public void Apply(ElementPose pose, EnumAnimatedElement element, Vector3 eyePosition, float eyeHeight, float cameraPitch = 0, bool applyCameraPitch = false, bool overrideTorso = true)
    {
        switch (element)
        {
            case EnumAnimatedElement.Unknown:
                Item?.Apply(pose);
                break;
            default:
                Player.Apply(pose, element, eyePosition, eyeHeight, cameraPitch, applyCameraPitch, overrideTorso);
                break;
        }
    }

    public void Apply(ExtendedElementPose pose, EnumAnimatedElement element, Vector3 eyePosition, float eyeHeight, float cameraPitch = 0, bool applyCameraPitch = false, bool overrideTorso = true)
    {
        switch (element)
        {
            case EnumAnimatedElement.Unknown:
                Item?.Apply(pose);
                break;
            default:
                Player.Apply(pose, element, eyePosition, eyeHeight, cameraPitch, applyCameraPitch, overrideTorso);
                break;
        }
    }

    public static PlayerItemFrame Compose(IEnumerable<(PlayerItemFrame element, float weight)> frames)
    {
        PlayerFrame player = PlayerFrame.Compose(frames.Select(entry => (entry.element.Player, entry.weight)));
        ItemFrame item = ItemFrame.Compose(frames
            .Where(entry => entry.element.Item != null)
            .Select(entry => (entry.element.Item.Value, entry.weight))
            );

        return new(player, item);
    }
}

public readonly struct SoundFrame
{
    public readonly string[] Code;
    public readonly float DurationFraction;
    public readonly bool RandomizePitch;
    public readonly float Range;
    public readonly float Volume;
    public readonly bool Synchronize;

    public SoundFrame(string[] code, float durationFraction, bool randomizePitch = false, float range = 32, float volume = 1, bool synchronize = true)
    {
        Code = code;
        DurationFraction = durationFraction;
        RandomizePitch = randomizePitch;
        Range = range;
        Volume = volume;
        Synchronize = synchronize;
    }

#if DEBUG
    public SoundFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code.Aggregate((first, second) => $"{first},{second}") ?? "";
        ImGui.InputText($"Sound code##{title}", ref code, 300);
        string[] codes = code.Split(',');

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        bool pitch = RandomizePitch;
        ImGui.Checkbox($"Randomize pitch##{title}", ref pitch);

        float range = Range;
        ImGui.InputFloat($"Range##{title}", ref range);

        float volume = Volume;
        ImGui.SliderFloat($"Volume##{title}", ref volume, 0, 1);

        bool sync = Synchronize;
        ImGui.Checkbox($"Synchronize##{title}", ref sync);

        return new(codes, time / (float)totalDuration.TotalMilliseconds, pitch, range, volume, sync);
    }
#endif
}

public readonly struct ParticlesFrame
{
    public readonly string Code;
    public readonly float DurationFraction;
    public readonly Vector3 Position;
    public readonly Vector3 Velocity;
    public readonly float Intensity;

    public ParticlesFrame(string code, float durationFraction, Vector3 position, Vector3 velocity, float intensity)
    {
        Code = code;
        DurationFraction = durationFraction;
        Position = position;
        Velocity = velocity;
        Intensity = intensity;
    }

#if DEBUG
    public ParticlesFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code;
        ImGui.InputText($"Effect code##{title}", ref code, 300);

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        System.Numerics.Vector3 position = Position.ToSystem();
        ImGui.DragFloat3($"Position ##{title}", ref position);

        System.Numerics.Vector3 velocity = Velocity.ToSystem();
        ImGui.DragFloat3($"Velocity ##{title}", ref velocity);

        float intensity = Intensity;
        ImGui.InputFloat($"Intensity ##{title}", ref intensity, 0.1f, 1);

        return new(code, time / (float)totalDuration.TotalMilliseconds, position.ToOpenTK(), velocity.ToOpenTK(), intensity);
    }
#endif
}

public readonly struct CallbackFrame
{
    public readonly string Code;
    public readonly float DurationFraction;

    public CallbackFrame(string code, float durationFraction)
    {
        Code = code;
        DurationFraction = durationFraction;
    }

#if DEBUG
    public CallbackFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code;
        ImGui.InputText($"Effect code##{title}", ref code, 300);

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        return new(code, time / (float)totalDuration.TotalMilliseconds);
    }
#endif
}

public readonly struct ItemKeyFrame
{
    public readonly ItemFrame Frame;
    public readonly float DurationFraction;
    public readonly EasingFunctionType EasingFunction;

    public ItemKeyFrame(ItemFrame frame, float durationFraction, EasingFunctionType easeFunction)
    {
        Frame = frame;
        DurationFraction = durationFraction;
        EasingFunction = easeFunction;
    }

    public static readonly ItemKeyFrame Empty = new(ItemFrame.Empty, 0, EasingFunctionType.Linear);

    public ItemFrame Interpolate(ItemFrame frame, float frameProgress)
    {
        float interpolatedProgress = EasingFunctions.Get(EasingFunction).Invoke((float)frameProgress);
        return ItemFrame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(float animationProgress) => animationProgress >= DurationFraction;

#if DEBUG
    public ItemKeyFrame Edit(string title, TimeSpan totalDuration, TimeSpan startDuration)
    {
        float total = (float)totalDuration.TotalMilliseconds;

        if (total < 1E-8) return this;

        float progress = (float)startDuration.TotalMilliseconds + DurationFraction * total;
        ImGui.InputFloat($"Time", ref progress, 0, 1);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingFunction);

        ItemFrame frame = Frame.Edit(title);

        return new(frame, Math.Clamp((progress - (float)startDuration.TotalMilliseconds) / total, 0, 1), function);
    }
#endif

    public static List<ItemKeyFrame> FromVanillaAnimation(string code, Shape shape)
    {
        AnimationKeyFrame[] vanillaKeyFrames = GetVanillaKeyFrames(code, shape);
        bool missingFirstFrame = vanillaKeyFrames[0].Frame != 0;
        int keyFramesCount = !missingFirstFrame ? vanillaKeyFrames.Length : vanillaKeyFrames.Length + 1;

        HashSet<string> elements = new();
        foreach (AnimationKeyFrame keyFrame in vanillaKeyFrames)
        {
            foreach ((string element, _) in keyFrame.Elements)
            {
                elements.Add(element);
            }
        }

        Dictionary<string, List<AnimationElement>> frames = new();
        foreach (string element in elements)
        {
            frames.Add(element, GetElementFrames(element, vanillaKeyFrames));
        }

        List<ItemKeyFrame> result = new();
        if (missingFirstFrame)
        {
            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[0])), 0, EasingFunctionType.Linear));
        }

        for (int index = missingFirstFrame ? 1 : 0; index < keyFramesCount; index++)
        {
            int frameIndex = missingFirstFrame ? index - 1 : index;
            float durationFraction = (float)vanillaKeyFrames[frameIndex].Frame / (vanillaKeyFrames[^1].Frame == 0 ? 1 : vanillaKeyFrames[^1].Frame);

            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[index])), durationFraction, EasingFunctionType.Linear));
        }

        return result;
    }

    private static uint ToCrc32(string value) => GameMath.Crc32(value.ToLowerInvariant()) & int.MaxValue;

    private static AnimationKeyFrame[] GetVanillaKeyFrames(string code, Shape shape)
    {
        Dictionary<uint, Vintagestory.API.Common.Animation> animations = shape.AnimationsByCrc32;
        uint crc32 = ToCrc32(code);
        if (!animations.TryGetValue(crc32, out Vintagestory.API.Common.Animation? value))
        {
            throw new InvalidOperationException($"Animation '{code}' not found");
        }

        return value.KeyFrames;
    }

    private static List<AnimationElement> GetElementFrames(string elementName, AnimationKeyFrame[] vanillaFrames)
    {
        IEnumerable<AnimationKeyFrameElement> vanillaElementFrames = vanillaFrames.Where(element => element.Elements.ContainsKey(elementName)).Select(element => element.Elements[elementName]);

        AnimationElement firstFrame = AnimationElement.FromVanilla(vanillaElementFrames.First());
        AnimationElement lastFrame = AnimationElement.FromVanilla(vanillaElementFrames.Last());

        List<AnimationElement> result = new();
        int previousFrameFrame = 0;
        AnimationElement previousFoundElement = firstFrame;

        int startingIndex = 0;
        if (vanillaFrames[0].Frame == 0)
        {
            startingIndex = 1;
        }
        result.Add(firstFrame);

        bool reachedLast = false;

        for (int index = startingIndex; index < vanillaFrames.Length; index++)
        {
            if (reachedLast)
            {
                result.Add(lastFrame);
                continue;
            }

            if (vanillaFrames[index].Elements.ContainsKey(elementName))
            {
                AnimationElement frame = AnimationElement.FromVanilla(vanillaFrames[index].Elements[elementName]);
                result.Add(frame);
                previousFoundElement = frame;
                previousFrameFrame = vanillaFrames[index].Frame;
            }
            else
            {
                int nextFrameFrame = 0;
                AnimationElement nextFrameElement = previousFoundElement;
                bool found = false;

                for (int nextIndex = index + 1; nextIndex < vanillaFrames.Length; nextIndex++)
                {
                    if (vanillaFrames[nextIndex].Elements.ContainsKey(elementName))
                    {
                        nextFrameFrame = vanillaFrames[nextIndex].Frame;
                        nextFrameElement = AnimationElement.FromVanilla(vanillaFrames[nextIndex].Elements[elementName]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    reachedLast = true;
                    result.Add(lastFrame);
                    continue;
                }

                int currentFrameFrame = vanillaFrames[index].Frame;
                float interpolationProgress = (currentFrameFrame - previousFrameFrame) / (float)(nextFrameFrame - previousFrameFrame);
                result.Add(AnimationElement.Interpolate(previousFoundElement, nextFrameElement, interpolationProgress));
            }
        }

        return result;
    }
}

public readonly struct ItemFrame
{
    public readonly int ElementsHash = 0;
    public readonly Dictionary<string, AnimationElement> Elements = new();
    public readonly Dictionary<int, AnimationElement> ElementsByHash = new();

    public ItemFrame(Dictionary<string, AnimationElement> elements)
    {
        foreach ((string code, AnimationElement value) in elements)
        {
            Elements.Add(code, value);
            ElementsByHash.Add(code.GetHashCode(), value);
        }

        if (elements.Any())
        {
            ElementsHash = Elements.Select(entry => entry.Key.GetHashCode()).Aggregate((first, second) => HashCode.Combine(first, second));
        }
    }

    public static readonly ItemFrame Empty = new(new Dictionary<string, AnimationElement>());

    public void Apply(ElementPose pose)
    {
        if (Elements.TryGetValue(pose.ForElement.Name, out AnimationElement element))
        {
            element.Apply(pose);
        }
    }
    public void Apply(ExtendedElementPose pose)
    {
        if (ElementsByHash.TryGetValue(pose.ElementNameHash, out AnimationElement element))
        {
            element.Apply(pose);
        }
    }
    public static ItemFrame Interpolate(ItemFrame from, ItemFrame to, float progress)
    {
        if (!from.Elements.Any()) return to;

        Dictionary<string, AnimationElement> elements = new();
        foreach ((string key, AnimationElement toElement) in to.Elements)
        {
            if (from.Elements.TryGetValue(key, out AnimationElement fromElement))
            {
                elements.Add(key, AnimationElement.Interpolate(fromElement, toElement, progress));
            }
            else
            {
                elements.Add(key, toElement);
            }
        }

        return new(elements);
    }
    public static ItemFrame Compose(IEnumerable<(ItemFrame element, float weight)> frames)
    {
        if (!frames.Any()) return Empty;

        HashSet<string> keys = frames
            .Select(entry => entry.element.Elements.Keys)
            .SelectMany(entry => entry)
            .ToHashSet();


        Dictionary<string, AnimationElement> elements = new();
        foreach (string key in keys)
        {
            elements.Add(key,
                AnimationElement.Compose(
                    frames
                        .Where(entry => entry.element.Elements.ContainsKey(key))
                        .Select(entry => (entry.element.Elements[key], entry.weight))
                    )
                );
        }

        return new(elements);
    }

#if DEBUG
    public ItemFrame Edit(string title)
    {
        Dictionary<string, AnimationElement> newElements = new();
        foreach ((string key, AnimationElement element) in Elements)
        {
            ImGui.SeparatorText(key);
            AnimationElement newElement = element.Edit(title + key, 1);
            newElements.Add(key, newElement);
        }
        return new(newElements);
    }
#endif
}

public readonly struct PLayerKeyFrame
{
    public readonly PlayerFrame Frame;
    public readonly TimeSpan Time;
    public readonly EasingFunctionType EasingFunction;
    public readonly EasingFunctionType EasingType;
    public readonly Vector2 FrameProgressRange;

    public PLayerKeyFrame(PlayerFrame frame, TimeSpan easingTime, EasingFunctionType easeFunction, EasingFunctionType easingType, Vector2 frameProgressRange)
    {
        Frame = frame;
        Time = easingTime;
        EasingFunction = easeFunction;
        EasingType = easingType;
        FrameProgressRange = frameProgressRange;
    }

    public PLayerKeyFrame(PlayerFrame frame, TimeSpan easingTime, EasingFunctionType easeFunction)
    {
        Frame = frame;
        Time = easingTime;
        EasingFunction = easeFunction;
        EasingType = easeFunction;
        FrameProgressRange = new(0, 1);
    }

    public static readonly PLayerKeyFrame Zero = new(PlayerFrame.Zero, TimeSpan.Zero, EasingFunctionType.Linear);

    public PlayerFrame Interpolate(PlayerFrame frame, float frameProgress)
    {
        float interpolatedProgress = GetInterpolatedProgress(frameProgress);


        return PlayerFrame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(TimeSpan currentDuration) => currentDuration >= Time;

    private float GetInterpolatedProgress(float frameProgress)
    {
        EasingFunctions.EasingFunctionDelegate easingFunction = EasingFunctions.Get(EasingFunction);
        float currentProgress = easingFunction.Invoke(GetAdjustedFrameProgress(frameProgress));
        float startProgress = easingFunction.Invoke(FrameProgressRange.X);
        float endProgress = easingFunction.Invoke(FrameProgressRange.Y);

        return (currentProgress - startProgress) / (endProgress - startProgress);
    }

    private float GetAdjustedFrameProgress(float frameProgress)
    {
        return FrameProgressRange.X + frameProgress * (FrameProgressRange.Y - FrameProgressRange.X);
    }

#if DEBUG
    public PLayerKeyFrame Edit(string title, out bool easingFunctionChanged)
    {
        int milliseconds = (int)Time.TotalMilliseconds;
        ImGui.DragInt($"Easing time##{title}", ref milliseconds);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingType);

        ImGui.SeparatorText("Key frame");

        PlayerFrame frame = Frame.Edit(title);

        easingFunctionChanged = function != EasingType;

        EasingFunctionType oldFunction = EasingFunction;

        if (function != EasingFunctionType.Skip)
        {
            oldFunction = function;
        }

        return new(frame, TimeSpan.FromMilliseconds(milliseconds), oldFunction, function, FrameProgressRange);
    }
#endif
}

public readonly struct PlayerFrame
{
    public readonly RightHandFrame? RightHand;
    public readonly LeftHandFrame? LeftHand;
    public readonly OtherPartsFrame? OtherParts = null;
    public readonly AnimationElement? UpperTorso = null;
    public readonly AnimationElement? DetachedAnchorFrame = null;
    public readonly AnimationElement? LowerTorso = null;
    public readonly bool DetachedAnchor = false;
    public readonly bool SwitchArms = false;
    public readonly float PitchFollow = DefaultPitchFollow;
    public readonly float FovMultiplier = 1;
    public readonly float BobbingAmplitude = 1;
    public readonly float DetachedAnchorFollow = 1;

    public const float DefaultPitchFollow = 0.8f;
    public const float PerfectPitchFollow = 1.0f;
    public const float Epsilon = 1E-6f;
    public const float EyeHeightToAnimationDistanceMultiplier = 16.1f;
    public const float PitchAngleMin = -45;
    public const float PitchAngleMax = 75;

    public PlayerFrame(
        RightHandFrame? rightHand = null,
        LeftHandFrame? leftHand = null,
        OtherPartsFrame? otherParts = null,
        AnimationElement? upperTorso = null,
        AnimationElement? detachedAnchorFrame = null,
        bool detachedAnchor = false,
        bool switchArms = false,
        float pitchFollow = DefaultPitchFollow,
        float fovMultiplier = 1.0f,
        float bobbingAmplitude = 1.0f,
        float? detachedAnchorFollow = null,
        AnimationElement? lowerTorso = null)
    {
        RightHand = rightHand;
        LeftHand = leftHand;
        OtherParts = otherParts;
        UpperTorso = upperTorso;
        DetachedAnchorFrame = detachedAnchorFrame;
        DetachedAnchor = detachedAnchor;
        SwitchArms = switchArms;
        PitchFollow = pitchFollow;
        FovMultiplier = fovMultiplier;
        BobbingAmplitude = bobbingAmplitude;
        LowerTorso = lowerTorso;
        DetachedAnchorFollow = detachedAnchorFollow ?? (detachedAnchor ? 0 : 1);
    }

    public static readonly PlayerFrame Zero = new(RightHandFrame.Zero, LeftHandFrame.Zero, OtherPartsFrame.Zero);
    public static readonly PlayerFrame Empty = new(null, null, null);

    public void Apply(ElementPose pose, EnumAnimatedElement element, Vector3 eyePosition, float eyeHeight, float cameraPitch, bool applyCameraPitch, bool overrideTorso)
    {
        switch (element)
        {
            case EnumAnimatedElement.DetachedAnchor:
                DetachedAnchorFrame?.Apply(pose);
                break;
            case EnumAnimatedElement.UpperTorso:
                UpperTorso?.Apply(pose);
                if (applyCameraPitch)
                {
                    pose.degZ += GameMath.Clamp(cameraPitch * GameMath.RAD2DEG * PitchFollow * DetachedAnchorFollow, PitchAngleMin, PitchAngleMax);
                }
                break;
            case EnumAnimatedElement.Neck:
                OtherParts?.Apply(pose, element);
                if (applyCameraPitch)
                {
                    pose.degZ = -GameMath.Clamp(cameraPitch * GameMath.RAD2DEG * PitchFollow * DetachedAnchorFollow, PitchAngleMin, PitchAngleMax) / 2;
                }
                break;
            case EnumAnimatedElement.LowerTorso:
                if (overrideTorso)
                {
                    if (LowerTorso != null)
                    {
                        AnimationElement torso = new(LowerTorso.Value.OffsetX, new AnimationElementValue((eyePosition.Y - eyeHeight) * EyeHeightToAnimationDistanceMultiplier), LowerTorso.Value.OffsetZ, LowerTorso.Value.Rotation);
                        torso.Apply(pose);
                    }
                    else
                    {
                        AnimationElement torso = new(0, (eyePosition.Y - eyeHeight) * EyeHeightToAnimationDistanceMultiplier, 0, 0, 0, 0);
                        torso.Apply(pose);
                    }
                }
                else
                {
                    LowerTorso?.Apply(pose);
                    pose.translateY = (eyePosition.Y - eyeHeight) * EyeHeightToAnimationDistanceMultiplier / 16;
                }
                break;
            default:
                OtherParts?.Apply(pose, element);
                RightHand?.Apply(pose, element);
                LeftHand?.Apply(pose, element);
                break;
        }
    }

#if DEBUG
    public PlayerFrame Edit(string title)
    {
        bool detachedAnchor = DetachedAnchor;
        ImGui.Checkbox($"Detached anchor##{title}", ref detachedAnchor);

        bool switchArms = SwitchArms;
        ImGui.Checkbox($"Switch arms##{title}", ref switchArms);

        bool pitchFollow = Math.Abs(PitchFollow - PerfectPitchFollow) < Epsilon;
        ImGui.Checkbox($"Pitch follow##{title}", ref pitchFollow);

        bool pitchDontFollow = Math.Abs(PitchFollow - 0) < Epsilon && !pitchFollow;
        ImGui.Checkbox($"Pitch dont follow##{title}", ref pitchDontFollow);

        float fov = FovMultiplier;
        ImGui.SliderFloat($"FOV multiplier##{title}", ref fov, 0.1f, 2.0f);

        float bobbing = BobbingAmplitude;
        ImGui.SliderFloat($"Bobbing amplitude##{title}", ref bobbing, 0.1f, 2.0f);

        bool rightHand = RightHand != null;
        bool leftHand = LeftHand != null;
        bool upperTorso = UpperTorso != null;
        bool lowerTorso = LowerTorso != null;
        bool otherParts = OtherParts != null;
        bool detachedAnchorFrame = DetachedAnchorFrame != null;

        ImGui.Checkbox($"Right hand frame##{title}", ref rightHand); ImGui.SameLine();
        ImGui.Checkbox($"Left hand frame##{title}", ref leftHand); ImGui.SameLine();
        ImGui.Checkbox($"Upper torso frame##{title}", ref upperTorso);
        ImGui.Checkbox($"Lower torso frame##{title}", ref lowerTorso); ImGui.SameLine();
        ImGui.Checkbox($"Other parts frame##{title}", ref otherParts); ImGui.SameLine();
        ImGui.Checkbox($"Detached anchor frame##{title}", ref detachedAnchorFrame);

        RightHandFrame? right = RightHand?.Edit(title);
        LeftHandFrame? left = LeftHand?.Edit(title);

        if (upperTorso) ImGui.SeparatorText($"UpperTorso");
        AnimationElement? torso = UpperTorso?.Edit($"{title}##UpperTorso");

        if (lowerTorso) ImGui.SeparatorText($"LowerTorso");
        AnimationElement? torsoLower = LowerTorso?.Edit($"{title}##LowerTorso");

        if (detachedAnchorFrame) ImGui.SeparatorText($"DetachedAnchor");
        AnimationElement? anchor = DetachedAnchorFrame?.Edit($"{title}##DetachedAnchor");

        OtherPartsFrame? other = OtherParts?.Edit($"{title}##OtherParts");

        if (RightHand == null && rightHand) right = RightHandFrame.Zero;
        if (RightHand != null && !rightHand) right = null;
        if (LeftHand == null && leftHand) left = LeftHandFrame.Zero;
        if (LeftHand != null && !leftHand) left = null;
        if (OtherParts == null && otherParts) other = OtherPartsFrame.Zero;
        if (OtherParts != null && !otherParts) other = null;
        if (UpperTorso == null && upperTorso) torso = AnimationElement.Zero;
        if (UpperTorso != null && !upperTorso) torso = null;
        if (LowerTorso == null && lowerTorso) torsoLower = AnimationElement.Zero;
        if (LowerTorso != null && !lowerTorso) torsoLower = null;
        if (DetachedAnchorFrame == null && detachedAnchorFrame) anchor = AnimationElement.Zero;
        if (DetachedAnchorFrame != null && !detachedAnchorFrame) anchor = null;

        float pitch = DefaultPitchFollow;
        if (pitchFollow) pitch = PerfectPitchFollow;
        if (pitchDontFollow) pitch = 0;

        return new(right, left, other, torso, anchor, detachedAnchor, switchArms, pitch, fov, bobbing, lowerTorso: torsoLower);
    }
#endif

    public static PlayerFrame Interpolate(PlayerFrame from, PlayerFrame to, float progress)
    {
        RightHandFrame? righthand = null;
        if (from.RightHand == null && to.RightHand != null)
        {
            righthand = null;//to.RightHand;
        }
        else if (from.RightHand != null && to.RightHand == null)
        {
            righthand = null;//from.RightHand;
        }
        else if (from.RightHand != null && to.RightHand != null)
        {
            righthand = RightHandFrame.Interpolate(from.RightHand.Value, to.RightHand.Value, progress);
        }

        LeftHandFrame? leftHand = null;
        if (from.LeftHand == null && to.LeftHand != null)
        {
            leftHand = null;//to.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand == null)
        {
            leftHand = null;//from.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand != null)
        {
            leftHand = LeftHandFrame.Interpolate(from.LeftHand.Value, to.LeftHand.Value, progress);
        }

        OtherPartsFrame? otherParts = null;
        if (from.OtherParts == null && to.OtherParts != null)
        {
            otherParts = null;//to.LeftHand;
        }
        else if (from.OtherParts != null && to.OtherParts == null)
        {
            otherParts = null;//from.LeftHand;
        }
        else if (from.OtherParts != null && to.OtherParts != null)
        {
            otherParts = OtherPartsFrame.Interpolate(from.OtherParts.Value, to.OtherParts.Value, progress);
        }

        AnimationElement? anchor = AnimationElement.Interpolate(from.DetachedAnchorFrame ?? AnimationElement.Zero, to.DetachedAnchorFrame ?? AnimationElement.Zero, progress);
        AnimationElement? torso = AnimationElement.Interpolate(from.UpperTorso ?? AnimationElement.Zero, to.UpperTorso ?? AnimationElement.Zero, progress);
        AnimationElement? lowerTorso = AnimationElement.Interpolate(from.LowerTorso ?? AnimationElement.Zero, to.LowerTorso ?? AnimationElement.Zero, progress);

        if (from.DetachedAnchorFrame == null && to.DetachedAnchorFrame == null) anchor = null;
        if (from.UpperTorso == null && to.UpperTorso == null) torso = null;

        float pitchFollow = from.PitchFollow + (to.PitchFollow - from.PitchFollow) * progress;
        float fov = from.FovMultiplier + (to.FovMultiplier - from.FovMultiplier) * progress;
        float bobbing = from.BobbingAmplitude + (to.BobbingAmplitude - from.BobbingAmplitude) * progress;
        float detachedAnchorFollow = from.DetachedAnchorFollow + (to.DetachedAnchorFollow - from.DetachedAnchorFollow) * progress;

        return new(righthand, leftHand, otherParts, torso, anchor, to.DetachedAnchor, to.SwitchArms, pitchFollow, fov, bobbing, detachedAnchorFollow, lowerTorso);
    }
    public static PlayerFrame Compose(IEnumerable<(PlayerFrame element, float weight)> frames)
    {
        RightHandFrame rightHand = RightHandFrame.Compose(frames.Where(entry => entry.element.RightHand != null).Select(entry => (entry.element.RightHand.Value, entry.weight)));
        LeftHandFrame leftHand = LeftHandFrame.Compose(frames.Where(entry => entry.element.LeftHand != null).Select(entry => (entry.element.LeftHand.Value, entry.weight)));
        OtherPartsFrame otherParts = OtherPartsFrame.Compose(frames.Where(entry => entry.element.OtherParts != null).Select(entry => (entry.element.OtherParts.Value, entry.weight)));

        bool haveRightHandFrame = frames.Any(entry => entry.element.RightHand != null);
        bool haveLeftHandFrame = frames.Any(entry => entry.element.LeftHand != null);
        bool haveOtherPartsFrame = frames.Any(entry => entry.element.OtherParts != null);

        return new(
            haveRightHandFrame ? rightHand : null,
            haveLeftHandFrame ? leftHand : null,
            haveOtherPartsFrame ? otherParts : null,
            AnimationElement.Compose(frames.Where(entry => entry.element.UpperTorso != null).Select(entry => (entry.element.UpperTorso.Value, entry.weight))),
            AnimationElement.Compose(frames.Where(entry => entry.element.DetachedAnchorFrame != null).Select(entry => (entry.element.DetachedAnchorFrame.Value, entry.weight))),
            frames.Select(entry => entry.element.DetachedAnchor).Aggregate((first, second) => first || second),
            frames.Select(entry => entry.element.SwitchArms).Aggregate((first, second) => first || second),
            frames.Select(entry => entry.element.PitchFollow).Where(value => Math.Abs(value - DefaultPitchFollow) > 1E-6f).FirstOrDefault(DefaultPitchFollow),
            frames.Select(entry => entry.element.FovMultiplier).Min(),
            frames.Select(entry => entry.element.BobbingAmplitude).Min(),
            frames.Select(entry => entry.element.DetachedAnchorFollow).Min(),
            AnimationElement.Compose(frames.Where(entry => entry.element.LowerTorso != null).Select(entry => (entry.element.LowerTorso.Value, entry.weight)))
            );
    }
}

public readonly struct RightHandFrame
{
    public readonly AnimationElement ItemAnchor;
    public readonly AnimationElement LowerArmR;
    public readonly AnimationElement UpperArmR;

    public RightHandFrame(AnimationElement anchor, AnimationElement lower, AnimationElement upper)
    {
        ItemAnchor = anchor;
        LowerArmR = lower;
        UpperArmR = upper;
    }

    public void Apply(ElementPose pose, EnumAnimatedElement element)
    {
        switch (element)
        {
            case EnumAnimatedElement.ItemAnchor:
                ItemAnchor.Apply(pose);
                break;
            case EnumAnimatedElement.LowerArmR:
                LowerArmR.Apply(pose);
                break;
            case EnumAnimatedElement.UpperArmR:
                UpperArmR.Apply(pose);
                break;
        }
    }

    public static readonly RightHandFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

#if DEBUG
    public RightHandFrame Edit(string title)
    {
        ImGui.SeparatorText($"ItemAnchor");
        AnimationElement anchor = ItemAnchor.Edit($"{title}##ItemAnchor");
        ImGui.SeparatorText($"LowerArmR");
        AnimationElement lower = LowerArmR.Edit($"{title}##LowerArmR");
        ImGui.SeparatorText($"UpperArmR");
        AnimationElement upper = UpperArmR.Edit($"{title}##UpperArmR");

        return new(anchor, lower, upper);
    }
#endif

    public static RightHandFrame Interpolate(RightHandFrame from, RightHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchor, to.ItemAnchor, progress),
            AnimationElement.Interpolate(from.LowerArmR, to.LowerArmR, progress),
            AnimationElement.Interpolate(from.UpperArmR, to.UpperArmR, progress)
            );
    }

    public static RightHandFrame Compose(IEnumerable<(RightHandFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.ItemAnchor, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerArmR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperArmR, entry.weight)))
            );
    }
}

public readonly struct LeftHandFrame
{
    public readonly AnimationElement ItemAnchorL;
    public readonly AnimationElement LowerArmL;
    public readonly AnimationElement UpperArmL;

    public LeftHandFrame(AnimationElement anchor, AnimationElement lower, AnimationElement upper)
    {
        ItemAnchorL = anchor;
        LowerArmL = lower;
        UpperArmL = upper;
    }

    public void Apply(ElementPose pose, EnumAnimatedElement element)
    {
        switch (element)
        {
            case EnumAnimatedElement.ItemAnchorL:
                ItemAnchorL.Apply(pose);
                break;
            case EnumAnimatedElement.LowerArmL:
                LowerArmL.Apply(pose);
                break;
            case EnumAnimatedElement.UpperArmL:
                UpperArmL.Apply(pose);
                break;
        }
    }

    public static readonly LeftHandFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

#if DEBUG
    public LeftHandFrame Edit(string title)
    {
        ImGui.SeparatorText($"ItemAnchorL");
        AnimationElement anchor = ItemAnchorL.Edit($"{title}##ItemAnchorL");
        ImGui.SeparatorText($"LowerArmL");
        AnimationElement lower = LowerArmL.Edit($"{title}##LowerArmL");
        ImGui.SeparatorText($"UpperArmL");
        AnimationElement upper = UpperArmL.Edit($"{title}##UpperArmL");

        return new(anchor, lower, upper);
    }
#endif

    public static LeftHandFrame Interpolate(LeftHandFrame from, LeftHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchorL, to.ItemAnchorL, progress),
            AnimationElement.Interpolate(from.LowerArmL, to.LowerArmL, progress),
            AnimationElement.Interpolate(from.UpperArmL, to.UpperArmL, progress)
            );
    }

    public static LeftHandFrame Compose(IEnumerable<(LeftHandFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.ItemAnchorL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerArmL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperArmL, entry.weight)))
            );
    }
}

public readonly struct OtherPartsFrame
{
    public readonly AnimationElement Neck;
    public readonly AnimationElement Head;
    public readonly AnimationElement UpperFootR;
    public readonly AnimationElement UpperFootL;
    public readonly AnimationElement LowerFootR;
    public readonly AnimationElement LowerFootL;

    public OtherPartsFrame(
        AnimationElement neck,
        AnimationElement head,
        AnimationElement upperFootR,
        AnimationElement upperFootL,
        AnimationElement lowerFootR,
        AnimationElement lowerFootL)
    {
        Neck = neck;
        Head = head;
        UpperFootR = upperFootR;
        UpperFootL = upperFootL;
        LowerFootR = lowerFootR;
        LowerFootL = lowerFootL;
    }

    public void Apply(ElementPose pose, EnumAnimatedElement element)
    {
        switch (element)
        {
            case EnumAnimatedElement.Neck:
                Neck.Apply(pose);
                break;
            case EnumAnimatedElement.Head:
                Head.Apply(pose);
                break;
            case EnumAnimatedElement.UpperFootR:
                UpperFootR.Apply(pose);
                break;
            case EnumAnimatedElement.UpperFootL:
                UpperFootL.Apply(pose);
                break;
            case EnumAnimatedElement.LowerFootR:
                LowerFootR.Apply(pose);
                break;
            case EnumAnimatedElement.LowerFootL:
                LowerFootL.Apply(pose);
                break;
        }
    }

    public static readonly OtherPartsFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

#if DEBUG
    public OtherPartsFrame Edit(string title)
    {
        ImGui.SeparatorText($"Neck");
        AnimationElement neck = Neck.Edit($"{title}##Neck");
        ImGui.SeparatorText($"Head");
        AnimationElement head = Head.Edit($"{title}##Head");
        ImGui.SeparatorText($"UpperFootR");
        AnimationElement upperFootR = UpperFootR.Edit($"{title}##UpperFootR");
        ImGui.SeparatorText($"UpperFootL");
        AnimationElement upperFootL = UpperFootL.Edit($"{title}##UpperFootL");
        ImGui.SeparatorText($"LowerFootR");
        AnimationElement lowerFootR = LowerFootR.Edit($"{title}##LowerFootR");
        ImGui.SeparatorText($"LowerFootL");
        AnimationElement lowerFootL = LowerFootL.Edit($"{title}##LowerFootL");

        return new(neck, head, upperFootR, upperFootL, lowerFootR, lowerFootL);
    }
#endif

    public static OtherPartsFrame Interpolate(OtherPartsFrame from, OtherPartsFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.Neck, to.Neck, progress),
            AnimationElement.Interpolate(from.Head, to.Head, progress),
            AnimationElement.Interpolate(from.UpperFootR, to.UpperFootR, progress),
            AnimationElement.Interpolate(from.UpperFootL, to.UpperFootL, progress),
            AnimationElement.Interpolate(from.LowerFootR, to.LowerFootR, progress),
            AnimationElement.Interpolate(from.LowerFootL, to.LowerFootL, progress)
            );
    }

    public static OtherPartsFrame Compose(IEnumerable<(OtherPartsFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.Neck, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.Head, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperFootR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperFootL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerFootR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerFootL, entry.weight)))
            );
    }
}

public readonly struct AnimationElement
{
    public readonly AnimationElementValue OffsetX;
    public readonly AnimationElementValue OffsetY;
    public readonly AnimationElementValue OffsetZ;
    public readonly AnimationElementRotation Rotation;

    public static readonly AnimationElement Empty = new();
    public static readonly AnimationElement Zero = new(0, 0, 0, 0, 0, 0);
    public const float OffsetFactor = 1 / 16f;

    public AnimationElement()
    {
        OffsetX = AnimationElementValue.Empty;
        OffsetY = AnimationElementValue.Empty;
        OffsetZ = AnimationElementValue.Empty;
        Rotation = AnimationElementRotation.Empty;
    }
    public AnimationElement(params AnimationElementValue[] values)
    {
        OffsetX = values[0];
        OffsetY = values[1];
        OffsetZ = values[2];
        Rotation = new(values[3].Value, values[4].Value, values[5].Value, values[3].HasValue || values[4].HasValue || values[5].HasValue);
    }
    public AnimationElement(AnimationElementValue x, AnimationElementValue y, AnimationElementValue z, AnimationElementRotation rotation)
    {
        OffsetX = x;
        OffsetY = y;
        OffsetZ = z;
        Rotation = rotation;
    }
    public AnimationElement(params float?[] values)
    {
        OffsetX = new(values[0] ?? 0, values[0].HasValue);
        OffsetY = new(values[1] ?? 0, values[1].HasValue);
        OffsetZ = new(values[2] ?? 0, values[2].HasValue);
        Rotation = new(values[3] ?? 0, values[4] ?? 0, values[5] ?? 0);
    }

    public void Apply(ElementPose pose)
    {
        if (OffsetX.HasValue) pose.translateX = OffsetX.Value * OffsetFactor;
        if (OffsetY.HasValue) pose.translateY = OffsetY.Value * OffsetFactor;
        if (OffsetZ.HasValue) pose.translateZ = OffsetZ.Value * OffsetFactor;

        if (Rotation.HasValue)
        {
            Rotation.Apply(pose);
        }
    }

#if DEBUG
    public AnimationElement Edit(string title, float multiplier = 10)
    {
        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        AnimationElementValue offsetX = EditValue(OffsetX, multiplier, speed, $"X##translation{title}"); ImGui.SameLine();
        AnimationElementValue offsetY = EditValue(OffsetY, multiplier, speed, $"Y##translation{title}"); ImGui.SameLine();
        AnimationElementValue offsetZ = EditValue(OffsetZ, multiplier, speed, $"Z##translation{title}"); ImGui.SameLine();
        ImGui.Text("Translation"); ImGui.SameLine();

        ImGui.SameLine();
        if (ImGui.Button($"Copy##{title}"))
        {
            _buffer = this;
        }

        Vector3 angles = Rotation.Value.ToEulerAngles();

        AnimationElementValue rotationX = EditValue(new(angles.X * GameMath.RAD2DEG, Rotation.HasValue), 1, speed, $"X##rotation{title}"); ImGui.SameLine();
        AnimationElementValue rotationY = EditValue(new(angles.Y * GameMath.RAD2DEG, Rotation.HasValue), 1, speed, $"Y##rotation{title}"); ImGui.SameLine();
        AnimationElementValue rotationZ = EditValue(new(angles.Z * GameMath.RAD2DEG, Rotation.HasValue), 1, speed, $"Z##rotation{title}"); ImGui.SameLine();
        ImGui.Text("Rotation     "); ImGui.SameLine();
        Quaternion rotation = Quaternion.FromEulerAngles(rotationX.Value * GameMath.DEG2RAD, rotationY.Value * GameMath.DEG2RAD, rotationZ.Value * GameMath.DEG2RAD);

        /*Rotation.Value.ToAxisAngle(out Vector3 axis, out float angle);
        angle = GameMath.RAD2DEG * angle;
        AnimationElementValue rotationX = EditValue(new(axis.X, Rotation.HasValue), 1, speed, $"X##rotation{title}"); ImGui.SameLine();
        AnimationElementValue rotationY = EditValue(new(axis.Y, Rotation.HasValue), 1, speed, $"Y##rotation{title}"); ImGui.SameLine();
        AnimationElementValue rotationZ = EditValue(new(axis.Z, Rotation.HasValue), 1, speed, $"Z##rotation{title}"); ImGui.SameLine();
        AnimationElementValue rotationW = EditValue(new(angle, Rotation.HasValue), 1, speed, $"Deg##rotation{title}"); ImGui.SameLine();
        angle = GameMath.DEG2RAD * rotationW.Value;
        axis = new(rotationX.Value, rotationY.Value, rotationZ.Value);
        Quaternion rotation = Quaternion.FromAxisAngle(axis, angle);*/


        ImGui.SameLine();
        if (ImGui.Button($"Paste##{title}"))
        {
            return _buffer;
        }

        return new(
            offsetX,
            offsetY,
            offsetZ,
            new AnimationElementRotation(rotation)
            );
    }
#endif

    public float?[] ToArray() =>
    [
        OffsetX.HasValue ? OffsetX.Value : null,
        OffsetY.HasValue ? OffsetY.Value : null,
        OffsetZ.HasValue ? OffsetZ.Value : null,
        Rotation.HasValue ? Rotation.Value.ToEulerAngles().X : null,
        Rotation.HasValue ? Rotation.Value.ToEulerAngles().Y : null,
        Rotation.HasValue ? Rotation.Value.ToEulerAngles().Z : null
    ];

    public static AnimationElement Interpolate(AnimationElement from, AnimationElement to, float progress)
    {
        return new(
            new(from.OffsetX.Value + (to.OffsetX.Value - from.OffsetX.Value) * progress, from.OffsetX.HasValue || to.OffsetX.HasValue),
            new(from.OffsetY.Value + (to.OffsetY.Value - from.OffsetY.Value) * progress, from.OffsetY.HasValue || to.OffsetY.HasValue),
            new(from.OffsetZ.Value + (to.OffsetZ.Value - from.OffsetZ.Value) * progress, from.OffsetZ.HasValue || to.OffsetZ.HasValue),
            AnimationElementRotation.Interpolate(from.Rotation, to.Rotation, progress)
            );
    }
    public static AnimationElement Compose(IEnumerable<(AnimationElement element, float weight)> elements)
    {
        AnimationElementValue offsetX = AnimationElementValue.Empty;
        AnimationElementValue offsetY = AnimationElementValue.Empty;
        AnimationElementValue offsetZ = AnimationElementValue.Empty;
        AnimationElementRotation rotation = AnimationElementRotation.Empty;

        float offsetXMaxWeight = 0;
        float offsetYMaxWeight = 0;
        float offsetZMaxWeight = 0;
        float rotationMaxWeight = 0;

        foreach ((AnimationElement element, float weight) in elements.Where(entry => entry.weight > 0))
        {
            if (weight >= offsetXMaxWeight && element.OffsetX.HasValue) { offsetXMaxWeight = weight; offsetX = element.OffsetX; }
            if (weight >= offsetYMaxWeight && element.OffsetY.HasValue) { offsetYMaxWeight = weight; offsetY = element.OffsetY; }
            if (weight >= offsetZMaxWeight && element.OffsetZ.HasValue) { offsetZMaxWeight = weight; offsetZ = element.OffsetZ; }
            if (weight >= rotationMaxWeight && element.Rotation.HasValue) { rotationMaxWeight = weight; rotation = element.Rotation; }
        }

        foreach ((AnimationElement element, float weight) in elements.Where(entry => entry.weight <= 0))
        {
            if (element.OffsetX.HasValue) offsetX += element.OffsetX;
            if (element.OffsetY.HasValue) offsetY += element.OffsetY;
            if (element.OffsetZ.HasValue) offsetZ += element.OffsetZ;
            if (element.Rotation.HasValue) rotation += element.Rotation;
        }

        return new(
            offsetX,
            offsetY,
            offsetZ,
            rotation
            );
    }
    public static AnimationElement FromVanilla(AnimationKeyFrameElement frame)
    {
        return new(
            new((float)(frame.OffsetX ?? 0), frame.OffsetX != null),
            new((float)(frame.OffsetY ?? 0), frame.OffsetY != null),
            new((float)(frame.OffsetZ ?? 0), frame.OffsetZ != null),
            new AnimationElementRotation((float)(frame.RotationX ?? 0), (float)(frame.RotationY ?? 0), (float)(frame.RotationZ ?? 0)));
    }

    public override string ToString() => $"[{OffsetX}, {OffsetY}, {OffsetZ}, {Rotation.Value.ToEulerAngles()}]";

#if DEBUG
    private static AnimationElementValue EditValue(AnimationElementValue value, float multiplier, float speed, string title)
    {
        bool enabled = value.HasValue;
        float newValue = value.Value;
        if (enabled)
        {
            float valueValue = value.Value * multiplier;
            ImGui.SetNextItemWidth(90);
            ImGui.DragFloat($"##{title}value", ref valueValue, speed); ImGui.SameLine();
            ImGui.Checkbox($"##{title}checkbox", ref enabled);

            newValue = valueValue / multiplier;
        }
        else
        {
            ImGui.Checkbox($"{title}##checkbox", ref enabled);
        }
        return new(newValue, enabled);
    }
    private static AnimationElement _buffer = AnimationElement.Zero;
#endif
}

public readonly struct AnimationElementRotation
{
    public readonly Quaternion Value;
    public readonly bool HasValue;

    public static readonly AnimationElementRotation Empty = new();
    public static readonly AnimationElementRotation Zero = new(Quaternion.Identity);

    public AnimationElementRotation()
    {
        Value = Quaternion.Identity;
        HasValue = false;
    }
    public AnimationElementRotation(float x, float y, float z)
    {
        Value = Quaternion.FromEulerAngles(x * GameMath.DEG2RAD, y * GameMath.DEG2RAD, z * GameMath.DEG2RAD);
        HasValue = true;
    }
    public AnimationElementRotation(float x, float y, float z, bool hasValue)
    {
        Value = Quaternion.FromEulerAngles(x * GameMath.DEG2RAD, y * GameMath.DEG2RAD, z * GameMath.DEG2RAD);
        HasValue = hasValue;
    }
    public AnimationElementRotation(Quaternion value)
    {
        Value = value;
        HasValue = true;
    }
    public AnimationElementRotation(Quaternion value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    public void Apply(ElementPose pose)
    {
        if (!HasValue) return;
        Vector3 angles = Value.ToEulerAngles();
        pose.degX = angles.X * GameMath.RAD2DEG;
        pose.degY = angles.Y * GameMath.RAD2DEG;
        pose.degZ = angles.Z * GameMath.RAD2DEG;
    }

    public AnimationElementRotation Add(AnimationElementRotation element)
    {
        if (HasValue)
        {
            if (element.HasValue)
            {
                return new AnimationElementRotation(Value * element.Value);
            }
            else
            {
                return this;
            }
        }
        else
        {
            return element;
        }
    }

    public static AnimationElementRotation operator +(AnimationElementRotation first, AnimationElementRotation second) => first.Add(second);

    public static AnimationElementRotation Interpolate(AnimationElementRotation from, AnimationElementRotation to, float progress)
    {
        return new(Quaternion.Slerp(from.Value, to.Value, progress), from.HasValue || to.HasValue);
    }

    public override string ToString() => HasValue ? $"{Value:F3}" : "-";
}

public readonly struct AnimationElementValue
{
    public readonly float Value;
    public readonly bool HasValue;

    public static readonly AnimationElementValue Empty = new();
    public static readonly AnimationElementValue Zero = new(0);

    public AnimationElementValue()
    {
        HasValue = false;
    }
    public AnimationElementValue(float value)
    {
        Value = value;
        HasValue = true;
    }
    public AnimationElementValue(float value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    public AnimationElementValue Add(AnimationElementValue element)
    {
        if (HasValue)
        {
            if (element.HasValue)
            {
                return new AnimationElementValue(Value + element.Value);
            }
            else
            {
                return this;
            }
        }
        else
        {
            return element;
        }
    }

    public static AnimationElementValue operator +(AnimationElementValue first, AnimationElementValue second) => first.Add(second);

    public override string ToString() => HasValue ? $"{Value:F3}" : "-";
}