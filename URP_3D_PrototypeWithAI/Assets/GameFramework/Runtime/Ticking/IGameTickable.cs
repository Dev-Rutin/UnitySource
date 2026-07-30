namespace Rutin.GameFramework.Ticking
{
    public enum TickUnregistrationReason
    {
        Explicit = 0,
        Quarantined = 1,
        SchedulerCleared = 2
    }

    public interface IGameTickable
    {
        bool IsTickEnabled { get; }

        /// <summary>
        /// Receives the elapsed game time since this item was last visited by the scheduler.
        /// Budget-delayed items therefore receive an accumulated delta rather than losing time.
        /// </summary>
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Optional notification for tickables that cache scheduler registration state.
    /// The scheduler removes the registration before invoking this callback, so an observer
    /// may explicitly register for a new scheduling session.
    /// </summary>
    public interface ITickSchedulerRegistrationObserver
    {
        void OnTickSchedulerUnregistered(
            ITickScheduler scheduler,
            TickUnregistrationReason reason);
    }

    public interface ITickScheduler
    {
        int Count { get; }

        bool Register(IGameTickable tickable);

        bool Unregister(IGameTickable tickable);
    }
}
