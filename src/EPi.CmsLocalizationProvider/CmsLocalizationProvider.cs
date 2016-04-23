using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using EPi.CmsLocalizationProvider.Model;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Framework.Localization;
using EPiServer.ServiceLocation;
using EPiServer.Web;

namespace EPi.CmsLocalizationProvider
{
    public class CmsLocalizationProvider : LocalizationProvider
    {
        #region fields
        private static readonly object Lock = new object();
        private static bool _updateContent;
        private static string _prefix;
        private static ConcurrentDictionary<string, string> PropertyDefinitionMapping;
        private static Injected<LocalizationPageService> LocalizationPageService { get; set; }
        #endregion

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            PropertyDefinitionMapping = new ConcurrentDictionary<string, string>();

            bool updateContent;
            _updateContent = !bool.TryParse(config["updateContent"], out updateContent) || updateContent;
            _prefix = config["prefix"] ?? "custom";
        }

        public override string GetString(string originalKey, string[] normalizedKey, CultureInfo culture)
        {
            // the default provider returns null also for episerver labels so we need to prefix the custom labels.            
            if (!IsSupportedKey(normalizedKey))
                return null;

            var localizationPages = GetOrAddLocalizationPages(normalizedKey, culture);
            if (localizationPages == null || (localizationPages.SiteLocalizationPage == null && localizationPages.RootLocalizationPage == null))
                return null;

            return GetValueFromLocalizationPagePair(originalKey, normalizedKey, localizationPages);
        }

        public override IEnumerable<ResourceItem> GetAllStrings(string originalKey, string[] normalizedKey, CultureInfo culture)
        {
            // the default provider returns null also for episerver labels so we need to prefix the custom labels.            
            if (!IsSupportedKey(normalizedKey))
                yield break;

            // TODO if the normalizedkey length is 1 we need to loop through all the pages and return the result

            var site = SiteDefinition.Current;

            IContent siteLocalizationPage = null;
            if (site != null)
                siteLocalizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey, site, new LoaderOptions() { LanguageLoaderOption.FallbackWithMaster(culture) });

            var rootLocalizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey, new LoaderOptions() { LanguageLoaderOption.FallbackWithMaster(culture) });
            if (siteLocalizationPage == null && rootLocalizationPage == null)
                yield break;

            var path = originalKey[originalKey.Length - 1].Equals('/') ? originalKey : originalKey + "/";
            foreach (var property in
                ServiceLocator.Current.GetInstance<IContentTypeRepository>()
                    .Load(rootLocalizationPage.ContentTypeID)
                    .PropertyDefinitions.Where(x => x.HelpText.Contains(path)))
            {
                // Get values from both the site and root localization page to make sure the properties are created on both pages
                string siteValue = null;
                if (siteLocalizationPage != null)
                    siteValue = (string)siteLocalizationPage.Property[property.Name].Value;

                var rootValue = (string)rootLocalizationPage.Property[property.Name].Value;

                yield return new ResourceItem(property.HelpText, !string.IsNullOrEmpty(siteValue) ? siteValue : rootValue, culture);
            }

        }

        private static LocalizationPagePair GetOrAddLocalizationPages(string[] normalizedKey, CultureInfo culture)
        {
            var site = SiteDefinition.Current;

            IContent siteLocalizationPage = null;
            if (site != null)
                siteLocalizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey, site, new LoaderOptions { LanguageLoaderOption.FallbackWithMaster(culture) });

            var rootLocalizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey, new LoaderOptions { LanguageLoaderOption.FallbackWithMaster(culture) });

            if ((rootLocalizationPage == null || (site != null && siteLocalizationPage == null)) && _updateContent)
            {
                lock (Lock)
                {
                    // Make sure the page exists in the root
                    if (rootLocalizationPage == null)
                        rootLocalizationPage = LocalizationPageService.Service.AddLocalizationPage(normalizedKey);

                    // If a site is present, make sure the page exists in the site.
                    if (site != null && siteLocalizationPage == null)
                        siteLocalizationPage = LocalizationPageService.Service.AddLocalizationPage(normalizedKey, site);
                }
            }


            return new LocalizationPagePair
            {
                RootLocalizationPage = rootLocalizationPage,
                SiteLocalizationPage = siteLocalizationPage
            };
        }

        private static string GetValueFromLocalizationPagePair(string originalKey, string[] normalizedKey, LocalizationPagePair localizationPages)
        {
            // Get values from both the site and root localization page to make sure the properties are created on both pages
            string siteValue = null;
            if (localizationPages.SiteLocalizationPage != null)
                siteValue = GetOrAddPropertyValue(originalKey, normalizedKey, localizationPages.SiteLocalizationPage);

            var rootValue = GetOrAddPropertyValue(originalKey, normalizedKey, localizationPages.RootLocalizationPage);

            return !string.IsNullOrEmpty(siteValue) ? siteValue : rootValue;
        }

        private static string GetOrAddPropertyValue(string originalKey, string[] normalizedKey,
            IContent localizationPage)
        {
            string hashedPropertyName;
            if (!PropertyDefinitionMapping.TryGetValue(originalKey, out hashedPropertyName))
            {
                hashedPropertyName = LocalizationPageService.Service.GeneratePropertyName(originalKey);
                PropertyDefinitionMapping.TryAdd(originalKey, hashedPropertyName);
            }

            string value;
            var tryGetValue = localizationPage.Property.TryGetPropertyValue(hashedPropertyName, out value);

            if (!tryGetValue && _updateContent)
                lock (Lock)
                    if (!localizationPage.Property.TryGetPropertyValue(hashedPropertyName, out value))
                        LocalizationPageService.Service.UpdatePageTypeDefinition(localizationPage, originalKey, normalizedKey, hashedPropertyName);

            return value;
        }

        private static bool IsSupportedKey(string[] normalizedKey)
        {
            return normalizedKey[0].Equals(_prefix, StringComparison.OrdinalIgnoreCase);
        }

        public override IEnumerable<CultureInfo> AvailableLanguages
        {
            get { return ServiceLocator.Current.GetInstance<ILanguageBranchRepository>().ListEnabled().Select(x => x.Culture); }
        }
    }
}
