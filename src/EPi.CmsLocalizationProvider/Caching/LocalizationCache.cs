using System;
using System.Collections.Generic;
using System.Linq;
using EPiServer.Core;
using EPiServer.Web;

namespace EPi.CmsLocalizationProvider.Caching
{
    [Serializable]
    public class LocalizationCache : IIsDirty
    {
        protected bool _isDirty = false;
        public bool IsDirty { get { return _isDirty; } }

        public LocalizationCache()
        {
            RootLocaliationContainer = new CachedLocalizationContainer();
            Sites = new List<CachedLocalizationSite>();
        }

        protected CachedLocalizationContainer RootLocaliationContainer { get; set; }

        protected List<CachedLocalizationSite> Sites { get; set; }

        public bool TryGetPage(string basePath, out ContentReference pageReference)
        {
            pageReference = null;

            var page = RootLocaliationContainer.LocalizationPages.FirstOrDefault(x => x.BasePath == basePath);
            if (page == null) return false;

            pageReference = page.LocalizationPageReference;
            return true;
        }

        public bool TryGetPage(string basePath, SiteDefinition siteDefinition, out ContentReference pageReference)
        {
            pageReference = null;

            var site = Sites.FirstOrDefault(x => x.Name == siteDefinition.Name);
            if (site == null) return false;

            var page = site.LocalizationContainer.LocalizationPages.FirstOrDefault(x => x.BasePath == basePath);
            if (page == null) return false;

            pageReference = page.LocalizationPageReference;
            return true;
        }

        public bool TryGetContainer(out ContentReference containerReference)
        {
            containerReference = RootLocaliationContainer.LocalizationContainerReference;
            return RootLocaliationContainer.LocalizationContainerReference != null;
        }

        public bool TryGetContainer(SiteDefinition siteDefiniton, out ContentReference containerReference)
        {
            containerReference = null;

            var site = Sites.FirstOrDefault(x => x.Name == siteDefiniton.Name);
            if (site == null) return false;

            containerReference = site.LocalizationContainer.LocalizationContainerReference;
            return site.LocalizationContainer.LocalizationContainerReference != null;
        }

        public void AddOrUpdatePage(string basePath, ContentReference pageReference)
        {
            _isDirty = true;

            var cachedLocalizationPage = RootLocaliationContainer.LocalizationPages.FirstOrDefault(x => x.BasePath == basePath);

            if (cachedLocalizationPage != null)
            {
                cachedLocalizationPage.LocalizationPageReference = pageReference;
            }
            else
            {
                RootLocaliationContainer.LocalizationPages.Add(new CachedLocalizationPage
                {
                    BasePath = basePath,
                    LocalizationPageReference = pageReference
                });
            }
        }

        public void AddOrUpdatePage(string basePath, SiteDefinition siteDefinition, ContentReference pageReference)
        {
            _isDirty = true;

            var site = Sites.FirstOrDefault(x => x.Name == siteDefinition.Name);

            if (site != null)
            {
                var page = site.LocalizationContainer.LocalizationPages.FirstOrDefault(x => x.BasePath == basePath);

                if (page != null)
                {
                    page.LocalizationPageReference = pageReference;
                }
                else
                {
                    site.LocalizationContainer.LocalizationPages.Add(new CachedLocalizationPage
                    {
                        BasePath = basePath,
                        LocalizationPageReference = pageReference
                    });
                }
            }
            else
            {
                site = new CachedLocalizationSite();
                site.Name = siteDefinition.Name;
                site.LocalizationContainer.LocalizationPages.Add(new CachedLocalizationPage
                {
                    BasePath = basePath,
                    LocalizationPageReference = pageReference
                });
                Sites.Add(site);
            }
        }

        public void AddOrUpdateContainer(ContentReference containerReference)
        {
            _isDirty = true;
            RootLocaliationContainer.LocalizationContainerReference = containerReference;
        }

        public void AddOrUpdateContainer(SiteDefinition siteDefinition, ContentReference containerReference)
        {
            _isDirty = true;

            var site = Sites.FirstOrDefault(x => x.Name == siteDefinition.Name);

            if (site != null)
            {
                site.LocalizationContainer.LocalizationContainerReference = containerReference;
            }
            else
            {
                site = new CachedLocalizationSite();
                site.Name = siteDefinition.Name;
                site.LocalizationContainer.LocalizationContainerReference = containerReference;
                Sites.Add(site);
            }
        }

        public void RemovePage(string basePath)
        {
            var count = RootLocaliationContainer.LocalizationPages.RemoveAll(x => x.BasePath == basePath);
            _isDirty = count > 0;
        }

        public void RemovePage(SiteDefinition siteDefinition, string basePath)
        {
            var site = Sites.FirstOrDefault(x => x.Name == siteDefinition.Name);
            if (site == null) return;

            var count = site.LocalizationContainer.LocalizationPages.RemoveAll(x => x.BasePath == basePath);
            _isDirty = count > 0;
        }

        public void RemoveContainer()
        {
            _isDirty = true;
            RootLocaliationContainer = new CachedLocalizationContainer();
        }

        public void RemoveContainer(SiteDefinition siteDefinition)
        {
            var site = Sites.FirstOrDefault(x => x.Name == siteDefinition.Name);
            if (site == null) return;

            site.LocalizationContainer = new CachedLocalizationContainer();
            _isDirty = true;
        }
    }
}
