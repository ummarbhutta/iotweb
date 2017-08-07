using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Common.Util
{
    public enum SessionAttributes
    {
        Size,
        LastAccessTime
    }

    /// <summary>
    /// Possible values of file access mode
    /// </summary>
    public enum FileAccessMode
    {
        Append,
        Create,
        CreateNew,
        Open,
        OpenOrCreate,
        Truncate
    }
}
