namespace Rutin.GameFramework.Factory
{
    /// <summary>
    /// Receives deterministic pool lease callbacks. A newly created inactive clone can receive
    /// its first rent callback before Unity invokes <c>Awake</c>, so dependencies used here must
    /// be initialized lazily in the callback instead of relying exclusively on <c>Awake</c>.
    /// Rent callbacks always complete before <c>OnEnable</c>.
    /// </summary>
    public interface IPoolable
    {
        void OnRentFromPool();

        void OnReturnToPool();
    }
}
