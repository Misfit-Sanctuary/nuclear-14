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
                var maxAway = distance >= harpooned.MaxRopeLength
                    ? 0f
                    : harpooned.StruggleModifier * GetMoveSpeed(uid);

                if (-along > maxAway)
                {
                    velocity += toward * (-along - maxAway);
                    along = -maxAway;
                }
            }

            var pull = 0f;
            if (harpooned.Reeling && distance > harpooned.MinDistance)
                pull = harpooned.ReelSpeed * MathF.Min(1f, distance - harpooned.MinDistance);

            if (distance > harpooned.MaxRopeLength)
                pull = MathF.Max(pull, harpooned.ReelSpeed + (distance - harpooned.MaxRopeLength) * LeashStiffness);

            if (along < pull)
                velocity += toward * (pull - along);

            if (velocity != body.LinearVelocity)
                _physics.SetLinearVelocity(uid, velocity, body: body);
        }
    }

    private float GetMoveSpeed(EntityUid uid)
    {
        var sprinting = !TryComp<InputMoverComponent>(uid, out var mover) || mover.Sprinting;
        if (!TryComp<MovementSpeedModifierComponent>(uid, out var speed))
            return sprinting ? MovementSpeedModifierComponent.DefaultBaseSprintSpeed : MovementSpeedModifierComponent.DefaultBaseWalkSpeed;

        return sprinting ? speed.CurrentSprintSpeed : speed.CurrentWalkSpeed;
    }
}
