namespace Content.Shared.Paint;

public abstract class SharedPaintSystem : EntitySystem
{
    public virtual void UpdateAppearance(EntityUid uid, PaintedComponent? component = null) { }

    // Lets other systems set the paint colour of a can without touching the component directly.
    public void SetColor(EntityUid uid, PaintComponent component, Color color)
    {
        component.Color = color;
        Dirty(uid, component);
    }
}
