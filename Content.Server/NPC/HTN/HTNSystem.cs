using System.Threading;
using Content.Server.Administration.Managers;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared._Misfits.CCVar; // #Misfits Add - CVar gate for ReplanRate
using Content.Shared.Mobs;
using Content.Shared.NPC;
using JetBrains.Annotations;
using Robust.Server.GameObjects; // Corvax
using Robust.Shared.Configuration; // #Misfits Add - CVar gate for ReplanRate
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Server.Worldgen; // Corvax
using Content.Server.Worldgen.Components; // Corvax
using Content.Server.Worldgen.Systems; // Corvax
// Misfit: unused
// using Robust.Server.GameObjects; // Corvax
// using Content.Shared.Administration;
// using System.Linq;
// using System.Text;


namespace Content.Server.NPC.HTN;
/// <summary>
///
/// HTN: Hierarchical Task Network
/// System that handles the AI of NPCs every tick
/// ticked/updated by <see cref="NPCSystem"/>
///
/// </summary>
public sealed partial class HTNSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!; // #Misfits Add - CVar gate for ReplanRate
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NPCUtilitySystem _utility = default!;
    [Dependency] private readonly WorldControllerSystem _world = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    private EntityQuery<WorldControllerComponent> _mapQuery;
    private EntityQuery<LoadedChunkComponent> _loadedQuery;

    private readonly JobQueue _planQueue = new(0.004);

    private readonly HashSet<ICommonSession> _subscribers = new();

    // #Misfits Change — ReplanRate was forced to a const 7f Hz to cut combat response latency.
    // Reverted to upstream 5f default and surfaced as CVar `misfits.htn_replan_rate` so ops
    // can tune without a rebuild. At 150+ pop on constrained VPS hardware the extra 2 Hz of
    // HTN work across every active NPC was measurable; 5 Hz is the safer baseline.
    private float _replanRate = 5f; // per second, CVar-driven
    private float _accumulator; // limit replanning rate

    // Hierarchical Task Network
    public override void Initialize()
    {
        base.Initialize();
        _mapQuery = GetEntityQuery<WorldControllerComponent>(); // Corvax
        _loadedQuery = GetEntityQuery<LoadedChunkComponent>(); // Corvax
        // #Misfits Add - Live-track the replan rate CVar (initial fire + updates).
        Subs.CVar(_cfg, PerformanceCVars.HTNReplanRate, v => _replanRate = v > 0f ? v : 1f, true);
        SubscribeLocalEvent<HTNComponent, MobStateChangedEvent>(_npc.OnMobStateChange);
        SubscribeLocalEvent<HTNComponent, MapInitEvent>(_npc.OnNPCMapInit);
        SubscribeLocalEvent<HTNComponent, PlayerAttachedEvent>(_npc.OnPlayerNPCAttach);
        SubscribeLocalEvent<HTNComponent, PlayerDetachedEvent>(_npc.OnPlayerNPCDetach);
        SubscribeLocalEvent<HTNComponent, ComponentShutdown>(OnHTNShutdown);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypeLoad);
        // Misfit Change: debug stuff added to its own file
        DebugInit();
        OnLoad();
    }
    [PublicAPI]
    public void Replan(HTNComponent component)
    {
        component.NextPlanTime = _gameTiming.CurTime;
    }



    /// <summary>
    /// (Re)Initialization of HTN system parts TODO: better summary
    ///
    /// NPCs with HTNComp are forced reset and queued new jobs
    /// Also where all NPCs are initially given planningJobs
    /// and where HTN primitives and compounds are loaded and
    /// operators injected
    /// </summary>
    private void OnLoad()
    {
        // Clear all NPCs in case they're hanging onto stale tasks
        var query = AllEntityQuery<HTNComponent>();

        while (query.MoveNext(out var comp))
        {
            comp.PlanningToken?.Cancel();
            comp.PlanningToken = null;

            // I guess logic here is that a null plan will already be replanned later on
            if (comp.Plan != null)
            {

                var currentOperator = comp.Plan.CurrentOperator;
                ShutdownTask(currentOperator, comp.Blackboard, HTNOperatorStatus.Failed);
                ShutdownPlan(comp);
                comp.Plan = null;
                RequestPlan(comp);
            }
        }

        // Add dependencies for all operators.
        // We put code on operators as I couldn't think of a clean way to put it on systems.
        /// Misfit: I mean that's what operators are supposed to be ^^.
        /// Place where the code is and not the planning/tasks that hold the code and decide when it runs
        foreach (var compound in _prototypeManager.EnumeratePrototypes<HTNCompoundPrototype>())
        {
            UpdateCompound(compound);
        }
    }

    private void OnPrototypeLoad(PrototypesReloadedEventArgs obj)
    {
        OnLoad();
    }

    private void UpdateCompound(HTNCompoundPrototype compound)
    {
        for (var i = 0; i < compound.Branches.Count; i++)
        {
            var branch = compound.Branches[i];

            foreach (var precon in branch.Preconditions)
            {
                precon.Initialize(EntityManager.EntitySysManager);
            }

            foreach (var task in branch.Tasks)
            {
                UpdateTask(task);
            }
        }
    }

    private void UpdateTask(HTNTask task)
    {
        switch (task)
        {
            case HTNCompoundTask:
                // NOOP, handled elsewhere
                break;
            case HTNPrimitiveTask primitive:
                foreach (var precon in primitive.Preconditions)
                {
                    precon.Initialize(EntityManager.EntitySysManager);
                }

                primitive.Operator.Initialize(EntityManager.EntitySysManager);
                break;
            default:
                throw new NotImplementedException();
        }
    }

    private void OnHTNShutdown(EntityUid uid, HTNComponent component, ComponentShutdown args)
    {
        _npc.OnNPCShutdown(uid, component, args);
        component.PlanningToken?.Cancel();
        component.PlanningJob = null;
    }


    /// <summary>
    /// Starts all planning, handling only newly completed plans on active NPCs
    /// decides if new plans should replace current executing NPC plan
    /// Called by NPCsystem every tick
    /// </summary>
    /// <param name="count"> number of NPCs updated so far </param>
    /// <param name="maxUpdates"> max NPCs to update </param>
    public void UpdateNPC(ref int count, int maxUpdates, float frameTime)
    {
        /// handles and runs each NPC planner
        ///
        _planQueue.Process();

        // Limit update rate
        // #Misfits Tweak - Was `const float updatePeriod = 1/ReplanRate;` when ReplanRate
        // was a const. Now computed per-call from the CVar-driven field.
        var updatePeriod = 1f / _replanRate;
        _accumulator += frameTime;
        if (_accumulator < updatePeriod)
            return;
        _accumulator -= updatePeriod;

        var query = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent>();

        while (query.MoveNext(out var uid, out _, out var comp))
        {
            // If we're over our max count or it's not MapInit then ignore the NPC.
            if (count >= maxUpdates)
                break;

            // Misfit change: Redundant check. Query will only get ents with ActiveNPCComp AND HTNComp
            /*
            if (!IsNPCActive(uid))  // Corvax
                continue;
            */
            // Misfit End
            if (comp.PlanningJob != null)
            {
                if (comp.PlanningJob.Exception != null)
                {
                    Log.Fatal($"Received exception on planning job for {uid}!");
                    _npc.SleepNPC(uid);
                    var exc = comp.PlanningJob.Exception;
                    RemComp<HTNComponent>(uid);
                    throw exc;
                }

                // If a new planning job has finished then handle it.
                if (comp.PlanningJob.Status != JobStatus.Finished)
                    continue;

                var newPlanBetter = false;

                /// TODO: explain how this works better and push its functionality to HTNPlanJob
                /// WE IGNORE NEW PLANS IF THEIR MTR(method traversal record) IS HIGHER

                // If old traversal is better than new traversal then ignore the new plan
                if (comp.Plan != null && comp.PlanningJob.Result != null)
                {
                    var oldMtr = comp.Plan.BranchTraversalRecord;
                    var mtr = comp.PlanningJob.Result.BranchTraversalRecord;

                    for (var i = 0; i < oldMtr.Count; i++)
                    {
                        if (i < mtr.Count && oldMtr[i] > mtr[i])
                        {
                            newPlanBetter = true;
                            break;
                        }
                    }
                }
                /// TODO:  MAKE OWN METHOD AND CLEAN UP!
                if (comp.Plan == null || newPlanBetter)
                {
                    comp.CheckServices = false;

                    if (comp.Plan != null)
                    {
                        ShutdownTask(comp.Plan.CurrentOperator, comp.Blackboard, HTNOperatorStatus.BetterPlan);
                        ShutdownPlan(comp);
                    }

                    comp.Plan = comp.PlanningJob.Result;

                    // Startup the first task and anything else we need to do.
                    if (comp.Plan != null)
                    {
                        StartupTask(comp.Plan.Tasks[comp.Plan.Index], comp.Blackboard, comp.Plan.Effects[comp.Plan.Index]);
                    }
                }
                // Keeping old plan
                else
                {
                    comp.CheckServices = true;
                }
                /// get rid of completed planned job
                /// because we are sticking with currently executing plan
                comp.PlanningJob = null;
                comp.PlanningToken = null;
            }

            Update(comp, frameTime);
            count++;
        }
    }
    // Corvax-start
    private bool IsNPCActive(EntityUid entity)
    {
        var transform = Transform(entity);

        if (!_mapQuery.TryGetComponent(transform.MapUid, out var worldComponent))
            return true;

        var chunk = _world.GetOrCreateChunk(WorldGen.WorldToChunkCoords(_transform.GetWorldPosition(transform)).Floored(), transform.MapUid.Value, worldComponent);

        return _loadedQuery.TryGetComponent(chunk, out var loaded) && loaded.Loaders is not null;
    }
    // Corvax-end

    private void Update(HTNComponent component, float frameTime)
    {
        // We'll still try re-planning occasionally even when we're updating in case new data comes in.
        /// what? ^^
        if (component.NextPlanTime <= _gameTiming.CurTime)
        {
            RequestPlan(component);
        }

        // Getting a new plan so do nothing.
        if (component.Plan == null)
            return;

        // Run the existing plan still
        var status = HTNOperatorStatus.Finished;

        // Continuously run operators until we can't anymore.
        while (status != HTNOperatorStatus.Continuing && component.Plan != null)
        {
            // Run the existing operator
            var currentOperator = component.Plan.CurrentOperator;
            var currentTask = component.Plan.CurrentTask;
            var blackboard = component.Blackboard;

            // Service still on cooldown.
            if (component.CheckServices)
            {
                foreach (var service in currentTask.Services)
                {
                    var serviceResult = _utility.GetEntities(blackboard, service.Prototype);
                    blackboard.SetValue(service.Key, serviceResult.GetHighest());
                }

                component.CheckServices = false;
            }

            status = currentOperator.Update(blackboard, frameTime);

            switch (status)
            {
                case HTNOperatorStatus.Continuing:
                    break;
                case HTNOperatorStatus.Failed:
                    ShutdownTask(currentOperator, blackboard, status);
                    ShutdownPlan(component);
                    break;
                // Operator completed so go to the next one.
                case HTNOperatorStatus.Finished:
                    ShutdownTask(currentOperator, blackboard, status);
                    component.Plan.Index++;

                    // Plan finished!
                    if (component.Plan.Tasks.Count <= component.Plan.Index)
                    {
                        ShutdownPlan(component);
                        break;
                    }

                    ConditionalShutdown(component.Plan, currentOperator, blackboard, HTNPlanState.TaskFinished);
                    StartupTask(component.Plan.Tasks[component.Plan.Index], component.Blackboard, component.Plan.Effects[component.Plan.Index]);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        HTNDebug(component);
    }

    // TODO: probably have a FORCED shutdown and also why is conditional shutdown literally
    // TODO: a secondary shutdown that runs with default one???
    /// <summary>
    ///  So this is shutting down the running task/current popped off stack
    /// task
    /// </summary>
    /// <param name="currentOperator">coded operation </param>
    /// <param name="blackboard"></param>
    /// <param name="status"></param>
    public void ShutdownTask(HTNOperator currentOperator, NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (currentOperator is IHtnConditionalShutdown conditional &&
            (conditional.ShutdownState & HTNPlanState.TaskFinished) != 0x0)
        {
            conditional.ConditionalShutdown(blackboard);
        }

        currentOperator.TaskShutdown(blackboard, status);
    }
    /// <summary>
    /// so THIS is shutting down all the other tasks in the stack(not ran/pop'd yet)
    /// WHY do we need to shutdown these?? They are not running yet, can't we
    /// do just Plan = null???
    /// Maybe reversing side effects?? but cant we just keep side effect from current task?
    /// also why do we run both conditional and plan shutdown no matter what???
    /// </summary>
    /// <param name="component"></param>
    public void ShutdownPlan(HTNComponent component)
    {
        DebugTools.Assert(component.Plan != null);
        var blackboard = component.Blackboard;

        foreach (var task in component.Plan.Tasks)
        {
            if (task.Operator is IHtnConditionalShutdown conditional &&
                (conditional.ShutdownState & HTNPlanState.PlanFinished) != 0x0)
            {
                conditional.ConditionalShutdown(blackboard);
            }

            task.Operator.PlanShutdown(component.Blackboard);
        }

        component.Plan = null;
    }

    /// <summary>
    /// Shuts down the current operator conditionally.
    /// different from other ones named the same. Used in regular update loop where we don't
    /// know immeditly if branch/task has a condition
    /// </summary>
    private void ConditionalShutdown(HTNPlan plan, HTNOperator currentOperator, NPCBlackboard blackboard, HTNPlanState state)
    {
        if (currentOperator is not IHtnConditionalShutdown conditional)
            return;

        if ((conditional.ShutdownState & state) == 0x0)
            return;

        conditional.ConditionalShutdown(blackboard);
    }

    /// <summary>
    /// Starts a new primitive task. Will apply effects from planning if applicable.
    /// </summary>
    /// TODO: Check what we actually need from startUp and if we need "planning only startup side effects" like this
    /// ie. why cant we just have this check in a branch's conditional? Why cant we have the side effect just carry
    /// over from previous tasks or just be informed by world state like normal?
    private void StartupTask(HTNPrimitiveTask primitive, NPCBlackboard blackboard, Dictionary<string, object>? effects)
    {
        // We may have planner only tasks where we want to reuse their data during update
        // e.g. if we pathfind to an enemy to know if we can attack it, we don't want to do another pathfind immediately
        /// Past me: So why not just have a higher priority branch for that????^^^^
        ///         why offload that work to startup???
        if (effects != null && primitive.ApplyEffectsOnStartup)
        {
            foreach (var (key, value) in effects)
            {
                blackboard.SetValue(key, value);
            }
        }

        primitive.Operator.Startup(blackboard);
    }

    /// <summary>
    /// Request a new plan for this component, even if running an existing plan.
    ///
    /// instatiates HTNPlanJob and adds to planQueue
    /// </summary>
    /// <param name="component"></param>
    ///
    /// "Even if running an existing plan"
    /// yes so we will have a JobPlan running even while we are still
    /// executing tasks
    /// TODO: rename to something less misleading
    private void RequestPlan(HTNComponent component)
    {
        /// already planning so dont
        if (component.PlanningJob != null)
            return;

        component.NextPlanTime = _gameTiming.CurTime + TimeSpan.FromSeconds(component.PlanCooldown);
        var cancelToken = new CancellationTokenSource();
        var branchTraversal = component.Plan?.BranchTraversalRecord;
        ///
        var job = new HTNPlanJob(
            0.02,
            _prototypeManager,
            component.RootTask,
            component.Blackboard.ShallowClone(), branchTraversal, cancelToken.Token);

        _planQueue.EnqueueJob(job);
        component.PlanningJob = job;
        component.PlanningToken = cancelToken;
    }


}

/// <summary>
/// The outcome of the current operator during update.
/// </summary>
public enum HTNOperatorStatus : byte
{
    Continuing,
    Failed,
    Finished,

    /// <summary>
    /// Was a better plan than this found?
    /// </summary>
    BetterPlan,
}
