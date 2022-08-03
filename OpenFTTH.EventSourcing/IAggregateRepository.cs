using System;
using System.Collections.Generic;

namespace OpenFTTH.EventSourcing
{
    public interface IAggregateRepository
    {
        void Store(AggregateBase aggregate);
        void StoreMany(IReadOnlyList<AggregateBase> aggregates);
        T Load<T>(Guid id, int? version = null) where T : AggregateBase;
        bool CheckIfAggregateIdHasBeenUsed(Guid id);
    }
}
