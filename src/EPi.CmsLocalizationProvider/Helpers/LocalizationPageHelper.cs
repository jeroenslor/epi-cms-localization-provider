using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EPi.CmsLocalizationProvider.PageTypes;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Filters;
using EPiServer.Logging;
using EPiServer.Security;
using EPiServer.ServiceLocation;

namespace EPi.CmsLocalizationProvider.Helpers
{
    public class LocalizationPageHelper
    {
        private readonly IContentRepository _contentRepository;
        private readonly IContentTypeRepository _contentTypeRepository;
        private readonly IPropertyDefinitionRepository _propertyDefinitionRepository;
        private readonly ITabDefinitionRepository _tabDefinitionRepository;
        private readonly IAvailableSettingsRepository _availableContentTypeRepository;
        private readonly LocalizationConfiguration _configuration;
        private static readonly object Lock = new object();
        protected static ContentReference CachedLocalizationContainer;
        protected static ConcurrentDictionary<string, ContentReference> CachedLocalizationPages; 
        private readonly PropertyDefinitionType _stringPropertyDefinitionType;
        private static readonly ILogger Logger = LogManager.GetLogger();

        public LocalizationPageHelper()
            : this(
                ServiceLocator.Current.GetInstance<IContentRepository>(),
                ServiceLocator.Current.GetInstance<IContentTypeRepository>(),
                ServiceLocator.Current.GetInstance<IPropertyDefinitionRepository>(),
                ServiceLocator.Current.GetInstance<ITabDefinitionRepository>(),
                ServiceLocator.Current.GetInstance<IPropertyDefinitionTypeRepository>(),
                ServiceLocator.Current.GetInstance<IAvailableSettingsRepository>(),
                ServiceLocator.Current.GetInstance<LocalizationConfiguration>())
        {
        }

        private LocalizationPageHelper(IContentRepository contentRepository, IContentTypeRepository contentTypeRepository, IPropertyDefinitionRepository propertyDefinitionRepository, ITabDefinitionRepository tabDefinitionRepository, IPropertyDefinitionTypeRepository propertyDefinitionTypeRepository, IAvailableSettingsRepository availableContentTypeRepository, LocalizationConfiguration configuration)
        {
            CachedLocalizationPages = new ConcurrentDictionary<string, ContentReference>();

            _contentRepository = contentRepository;
            _contentTypeRepository = contentTypeRepository;
            _propertyDefinitionRepository = propertyDefinitionRepository;
            _tabDefinitionRepository = tabDefinitionRepository;
            _availableContentTypeRepository = availableContentTypeRepository;
            _configuration = configuration;

            _stringPropertyDefinitionType = propertyDefinitionTypeRepository.Load(
                PropertyDefinitionType.ResolvePropertyDataType(PropertyDataType.LongString));
        }

        public virtual void UpdatePageTypeDefinition(IContent localizationPage, string originalKey, string[] normalizedKey, string hashedPropertyName)
        {
            if (normalizedKey.Length < 3)
                throw new InvalidOperationException("The translation label path needs to be at least 3 levels deep (including the provider prefix)");

            string tabName = normalizedKey.Length > 3 ? normalizedKey[2] : SystemTabNames.Content; // ignore the prefix
            var tabDefinition = GetOrCreateTabDefinition(tabName);

            var contentType = _contentTypeRepository.Load(localizationPage.ContentTypeID);
            if (contentType.PropertyDefinitions.Any(
                x => x.Name.Equals(hashedPropertyName, StringComparison.OrdinalIgnoreCase))) return;

            AddNewPropertyDefinitionToPageType(contentType, hashedPropertyName,
                normalizedKey.Length > 4
                    ? string.Join(" ", normalizedKey.Skip(normalizedKey.Length - 2).Take(2))
                    : normalizedKey.Last(), originalKey, tabDefinition);
        }

        public virtual TabDefinition GetOrCreateTabDefinition(string tabName)
        {
            var tabDefinition = _tabDefinitionRepository.Load(tabName);

            if (tabDefinition == null)
            {
                _tabDefinitionRepository.Save(new TabDefinition(-1, tabName, AccessLevel.Read, -1, false));
                tabDefinition = _tabDefinitionRepository.Load(tabName);
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
            _propertyDefinitionRepository.Save(propertyDefinition);

            contentType = (ContentType)contentType.CreateWritableClone();
            contentType.PropertyDefinitions.Add(propertyDefinition);
            _contentTypeRepository.Save(contentType);
        }

        public virtual LocalizationContainer AddLocalizationContainer()
        {
            var containerParent = _contentRepository.Get<IContent>(_configuration.ContainerParent, CultureInfo.InvariantCulture);

            var localizable = containerParent as ILocalizable;
            var localizationContainer =
                _contentRepository.GetDefault<LocalizationContainer>(containerParent.ContentLink,
                    localizable != null ? localizable.MasterLanguage : new CultureInfo("en"));
            localizationContainer.Name = "Labels";
            
            return
                _contentRepository.Get<LocalizationContainer>(
                    _contentRepository.Save(localizationContainer, SaveAction.Publish, AccessLevel.NoAccess),
                    CultureInfo.InvariantCulture);
        }

        public virtual IContent AddLocalizationPage(string[] normalizedKey)
        {
            var localizationContainer = GetOrAddLocalizationContainer();
            if (localizationContainer == null)
                return null;

            var contentType = GetOrCreateContentType(normalizedKey, localizationContainer);

            var localizationPage = _contentRepository.GetDefault<PageData>(localizationContainer.ContentLink,
                contentType.ID, localizationContainer.MasterLanguage);
            localizationPage.Name = normalizedKey[1];
            localizationPage["BasePath"] = normalizedKey[1];
            _contentRepository.Save(localizationPage, SaveAction.Publish, AccessLevel.NoAccess);
            
            return localizationPage;
        }

        private ContentType GetOrCreateContentType(string[] normalizedKey, LocalizationContainer localizationContainer)
        {
            var basePath = normalizedKey[1];
            var contentTypeName = basePath + "LocalizationPage";

            var existingContentType = _contentTypeRepository.Load(contentTypeName);
            if (existingContentType != null)
                return existingContentType;

            var contentType = new PageType
            {
                Name = contentTypeName,
                DisplayName = string.Format("[Localization] {0}", contentTypeName),
            };
            _contentTypeRepository.Save(contentType);
            
            var basePathProperty = new PropertyDefinition
            {
                ContentTypeID = contentType.ID,
                ID = 0,
                DisplayEditUI = false,                
                ExistsOnModel = false,
                Name = "BasePath",
                Type = _stringPropertyDefinitionType
            };
            _propertyDefinitionRepository.Save(basePathProperty);
            
            var writableClone = (ContentType)contentType.CreateWritableClone();
            writableClone.PropertyDefinitions.Add(basePathProperty);
            _contentTypeRepository.Save(writableClone);            

            var containerContentType = _contentTypeRepository.Load(localizationContainer.ContentTypeID);
            var availableSetting = _availableContentTypeRepository.GetSetting(containerContentType);
            availableSetting.Availability = Availability.Specific;
            availableSetting.AllowedContentTypeNames.Add(contentType.Name);
            _availableContentTypeRepository.RegisterSetting(containerContentType, availableSetting);

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
                    return _contentRepository.Get<IContent>(cachedLocalizationPageReference, loaderOptions);
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
            return _contentRepository.GetChildren<IContent>(GetOrAddLocalizationContainer().ContentLink, loaderOptions);
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
                return _contentRepository.Get<LocalizationContainer>(CachedLocalizationContainer, LanguageSelector.MasterLanguage());
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
