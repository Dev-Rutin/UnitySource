namespace Rutin.GameFramework.Ticking
{
    public interface IGameTickable
    {
        bool IsTickEnabled { get; }

        /// <summary>
        /// Receives the elapsed game time since this item was last visited by the scheduler.
        /// Budget-delayed items therefore receive an accumulated delta rather than losing time.
        /// </summary>
        void Tick(float deltaTime);
    }

    public interface ITickScheduler
    {
        int Count { get; }

        bool Register(IGameTickable tickable);

        bool Unregister(IGameTickable tickable);
    }
}
