using EPiServer.Core;
using EPiServer.DataAnnotations;

namespace EPi.CmsLocalizationProvider.PageTypes
{
    [ContentType(DisplayName = "[Localization] Container", GUID = "fe5039e5-7e87-43ca-9bcc-c7b6fe4951df")]
    [AvailableContentTypes(Include = new []{typeof(LocalizationPage)})]
    public class LocalizationContainer : PageData
    {
    }
}