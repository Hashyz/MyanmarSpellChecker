using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace MyanmarSpellChecker.Core.Dictionary
{
    /// <summary>
    /// Zero-dependency offline English Text-to-Speech (TTS) helper powered by Windows built-in SAPI.SpVoice COM automation.
    /// Safe on every Windows 7/8/10/11 desktop with zero assembly references and graceful no-op fallback if audio is unavailable.
    /// </summary>
    public static class OfflineSpeechService
    {
        private static readonly object VoiceLock = new object();
        private static object? s_cachedSpVoice;
        private static bool s_spVoiceUnavailable;

        private static readonly Regex StripParenAnnotationsRegex = new Regex(
            @"\([^)]*\)|\[[^\]]*\]",
            RegexOptions.Compiled);

        // SAPI SpeechVoiceSpeakFlags: SVSFlagsAsync (1) | SVSFPurgeBeforeSpeak (2) | SVSFIsNotXML (16)
        private const int SapiAsyncPurgePlainTextFlags = 1 | 2 | 16;

        public static string CleanSpeakableEnglishTerm(string? rawEnglish)
        {
            if (string.IsNullOrWhiteSpace(rawEnglish))
                return string.Empty;

            string s = StripParenAnnotationsRegex.Replace(rawEnglish!, " ");
            int splitIdx = s.IndexOfAny(new[] { ';', ',', '/', '|', '•' });
            if (splitIdx > 0)
                s = s.Substring(0, splitIdx);

            s = s.Trim();
            if (s.StartsWith("to ", StringComparison.OrdinalIgnoreCase) && s.Length > 4)
                s = s.Substring(3).Trim();

            // Verify there is at least one ASCII letter to speak
            bool hasAsciiLetter = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    hasAsciiLetter = true;
                    break;
                }
            }

            return hasAsciiLetter ? s : string.Empty;
        }

        public static void SpeakEnglishAsync(string? rawEnglish)
        {
            string clean = CleanSpeakableEnglishTerm(rawEnglish);
            if (string.IsNullOrEmpty(clean) || s_spVoiceUnavailable)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (VoiceLock)
                    {
                        if (s_spVoiceUnavailable)
                            return;

                        if (s_cachedSpVoice == null)
                        {
                            Type? spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice", throwOnError: false);
                            if (spVoiceType == null)
                            {
                                s_spVoiceUnavailable = true;
                                return;
                            }

                            s_cachedSpVoice = Activator.CreateInstance(spVoiceType);
                            if (s_cachedSpVoice == null)
                            {
                                s_spVoiceUnavailable = true;
                                return;
                            }
                        }

                        s_cachedSpVoice.GetType().InvokeMember(
                            "Speak",
                            BindingFlags.InvokeMethod,
                            null,
                            s_cachedSpVoice,
                            new object[] { clean, SapiAsyncPurgePlainTextFlags });
                    }
                }
                catch
                {
                    // Audio device missing or SAPI disabled on server SKU -> silently ignore
                }
            });
        }
    }
}

