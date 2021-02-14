using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IProjectionRepository
    {
        void Add(IProjection projection);
        T Get<T>();
    }
}
