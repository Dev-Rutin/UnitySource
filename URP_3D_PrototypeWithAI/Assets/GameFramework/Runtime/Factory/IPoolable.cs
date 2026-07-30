namespace Rutin.GameFramework.Factory
{
    public interface IPoolable
    {
        void OnRentFromPool();

        void OnReturnToPool();
    }
}
