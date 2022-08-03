using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IEventStore
    {
        void AppendStream(Guid streamId, int expectedVersion, object[] events);
        void AppendStream(IReadOnlyList<AggregateBase> aggregate);
        object[] FetchStream(Guid streamId, int version = 0);
        IProjectionRepository Projections { get; }
        IAggregateRepository Aggregates { get; }
        ICommandLog CommandLog { get; }
        ISequences Sequences { get; }
        void DehydrateProjections();
        Task DehydrateProjectionsAsync();
        long CatchUp();
        Task<long> CatchUpAsync();
    }
}
