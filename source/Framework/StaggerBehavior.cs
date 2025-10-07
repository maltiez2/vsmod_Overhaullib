using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public class StaggerBehavior : EntityBehavior
{
    public StaggerBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "StaggerBehavior";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (entity is not EntityAgent agent) return;
        AiTaskConfig = attributes["taskConfig"]?.AsObject<AiTaskBaseConfig>();
        ResistanceTier = attributes["resistanceTier"].AsInt(0);
        //AnimationsToStop = attributes["animationsToStop"].AsObject<string[]>([]);
        AiTaskConfig?.Init(agent);
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        EntityBehaviorTaskAI? behavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (behavior == null || AiTaskConfig == null || entity is not EntityAgent agent) return;
        behavior.TaskManager.AddTask(new StaggerAiTask(agent, AiTaskConfig));
    }

    public void TriggerStagger(TimeSpan duration, int tier)
    {
        if (!entity.Alive) return;
        
        EntityBehaviorTaskAI? behavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (behavior == null) return;

        StaggerAiTask? task = behavior.TaskManager?.GetTask<StaggerAiTask>();
        if (task == null) return;

        foreach (string animation in entity.AnimManager.ActiveAnimationsByAnimCode.Keys)
        {
            entity.AnimManager.StopAnimation(animation);
        }

        task.SetStaggerTime(duration * ApplyResistance(tier));
        behavior.TaskManager?.ExecuteTask<StaggerAiTask>();
    }

    public void ClearStagger()
    {
        EntityBehaviorTaskAI? behavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (behavior == null) return;

        StaggerAiTask? task = behavior.TaskManager?.GetTask<StaggerAiTask>();
        if (task == null) return;

        task.ResetStaggerTime();
    }

    protected AiTaskBaseConfig? AiTaskConfig;
    protected int ResistanceTier = 0;
    //protected string[] AnimationsToStop = [];

    protected virtual float ApplyResistance(int tier)
    {
        if (ResistanceTier <= tier) return 1;

        return MathF.Pow(0.5f, tier - ResistanceTier);
    }
}

public class StaggerAiTask : AiTaskBaseR
{
    public StaggerAiTask(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public StaggerAiTask(EntityAgent entity, AiTaskBaseConfig config) : base(entity)
    {
        baseConfig = config;
    }

    public override bool ShouldExecute() => false;

    public override bool ContinueExecute(float dt)
    {
        return Staggered();// && base.ContinueExecute(dt);
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
    }

    public virtual void AddStaggerTime(TimeSpan duration)
    {
        if (StaggerUntil < CurrentTime)
        {
            StaggerUntil = CurrentTime + duration;
        }
        else
        {
            StaggerUntil += duration;
        }
    }

    public virtual void SetStaggerTime(TimeSpan duration)
    {
        StaggerUntil = CurrentTime + duration;
    }

    public virtual void ResetStaggerTime()
    {
        StaggerUntil = TimeSpan.Zero;
    }

    public virtual bool Staggered() => StaggerUntil > CurrentTime;

    protected TimeSpan StaggerUntil = TimeSpan.Zero;
    protected TimeSpan CurrentTime => TimeSpan.FromMilliseconds(entity.Api.World.ElapsedMilliseconds);
}