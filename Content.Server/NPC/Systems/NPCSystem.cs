using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Shared._NC.Mountable;
using Content.Shared._NC.Mountable.Components;
using Content.Shared._Misfits.NPC;
using Content.Shared._Misfits.NPC.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Pointing;
using Content.Shared.Projectiles;
using Content.Server.Weapons.Ranged.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Systems
{
    /// <summary>
    ///     Handles NPCs running every tick.
    /// </summary>
    public sealed partial class NPCSystem : EntitySystem
    {
        private const string IdleTimeKey = "IdleTime";
        private const string FollowIdleTargetKey = "FollowIdleTarget";
        private const string HoldPositionCompoundId = "HoldPositionCompound";
        private const float AutoHoldResumeRange = 3f;
        private const float NeutralDetectionInterval = 0.5f;
        private const float NeutralDetectionRange = 8f;

        private float _neutralDetectionAccumulator;

        private readonly Dictionary<string, (string Follow, string Passive, string Neutral)> _compoundFamilies = new();

        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly HTNSystem _htn = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
        [Dependency] private readonly NPCRetaliationSystem _npcRetaliation = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;
        [Dependency] private readonly SharedSpecialSystem _special = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!;

        /// <summary>
        /// Whether any NPCs are allowed to run at all.
        /// </summary>
        public bool Enabled { get; set; } = true;

        private int _maxUpdates;

        private int _count;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            Subs.CVar(_configurationManager, CCVars.NPCEnabled, value => Enabled = value, true);
            Subs.CVar(_configurationManager, CCVars.NPCMaxUpdates, obj => _maxUpdates = obj, true);
            BuildCompoundFamilies();
            SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

            SubscribeLocalEvent<GetVerbsEvent<Verb>>(AddNpcOrderVerbs);
            SubscribeLocalEvent<FollowerCommanderComponent, AttackAttemptEvent>(OnCommanderAttackAttempt);
            SubscribeLocalEvent<FollowerCommanderComponent, DamageChangedEvent>(OnCommanderDamaged);
            SubscribeLocalEvent<FollowerCommanderComponent, DisarmedEvent>(OnCommanderDisarmed);
            SubscribeLocalEvent<FollowerCommanderComponent, AfterPointedAtEvent>(OnCommanderPointedAt);
            SubscribeLocalEvent<ProjectileComponent, AfterProjectileHitEvent>(OnCommanderProjectileHit);
            SubscribeLocalEvent<FollowerCommanderComponent, HitscanHitEntityEvent>(OnCommanderHitscanHit);
            SubscribeLocalEvent<RecruitedFollowerComponent, DamageChangedEvent>(OnFollowerDamaged);
            SubscribeLocalEvent<RecruitedFollowerComponent, ComponentShutdown>(OnRecruitedFollowerShutdown);
            SubscribeLocalEvent<RecruitedFollowerComponent, MountMovementControlAttemptEvent>(OnFollowerMountMovementControlAttempt);
            SubscribeLocalEvent<FollowerCommanderComponent, ComponentStartup>(OnCommanderStartup);
            SubscribeAllEvent<IssueFollowerOrderMessage>(OnIssueFollowerOrder);
            SubscribeLocalEvent<FollowerAutoRecruitComponent, ComponentStartup>(OnFollowerAutoRecruitStartup);
        }

        public void OnPlayerNPCAttach(EntityUid uid, HTNComponent component, PlayerAttachedEvent args)
        {
            SleepNPC(uid, component);
        }

        public void OnPlayerNPCDetach(EntityUid uid, HTNComponent component, PlayerDetachedEvent args)
        {
            if (_mobState.IsIncapacitated(uid) || TerminatingOrDeleted(uid))
                return;

            // This NPC has an attached mind, so it should not wake up.
            if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
                return;

            WakeNPC(uid, component);
        }

        public void OnNPCMapInit(EntityUid uid, HTNComponent component, MapInitEvent args)
        {
            component.Blackboard.SetValue(NPCBlackboard.Owner, uid);
            WakeNPC(uid, component);
        }

        public void OnNPCShutdown(EntityUid uid, HTNComponent component, ComponentShutdown args)
        {
            SleepNPC(uid, component);
        }

        /// <summary>
        /// Is the NPC awake and updating?
        /// </summary>
        public bool IsAwake(EntityUid uid, ActiveNPCComponent? active = null)
        {
            return Resolve(uid, ref active, false);
        }

        public bool TryGetNpc(EntityUid uid, [NotNullWhen(true)] out NPCComponent? component)
        {
            // If you add your own NPC components then add them here.

            if (TryComp<HTNComponent>(uid, out var htn))
            {
                component = htn;
                return true;
            }

            component = null;
            return false;
        }

        /// <summary>
        /// Allows the NPC to actively be updated.
        /// </summary>
        public void WakeNPC(EntityUid uid, HTNComponent? component = null)
        {
            if (!Resolve(uid, ref component, false))
            {
                return;
            }

            Log.Debug($"Waking {ToPrettyString(uid)}");
            EnsureComp<ActiveNPCComponent>(uid);
        }

        public void SleepNPC(EntityUid uid, HTNComponent? component = null)
        {
            if (!Resolve(uid, ref component, false))
            {
                return;
            }

            // Don't bother with an event
            if (TryComp<HTNComponent>(uid, out var htn))
            {
                if (htn.Plan != null)
                {
                    var currentOperator = htn.Plan.CurrentOperator;
                    _htn.ShutdownTask(currentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                    _htn.ShutdownPlan(htn);
                    htn.Plan = null;
                }
            }

            Log.Debug($"Sleeping {ToPrettyString(uid)}");
            RemComp<ActiveNPCComponent>(uid);
        }

        /// <inheritdoc />
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (!Enabled)
                return;

            _count = 0;
            // Add your system here.
            _htn.UpdateNPC(ref _count, _maxUpdates, frameTime);

            UpdateFollowerNoPathTimeout(frameTime);
            UpdateAutoHeldFollowers();
            UpdateNeutralEscortDetection(frameTime);
            UpdateGridTeleports(frameTime);
        }

        private List<EntityUid>? _followersToHold;

        private void UpdateFollowerNoPathTimeout(float frameTime)
        {
            var query = EntityQueryEnumerator<RecruitedFollowerComponent, HTNComponent>();
            while (query.MoveNext(out var follower, out var recruited, out var htn))
            {
                if (recruited.Order == FollowerOrderType.HoldPosition)
                {
                    recruited.NoPathAccumulator = 0f;
                    continue;
                }

                if (TryComp<NPCSteeringComponent>(follower, out var steering) &&
                    steering.Status == SteeringStatus.NoPath)
                {
                    recruited.NoPathAccumulator += frameTime;
                }
                else
                {
                    recruited.NoPathAccumulator = 0f;
                }

                if (recruited.NoPathAccumulator >= recruited.NoPathTimeoutSeconds)
                {
                    _followersToHold ??= new List<EntityUid>();
                    _followersToHold.Add(follower);
                }
            }

            if (_followersToHold == null)
                return;

            foreach (var follower in _followersToHold)
            {
                if (!TryComp<RecruitedFollowerComponent>(follower, out var recruited) ||
                    !TryComp<HTNComponent>(follower, out var htn))
                    continue;

                recruited.NoPathAccumulator = 0f;
                ApplyFollowerOrder(follower, recruited.Commander, htn, recruited, FollowerOrderType.HoldPosition);
                recruited.WasAutoHeld = true;
                _popup.PopupEntity(Loc.GetString("npc-follower-lost"), follower, recruited.Commander, PopupType.Small);
            }

            _followersToHold.Clear();
        }

        private void UpdateNeutralEscortDetection(float frameTime)
        {
            _neutralDetectionAccumulator += frameTime;
            if (_neutralDetectionAccumulator < NeutralDetectionInterval)
                return;
            _neutralDetectionAccumulator = 0f;

            var commanders = new Dictionary<EntityUid, (Vector2 Pos, NpcFactionMemberComponent? Faction)>();
            var followerQuery = EntityQueryEnumerator<RecruitedFollowerComponent>();
            while (followerQuery.MoveNext(out _, out var recruited))
            {
                if (recruited.Order != FollowerOrderType.Neutral || commanders.ContainsKey(recruited.Commander))
                    continue;
                if (TerminatingOrDeleted(recruited.Commander))
                    continue;
                TryComp<NpcFactionMemberComponent>(recruited.Commander, out var commanderFaction);
                commanders[recruited.Commander] = (_transform.GetWorldPosition(recruited.Commander), commanderFaction);
            }

            if (commanders.Count == 0)
                return;

            var handled = new HashSet<EntityUid>();
            var targetQuery = EntityQueryEnumerator<NpcFactionMemberComponent, MobStateComponent>();
            while (targetQuery.MoveNext(out var target, out var targetFaction, out _))
            {
                if (handled.Count == commanders.Count)
                    break;
                if (!_mobState.IsAlive(target))
                    continue;

                var targetPos = _transform.GetWorldPosition(target);

                foreach (var (commander, (commanderPos, commanderFaction)) in commanders)
                {
                    if (handled.Contains(commander) || target == commander)
                        continue;
                    if ((targetPos - commanderPos).Length() > NeutralDetectionRange)
                        continue;
                    if (!_npcFaction.IsEntityHostile((target, targetFaction), (commander, commanderFaction)))
                        continue;

                    IssueNeutralEscortTarget(commander, target, "ProximityDetection");
                    handled.Add(commander);
                }
            }
        }

        private void UpdateAutoHeldFollowers()
        {
            var query = EntityQueryEnumerator<RecruitedFollowerComponent, HTNComponent>();
            while (query.MoveNext(out var follower, out var recruited, out var htn))
            {
                if (!recruited.WasAutoHeld || recruited.Order != FollowerOrderType.HoldPosition)
                    continue;
                if (TerminatingOrDeleted(recruited.Commander))
                    continue;
                var dist = (_transform.GetWorldPosition(follower) - _transform.GetWorldPosition(recruited.Commander)).Length();
                if (dist <= AutoHoldResumeRange)
                    ApplyFollowerOrder(follower, recruited.Commander, htn, recruited, FollowerOrderType.Follow);
            }
        }

        public void OnFollowerWarped(EntityUid follower)
        {
            if (!TryComp<RecruitedFollowerComponent>(follower, out var recruited) ||
                !TryComp<HTNComponent>(follower, out var htn))
                return;

            if (recruited.WasAutoHeld)
                ApplyFollowerOrder(follower, recruited.Commander, htn, recruited, FollowerOrderType.Follow);
            else
                _htn.Replan(htn);
        }

        public void OnMobStateChange(EntityUid uid, HTNComponent component, MobStateChangedEvent args)
        {
            if (HasComp<ActorComponent>(uid))
                return;

            switch (args.NewMobState)
            {
                case MobState.Alive:
                    WakeNPC(uid, component);
                    break;
                case MobState.Critical:
                case MobState.Dead:
                    SleepNPC(uid, component);
                    break;
            }
        }

        private void AddNpcOrderVerbs(GetVerbsEvent<Verb> args)
        {
            if (!args.CanAccess || !args.CanInteract || args.User == args.Target || HasComp<ActorComponent>(args.Target))
                return;

            if (TryComp<NpcFactionMemberComponent>(args.Target, out var member)
                    && member.FriendlyOrderable
                    && _npcFaction.IsEntityFriendly(args.Target, args.User))
            {
                var start = new Verb()
                {
                    Text = Loc.GetString("npc-order-start"),
                    Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/sentient.svg.192dpi.png")),
                    Act = () => WakeNPC(args.Target)
                };

                var stop = new Verb()
                {
                    Text = Loc.GetString("npc-order-stop"),
                    Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/sentient.svg.192dpi.png")),
                    Act = () => SleepNPC(args.Target)
                };

                if (IsAwake(args.Target))
                {
                    args.Verbs.Add(stop);
                }
                else
                {
                    args.Verbs.Add(start);
                }
            }

            if (!TryComp<HTNComponent>(args.Target, out var htn) ||
                !TryComp<NpcFactionMemberComponent>(args.Target, out member))
            {
                return;
            }

            if (TryComp<RecruitedFollowerComponent>(args.Target, out var recruited))
            {
                if (recruited.Commander != args.User)
                    return;

                var stopFollowing = new Verb
                {
                    Text = Loc.GetString("npc-order-stop-following-neutral"),
                    Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/close.svg.192dpi.png")),
                    Act = () => StopFollowingUser(args.Target, args.User, htn)
                };

                args.Verbs.Add(stopFollowing);
                return;
            }

            if (!CanRecruitNeutralFollower(args.User, args.Target, htn, member))
                return;

            var currentCount = GetFollowerCountForCommander(args.User);
            var maxCount = GetNeutralFollowerCapacity(args.User);

            var follow = new Verb()
            {
                Text = Loc.GetString("npc-order-follow-neutral"),
                Message = Loc.GetString("npc-order-follow-neutral-count", ("current", currentCount), ("max", maxCount)),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
                Act = () => StartFollowingUser(args.Target, args.User, htn)
            };

            args.Verbs.Add(follow);
        }

        private bool CanRecruitNeutralFollower(EntityUid user, EntityUid target, HTNComponent htn, NpcFactionMemberComponent targetFaction)
        {
            if (!TryComp<NpcFactionMemberComponent>(user, out var userFaction))
                return false;

            if (TryComp<RecruitedFollowerComponent>(target, out var recruited) &&
                recruited.Commander != user)
            {
                return false;
            }

            var followerCount = GetFollowerCountForCommander(user);
            var followerCapacity = GetNeutralFollowerCapacity(user);
            if (followerCount >= followerCapacity)
                return false;

            if (_npcFaction.IsEntityFriendly((target, targetFaction), (user, userFaction)))
                return false;

            if (_npcFaction.IsEntityHostile((target, null), (user, userFaction)) ||
                _npcFaction.IsEntityHostile((user, null), (target, targetFaction)))
            {
                return false;
            }

            var rootTask = htn.RootTask.Task;
            if (!SupportsOrderedFollowRoot(rootTask))
                return false;

            return true;
        }

        private void StartFollowingUser(EntityUid target, EntityUid user, HTNComponent htn)
        {
            if (TryComp<RecruitedFollowerComponent>(target, out var existingRecruited))
            {
                if (existingRecruited.Commander != user)
                    return;

                return;
            }

            var followerCount = GetFollowerCountForCommander(user);
            var followerCapacity = GetNeutralFollowerCapacity(user);
            if (followerCount >= followerCapacity)
            {
                _popup.PopupEntity(Loc.GetString("npc-order-follow-cap-reached"), target, user, PopupType.Small);
                return;
            }

            _npcFaction.AddFriendlyEntity(target, user);
            _npcFaction.IgnoreEntity(target, user);
            var recruited = EnsureComp<RecruitedFollowerComponent>(target);
            recruited.Commander = user;
            if (string.IsNullOrEmpty(recruited.OriginalRootTask))
                recruited.OriginalRootTask = htn.RootTask.Task;

            ApplyFollowerOrder(target, user, htn, recruited, FollowerOrderType.Follow);
            EstablishCoFollowerIgnores(target, user);
            UpdateCommanderFollowerCount(user);
            _popup.PopupEntity(Loc.GetString("npc-order-followed-neutral"), target, user, PopupType.Small);
            _chat.TrySendInGameICMessage(user, Loc.GetString("npc-order-response-recruit"), InGameICChatType.Speak, false);
        }

        private void OnFollowerAutoRecruitStartup(Entity<FollowerAutoRecruitComponent> ent, ref ComponentStartup args)
        {
            var pet = ent.Owner;
            var commander = ent.Comp.Commander;
            RemCompDeferred<FollowerAutoRecruitComponent>(ent);

            if (!TryComp<HTNComponent>(pet, out var htn))
                return;

            _npcFaction.AddFriendlyEntity(pet, commander);
            _npcFaction.IgnoreEntity(pet, commander);

            var recruited = EnsureComp<RecruitedFollowerComponent>(pet);
            recruited.Commander = commander;
            if (string.IsNullOrEmpty(recruited.OriginalRootTask))
                recruited.OriginalRootTask = htn.RootTask.Task;

            ApplyFollowerOrder(pet, commander, htn, recruited, FollowerOrderType.Follow);
            EstablishCoFollowerIgnores(pet, commander);
            UpdateCommanderFollowerCount(commander);
        }

        private void StopFollowingUser(EntityUid target, EntityUid user, HTNComponent htn, bool showPopup = true)
        {
            if (!TryComp<RecruitedFollowerComponent>(target, out var recruited) ||
                recruited.Commander != user)
            {
                return;
            }

            SleepNPC(target, htn);
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
            ClearFollowBlackboardState(htn);
            if (!string.IsNullOrEmpty(recruited.OriginalRootTask))
                htn.RootTask.Task = recruited.OriginalRootTask;

            CleanupCoFollowerIgnores(target, user);
            _npcFaction.RemoveFriendlyEntity(target, user);
            _npcFaction.UnignoreEntity(target, user);
            RemComp<RecruitedFollowerComponent>(target);
            _htn.Replan(htn);
            WakeNPC(target, htn);
            if (showPopup)
                _popup.PopupEntity(Loc.GetString("npc-order-stopped-following-neutral"), target, user, PopupType.Small);
        }

        private void BuildCompoundFamilies()
        {
            _compoundFamilies.Clear();
            foreach (var proto in _prototypeManager.EnumeratePrototypes<HTNCompoundPrototype>())
            {
                if (proto.FollowerFollow == null)
                    continue;

                var follow = proto.FollowerFollow;
                var passive = proto.FollowerPassive ?? follow;
                var neutral = proto.FollowerNeutral ?? follow;
                var entry = (follow, passive, neutral);

                _compoundFamilies[proto.ID] = entry;
                _compoundFamilies[follow] = entry;
                _compoundFamilies[passive] = entry;
                _compoundFamilies[neutral] = entry;
            }
        }

        private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
        {
            BuildCompoundFamilies();
        }

        private bool SupportsOrderedFollowRoot(string rootTask)
        {
            return _compoundFamilies.ContainsKey(rootTask);
        }

        private void ClearFollowBlackboardState(HTNComponent htn)
        {
            htn.Blackboard.Remove<float>(IdleTimeKey);
            htn.Blackboard.Remove<EntityCoordinates>(FollowIdleTargetKey);
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.MovementTarget);
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);
            htn.Blackboard.Remove<EntityUid>("Target");
            htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
            htn.Blackboard.Remove<PathResultEvent>("TargetPathfind");
            htn.Blackboard.Remove<PathResultEvent>(NPCBlackboard.PathfindKey);

            if (TryComp<FactionExceptionComponent>(htn.Owner, out var factionException) && factionException.Hostiles.Count > 0)
            {
                var hostilesToClear = new List<EntityUid>(factionException.Hostiles);
                foreach (var hostile in hostilesToClear)
                    _npcFaction.DeAggroEntity((htn.Owner, factionException), hostile);
            }
        }

        private void OnRecruitedFollowerShutdown(Entity<RecruitedFollowerComponent> ent, ref ComponentShutdown args)
        {
            if (Deleted(ent.Comp.Commander) ||
                !TryComp<FollowerCommanderComponent>(ent.Comp.Commander, out var commander))
            {
                return;
            }

            if (commander.FollowerCount <= 1)
            {
                if (commander.InnateFollowerCapacity <= 0)
                {
                    RemCompDeferred<FollowerCommanderComponent>(ent.Comp.Commander);
                    return;
                }

                commander.FollowerCount = 0;
                Dirty(ent.Comp.Commander, commander);
                return;
            }

            commander.FollowerCount--;
            Dirty(ent.Comp.Commander, commander);
        }

        private void OnFollowerMountMovementControlAttempt(Entity<RecruitedFollowerComponent> ent, ref MountMovementControlAttemptEvent args)
        {
            if (args.Rider != ent.Comp.Commander)
                args.Cancelled = true;
        }

        private void OnIssueFollowerOrder(IssueFollowerOrderMessage msg, EntitySessionEventArgs args)
        {
            var commander = args.SenderSession.AttachedEntity;
            if (!commander.HasValue)
                return;

            var followers = new List<(EntityUid Uid, HTNComponent Htn, RecruitedFollowerComponent Recruited)>();
            var query = EntityQueryEnumerator<RecruitedFollowerComponent, HTNComponent>();
            while (query.MoveNext(out var uid, out var recruited, out var htn))
            {
                if (recruited.Commander != commander.Value)
                    continue;

                followers.Add((uid, htn, recruited));
            }

            if (followers.Count == 0)
            {
                _popup.PopupEntity(Loc.GetString("npc-order-command-no-followers"), commander.Value, commander.Value, PopupType.Small);
                return;
            }

            foreach (var (uid, htn, recruited) in followers)
            {
                ApplyFollowerOrder(uid, commander.Value, htn, recruited, msg.Order);
            }
            _chat.TrySendInGameICMessage(commander.Value, Loc.GetString(GetFollowerOrderSpeech(msg.Order)), InGameICChatType.Speak, false);
            _popup.PopupEntity(Loc.GetString(GetFollowerOrderPopup(msg.Order)), commander.Value, commander.Value, PopupType.Small);
        }

        private void ApplyFollowerOrder(EntityUid target, EntityUid commander, HTNComponent htn, RecruitedFollowerComponent recruited, FollowerOrderType order)
        {
            recruited.Order = order;
            recruited.WasAutoHeld = false;
            SleepNPC(target, htn);
            ClearFollowBlackboardState(htn);

            var newRoot = MapRootForFollowerOrder(recruited.OriginalRootTask, order);
            htn.RootTask.Task = newRoot;

            if (order is FollowerOrderType.Follow or FollowerOrderType.Passive or FollowerOrderType.Neutral)
                SetBlackboard(target, NPCBlackboard.FollowTarget, new EntityCoordinates(commander, Vector2.Zero), htn);
            else
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

            _htn.Replan(htn);
            EnsureComp<InputMoverComponent>(target);

            if (TryComp<MountableComponent>(target, out var mountable) &&
                mountable.RiderControlsMovement)
            {
                return;
            }

            WakeNPC(target, htn);
        }

        private void UpdateCommanderFollowerCount(EntityUid commander)
        {
            var count = GetFollowerCountForCommander(commander);
            var hasCommanderComponent = TryComp<FollowerCommanderComponent>(commander, out var component);

            if (count <= 0)
            {
                if (!hasCommanderComponent)
                    return;

                if (component!.InnateFollowerCapacity <= 0)
                {
                    RemCompDeferred<FollowerCommanderComponent>(commander);
                    return;
                }

                if (component.FollowerCount == 0)
                    return;

                component.FollowerCount = 0;
                Dirty(commander, component);
                return;
            }

            component ??= EnsureComp<FollowerCommanderComponent>(commander);
            if (component.FollowerCount == count)
                return;

            component.FollowerCount = count;
            Dirty(commander, component);
        }

        private int GetFollowerCountForCommander(EntityUid commander)
        {
            var count = 0;
            var query = EntityQueryEnumerator<RecruitedFollowerComponent>();
            while (query.MoveNext(out _, out var recruited))
            {
                if (recruited.Commander == commander)
                    count++;
            }

            return count;
        }

        private int GetNeutralFollowerCapacity(EntityUid commander, FollowerCommanderComponent? component = null)
        {
            var charismaCapacity = GetCharismaFollowerCapacity(_special.GetEffective(commander, SpecialStat.Charisma), _special.GetTuning());
            var innateCapacity = Resolve(commander, ref component, false) ? component.InnateFollowerCapacity : 0;
            return charismaCapacity + innateCapacity;
        }

        private static int GetCharismaFollowerCapacity(int charisma, SpecialTuningPrototype tuning)
        {
            if (charisma < tuning.CharismaNeutralFollowerMinimum)
                return 0;

            return charisma - tuning.CharismaNeutralFollowerMinimum + 1;
        }

        private static string GetFollowerOrderPopup(FollowerOrderType order)
        {
            return order switch
            {
                FollowerOrderType.Follow => "npc-order-command-follow",
                FollowerOrderType.Passive => "npc-order-command-passive",
                FollowerOrderType.HoldPosition => "npc-order-command-hold-position",
                FollowerOrderType.Neutral => "npc-order-command-neutral",
                _ => "npc-order-command-follow",
            };
        }

        private static string GetFollowerOrderSpeech(FollowerOrderType order)
        {
            return order switch
            {
                FollowerOrderType.Follow => "npc-order-response-follow",
                FollowerOrderType.Passive => "npc-order-response-passive",
                FollowerOrderType.HoldPosition => "npc-order-response-hold-position",
                FollowerOrderType.Neutral => "npc-order-response-neutral",
                _ => "npc-order-response-follow",
            };
        }

        private string MapRootForFollowerOrder(string originalRootTask, FollowerOrderType order)
        {
            if (order == FollowerOrderType.HoldPosition)
                return HoldPositionCompoundId;

            if (!_compoundFamilies.TryGetValue(originalRootTask, out var family))
                return originalRootTask;

            return order switch
            {
                FollowerOrderType.Follow => family.Follow,
                FollowerOrderType.Passive => family.Passive,
                FollowerOrderType.Neutral => family.Neutral,
                _ => originalRootTask,
            };
        }

        private void OnCommanderStartup(Entity<FollowerCommanderComponent> ent, ref ComponentStartup args)
        {
            ent.Comp.LastKnownGrid = Transform(ent.Owner).GridUid;
        }

        private void UpdateGridTeleports(float frameTime)
        {
            var query = EntityQueryEnumerator<FollowerCommanderComponent>();
            while (query.MoveNext(out var commander, out var comp))
            {
                var commanderXform = Transform(commander);

                var newGrid = commanderXform.GridUid;
                if (newGrid != comp.LastKnownGrid)
                {
                    var oldGrid = comp.LastKnownGrid;
                    comp.LastKnownGrid = newGrid;

                    if (oldGrid != null && newGrid != null)
                        comp.GridTeleportAccumulator = FollowerCommanderComponent.GridTeleportDelaySeconds;
                }

                if (comp.GridTeleportAccumulator <= 0f)
                    continue;

                comp.GridTeleportAccumulator -= frameTime;
                if (comp.GridTeleportAccumulator > 0f)
                    continue;

                comp.GridTeleportAccumulator = 0f;

                var commanderMapCoords = _transform.GetMapCoordinates(commander, commanderXform);
                var followerQuery = EntityQueryEnumerator<RecruitedFollowerComponent>();
                while (followerQuery.MoveNext(out var follower, out var recruited))
                {
                    if (recruited.Commander != commander || recruited.Order == FollowerOrderType.HoldPosition)
                        continue;

                    if (commanderXform.ParentUid == follower)
                        continue;

                    _transform.SetMapCoordinates(follower, commanderMapCoords);
                }
            }
        }

        private void OnCommanderAttackAttempt(Entity<FollowerCommanderComponent> ent, ref AttackAttemptEvent args)
        {
            if (args.Disarm || args.Target is not {} target)
                return;

            IssueNeutralEscortTarget(ent.Owner, target, "CommanderAttack");
        }

        private void OnCommanderProjectileHit(Entity<ProjectileComponent> ent, ref AfterProjectileHitEvent args)
        {
            if (ent.Comp.Shooter is not {} shooter || !HasComp<FollowerCommanderComponent>(shooter))
                return;

            IssueNeutralEscortTarget(shooter, args.Target, "CommanderGunshot");
        }

        private void OnCommanderHitscanHit(Entity<FollowerCommanderComponent> ent, ref HitscanHitEntityEvent args)
        {
            IssueNeutralEscortTarget(ent.Owner, args.Target, "CommanderHitscan");
        }

        private void OnCommanderDamaged(Entity<FollowerCommanderComponent> ent, ref DamageChangedEvent args)
        {
            if (!args.DamageIncreased || args.Origin is not {} attacker)
                return;

            IssueNeutralEscortTarget(ent.Owner, attacker, "CommanderDamaged");
        }

        private void OnCommanderDisarmed(Entity<FollowerCommanderComponent> ent, ref DisarmedEvent args)
        {
            IssueNeutralEscortTarget(ent.Owner, args.Source, "CommanderDisarmed");
        }

        private void OnCommanderPointedAt(Entity<FollowerCommanderComponent> ent, ref AfterPointedAtEvent args)
        {
            IssueNeutralEscortTarget(ent.Owner, args.Pointed, "CommanderPointed");
        }

        private void OnFollowerDamaged(Entity<RecruitedFollowerComponent> ent, ref DamageChangedEvent args)
        {
            if (!args.DamageIncreased ||
                args.Origin is not {} attacker ||
                ent.Comp.Order != FollowerOrderType.Neutral)
            {
                return;
            }

            // Don't issue a co-follower as an escort target.
            if (TryComp<RecruitedFollowerComponent>(attacker, out var attackerRecruited) &&
                attackerRecruited.Commander == ent.Comp.Commander)
                return;

            IssueNeutralEscortTarget(ent.Comp.Commander, attacker, "FollowerDamaged");
        }

        private void IssueNeutralEscortTarget(EntityUid commander, EntityUid target, string reason)
        {
            if (!HasComp<MobStateComponent>(target))
                return;

            var query = EntityQueryEnumerator<RecruitedFollowerComponent, HTNComponent>();
            while (query.MoveNext(out var follower, out var recruited, out var htn))
            {
                if (recruited.Commander != commander || recruited.Order != FollowerOrderType.Neutral)
                    continue;

                SetNeutralEscortTarget(follower, commander, target, htn, reason);
            }
        }

        private void EstablishCoFollowerIgnores(EntityUid newFollower, EntityUid commander)
        {
            var query = EntityQueryEnumerator<RecruitedFollowerComponent>();
            while (query.MoveNext(out var existing, out var recruited))
            {
                if (existing == newFollower || recruited.Commander != commander)
                    continue;
                _npcFaction.IgnoreEntity(newFollower, existing);
                _npcFaction.IgnoreEntity(existing, newFollower);
            }
        }

        private void CleanupCoFollowerIgnores(EntityUid leavingFollower, EntityUid commander)
        {
            var query = EntityQueryEnumerator<RecruitedFollowerComponent>();
            while (query.MoveNext(out var existing, out var recruited))
            {
                if (existing == leavingFollower || recruited.Commander != commander)
                    continue;
                _npcFaction.UnignoreEntity(leavingFollower, existing);
                _npcFaction.UnignoreEntity(existing, leavingFollower);
            }
        }

        private void SetNeutralEscortTarget(EntityUid follower, EntityUid commander, EntityUid target, HTNComponent htn, string reason)
        {
            if (target == follower || target == commander)
                return;
            if (TryComp<RecruitedFollowerComponent>(target, out var targetRecruited) && targetRecruited.Commander == commander)
                return;

            if (!_mobState.IsAlive(target))
                return;

            _npcFaction.AggroEntity(follower, target);
            SetBlackboard(follower, NPCBlackboard.CurrentOrderedTarget, target, htn);

            if (TryComp<NPCRetaliationComponent>(follower, out var retaliation))
                _npcRetaliation.TryRetaliate((follower, retaliation), target);
            else
                _htn.Replan(htn);
        }
    }
}
