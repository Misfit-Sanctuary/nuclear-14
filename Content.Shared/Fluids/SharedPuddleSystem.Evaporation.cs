using System;
using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    public static string[] EvaporationReagents { get; private set; } = Array.Empty<string>();

	// A second array of only reagents with FastEvaporation set to true, to be passed to the puddle system
	public static string[] FastEvaporationReagents { get; private set; } = Array.Empty<string>();

    private void InitializeEvaporation()
    {
        EvaporationReagents = _prototypeManager
            .EnumeratePrototypes<ReagentPrototype>()
            .Where(p => p.Evaporates)
            .Select(p => p.ID)
            .ToArray();
    }
	// Ideally, anything in this array will evaporate X times faster
    // value located at Content.Server/Fluids/EntitySystems/PuddleSystem.Evaporation.cs line 50
	private void InitializeFastEvaporation()
	{
		FastEvaporationReagents = _prototypeManager
            .EnumeratePrototypes<ReagentPrototype>()
            .Where(p => p.FastEvaporation)
            .Select(p => p.ID)
            .ToArray();
	}

    public bool CanFullyEvaporate(Solution solution)
    {
        return solution.GetTotalPrototypeQuantity(EvaporationReagents) == solution.Volume;
    }
}
