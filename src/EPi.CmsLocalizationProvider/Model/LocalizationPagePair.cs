using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EPiServer.Core;

namespace EPi.CmsLocalizationProvider.Model
{
    public class LocalizationPagePair
    {
        public IContent RootLocalizationPage { get; set; }
        public IContent SiteLocalizationPage { get; set; }
    }
}
