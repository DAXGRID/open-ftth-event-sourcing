namespace OpenFTTH.EventSourcing
{
    public interface IProjectionRepository
    {
        void Add(IProjection projection);
        T Get<T>();
    }
}
