using Rutin.GameFramework.Core;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Base for detachable decision policies. The first active provider returning true wins.
    /// </summary>
    public abstract class NpcDecisionProviderFeature :
        EntityFeature,
        INpcDecisionProvider
    {
        private NpcBrainFeature _brain;

        public abstract int DecisionOrder { get; }

        public abstract bool TryDecide(
            in NpcBlackboard blackboard,
            float deltaTime,
            out NpcDecision decision);

        public abstract void ResetNpcDecisionState();

        protected override void OnFeatureInitialized()
        {
            _brain = GetComponent<NpcBrainFeature>();
        }

        protected override void OnFeatureActivated()
        {
            _brain?.RegisterDecisionProvider(this);
        }

        protected override void OnFeatureDeactivated()
        {
            _brain?.UnregisterDecisionProvider(this);
            ResetNpcDecisionState();
        }

        protected override void OnFeatureShutdown()
        {
            _brain?.UnregisterDecisionProvider(this);
            ResetNpcDecisionState();
            _brain = null;
        }
    }
}
