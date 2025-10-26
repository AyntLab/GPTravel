using System.Globalization;
using System.Resources;

namespace GPTravel
{
    public static class LocalizationResourceManager
    {
        private static readonly ResourceManager _resourceManager =
            new ResourceManager("GPTravel.Resources.Strings.AppResources", typeof(LocalizationResourceManager).Assembly);

        private static CultureInfo _currentCulture = CultureInfo.CurrentCulture;

        public static string GetString(string key)
        {
            return _resourceManager.GetString(key, _currentCulture) ?? key;
        }

        public static void SetCulture(string language)
        {
            // 言語名に応じてカルチャを切り替え
            _currentCulture = language switch
            {
                "日本語" or "Japanese" => new CultureInfo("ja"),
                "English" => new CultureInfo("en"),
                "Français" or "French" => new CultureInfo("fr"),
                "Deutsch" or "German" => new CultureInfo("de"),
                "Español" or "Spanish" => new CultureInfo("es"),
                _ => new CultureInfo("en"), // fallback
            };

            CultureInfo.DefaultThreadCurrentUICulture = _currentCulture;
            CultureInfo.DefaultThreadCurrentCulture = _currentCulture;
        }

        public static string CurrentLanguage => _currentCulture.TwoLetterISOLanguageName;
    }
}
