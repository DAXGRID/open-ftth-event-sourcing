using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IEventStore
    {
        void AppendStream(Guid streamId, long expectedVersion, object[] events);
        Task AppendStreamAsync(Guid streamId, long expectedVersion, object[] events);
        void AppendStream(IReadOnlyList<AggregateBase> aggregate);
        Task AppendStreamAsync(IReadOnlyList<AggregateBase> aggregate);
        object[] FetchStream(Guid streamId, long version = 0);
        IProjectionRepository Projections { get; }
        IAggregateRepository Aggregates { get; }
        ICommandLog CommandLog { get; }
        ISequences Sequences { get; }
        void DehydrateProjections();
        Task DehydrateProjectionsAsync(CancellationToken cancellationToken = default);
        long CatchUp();
        Task<long> CatchUpAsync(CancellationToken cancellationToken = default);
        long? CurrentStreamVersion(Guid streamId);
        Task<long?> CurrentStreamVersionAsync(Guid streamId);
        void ScanForProjections();
    }
}
