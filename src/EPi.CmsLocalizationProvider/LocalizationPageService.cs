using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EPi.CmsLocalizationProvider.Caching;
using EPi.CmsLocalizationProvider.Model;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Filters;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using EPiServer.Web;

namespace EPi.CmsLocalizationProvider
{
    [ServiceConfiguration(ServiceType = typeof(LocalizationPageService))]
    public class LocalizationPageService
    {
        private Injected<IContentRepository> _contentRepository { get; set; }
        private Injected<IContentTypeRepository> _contentTypeRepository { get; set; }
        private Injected<IPropertyDefinitionRepository> _propertyDefinitionRepository { get; set; }
        private Injected<ITabDefinitionRepository> _tabDefinitionRepository { get; set; }
        private Injected<IAvailableSettingsRepository> _availableContentTypeRepository { get; set; }
        private Injected<IPropertyDefinitionTypeRepository> _propertyDefinitionTypeRepository { get; set; }
        private static readonly object Lock = new object();
        private readonly PropertyDefinitionType _stringPropertyDefinitionType;
        private static readonly ILogger Logger = LogManager.GetLogger();

        public LocalizationPageService()
        {
            _stringPropertyDefinitionType = _propertyDefinitionTypeRepository.Service.Load(
                PropertyDefinitionType.ResolvePropertyDataType(PropertyDataType.LongString));
        }

        public virtual void UpdatePageTypeDefinition(IContent localizationPage, string originalKey, string[] normalizedKey, string hashedPropertyName)
        {
            if (normalizedKey.Length < 3)
                throw new InvalidOperationException("The translation label path needs to be at least 3 levels deep (including the provider prefix)");

            string tabName = normalizedKey.Length > 3 ? normalizedKey[2] : SystemTabNames.Content; // ignore the prefix
            var tabDefinition = GetOrCreateTabDefinition(tabName);

            var contentType = _contentTypeRepository.Service.Load(localizationPage.ContentTypeID);
            if (contentType.PropertyDefinitions.Any(
                x => x.Name.Equals(hashedPropertyName, StringComparison.OrdinalIgnoreCase))) return;

            AddNewPropertyDefinitionToPageType(contentType, hashedPropertyName,
                normalizedKey.Length > 4
                    ? string.Join(" ", normalizedKey.Skip(normalizedKey.Length - 2).Take(2))
                    : normalizedKey.Last(), originalKey, tabDefinition);
        }

        public virtual TabDefinition GetOrCreateTabDefinition(string tabName)
        {
            var tabDefinition = _tabDefinitionRepository.Service.Load(tabName);

            if (tabDefinition == null)
            {
                _tabDefinitionRepository.Service.Save(new TabDefinition(-1, tabName, AccessLevel.Read, -1, false));
                tabDefinition = _tabDefinitionRepository.Service.Load(tabName);
            }

            return tabDefinition;
        }

        public virtual void AddNewPropertyDefinitionToPageType(ContentType contentType, string hashedPropertyName, string editCaption, string helpText, TabDefinition tabDefinition)
        {
            var propertyDefinition = new PropertyDefinition
            {
                ContentTypeID = contentType.ID,
                ID = 0,
                DisplayEditUI = true,
                EditCaption = editCaption,
                ExistsOnModel = false,
                HelpText = helpText,
                LanguageSpecific = true,
                Name = hashedPropertyName,
                Required = false,
                Searchable = false,
                Tab = tabDefinition,
                Type = _stringPropertyDefinitionType
            };
            _propertyDefinitionRepository.Service.Save(propertyDefinition);

            contentType = (ContentType)contentType.CreateWritableClone();
            contentType.PropertyDefinitions.Add(propertyDefinition);
            _contentTypeRepository.Service.Save(contentType);
        }

        public virtual LocalizationContainer AddLocalizationContainer()
        {
            return AddLocalizationContainer(ContentReference.RootPage);
        }

