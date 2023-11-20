using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frosty.Sdk.IO.Ebx;
[Flags]
public enum EbxWriteFlags
{
    None = 0,
    IncludeTransient = 1,
    DoNotSort
}
