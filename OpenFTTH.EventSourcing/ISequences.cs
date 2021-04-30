namespace OpenFTTH.EventSourcing
{
    public interface ISequences
    {
        long GetNextVal(string sequenceName);
        void DropSequence(string sequenceName);
    }
}
