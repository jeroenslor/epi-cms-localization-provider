namespace EPi.CmsLocalizationProvider.Helpers
{
    public static class StringHelper
    {
        public static string FirstCharToUpper(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
