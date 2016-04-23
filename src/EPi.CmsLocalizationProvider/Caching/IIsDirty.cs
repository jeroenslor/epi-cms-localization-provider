using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EPi.CmsLocalizationProvider.Caching
{
    public interface IIsDirty
    {
        bool IsDirty { get; }
    }
}
