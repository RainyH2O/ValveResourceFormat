using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>A dynamic model.</summary>
public sealed class PropDynamic : BaseModelEntity
{
    /// <summary>Spawn flags for dynamic props.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Spawns with collision off, until <c>EnableCollision</c>.</summary>
        StartCollisionDisabled = 256,
    }

    /// <summary>Gets the animation the prop returns to once another one ends, if it has one.</summary>
    public string? IdleAnimation { get; private set; }

    /// <summary>Gets whether an ended animation stays on its last frame instead of returning to <see cref="IdleAnimation"/>.</summary>
    public bool HoldAnimation { get; private set; }

    // Null leaves looping to the animation itself, see PlayAnimation
    private bool? idleLooping;
    private bool hasCollision;
    private bool isForcedAnimation;
    private bool isWaitingForAnimationEnd;

    /// <summary>Initializes a <c>prop_dynamic</c> from its keyvalues.</summary>
    public PropDynamic(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // CS2 names the starting and idle animations apart; Source 1 maps have one default for both
        IdleAnimation = NonEmpty(KeyValues.GetStringProperty("idleanim")) ?? NonEmpty(KeyValues.GetStringProperty("defaultanim"));
        idleLooping = ReadLoopMode("idleanimationloopmode");
        HoldAnimation = KeyValues.GetBooleanProperty("holdanimation");

        // The collider is rigid and ignores scale, and blocking with a hull of the wrong size is worse
        // than not blocking at all
        hasCollision = KeyValues.GetInt32Property("solid", 6) != 0 && EntityScale == Vector3.One;
        IsSolid = hasCollision && !HasSpawnFlags(SpawnFlag.StartCollisionDisabled);

        // Replayed over what BaseModelEntity posed, which neither knows the loop mode nor plays a held
        // animation through before stopping on its last frame
        if (NonEmpty(KeyValues.GetStringProperty("startinganim")) is { } startingAnimation)
        {
            PlayAnimation(startingAnimation, ReadLoopMode("startinganimationloopmode"), restart: true, forced: false);
        }
        else if (IdleAnimation != null)
        {
            PlayAnimation(IdleAnimation, idleLooping, restart: true, forced: false);
        }
    }

    /// <inheritdoc/>
    public override void Think()
    {
        if (!isWaitingForAnimationEnd || ModelNode is not { } node)
        {
            return;
        }

        // The node advances its animation per frame, so the end is noticed on the first tick after it
        if (!HasAnimationEnded(node.AnimationController))
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
            return;
        }

        isWaitingForAnimationEnd = false;

        if (isForcedAnimation)
        {
            isForcedAnimation = false;

            EntitySystem.TriggerOutput(this, "OnAnimationReachedEnd");
            EntitySystem.TriggerOutput(this, "OnAnimationDone");
        }

        ReturnToIdle();
    }

    [EntityInput("SetAnimationLooping")]
    private void InputSetAnimationLooping(EntityInputData data) => PlayForcedAnimation(data, looping: true, restart: true);

    [EntityInput("SetAnimationNotLooping")]
    private void InputSetAnimationNotLooping(EntityInputData data) => PlayForcedAnimation(data, looping: false, restart: true);

    [EntityInput("SetAnimationNoResetLooping")]
    private void InputSetAnimationNoResetLooping(EntityInputData data) => PlayForcedAnimation(data, looping: true, restart: false);

    [EntityInput("SetAnimationNoResetNotLooping")]
    private void InputSetAnimationNoResetNotLooping(EntityInputData data) => PlayForcedAnimation(data, looping: false, restart: false);

    [EntityInput("SetAnimation")]
    private void InputSetAnimation(EntityInputData data) => PlayForcedAnimation(data, looping: null, restart: true);

    [EntityInput("SetAnimationNoReset")]
    private void InputSetAnimationNoReset(EntityInputData data) => PlayForcedAnimation(data, looping: null, restart: false);

    [EntityInput("SetIdleAnimationLooping")]
    private void InputSetIdleAnimationLooping(EntityInputData data) => SetIdleAnimation(data, looping: true);

    [EntityInput("SetIdleAnimationNotLooping")]
    private void InputSetIdleAnimationNotLooping(EntityInputData data) => SetIdleAnimation(data, looping: false);

    [EntityInput("SetDefaultAnimation")]
    private void InputSetDefaultAnimation(EntityInputData data) => SetIdleAnimation(data, looping: null);

    [EntityInput("SetPlaybackRate")]
    private void InputSetPlaybackRate(EntityInputData data)
    {
        // Playing backwards is not simulated, so a negative rate holds the pose instead
        ModelNode?.AnimationController.FrametimeMultiplier = MathF.Max(data.Float(1f), 0f);
    }

    [EntityInput("TurnOn")] private void InputTurnOn(EntityInputData data) => IsDrawn = true;

    [EntityInput("TurnOff")] private void InputTurnOff(EntityInputData data) => IsDrawn = false;

    [EntityInput("EnableCollision")] private void InputEnableCollision(EntityInputData data) => IsSolid = hasCollision;

    [EntityInput("DisableCollision")] private void InputDisableCollision(EntityInputData data) => IsSolid = false;

    private void PlayForcedAnimation(EntityInputData data, bool? looping, bool restart)
    {
        if (NonEmpty(data.Parameter) is { } name)
        {
            PlayAnimation(name, looping, restart, forced: true);
        }
    }

    private void SetIdleAnimation(EntityInputData data, bool? looping)
    {
        IdleAnimation = NonEmpty(data.Parameter);
        idleLooping = looping;

        // An animation still playing hands over when it ends; one that already has is replaced now
        if (!isWaitingForAnimationEnd && ModelNode is { } node && HasAnimationEnded(node.AnimationController))
        {
            ReturnToIdle();
        }
    }

    private void ReturnToIdle()
    {
        if (HoldAnimation || IdleAnimation == null || ModelNode is not { } node)
        {
            return;
        }

        // An idle that has run out itself stays on its last frame rather than restarting forever
        if (string.Equals(node.AnimationController.ActiveAnimation?.Name, IdleAnimation, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PlayAnimation(IdleAnimation, idleLooping, restart: true, forced: false);
    }

    /// <param name="name">The animation to play.</param>
    /// <param name="looping">Whether it loops; null takes a sequence's own flag, and loops anything else.</param>
    /// <param name="restart">Whether an animation already playing starts over, rather than being left alone.</param>
    /// <param name="forced">Whether an input asked for it, which is what the animation outputs report on.</param>
    private void PlayAnimation(string name, bool? looping, bool restart, bool forced)
    {
        if (ModelNode is not { HasMeshes: true } node)
        {
            return;
        }

        if (!node.Animations.TryGetValue(name, out var animation))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no animation \"{Animation}\"", Classname, TargetName, name);
            return;
        }

        var controller = node.AnimationController;

        if (!restart && controller.ActiveAnimation == animation)
        {
            return;
        }

        var loops = looping ?? (animation is not SequenceAnimation sequence || sequence.IsLooping);

        // An input's animation fades in over the sequence's own fade-in, as a sequence change does
        var blendTime = 0f;

        if (forced && controller.ActiveAnimation != null && animation is SequenceAnimation fadingSequence)
        {
            blendTime = fadingSequence.SequenceParams.FadeInTime;
        }

        controller.Looping = loops;
        node.SetAnimation(animation, blendTime);

        // A non-looping animation that ran out paused the whole player, which a new one does not undo
        controller.IsPaused = false;

        isForcedAnimation = forced;
        isWaitingForAnimationEnd = !loops;

        if (isWaitingForAnimationEnd)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }

        if (forced)
        {
            EntitySystem.TriggerOutput(this, "OnAnimationBegun");
        }
    }

    // A single-frame animation never advances, so it has ended as soon as it has started
    private static bool HasAnimationEnded(AnimationController controller)
        => controller.ActiveClipFinished || (controller.ActiveAnimation is { FrameCount: <= 1 } && !controller.Looping);

    // Absent, or CS2's "use sequence settings", leaves it to the animation
    private bool? ReadLoopMode(string key) => KeyValues.GetStringProperty(key) switch
    {
        "ANIM_LOOP_MODE_LOOPING" => true,
        "ANIM_LOOP_MODE_NOT_LOOPING" => false,
        _ => null,
    };

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
