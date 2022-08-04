using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IAggregateRepository
    {
        void Store(AggregateBase aggregate);
        Task StoreAsync(AggregateBase aggregate);
        void StoreMany(IReadOnlyList<AggregateBase> aggregates);
        Task StoreManyAsync(IReadOnlyList<AggregateBase> aggregates);
        T Load<T>(Guid id, int? version = null) where T : AggregateBase;
        bool CheckIfAggregateIdHasBeenUsed(Guid id);
    }
}