        public virtual LocalizationContainer AddLocalizationContainer(SiteDefinition site)
        {
            return AddLocalizationContainer(site.StartPage);
        }

        public virtual LocalizationContainer AddLocalizationContainer(ContentReference contentReference)
        {
            var containerParent = _contentRepository.Service.Get<IContent>(contentReference, CultureInfo.InvariantCulture);

            var localizable = containerParent as ILocalizable;
            var localizationContainer = _contentRepository.Service.GetDefault<LocalizationContainer>(containerParent.ContentLink,
                    localizable != null ? localizable.MasterLanguage : new CultureInfo("en"));
            localizationContainer.Name = "Labels";

            return _contentRepository.Service.Get<LocalizationContainer>(
                    _contentRepository.Service.Save(localizationContainer, SaveAction.Publish, AccessLevel.NoAccess),
                    CultureInfo.InvariantCulture);
        }

        public virtual IContent AddLocalizationPage(string[] normalizedKey)
        {
            var localizationContainer = GetOrAddLocalizationContainer();
            return AddLocalizationPage(normalizedKey, localizationContainer);
        }

        public virtual IContent AddLocalizationPage(string[] normalizedKey, SiteDefinition site)
        {
            var localizationContainer = GetOrAddLocalizationContainer(site);
            return AddLocalizationPage(normalizedKey, localizationContainer);
            
        }

        public virtual IContent AddLocalizationPage(string[] normalizedKey, LocalizationContainer localizationContainer)
        {
            if (localizationContainer == null)
                return null;

            var contentType = GetOrCreateContentType(normalizedKey, localizationContainer);

            var localizationPage = _contentRepository.Service.GetDefault<PageData>(localizationContainer.ContentLink,
                contentType.ID, localizationContainer.MasterLanguage);
            localizationPage.Name = normalizedKey[1];
            localizationPage["BasePath"] = normalizedKey[1];
            _contentRepository.Service.Save(localizationPage, SaveAction.Publish, AccessLevel.NoAccess);

            return localizationPage;
        }

        private ContentType GetOrCreateContentType(string[] normalizedKey, LocalizationContainer localizationContainer)
        {
            var basePath = normalizedKey[1];
            var contentTypeName = basePath + "LocalizationPage";

            var existingContentType = _contentTypeRepository.Service.Load(contentTypeName);
            if (existingContentType != null)
                return existingContentType;

            var contentType = new PageType
            {
                Name = contentTypeName,
                DisplayName = string.Format("[Localization] {0}", contentTypeName),
            };
            _contentTypeRepository.Service.Save(contentType);
            
            var basePathProperty = new PropertyDefinition
            {
                ContentTypeID = contentType.ID,
                ID = 0,
                DisplayEditUI = false,                
                ExistsOnModel = false,
                Name = "BasePath",
                Type = _stringPropertyDefinitionType
            };
            _propertyDefinitionRepository.Service.Save(basePathProperty);
            
            var writableClone = (ContentType)contentType.CreateWritableClone();
            writableClone.PropertyDefinitions.Add(basePathProperty);
            _contentTypeRepository.Service.Save(writableClone);            

            var containerContentType = _contentTypeRepository.Service.Load(localizationContainer.ContentTypeID);
            var availableSetting = _availableContentTypeRepository.Service.GetSetting(containerContentType);
            availableSetting.Availability = Availability.Specific;
            availableSetting.AllowedContentTypeNames.Add(contentType.Name);
            _availableContentTypeRepository.Service.RegisterSetting(containerContentType, availableSetting);

            return contentType;
        }

