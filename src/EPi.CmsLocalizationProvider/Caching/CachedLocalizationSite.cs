using System;

namespace EPi.CmsLocalizationProvider.Caching
{
    [Serializable]
    public class CachedLocalizationSite
    {
        public CachedLocalizationSite()
        {
            LocalizationContainer = new CachedLocalizationContainer();
        }

        public string Name { get; set; }
        public CachedLocalizationContainer LocalizationContainer { get; set; }

    }
}
