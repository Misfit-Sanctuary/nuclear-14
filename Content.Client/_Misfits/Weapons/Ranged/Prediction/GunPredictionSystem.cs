using System.Linq;
using Content.Client.Projectiles;
using Content.Shared._Misfits.Weapons.Ranged.Prediction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Toolshed.Commands.Values;

namespace Content.Client._Misfits.Weapons.Ranged.Prediction;

public sealed partial class GunPredictionSystem : SharedGunPredictionSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ProjectileSystem _projectile = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _pendingProjectileDeletes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
        SubscribeLocalEvent<RequestShootEvent>(OnShootRequest);
        SubscribeNetworkEvent<MaxLinearVelocityMsg>(OnLinearVelocityMsg);

        SubscribeLocalEvent<PredictedProjectileClientComponent, UpdateIsPredictedEvent>(OnClientProjectileUpdateIsPredicted);
        SubscribeLocalEvent<PredictedProjectileClientComponent, PreventCollideEvent>(OnClientProjectilePreventCollide);
        SubscribeLocalEvent<PredictedProjectileClientComponent, StartCollideEvent>(OnClientProjectileStartCollide);
        SubscribeLocalEvent<PredictedProjectileServerComponent, ComponentStartup>(OnServerProjectileStartup);
        // Misfits Add:
        SubscribeNetworkEvent<ProjectileDeflectMsg>(OnServerProjectileReflected);

        UpdatesBefore.Add(typeof(TransformSystem));
    }
    /// <summary>
    /// Misfits added: server sends msg that projectile is reflected on <see cref="Shared.ReflectSystem.TryReflectProjectile">
    /// Client has its own predicted client version of that projectile that is visible.
    /// The invisible server projectile is only updated when server networks its state(delayed)
    /// the predicted projectile is deleted on impact and doesnt follow reflect code(seemingly)
    /// So on deflect we know the client proj is deleted and so look for and find the server projectile
    /// and make it visible
    /// This is temporary until guncode can be refactored and psuedo rng for this can be done
    /// so people are not being lied to by the client's fake bullets when theyre hit by invisible ones
    /// tho this does add a noticable delay to deflects
    /// </summary>
    private void OnServerProjectileReflected(ProjectileDeflectMsg ev)
    {

        if (TryGetEntity(ev.DeflectedEnt, out var deflectedEnt) && TryComp(deflectedEnt, out SpriteComponent? sprite))
            sprite.Visible = true;

    }
    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            predicted.Coordinates = Transform(uid).Coordinates;
        }
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent ev)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            if (_timing.IsFirstTimePredicted)
                continue;

            if (predicted.Coordinates is { } coordinates)
                _transform.SetCoordinates(uid, coordinates);

            predicted.Coordinates = null;
        }
    }

    private void OnShootRequest(RequestShootEvent ev, EntitySessionEventArgs args)
    {
        if (_timing.IsFirstTimePredicted)
            return;

        _gun.ShootRequested(ev.Gun, ev.Coordinates, ev.Target, null, args.SenderSession);
    }

    private void OnLinearVelocityMsg(MaxLinearVelocityMsg ev)
    {
        _config.SetCVar(CVars.MaxLinVelocity, ev.Velocity);
    }

    private void OnClientProjectileUpdateIsPredicted(Entity<PredictedProjectileClientComponent> ent, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    private void OnClientProjectilePreventCollide(Entity<PredictedProjectileClientComponent> _, ref PreventCollideEvent args)
    {
        if (HasComp<PredictedPhysicsComponent>(args.OtherEntity))
            args.Cancelled = true;
    }
    // TODO MISFITS: refactor
    private void OnClientProjectileStartCollide(Entity<PredictedProjectileClientComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.Hit ||
            args.OurFixtureId != SharedProjectileSystem.ProjectileFixture ||
            !args.OtherFixture.Hard)
        {
            return;
        }

        if (!TryComp(ent, out ProjectileComponent? projectile) ||
            !TryComp(ent, out PhysicsComponent? physics))
        {
            return;
        }

        var netEnt = GetNetEntity(args.OtherEntity);
        var pos = _transform.GetMapCoordinates(args.OtherEntity);
        var hit = new HashSet<(NetEntity, MapCoordinates)> { (netEnt, pos) };
        RaiseNetworkEvent(new PredictedProjectileHitEvent(ent.Owner.Id, hit));

        ent.Comp.Hit = true;

        _projectile.ProjectileCollide((ent, projectile, physics), args.OtherEntity, predicted: true);
        if (projectile.DeleteOnCollide)
        {
            _pendingProjectileDeletes.Add(ent.Owner);
            return;
        }
        // clientside deflected projectiles have these reset for the next collide which could be a reflect
        // projectile.DeleteOnCollide = true;
        // ent.Comp.Hit = false;
    }

    private void OnServerProjectileStartup(Entity<PredictedProjectileServerComponent> ent, ref ComponentStartup _)
    {
        if (!GunPrediction)
            return;

        if (ent.Comp.ClientEnt == _player.LocalEntity && TryComp(ent, out SpriteComponent? sprite))
            sprite.Visible = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        foreach (var uid in _pendingProjectileDeletes)
        {
            if (Exists(uid))
                QueueDel(uid);
        }
        _pendingProjectileDeletes.Clear();

        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, ProjectileComponent, PhysicsComponent>();
        while (projectiles.MoveNext(out var uid, out var predicted, out var projectile, out var physics))
        {
            if (predicted.Hit)
                continue;

            var contacts = _physics.GetContactingEntities(uid, physics, true);
            if (contacts.Count == 0)
                continue;

            var hit = new HashSet<(NetEntity, MapCoordinates)>();
            foreach (var contact in contacts)
            {
                var netEnt = GetNetEntity(contact);
                var pos = _transform.GetMapCoordinates(contact);
                hit.Add((netEnt, pos));
            }

            RaiseNetworkEvent(new PredictedProjectileHitEvent(uid.Id, hit));
            predicted.Hit = true;
            _projectile.ProjectileCollide((uid, projectile, physics), contacts.First());
        }

        var predictedQuery = EntityQueryEnumerator<PredictedProjectileHitComponent, SpriteComponent, TransformComponent>();
        while (predictedQuery.MoveNext(out _, out var hit, out var sprite, out var xform))
        {
            var origin = hit.Origin;
            var coordinates = xform.Coordinates;
            if (!origin.TryDistance(EntityManager, _transform, coordinates, out var distance) ||
                distance >= hit.Distance)
            {
                sprite.Visible = false;
            }
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (projectiles.MoveNext(out _, out var xform))
        {
            xform.ActivelyLerping = false;
        }
    }
}
