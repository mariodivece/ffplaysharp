using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unosquare.FFplaySharp.Interop;

public unsafe interface IUpdateableReference<T> : 
    INativeReference<T>
    where T : unmanaged
{
    void UpdatePointer(T* target);
}
