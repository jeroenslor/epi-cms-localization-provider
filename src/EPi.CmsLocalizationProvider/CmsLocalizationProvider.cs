using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Framework.Localization;
using EPiServer.ServiceLocation;

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

            var localizationPage = GetOrAddLocalizationPage(normalizedKey, culture);
            if (localizationPage == null)
                return null;

            return GetOrAddPropertyValue(originalKey, normalizedKey, localizationPage);
        }        

        public override IEnumerable<ResourceItem> GetAllStrings(string originalKey, string[] normalizedKey, CultureInfo culture)
        {
            // the default provider returns null also for episerver labels so we need to prefix the custom labels.            
            if (!IsSupportedKey(normalizedKey))
                yield break;

            // TODO if the normalizedkey length is 1 we need to loop through all the pages and return the result

            var localizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey, new LoaderOptions() {LanguageLoaderOption.FallbackWithMaster(culture)});
            if (localizationPage == null)
                yield break;

            var path = originalKey[originalKey.Length - 1].Equals('/') ? originalKey : originalKey + "/";
            foreach (
                var property in
                    ServiceLocator.Current.GetInstance<IContentTypeRepository>()
                        .Load(localizationPage.ContentTypeID)
                        .PropertyDefinitions.Where(x => x.HelpText.Contains(path)))
                yield return new ResourceItem(property.HelpText, (string) localizationPage.Property[property.Name].Value, culture);
        }

        private static IContent GetOrAddLocalizationPage(string[] normalizedKey, CultureInfo culture)
        {
            var localizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey,
                new LoaderOptions() { LanguageLoaderOption.FallbackWithMaster(culture) });

            if (localizationPage == null && _updateContent)
                lock (Lock)
                {
                    localizationPage = LocalizationPageService.Service.GetLocalizationPage(normalizedKey,
                        new LoaderOptions() { LanguageLoaderOption.FallbackWithMaster(culture) });

                    if (localizationPage == null)
                        localizationPage = LocalizationPageService.Service.AddLocalizationPage(normalizedKey);
                }

            return localizationPage;
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
