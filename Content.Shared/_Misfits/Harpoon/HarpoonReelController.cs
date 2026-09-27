using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Misfits.Harpoon;

public sealed class HarpoonReelController : VirtualController
{
    [Dependency] private HarpoonSystem _harpoon = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float LeashStiffness = 4f;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var query = EntityQueryEnumerator<HarpoonedComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var harpooned, out var body, out var xform))
        {
            if (prediction && !body.Predict)
                continue;

            if (harpooned.Thrower is not { } thrower
                || !TryComp<TransformComponent>(thrower, out var throwerXform)
                || throwerXform.MapID != xform.MapID
                || !_harpoon.CanBeHauled(uid, xform, body))
                continue;

            var delta = _transform.GetWorldPosition(throwerXform) - _transform.GetWorldPosition(xform);
            var distance = delta.Length();
            if (distance < 0.01f)
                continue;

            var toward = delta / distance;
            var velocity = body.LinearVelocity;
            var along = Vector2.Dot(velocity, toward);

            if (along < 0f)
            {
                var maxAway = float.MaxValue;
                if (distance >= harpooned.MaxRopeLength)
                    maxAway = 0f;
                else if (TryComp<InputMoverComponent>(uid, out var mover))
                    maxAway = harpooned.StruggleModifier * GetMoveSpeed(uid, mover);

                if (-along > maxAway)
                {
                    velocity += toward * (-along - maxAway);
                    along = -maxAway;
                }
            }

            var pull = 0f;
            if (harpooned.Reeling && distance > harpooned.MinDistance)
            {
                var resist = GetStruggleSpeed(uid, -toward) * harpooned.StruggleModifier;
                pull = MathF.Max(0f, harpooned.ReelSpeed * MathF.Min(1f, distance - harpooned.MinDistance) - resist);
            }

            if (distance > harpooned.MaxRopeLength)
                pull = MathF.Max(pull, harpooned.ReelSpeed + (distance - harpooned.MaxRopeLength) * LeashStiffness);

            if (pull > 0f && along < pull)
                velocity += toward * (pull - along);

            if (velocity != body.LinearVelocity)
                _physics.SetLinearVelocity(uid, velocity, body: body);
        }
    }

    private float GetStruggleSpeed(EntityUid uid, Vector2 away)
    {
        if (!TryComp<InputMoverComponent>(uid, out var mover) || !mover.CanMove)
            return 0f;

        var (walk, sprint) = _mover.GetVelocityInput(mover);
        var input = _mover.GetParentGridAngle(mover).RotateVec(walk + sprint);
        if (input.LengthSquared() < 0.01f)
            return 0f;

        return MathF.Max(0f, Vector2.Dot(input.Normalized(), away)) * GetMoveSpeed(uid, mover);
    }

    private float GetMoveSpeed(EntityUid uid, InputMoverComponent mover)
    {
        if (!TryComp<MovementSpeedModifierComponent>(uid, out var speed))
            return mover.Sprinting ? MovementSpeedModifierComponent.DefaultBaseSprintSpeed : MovementSpeedModifierComponent.DefaultBaseWalkSpeed;

        return mover.Sprinting ? speed.CurrentSprintSpeed : speed.CurrentWalkSpeed;
    }
}
