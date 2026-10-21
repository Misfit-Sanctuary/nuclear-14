using System.Text.Json.Serialization;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;


namespace Content.Shared.Vehicles;

[ImplicitDataDefinitionForInheritors]
public partial interface ICondition
{
    [JsonPropertyName("id")] private protected string _id => this.GetType().Name;
    public abstract bool Condition(EntityEffectBaseArgs args);
}

[ImplicitDataDefinitionForInheritors]
public partial interface IPopup
{
    [JsonPropertyName("id")] private protected string _id => this.GetType().Name;
    public abstract string FailPopup();

    public abstract string SucceedPopup();
}
