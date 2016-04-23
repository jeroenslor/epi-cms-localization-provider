using System;
using EPiServer.Core;

namespace EPi.CmsLocalizationProvider.Caching
{
    [Serializable]
    public class CachedLocalizationPage
    {
        public string BasePath { get; set; }
        public ContentReference LocalizationPageReference { get; set; }
    }
}
