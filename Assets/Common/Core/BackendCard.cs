namespace MillionObjects
{
    /// <summary>
    /// The talk's flexibility card for one backend: where behavioural code lives, how the Unity
    /// ecosystem connects, and what one more per-object feature costs. Displayed by the HUD.
    /// </summary>
    public readonly struct BackendCard
    {
        /// <summary>Where per-object behaviour is written, e.g. "MonoBehaviour.Update" or "HLSL compute kernel".</summary>
        public readonly string BehaviourLivesIn;
        /// <summary>How Inspector, Animator, physics and third-party assets connect: native, bridged, manual.</summary>
        public readonly string Ecosystem;
        /// <summary>What adding one more per-object feature costs, e.g. "a field" or "a buffer + compute pass".</summary>
        public readonly string FeatureCost;

        public BackendCard(string behaviourLivesIn, string ecosystem, string featureCost)
        {
            BehaviourLivesIn = behaviourLivesIn;
            Ecosystem = ecosystem;
            FeatureCost = featureCost;
        }
    }
}
