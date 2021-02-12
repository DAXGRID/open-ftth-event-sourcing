using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public class ProjectionRepository : IProjectionRepository
    {
        private readonly List<IProjection> _projections = new List<IProjection>();

        public void Add(IProjection projection)
        {
            _projections.Add(projection);
        }

        internal void ApplyEvents(IReadOnlyList<IEventEnvelope> events)
        {
            foreach (var projection in _projections)
            {
                projection.Apply(events);
            }
        }
    }
}