        public virtual IContent GetLocalizationPage(string[] normalizedKey, LoaderOptions loaderOptions)
        {
            ContentReference cachedLocalizationPageReference;
            var basePath = normalizedKey[1];
            IContent localizationPage;
            using (var accessor = new LocalizationCacheAccessor())
            {
                if (accessor.Cache.TryGetPage(basePath, out cachedLocalizationPageReference))
                {
                    try
                    {
                        return _contentRepository.Service.Get<IContent>(cachedLocalizationPageReference, loaderOptions);
                    }
                    catch (PageNotFoundException)
                    {
                        // this can happen when we cached the localization pagereference but the page has been removed mean while.
                        accessor.Cache.RemovePage(basePath);
                    }
                }

                var localizationPages = GetAllLocalizationPages(loaderOptions);
                localizationPage = localizationPages.SingleOrDefault(
                    x => x.Property.GetPropertyValue<string>("BasePath").Equals(basePath, StringComparison.OrdinalIgnoreCase));
                if (localizationPage != null)
                    accessor.Cache.AddOrUpdatePage(basePath, localizationPage.ContentLink);
            }
                

            return localizationPage;
        }

        public virtual IContent GetLocalizationPage(string[] normalizedKey, SiteDefinition site, LoaderOptions loaderOptions)
        {
            ContentReference cachedLocalizationPageReference;
            var basePath = normalizedKey[1];
            IContent localizationPage;
            using (var accessor = new LocalizationCacheAccessor())
            {
                if (accessor.Cache.TryGetPage(basePath, site, out cachedLocalizationPageReference))
                {
                    try
                    {
                        return _contentRepository.Service.Get<IContent>(cachedLocalizationPageReference, loaderOptions);
                    }
                    catch (PageNotFoundException)
                    {
                        // this can happen when we cached the localization pagereference but the page has been removed mean while.
                        accessor.Cache.RemovePage(site, basePath);
                    }
                }

                var localizationPages = GetAllLocalizationPages(site, loaderOptions);
                localizationPage = localizationPages.SingleOrDefault(
                    x => x.Property.GetPropertyValue<string>("BasePath").Equals(basePath, StringComparison.OrdinalIgnoreCase));
                if (localizationPage != null)
                    accessor.Cache.AddOrUpdatePage(basePath, site, localizationPage.ContentLink);
            }

            return localizationPage;
        }

        public virtual IEnumerable<IContent> GetAllLocalizationPages(LoaderOptions loaderOptions)
        {
            return _contentRepository.Service.GetChildren<IContent>(GetOrAddLocalizationContainer().ContentLink, loaderOptions);
        }

        public virtual IEnumerable<IContent> GetAllLocalizationPages(SiteDefinition site, LoaderOptions loaderOptions)
        {
            return _contentRepository.Service.GetChildren<IContent>(GetOrAddLocalizationContainer(site).ContentLink, loaderOptions);
        }

        public virtual LocalizationContainer GetOrAddLocalizationContainer()
        {
            using (var accessor = new LocalizationCacheAccessor())
            {
                ContentReference cachedRootContainerContentReference;

                if (!accessor.Cache.TryGetContainer(out cachedRootContainerContentReference) || cachedRootContainerContentReference == null)
                {
                    lock (Lock)
                    {
                        var findLocalizationContainer = FindLocalizationContainer(LanguageSelector.MasterLanguage()) ?? AddLocalizationContainer();
                        accessor.Cache.AddOrUpdateContainer(findLocalizationContainer.ContentLink);
                        return findLocalizationContainer;
                    }
                }

                try
                {
                    return _contentRepository.Service.Get<LocalizationContainer>(cachedRootContainerContentReference, LanguageSelector.MasterLanguage()); 
                }
                catch (PageNotFoundException)
                {
                    // this can happen when we cached the localization pagereference but the page has been removed mean while.
                    var newLocalizationContainer = AddLocalizationContainer();
                    accessor.Cache.AddOrUpdateContainer(newLocalizationContainer.ContentLink);
                    return newLocalizationContainer;
                }
            }
        }

