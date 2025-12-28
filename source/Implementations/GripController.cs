using CombatOverhaul.Animations;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Implementations;

public sealed class GripController
{
    public GripController(FirstPersonAnimationsBehavior? animationBehavior)
    {
        _animationBehavior = animationBehavior;
    }

    public void ChangeGrip(float delta, bool mainHand, float gripFactor, float min, float max)
    {
        if (min > max)
        {
            min = 0;
            max = 0;
        }

        if (min == 0 && max == 0)
        {
            StopAnimation(mainHand);
            return;
        }

        _grip = GameMath.Clamp(_grip + delta * gripFactor, min, max);

        PlayAnimation(mainHand);
    }
    public void ResetGrip(bool mainHand)
    {
        _grip = 0;

        _animationBehavior?.Stop("grip");
    }
    public void StopAnimation(bool mainHand)
    {
        _animationBehavior?.Stop("grip");
    }
    public void AdjustGrip(bool mainHand, float min, float max)
    {
        if (min > max)
        {
            min = 0;
            max = 0;
        }

        if (min == 0 && max == 0)
        {
            StopAnimation(mainHand);
            return;
        }

        _grip = GameMath.Clamp(_grip, min, max);

        PlayAnimation(mainHand);
    }

    private float _grip = 0;
    private readonly Animations.Animation _gripAnimation = Animations.Animation.Zero.Clone();
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;

    private PLayerKeyFrame GetAimingFrame()
    {
        AnimationElement element = new(_grip, null, null, null, null, null);
        AnimationElement nullElement = new(null, null, null, null, null, null);

        PlayerFrame frame = new(rightHand: new(element, nullElement, nullElement));

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }
    private void PlayAnimation(bool mainHand)
    {
        _gripAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _gripAnimation.Hold = true;

        AnimationRequest request = new(_gripAnimation, 1.0f, 0, "grip", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
    }
}
