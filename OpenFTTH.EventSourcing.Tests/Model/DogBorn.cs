using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class DogBorn
    {
        public string Name { get; }

        public DogBorn(string name)
        {
            this.Name = name;
        }
    }
}
