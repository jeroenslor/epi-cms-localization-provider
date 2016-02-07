# episerver-cms-localization-provider
A custom LocalizationProvider that uses auto generated cms pages to manage label translations instead of the lang.xml files. This enables content editors to manage label translations by using the CMS. It also enables versioning and fallback languages since this is default content functionality. 

# Installation
Add or replace the following localization node in the web.config:
```xml
<localization fallbackBehavior="FallbackCulture, MissingMessage, Echo" fallbackCulture="en">
    <providers>
        <add updatePageTypeDefinition="true" prefix="your-project-name" name="customLabels" type="EPi.CmsLocalizationProvider.Providers.CmsLocalizationProvider, EPi.CmsLocalizationProvider" />
    </providers>
</localization>
```
