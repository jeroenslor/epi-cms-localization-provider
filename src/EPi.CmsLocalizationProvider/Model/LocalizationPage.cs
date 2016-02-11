using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;

namespace EPi.CmsLocalizationProvider.Model
{
    [ContentType(
        DisplayName = "Localization Page",
        Description =
            "A page that containing all the labels used throughout the whole website. The labels are added dynamically.",
        GUID = "0cd7d018-04a8-4de8-a879-72cc79801ddf"
        )]
    public class LocalizationPage : PageData
    {
        [Editable(false)]
        public virtual string BasePath { get; set; }
    }
}
