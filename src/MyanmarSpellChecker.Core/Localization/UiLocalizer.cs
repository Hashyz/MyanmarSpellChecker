using System;
using System.IO;
using System.Text;

namespace MyanmarSpellChecker.Core.Localization
{
    public enum UiLanguage
    {
        English = 0,
        Myanmar = 1
    }

    /// <summary>
    /// Shared, zero-overhead UI localization coordinator for Microsoft Word &amp; Excel Add-ins.
    /// Defaults to English (<see cref="UiLanguage.English"/>) and supports live 1-click hot-switching
    /// between English and Myanmar (<c>မြန်မာ</c>) without restarting Office.
    /// </summary>
    public static class UiLocalizer
    {
        private static int _currentLanguageInt = (int)UiLanguage.English;

        public static event Action<UiLanguage>? LanguageChanged;

        public static UiLanguage CurrentLanguage
        {
            get => (UiLanguage)System.Threading.Volatile.Read(ref _currentLanguageInt);
            set
            {
                int next = value == UiLanguage.Myanmar ? (int)UiLanguage.Myanmar : (int)UiLanguage.English;
                int prev = System.Threading.Interlocked.Exchange(ref _currentLanguageInt, next);
                if (prev != next)
                {
                    LanguageChanged?.Invoke((UiLanguage)next);
                }
            }
        }

        public static bool IsEnglish => CurrentLanguage == UiLanguage.English;

        public static string Pick(string english, string myanmar) =>
            IsEnglish ? english : myanmar;

        public static UiLanguage ToggleLanguage()
        {
            UiLanguage next = IsEnglish ? UiLanguage.Myanmar : UiLanguage.English;
            CurrentLanguage = next;
            return next;
        }

        public static string FormatLanguageBadge() =>
            IsEnglish ? "A/က EN" : "A/က မြန်မာ";

        public static string FormatLanguageTooltip() =>
            IsEnglish
                ? "UI Language: English (Default) — Click to switch to Myanmar (မြန်မာ)"
                : "ဘာသာစကား: မြန်မာ — English သို့ ပြောင်းရန် နှိပ်ပါ";

        public static UiLanguage LoadSavedLanguage(string configFilePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configFilePath) && File.Exists(configFilePath))
                {
                    string raw = File.ReadAllText(configFilePath, Encoding.UTF8).Trim();
                    if (string.Equals(raw, "Myanmar", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "my", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "mm", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "my-MM", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "မြန်မာ", StringComparison.Ordinal))
                    {
                        return UiLanguage.Myanmar;
                    }
                }
            }
            catch { }

            return UiLanguage.English;
        }

        public static void SaveLanguage(string configFilePath, UiLanguage language)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configFilePath))
                    return;

                string? dir = Path.GetDirectoryName(configFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string serialized = language == UiLanguage.Myanmar ? "Myanmar" : "English";
                File.WriteAllText(configFilePath, serialized, Encoding.UTF8);
            }
            catch { }
        }
    }
}

