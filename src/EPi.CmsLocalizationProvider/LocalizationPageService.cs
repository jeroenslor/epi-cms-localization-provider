using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EPi.CmsLocalizationProvider.Model;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Filters;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;

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
        private Injected<LocalizationConfiguration> _configuration { get; set; }
        private static readonly object Lock = new object();
        protected static ContentReference CachedLocalizationContainer;
        protected static ConcurrentDictionary<string, ContentReference> CachedLocalizationPages; 
        private readonly PropertyDefinitionType _stringPropertyDefinitionType;
        private static readonly ILogger Logger = LogManager.GetLogger();

        public LocalizationPageService()
        {
            CachedLocalizationPages = new ConcurrentDictionary<string, ContentReference>();

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
            var containerParent = _contentRepository.Service.Get<IContent>(_configuration.Service.ContainerParent, CultureInfo.InvariantCulture);

            var localizable = containerParent as ILocalizable;
            var localizationContainer =
                _contentRepository.Service.GetDefault<LocalizationContainer>(containerParent.ContentLink,
                    localizable != null ? localizable.MasterLanguage : new CultureInfo("en"));
            localizationContainer.Name = "Labels";
            
            return
                _contentRepository.Service.Get<LocalizationContainer>(
                    _contentRepository.Service.Save(localizationContainer, SaveAction.Publish, AccessLevel.NoAccess),
                    CultureInfo.InvariantCulture);
        }

        public virtual IContent AddLocalizationPage(string[] normalizedKey)
        {
            var localizationContainer = GetOrAddLocalizationContainer();
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
            if (CachedLocalizationPages.TryGetValue(basePath, out cachedLocalizationPageReference))
            {
                try
                {
                    return _contentRepository.Service.Get<IContent>(cachedLocalizationPageReference, loaderOptions);
                }
                catch (PageNotFoundException)
                {
                    // this can happen when we cached the localization pagereference but the page has been removed mean while.
                    ContentReference removedReference;
                    CachedLocalizationPages.TryRemove(basePath, out removedReference);
                }
            }

            var localizationPages = GetAllLocalizationPages(loaderOptions);
            var localizationPage = localizationPages.SingleOrDefault(
                x => x.Property.GetPropertyValue<string>("BasePath").Equals(basePath, StringComparison.OrdinalIgnoreCase));
            if (localizationPage != null)
                CachedLocalizationPages.TryAdd(basePath, localizationPage.ContentLink);

            return localizationPage;
        }

        public virtual IEnumerable<IContent> GetAllLocalizationPages(LoaderOptions loaderOptions)
        {
            return _contentRepository.Service.GetChildren<IContent>(GetOrAddLocalizationContainer().ContentLink, loaderOptions);
        }

        public virtual LocalizationContainer GetOrAddLocalizationContainer()
        {
            if (CachedLocalizationContainer == null)
                lock (Lock)
                {
                    var findLocalizationContainer = FindLocalizationContainer(LanguageSelector.MasterLanguage()) ?? AddLocalizationContainer();

                    CachedLocalizationContainer = findLocalizationContainer.ContentLink;
                    return findLocalizationContainer;
                }

            try
            {
                return _contentRepository.Service.Get<LocalizationContainer>(CachedLocalizationContainer, LanguageSelector.MasterLanguage());
            }
            catch (PageNotFoundException)
            {
                // this can happen when we cached the localization pagereference but the page has been removed mean while.
                var newLocalizationContainer = AddLocalizationContainer();
                CachedLocalizationContainer = newLocalizationContainer.ContentLink;
                return newLocalizationContainer;
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
                var criteria = new PropertyCriteria
                {
                    Condition = CompareCondition.Equal,
                    Value = typeof(LocalizationContainer).Name,
                    Required = true,
                    Name = "PageTypeName",
                    Type = PropertyDataType.PageType
                };

                var localizationPages = DataFactory.Instance.FindPagesWithCriteria(rootPage, new PropertyCriteriaCollection {criteria}, null, languageSelector);

                if(localizationPages.Count > 1)
                    throw new InvalidOperationException("The cms tree contains more then 1 instance of the Localizations container this is not allowed");

                return (LocalizationContainer) localizationPages.FirstOrDefault();
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
