using Rutin.GameFramework.Core;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Base for detachable sensors. Active sensors register once with their sibling brain.
    /// </summary>
    public abstract class NpcSensorFeature : EntityFeature, INpcSensor
    {
        private NpcBrainFeature _brain;

        public override int InitializationOrder => -100;

        public abstract int SensorOrder { get; }

        public abstract void Sense(
            ref NpcBlackboard blackboard,
            float deltaTime);

        public abstract void ResetNpcSensorState();

        protected override void OnFeatureInitialized()
        {
            _brain = GetComponent<NpcBrainFeature>();
        }

        protected override void OnFeatureActivated()
        {
            _brain?.RegisterSensor(this);
        }

        protected override void OnFeatureDeactivated()
        {
            _brain?.UnregisterSensor(this);
            ResetNpcSensorState();
        }

        protected override void OnFeatureShutdown()
        {
            _brain?.UnregisterSensor(this);
            ResetNpcSensorState();
            _brain = null;
        }
    }
}
