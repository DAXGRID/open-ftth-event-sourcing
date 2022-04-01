using System.Collections.Concurrent;
using System.Collections.Generic;
namespace OpenFTTH.EventSourcing.InMem
{
    public class InMemSequenceStore : ISequences
    {
        private readonly ConcurrentDictionary<string, long> _sequences = new();

        public InMemSequenceStore()
        {
        }

        public long GetNextVal(string sequenceName)
        {
            var currentVal = _sequences.GetOrAdd(sequenceName, 0);

            var newVal = currentVal + 1;

            _sequences.TryUpdate(sequenceName, newVal, currentVal);

            return newVal;
        }

        public void DropSequence(string sequenceName)
        {
            _sequences.Remove(sequenceName, out _);
        }
    }
}
