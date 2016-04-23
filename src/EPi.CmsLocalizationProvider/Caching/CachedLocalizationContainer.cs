using System;
using System.Collections.Generic;
using EPiServer.Core;

namespace EPi.CmsLocalizationProvider.Caching
{
    [Serializable]
    public class CachedLocalizationContainer
    {
        public CachedLocalizationContainer()
        {
            LocalizationPages = new List<CachedLocalizationPage>();
        }

        public ContentReference LocalizationContainerReference { get; set; }
        public List<CachedLocalizationPage> LocalizationPages { get; set; }
    }
}
