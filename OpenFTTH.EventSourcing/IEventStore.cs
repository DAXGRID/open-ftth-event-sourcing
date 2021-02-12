using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IEventStore
    {
        void AppendStream(Guid streamId, long expectedVersion, object[] events);
        object[] FetchStream(Guid streamId, long version = 0);
        IProjectionRepository Projections { get; }
        IAggregateRepository AggregateRepository { get; }
    }
}
