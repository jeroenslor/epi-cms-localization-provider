using EPiServer.Core;
using EPiServer.ServiceLocation;

namespace EPi.CmsLocalizationProvider
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class LocalizationConfiguration
    {
        private ContentReference _containerParent;

        public ContentReference ContainerParent
        {
            get { return ContentReference.IsNullOrEmpty(_containerParent) ? ContentReference.RootPage : _containerParent; }
            set { _containerParent = value; }
        }
    }
}
