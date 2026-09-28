namespace Content.Server.Explosion.Components
{
    [RegisterComponent]
    public sealed partial class TriggerOnCollideComponent : Component
    {
		[DataField("fixtureID", required: true)]
		public string FixtureID = String.Empty;

        /// <summary>
        ///     Doesn't trigger if the other colliding fixture is nonhard.
        /// </summary>
        [DataField("ignoreOtherNonHard")]
        public bool IgnoreOtherNonHard = true;

        /// <summary>
        /// Prevents a single physical impact from triggering the payload more than once when
        /// the physics engine reports multiple fixture contacts before the entity is deleted.
        /// </summary>
        public bool Triggered;
    }
}