        public virtual LocalizationContainer GetOrAddLocalizationContainer(SiteDefinition site)
        {
            using (var accessor = new LocalizationCacheAccessor())
            {
                ContentReference cachedRootContainerContentReference;

                if (!accessor.Cache.TryGetContainer(site, out cachedRootContainerContentReference) || cachedRootContainerContentReference == null)
                {
                    lock (Lock)
                    {
                        var findLocalizationContainer = FindLocalizationContainer(site, LanguageSelector.MasterLanguage()) ?? AddLocalizationContainer(site);
                        accessor.Cache.AddOrUpdateContainer(site, findLocalizationContainer.ContentLink);
                        return findLocalizationContainer;
                    }
                }

                try
                {
                    return _contentRepository.Service.Get<LocalizationContainer>(cachedRootContainerContentReference, LanguageSelector.MasterLanguage());
                }
                catch (PageNotFoundException)
                {
                    // this can happen when we cached the localization pagereference but the page has been removed mean while.
                    var newLocalizationContainer = AddLocalizationContainer(site);
                    accessor.Cache.AddOrUpdateContainer(site, newLocalizationContainer.ContentLink);
                    return newLocalizationContainer;
                }
            }

        }

        protected virtual LocalizationContainer FindLocalizationContainer(ILanguageSelector languageSelector)
        {
            var rootPage = ContentReference.RootPage;
            if (rootPage == null)
            {
                // the GetString method is called before EPiServer is fully initialized.
                // If this is the case we are not able to return any resources since we depend on the epi cms api
                return null;
            }

            try
            {
                // Just get the LocalizationContainer from the root of EPiServer.
                var containers =  _contentRepository.Service.GetChildren<LocalizationContainer>(rootPage, languageSelector.Language).Where(x => x != null);

                if (containers.Count() > 1)
                    throw new InvalidOperationException("The root page has more than 1 instance of the Localizations container in its children, this is not allowed");

                return containers.Single();
            }
            catch (ContentProviderNotFoundException)
            {
                // the GetString method is called before EPiServer is fully initialized. 
                // If this is the case we are not able to return any resources since we depend on the epi cms api                        
                return null;
            }            
        }

        protected virtual LocalizationContainer FindLocalizationContainer(SiteDefinition site, ILanguageSelector languageSelector)
        {
            var rootPage = ContentReference.RootPage;
            if (rootPage == null)
            {
                // the GetString method is called before EPiServer is fully initialized.
                // If this is the case we are not able to return any resources since we depend on the epi cms api
                return null;
            }

            try
            {
                // Search for a single LocalizationContainer throughout the current site.
                var criteria = new PropertyCriteria
                {
                    Condition = CompareCondition.Equal,
                    Value = typeof(LocalizationContainer).Name,
                    Required = true,
                    Name = "PageTypeName",
                    Type = PropertyDataType.PageType
                };

                var siteHomePage = _contentRepository.Service.Get<PageData>(site.StartPage);

                var localizationPages = DataFactory.Instance.FindPagesWithCriteria(siteHomePage.PageLink, new PropertyCriteriaCollection { criteria }, null, languageSelector);

                if (localizationPages.Count > 1)
                    throw new InvalidOperationException("This site contains more than 1 instance of the Localizations container this is not allowed");

                return (LocalizationContainer)localizationPages.FirstOrDefault();
            }
            catch (ContentProviderNotFoundException)
            {
                // the GetString method is called before EPiServer is fully initialized. 
                // If this is the case we are not able to return any resources since we depend on the epi cms api                        
                return null;
            }
        }

        /// <summary>
        /// The name of a property can only be 50 characters long. This methods uses md5 hashing to ensure the name does not exceed the limit.
        /// </summary>
        /// <returns>The hashed name as a 32-character, hexadecimal-formatted string.</returns>
        public virtual string GeneratePropertyName(string originalName)
        {
            using (var md5Hash = MD5.Create())
            {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(originalName));

                var sBuilder = new StringBuilder("x"); // the name has to start with a letter (required by epi)
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }

                return sBuilder.ToString();
            }
        }
    }
}
