using System.Diagnostics.CodeAnalysis;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Audio; // Misfit Add for SleepNPC and WakeNPC
using Content.Shared.CCVar;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components; // Misfit Add for SleepNPC and WakeNPC
using Content.Shared.NPC;
using Content.Shared.Sound; // Misfit Add for SleepNPC and WakeNPC
using Content.Shared.Sound.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server.NPC.Systems
{
    /// <summary>
    ///
    /// Where NPC logic is ticked
    /// Everytick calls to systems(ie HTN) to handle NPC logic
    ///
    /// public methods and cvars to interface with NPC behavior
    /// and adjust systems
    ///
    /// </summary>
    public sealed partial class NPCSystem : EntitySystem
    {
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        [Dependency] private readonly HTNSystem _htn = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
        [Dependency] private readonly SharedEmitSoundSystem _emitSound = default!;

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
            FollowerSystemInit();
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
            FollowerUpdate(frameTime);
        }

        /// <summary>
        /// BELOW ARE ALL PUBLIC METHODS TO INTERFACE WITH NPCS!
        /// </summary>
        /// mostly used by just HTN

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

        /// <summary>
        ///
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="component"></param>
        /// <param name="args"></param>
        public void OnNPCMapInit(EntityUid uid, HTNComponent component, MapInitEvent args)
        {
            component.Blackboard.SetValue(NPCBlackboard.Owner, uid);
            /// Misfit Change: <see cref="_Misfits.NPC.ProximityNPCSystem"/> would sleep almost all NPCs on init anyway
            /// WakeNPC(uid, component);
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
            // Misfit add: Reduce repeated code and maintain consistency
            //             calls to WakeNPC should always undo what SleepNPC does vice versa


            // Add InputMover BEFORE wake so steering can write to it on the first tick.
            EnsureComp<InputMoverComponent>(uid);
            EnsureComp<ActiveNPCComponent>(uid);
            // re-Enable sound in-case SleepNPC disabled it
            _emitSound.SetEnabled((uid, (SpamEmitSoundComponent?) null), true);
            _ambient.SetAmbience(uid, true);


            // Misfit end:
        }
        //
        public void SleepNPC(EntityUid uid, HTNComponent? component = null, bool removeSound = true)
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


            // Misfit Change: remove sound and inputMover when sleeping NPCs
            //                code below was repeated in ProximityNPCSystem
            //                after SleepNPC, so just put it here for consistency

            // entirely — no HandleMobMovement per physics substep while asleep.
            RemCompDeferred<InputMoverComponent>(uid);
            // In-cases where sleep is called for player's possesing NPCs
            // though you may want to remove their audio for one reason or other like SPAM
            if (!removeSound) return;
            // Silence idle sounds while sleeping — no point emitting audio for NPCs
            // that are 60+ tiles from any player.
            _emitSound.SetEnabled((uid, (SpamEmitSoundComponent?) null), false);
            _ambient.SetAmbience(uid, false);
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
                    SleepNPC(uid, component); // Mob should't be able to move or act by this point
                    break;
            }
        }
    }
}
