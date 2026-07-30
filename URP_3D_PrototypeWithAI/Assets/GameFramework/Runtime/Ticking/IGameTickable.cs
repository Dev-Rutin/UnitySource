namespace Rutin.GameFramework.Ticking
{
    public interface IGameTickable
    {
        bool IsTickEnabled { get; }

        void Tick(float deltaTime);
    }

    public interface ITickScheduler
    {
        int Count { get; }

        bool Register(IGameTickable tickable);

        bool Unregister(IGameTickable tickable);
    }
}
