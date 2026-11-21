// #Misfits Add - Core disease tick/spread/cure system.
// Handles disease progression, stage-based effects, airborne/contact transmission,
// equipment protection, natural immunity, and cure condition checking.

// #Misfits Fix - Removed: using Content.Server._Misfits.Disease.Effects;
// Effect/Cure classes moved to Content.Shared for client-side type resolution.
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Server.Temperature.Components;
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.Disease;
using Content.Shared._Misfits.Disease.Components;
using Content.Shared._Misfits.Disease.Cures;
using Content.Shared._Misfits.Disease.Effects;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Disease;

/// <summary>
/// Core disease system. Every tick it advances each carried disease, rolls effects
/// based on the current stage, checks cure conditions, and handles airborne/contact
/// spread to nearby entities.
/// </summary>
public sealed class DiseaseSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!; // #Misfits Add - CVar gate
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    /// <summary>Range in tiles for airborne disease spread.</summary>
    private const float AirborneRange = 3f;

    // #Misfits Add - Performance: throttle the outer tick. Disease TickTime is seconds-scale
    // per prototype, so running the carrier sweep at 1 Hz is indistinguishable from 30 Hz.
    private const float UpdateInterval = 1f;
    private float _updateAccumulator;
    private bool _enabled;

    // #Misfits Add - Reusable scratch buffers so the per-tick sweep doesn't allocate.
    // _tickBuffer replaces the per-carrier `new Dictionary(carrier.Diseases)` copy.
    // _curesBuffer replaces `new List<DiseaseCure>()` in CheckCures.
    private readonly List<(ProtoId<DiseasePrototype> Id, float AccumulatedTime)> _tickBuffer = new();
    private readonly List<DiseaseCure> _curesBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        // #Misfits Add - Gate the entire system behind a CVar. Defaults false because no
        // in-game assets currently produce diseases.
        Subs.CVar(_cfg, PerformanceCVars.DiseaseEnabled, v => _enabled = v, true);

        // When a mask/gas-mask with DiseaseProtection is equipped, mark it active
        SubscribeLocalEvent<DiseaseProtectionComponent, GotEquippedEvent>(OnProtectionEquipped);
        SubscribeLocalEvent<DiseaseProtectionComponent, GotUnequippedEvent>(OnProtectionUnequipped);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // #Misfits Add - CVar kill switch. Skips the entire per-carrier sweep when disabled.
        if (!_enabled)
            return;

        // #Misfits Add - Throttle: run the carrier sweep once per UpdateInterval seconds.
        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;
        var elapsed = _updateAccumulator;
        _updateAccumulator = 0f;

        var query = EntityQueryEnumerator<DiseaseCarrierComponent>();
        while (query.MoveNext(out var uid, out var carrier))
        {
            // #Misfits Tweak - Hoisted the empty-carrier short-circuit above the MobState
            // check and BEFORE any allocation. Uninfected carriers now cost a single
            // dictionary count lookup + a deferred component remove.
            if (carrier.Diseases.Count == 0)
            {
                // No active diseases — remove the marker if present
                RemCompDeferred<DiseasedComponent>(uid);
                continue;
            }

            // Skip dead entities
            if (TryComp<MobStateComponent>(uid, out var mobState) && _mobState.IsDead(uid, mobState))
                continue;

            // Ensure marker component is present while sick
            EnsureComp<DiseasedComponent>(uid);

            // #Misfits Tweak - Snapshot into a reused buffer instead of allocating a new
            // Dictionary every iteration. Mutations to carrier.Diseases during the loop
            // are still safe because we iterate the buffer, not the live dictionary.
            _tickBuffer.Clear();
            foreach (var kv in carrier.Diseases)
                _tickBuffer.Add((kv.Key, kv.Value));

            foreach (var (diseaseId, accumulatedTime) in _tickBuffer)
            {
                if (!_proto.TryIndex(diseaseId, out var disease))
                    continue;

                // #Misfits Tweak - Use elapsed (time since last sweep) rather than
                // frameTime, since the sweep is now throttled.
                if (!carrier.Accumulators.TryGetValue(diseaseId, out var tickAcc))
                    tickAcc = 0f;

                tickAcc += elapsed;

                if (tickAcc < disease.TickTime)
                {
                    carrier.Accumulators[diseaseId] = tickAcc;
                    // Still advance total accumulated time for JustWaitCure
                    carrier.Diseases[diseaseId] = accumulatedTime + elapsed;
                    Dirty(uid, carrier);
                    continue;
                }

                // Reset tick accumulator and advance total time
                carrier.Accumulators[diseaseId] = 0f;
                var newTotal = accumulatedTime + tickAcc;
                carrier.Diseases[diseaseId] = newTotal;

                // Determine current stage (0-indexed)
                var currentStage = GetStage(disease, newTotal);

                // Check cure conditions — if ALL cures for this stage pass, cure the disease
                if (CheckCures(uid, disease, currentStage))
                {
                    CureDisease(uid, carrier, diseaseId);
                    continue;
                }

                // Roll effects for current stage
                var effectArgs = new DiseaseEffectArgs(uid, disease, EntityManager);
                foreach (var effect in disease.Effects)
                {
                    // Effect only applies to certain stages
                    if (effect.Stages.Count > 0 && !effect.Stages.Contains(currentStage))
                        continue;

                    // Probability check
                    if (!_random.Prob(effect.Probability))
                        continue;

                    // Special handling for sneeze/cough — needs injected dependencies
                    if (effect is DiseaseSnough snough)
                    {
                        HandleSnough(uid, carrier, disease, snough);
                        continue;
                    }

                    // #Misfits Fix - Server-only effects handled inline because their
                    // dependencies (VomitSystem, BloodstreamComponent) are server-only.
                    if (effect is DiseaseVomit vomit)
                    {
                        HandleVomit(uid, vomit);
                        continue;
                    }

                    if (effect is DiseaseAdjustReagent adjustReagent)
                    {
                        HandleAdjustReagent(uid, adjustReagent);
                        continue;
                    }

                    effect.Effect(effectArgs);
                }

                Dirty(uid, carrier);
            }
        }
    }

    /// <summary>
    /// Calculate the current stage index from accumulated time and stage thresholds.
    /// Stages list contains ascending time thresholds; stage 0 is below first threshold.
    /// </summary>
    private static int GetStage(DiseasePrototype disease, float accumulatedTime)
    {
        for (var i = disease.Stages.Count - 1; i >= 0; i--)
        {
            if (accumulatedTime >= disease.Stages[i])
                return i + 1;
        }
        return 0;
    }

    /// <summary>
    /// Check if all cure conditions for the current stage are met.
    /// Returns true only if the disease has at least one cure and all cures pass.
    /// </summary>
    private bool CheckCures(EntityUid uid, DiseasePrototype disease, int stage)
    {
        // #Misfits Tweak - Reuse a cached buffer so the cure check doesn't allocate per tick.
        _curesBuffer.Clear();
        foreach (var cure in disease.Cures)
        {
            if (cure.Stages.Count > 0 && !cure.Stages.Contains(stage))
                continue;
            _curesBuffer.Add(cure);
        }

        if (_curesBuffer.Count == 0)
            return false;

        var args = new DiseaseEffectArgs(uid, disease, EntityManager);
        foreach (var cure in _curesBuffer)
        {
            // #Misfits Fix - Server-only cures handled inline because their
            // dependencies (TemperatureComponent, BloodstreamComponent) are server-only.
            bool cured;
            if (cure is DiseaseBodyTemperatureCure tempCure)
                cured = CheckBodyTemperatureCure(uid, tempCure);
            else if (cure is DiseaseReagentCure reagentCure)
                cured = CheckReagentCure(uid, reagentCure);
            else
                cured = cure.Cure(args);

            if (!cured)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Removes a disease from a carrier and grants immunity.
    /// </summary>
    public void CureDisease(EntityUid uid, DiseaseCarrierComponent carrier, ProtoId<DiseasePrototype> diseaseId)
    {
        carrier.Diseases.Remove(diseaseId);
        carrier.Accumulators.Remove(diseaseId);
        carrier.PastDiseases.Add(diseaseId); // immunity
        Dirty(uid, carrier);

        if (carrier.Diseases.Count == 0)
            RemCompDeferred<DiseasedComponent>(uid);
    }

    /// <summary>
    /// Attempt to infect an entity with a disease. Respects immunity, resistance, and equipment protection.
    /// </summary>
    public bool TryInfect(EntityUid target, ProtoId<DiseasePrototype> diseaseId, float spreadChance = 0.5f)
    {
        if (!TryComp<DiseaseCarrierComponent>(target, out var carrier))
            return false;

        // Already infected or immune
        if (carrier.Diseases.ContainsKey(diseaseId))
            return false;
        if (carrier.PastDiseases.Contains(diseaseId))
            return false;
        if (carrier.NaturalImmunities.Contains(diseaseId))
            return false;

        // Equipment protection reduces chance
        var protection = GetEquipmentProtection(target);
        var finalChance = spreadChance * (1f - carrier.DiseaseResist) * (1f - protection);

        if (!_random.Prob(Math.Clamp(finalChance, 0f, 1f)))
            return false;

        carrier.Diseases[diseaseId] = 0f;
        carrier.Accumulators[diseaseId] = 0f;
        EnsureComp<DiseasedComponent>(target);
        Dirty(target, carrier);
        return true;
    }

    /// <summary>
    /// Add a disease directly without resistance checks (admin, vaccine failure, etc.).
    /// </summary>
    public void AddDisease(EntityUid target, ProtoId<DiseasePrototype> diseaseId)
    {
        var carrier = EnsureComp<DiseaseCarrierComponent>(target);

        if (carrier.Diseases.ContainsKey(diseaseId))
            return;

        carrier.Diseases[diseaseId] = 0f;
        carrier.Accumulators[diseaseId] = 0f;
        EnsureComp<DiseasedComponent>(target);
        Dirty(target, carrier);
    }

    /// <summary>
    /// Handle sneeze/cough: play emote, then attempt airborne spread if configured.
    /// </summary>
    private void HandleSnough(EntityUid uid, DiseaseCarrierComponent carrier,
        DiseasePrototype disease, DiseaseSnough snough)
    {
        // Play the emote (Sneeze or Cough)
        _chat.TryEmoteWithChat(uid, snough.EmoteId, forceEmote: true);

        if (!snough.AirTransmit || !disease.Airborne)
            return;

        // Spread to nearby entities
        var nearby = _lookup.GetEntitiesInRange(uid, AirborneRange);
        foreach (var target in nearby)
        {
            if (target == uid)
                continue;

            TryInfect(target, disease.ID, disease.ContactSpread);
        }
    }

    /// <summary>
    /// Handle vomit effect (server-only — VomitSystem is in Content.Server).
    /// </summary>
    private void HandleVomit(EntityUid uid, DiseaseVomit vomit)
    {
        _vomit.Vomit(uid, vomit.ThirstAmount, vomit.HungerAmount);
    }

    /// <summary>
    /// Handle reagent injection/removal (server-only — BloodstreamComponent is in Content.Server).
    /// </summary>
    private void HandleAdjustReagent(EntityUid uid, DiseaseAdjustReagent adjustReagent)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var blood))
            return;

        if (!_solution.ResolveSolution(uid, blood.ChemicalSolutionName,
                ref blood.ChemicalSolution, out _))
            return;

        if (adjustReagent.Amount > 0)
            _solution.TryAddReagent(blood.ChemicalSolution.Value, adjustReagent.Reagent, FixedPoint2.New(adjustReagent.Amount));
        else
            _solution.RemoveReagent(blood.ChemicalSolution.Value, adjustReagent.Reagent, FixedPoint2.New(-adjustReagent.Amount));
    }

    /// <summary>
    /// Check body temperature cure condition (server-only — TemperatureComponent is in Content.Server).
    /// </summary>
    private bool CheckBodyTemperatureCure(EntityUid uid, DiseaseBodyTemperatureCure tempCure)
    {
        if (!TryComp<TemperatureComponent>(uid, out var temp))
            return false;

        return tempCure.MustBeAbove
            ? temp.CurrentTemperature >= tempCure.Threshold
            : temp.CurrentTemperature <= tempCure.Threshold;
    }

    /// <summary>
    /// Check reagent cure condition (server-only — BloodstreamComponent is in Content.Server).
    /// </summary>
    private bool CheckReagentCure(EntityUid uid, DiseaseReagentCure reagentCure)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var blood))
            return false;

        if (!_solution.ResolveSolution(uid, blood.ChemicalSolutionName,
                ref blood.ChemicalSolution, out var solution))
            return false;

        foreach (var (reagentId, quantity) in solution.Contents)
        {
            if (reagentId.Prototype == reagentCure.Reagent && quantity.Float() >= reagentCure.MinAmount)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calculate total equipment disease protection for an entity.
    /// Checks all equipped items with DiseaseProtectionComponent.
    /// </summary>
    private float GetEquipmentProtection(EntityUid uid)
    {
        var totalProtection = 0f;

        // Check common protective slots: mask, head, outerClothing
        string[] slots = { "mask", "head", "outerClothing" };
        foreach (var slot in slots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var item))
                continue;
            if (!TryComp<DiseaseProtectionComponent>(item, out var prot))
                continue;
            if (!prot.IsActive)
                continue;

            totalProtection += prot.Protection;
        }

        // Clamp to [0, 1]
        return Math.Clamp(totalProtection, 0f, 1f);
    }

    // -- Equipment protection event handlers --

    private void OnProtectionEquipped(EntityUid uid, DiseaseProtectionComponent comp, GotEquippedEvent args)
    {
        // #Misfits Fix - this component is server-only, so it has nothing to dirty.
        comp.IsActive = true;
    }

    private void OnProtectionUnequipped(EntityUid uid, DiseaseProtectionComponent comp, GotUnequippedEvent args)
    {
        comp.IsActive = false;
    }
}
