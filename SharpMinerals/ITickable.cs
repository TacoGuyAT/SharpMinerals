using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpMinerals;
public interface ITickable : ISystem {
    public void Tick();
}
