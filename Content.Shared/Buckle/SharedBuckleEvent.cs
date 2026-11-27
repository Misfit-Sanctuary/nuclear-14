using Content.Shared.Buckle.Components;


namespace Content.Shared.Buckle;


[ByRefEvent]
public record struct BuckleOffsetOverride (Entity<BuckleComponent> Buckle, Entity<StrapComponent> Strap,TransformComponent Xform, bool Handled = false);
