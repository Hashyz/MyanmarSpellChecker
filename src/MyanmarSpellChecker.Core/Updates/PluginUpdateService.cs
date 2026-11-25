// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.Core.Updates
{
    [DataContract]
    public sealed class UpdateManifest
    {
        [DataMember(Name = "version", Order = 1)]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "tag_name", Order = 2)]
        public string TagName { get; set; } = string.Empty;

        [DataMember(Name = "published_utc", Order = 3)]
        public string PublishedUtc { get; set; } = string.Empty;

        [DataMember(Name = "setup_exe_file", Order = 4)]
        public string SetupExeFileName { get; set; } = "MyanmarSpellChecker-Setup.exe";

        [DataMember(Name = "setup_exe_sha256", Order = 5)]
        public string SetupExeSha256 { get; set; } = string.Empty;

        [DataMember(Name = "setup_exe_bytes", Order = 6)]
        public long SetupExeBytes { get; set; }

        [DataMember(Name = "setup_exe_url", Order = 7)]
        public string SetupExeDownloadUrl { get; set; } = string.Empty;

        [DataMember(Name = "easysetup_zip_file", Order = 8)]
        public string EasySetupZipFileName { get; set; } = "MyanmarSpellChecker-EasySetup-OfficeSuite.zip";

        [DataMember(Name = "easysetup_zip_sha256", Order = 9)]
        public string EasySetupZipSha256 { get; set; } = string.Empty;

        [DataMember(Name = "easysetup_zip_bytes", Order = 10)]
        public long EasySetupZipBytes { get; set; }

        [DataMember(Name = "easysetup_zip_url", Order = 11)]
        public string EasySetupZipDownloadUrl { get; set; } = string.Empty;

        [DataMember(Name = "database_file", Order = 12)]
        public string DatabaseFileName { get; set; } = "mlc_thatpon.db";

        [DataMember(Name = "database_sha256", Order = 13)]
        public string DatabaseSha256 { get; set; } = string.Empty;

        [DataMember(Name = "database_bytes", Order = 14)]
        public long DatabaseBytes { get; set; }

        [DataMember(Name = "database_url", Order = 15)]
        public string DatabaseDownloadUrl { get; set; } = string.Empty;

        [DataMember(Name = "core_dll_sha256", Order = 16)]
        public string CoreDllSha256 { get; set; } = string.Empty;

        [DataMember(Name = "word_dll_sha256", Order = 17)]
        public string WordDllSha256 { get; set; } = string.Empty;

        [DataMember(Name = "excel_dll_sha256", Order = 18)]
        public string ExcelDllSha256 { get; set; } = string.Empty;

        [DataMember(Name = "powerpoint_dll_sha256", Order = 19)]
        public string PowerPointDllSha256 { get; set; } = string.Empty;

        [DataMember(Name = "release_notes_en", Order = 20)]
        public string ReleaseNotesEn { get; set; } = string.Empty;

        [DataMember(Name = "release_notes_mm", Order = 21)]
        public string ReleaseNotesMm { get; set; } = string.Empty;

        [DataMember(Name = "rsa_signature_base64", Order = 22, EmitDefaultValue = false)]
        public string? RsaSignatureBase64 { get; set; }
    }

    [DataContract]
    internal sealed class GitHubReleaseAssetDto
    {
        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    [DataContract]
    internal sealed class GitHubReleaseDto
    {
        [DataMember(Name = "tag_name")]
        public string? TagName { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "published_at")]
        public string? PublishedAt { get; set; }

        [DataMember(Name = "body")]
        public string? Body { get; set; }

        [DataMember(Name = "html_url")]
        public string? HtmlUrl { get; set; }

        [DataMember(Name = "assets")]
        public List<GitHubReleaseAssetDto>? Assets { get; set; }
    }

    public enum LocalUpdatePackageKind
    {
        Invalid = 0,
        DictionaryDatabaseOnly = 1,
        EasySetupZipBundle = 2,
        SingleFileSetupExe = 3
    }

    public sealed class LocalPackageInspectionResult
    {
        public bool IsValid { get; set; }
        public LocalUpdatePackageKind Kind { get; set; }
        public string PackageVersion { get; set; } = string.Empty;
        public string FileSha256 { get; set; } = string.Empty;
        public long FileBytes { get; set; }
        public int VerifiedHeadwordCount { get; set; }
        public bool IsDictionaryOnlyChange { get; set; }
        public string ErrorMessageEn { get; set; } = string.Empty;
        public string ErrorMessageMm { get; set; } = string.Empty;
    }

    public sealed class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public bool IsDictionaryOnlyUpdate { get; set; }
        public bool SignatureVerified { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = PluginUpdateService.ReleasesWebUrl;
        public UpdateManifest? Manifest { get; set; }
        public string ErrorMessageEn { get; set; } = string.Empty;
        public string ErrorMessageMm { get; set; } = string.Empty;
    }

    public sealed class UpdatePreferences
    {
        public bool WeeklyAutoCheckEnabled { get; set; }
        public long LastCheckedUtcTicks { get; set; }
        public string AvailableCachedVersion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Privacy-first, zero-telemetry update &amp; package verification engine for Word, Excel, and PowerPoint.
    /// Zero background network calls occur unless the user explicitly clicks "Check Online" or opts into weekly checks.
    /// Supports both zero-restart live dictionary hot-swapping and SHA-256 verified deferred binary staging.
    /// </summary>
    public static class PluginUpdateService
    {
        public const string RepoOwner = "Hashyz";
        public const string RepoName = "MyanmarSpellChecker";
        public const string ReleasesWebUrl = "https://github.com/Hashyz/MyanmarSpellChecker/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/Hashyz/MyanmarSpellChecker/releases/latest";
        public const string EmbeddedPayloadResourceName = "MyanmarSpellChecker.Payload.zip";
        private const string DefaultDbPassword = "MM-GOV-THATPON-SQLCIPHER-2026!";

        public static string GetLocalDataRootDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "MyanmarSpellChecker");
        }

        public static string GetInstalledAppDir() => Path.Combine(GetLocalDataRootDir(), "app");

        public static string GetPreferencesFilePath() => Path.Combine(GetLocalDataRootDir(), "update_preferences.cfg");

        public static string GetCurrentInstalledVersion()
        {
            try
            {
                string asmLoc = typeof(PluginUpdateService).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(asmLoc) && File.Exists(asmLoc))
                {
                    string? fv = FileVersionInfo.GetVersionInfo(asmLoc).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fv))
                        return NormalizeVersionString(fv!);
                }
            }
            catch { }

            var attr = typeof(PluginUpdateService).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return NormalizeVersionString(attr?.Version ?? "1.0.84.0");
        }

        public static string NormalizeVersionString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "1.0.0.0";

            string s = raw.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1).Trim();

            int dashIdx = s.IndexOf('-');
            if (dashIdx > 0)
                s = s.Substring(0, dashIdx);

            string[] parts = s.Split('.');
            int[] nums = new int[4];
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]) || nums[i] < 0)
                    nums[i] = 0;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", nums[0], nums[1], nums[2], nums[3]);
        }

        public static bool TryParseVersion(string? raw, out Version version)
        {
            string norm = NormalizeVersionString(raw ?? string.Empty);
            if (Version.TryParse(norm, out Version? parsed) && parsed != null)
            {
                version = parsed;
                return true;
            }
            version = new Version(1, 0, 0, 0);
            return false;
        }

        public static bool IsNewerVersion(string? candidateVersion, string? currentVersion)
        {
            if (!TryParseVersion(candidateVersion, out Version cand))
                return false;
            if (!TryParseVersion(currentVersion, out Version cur))
                return true;
            return cand > cur;
        }

        public static string FormatShortDisplayVersion(string? rawVersion)
        {
            if (!TryParseVersion(rawVersion, out Version v))
                return "v1.0.0";
            return v.Revision > 0
                ? string.Format(CultureInfo.InvariantCulture, "v{0}.{1}.{2}.{3}", v.Major, v.Minor, v.Build, v.Revision)
                : string.Format(CultureInfo.InvariantCulture, "v{0}.{1}.{2}", v.Major, v.Minor, v.Build);
        }

        public static string ComputeFileSha256(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return ComputeStreamSha256(fs);
            }
        }

        public static string ComputeStreamSha256(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static byte[] BuildCanonicalManifestSignableBytes(UpdateManifest m)
        {
            string canonical = string.Join("|",
                NormalizeVersionString(m.Version),
                m.TagName ?? string.Empty,
                (m.SetupExeSha256 ?? string.Empty).ToLowerInvariant(),
                m.SetupExeBytes.ToString(CultureInfo.InvariantCulture),
                (m.EasySetupZipSha256 ?? string.Empty).ToLowerInvariant(),
                m.EasySetupZipBytes.ToString(CultureInfo.InvariantCulture),
                (m.DatabaseSha256 ?? string.Empty).ToLowerInvariant(),
                m.DatabaseBytes.ToString(CultureInfo.InvariantCulture),
                (m.CoreDllSha256 ?? string.Empty).ToLowerInvariant(),
                (m.WordDllSha256 ?? string.Empty).ToLowerInvariant(),
                (m.ExcelDllSha256 ?? string.Empty).ToLowerInvariant(),
                (m.PowerPointDllSha256 ?? string.Empty).ToLowerInvariant());
            return Encoding.UTF8.GetBytes(canonical);
        }

        public static void SignManifestWithRsaKey(UpdateManifest manifest, string rsaPrivateKeyXml)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            byte[] data = BuildCanonicalManifestSignableBytes(manifest);
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(rsaPrivateKeyXml);
                byte[] sig = rsa.SignData(data, CryptoConfig.MapNameToOID("SHA256")!);
                manifest.RsaSignatureBase64 = Convert.ToBase64String(sig);
            }
        }

        public static bool VerifyManifestRsaSignature(UpdateManifest manifest, string? rsaPublicKeyXml = null)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.RsaSignatureBase64))
                return false;

            try
            {
                string pubXml = string.IsNullOrWhiteSpace(rsaPublicKeyXml)
                    ? LicenseService.ResolvePublicKeyXml(null, LicenseService.DefaultEmbeddedPublicKeyXml)
                    : rsaPublicKeyXml!;
                byte[] data = BuildCanonicalManifestSignableBytes(manifest);
                byte[] sig = Convert.FromBase64String(manifest.RsaSignatureBase64!);
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(pubXml);
                    return rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256")!, sig);
                }
            }
            catch
            {
                return false;
            }
        }

        public static UpdateManifest BuildReleaseManifestFromArtifacts(
            string artifactsDir,
            string releasePkgDir,
            string version,
            string tagName,
            string? rsaPrivateKeyXml = null)
        {
            string normVer = NormalizeVersionString(version);
            string cleanTag = string.IsNullOrWhiteSpace(tagName)
                ? FormatShortDisplayVersion(normVer)
                : (tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName : "v" + tagName);
            string baseDownload = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{cleanTag}/";

            string setupExePath = Path.Combine(artifactsDir, "MyanmarSpellChecker-Setup.exe");
            string zipPath = Path.Combine(artifactsDir, "MyanmarSpellChecker-EasySetup-OfficeSuite.zip");
            string dbPath = File.Exists(Path.Combine(artifactsDir, "mlc_thatpon.db"))
                ? Path.Combine(artifactsDir, "mlc_thatpon.db")
                : Path.Combine(releasePkgDir, "mlc_thatpon.db");

            var manifest = new UpdateManifest
            {
                Version = normVer,
                TagName = cleanTag,
                PublishedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                SetupExeFileName = "MyanmarSpellChecker-Setup.exe",
                SetupExeDownloadUrl = baseDownload + "MyanmarSpellChecker-Setup.exe",
                EasySetupZipFileName = "MyanmarSpellChecker-EasySetup-OfficeSuite.zip",
                EasySetupZipDownloadUrl = baseDownload + "MyanmarSpellChecker-EasySetup-OfficeSuite.zip",
                DatabaseFileName = "mlc_thatpon.db",
                DatabaseDownloadUrl = baseDownload + "mlc_thatpon.db",
                ReleaseNotesEn = $"Myanmar Official Spell, Grammar & Dictionary Suite {cleanTag} ({normVer}) — Unified offline release for Microsoft Word, Excel, and PowerPoint.",
                ReleaseNotesMm = $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း၊ သဒ္ဒါနှင့် စွယ်စုံအဘိဓာန် {cleanTag} ({normVer}) — Microsoft Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံးအတွက် ဗားရှင်းအသစ်။"
            };

            if (File.Exists(setupExePath))
            {
                manifest.SetupExeSha256 = ComputeFileSha256(setupExePath);
                manifest.SetupExeBytes = new FileInfo(setupExePath).Length;
            }
            if (File.Exists(zipPath))
            {
                manifest.EasySetupZipSha256 = ComputeFileSha256(zipPath);
                manifest.EasySetupZipBytes = new FileInfo(zipPath).Length;
            }
            if (File.Exists(dbPath))
            {
                manifest.DatabaseSha256 = ComputeFileSha256(dbPath);
                manifest.DatabaseBytes = new FileInfo(dbPath).Length;
            }

            string coreDll = Path.Combine(releasePkgDir, "MyanmarSpellChecker.Core.dll");
            string wordDll = Path.Combine(releasePkgDir, "MyanmarSpellChecker.WordAddIn.dll");
            string excelDll = Path.Combine(releasePkgDir, "MyanmarSpellChecker.ExcelAddIn.dll");
            string pptDll = Path.Combine(releasePkgDir, "MyanmarSpellChecker.PowerPointAddIn.dll");

            if (File.Exists(coreDll)) manifest.CoreDllSha256 = ComputeFileSha256(coreDll);
            if (File.Exists(wordDll)) manifest.WordDllSha256 = ComputeFileSha256(wordDll);
            if (File.Exists(excelDll)) manifest.ExcelDllSha256 = ComputeFileSha256(excelDll);
            if (File.Exists(pptDll)) manifest.PowerPointDllSha256 = ComputeFileSha256(pptDll);

            if (!string.IsNullOrWhiteSpace(rsaPrivateKeyXml))
            {
                SignManifestWithRsaKey(manifest, rsaPrivateKeyXml!);
            }

            return manifest;
        }

        public static string SerializeManifestJson(UpdateManifest manifest)
        {
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, manifest);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static UpdateManifest? ParseManifestJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(ms) as UpdateManifest;
            }
        }

        public static bool IsDictionaryOnlyUpdate(UpdateManifest manifest, string? installedAppDir = null)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.DatabaseSha256) || string.IsNullOrWhiteSpace(manifest.DatabaseDownloadUrl))
                return false;
            if (string.IsNullOrWhiteSpace(manifest.CoreDllSha256) ||
                string.IsNullOrWhiteSpace(manifest.WordDllSha256) ||
                string.IsNullOrWhiteSpace(manifest.ExcelDllSha256) ||
                string.IsNullOrWhiteSpace(manifest.PowerPointDllSha256))
            {
                return false;
            }

            string appDir = string.IsNullOrWhiteSpace(installedAppDir) ? GetInstalledAppDir() : installedAppDir!;
            string coreDll = Path.Combine(appDir, "MyanmarSpellChecker.Core.dll");
            string wordDll = Path.Combine(appDir, "MyanmarSpellChecker.WordAddIn.dll");
            string excelDll = Path.Combine(appDir, "MyanmarSpellChecker.ExcelAddIn.dll");
            string pptDll = Path.Combine(appDir, "MyanmarSpellChecker.PowerPointAddIn.dll");
            string dbFile = Path.Combine(appDir, "mlc_thatpon.db");

            if (!File.Exists(coreDll) || !File.Exists(wordDll) || !File.Exists(excelDll) || !File.Exists(pptDll))
                return false;

            try
            {
                bool dllsIdentical =
                    string.Equals(ComputeFileSha256(coreDll), manifest.CoreDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(wordDll), manifest.WordDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(excelDll), manifest.ExcelDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(pptDll), manifest.PowerPointDllSha256, StringComparison.OrdinalIgnoreCase);

                if (!dllsIdentical)
                    return false;

                if (!File.Exists(dbFile))
                    return true;

                return !string.Equals(ComputeFileSha256(dbFile), manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static UpdatePreferences LoadPreferences()
        {
            var prefs = new UpdatePreferences();
            try
            {
                string path = GetPreferencesFilePath();
                if (!File.Exists(path))
                    return prefs;

                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;
                    int eq = rawLine.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = rawLine.Substring(0, eq).Trim();
                    string val = rawLine.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "WeeklyAutoCheck":
                            if (bool.TryParse(val, out bool w)) prefs.WeeklyAutoCheckEnabled = w;
                            break;
                        case "LastCheckedUtcTicks":
                            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t)) prefs.LastCheckedUtcTicks = t;
                            break;
                        case "AvailableCachedVersion":
                            prefs.AvailableCachedVersion = val;
                            break;
                    }
                }
            }
            catch { }
            return prefs;
        }

        public static void SavePreferences(UpdatePreferences prefs)
        {
            if (prefs == null)
                return;
            try
            {
                string path = GetPreferencesFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string content = string.Join(
                    Environment.NewLine,
                    $"WeeklyAutoCheck={prefs.WeeklyAutoCheckEnabled}",
                    $"LastCheckedUtcTicks={prefs.LastCheckedUtcTicks.ToString(CultureInfo.InvariantCulture)}",
                    $"AvailableCachedVersion={prefs.AvailableCachedVersion ?? string.Empty}");
                File.WriteAllText(path, content, Encoding.UTF8);
            }
            catch { }
        }

        public static bool ShouldRunOptInWeeklyCheckNow()
        {
            var prefs = LoadPreferences();
            if (!prefs.WeeklyAutoCheckEnabled)
                return false;

            if (prefs.LastCheckedUtcTicks <= 0)
                return true;

            DateTime lastUtc = new DateTime(prefs.LastCheckedUtcTicks, DateTimeKind.Utc);
            return (DateTime.UtcNow - lastUtc).TotalDays >= 7.0;
        }

        public static async Task<UpdateCheckResult> CheckGitHubReleaseAsync(CancellationToken cancellationToken = default)
        {
            string currentVer = GetCurrentInstalledVersion();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = CreateGitHubHttpClient(TimeSpan.FromSeconds(14)))
                {
                    using (var resp = await client.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false))
                    {
                        if (resp.StatusCode == HttpStatusCode.NotFound)
                        {
                            RecordCheckTimestamp(string.Empty);
                            return new UpdateCheckResult
                            {
                                Success = true,
                                IsUpdateAvailable = false,
                                CurrentVersion = currentVer,
                                LatestVersion = currentVer
                            };
                        }

                        resp.EnsureSuccessStatusCode();
                        string releaseJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var relSerializer = new DataContractJsonSerializer(typeof(GitHubReleaseDto));
                        GitHubReleaseDto? relDto;
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(releaseJson)))
                        {
                            relDto = relSerializer.ReadObject(ms) as GitHubReleaseDto;
                        }

                        if (relDto == null)
                        {
                            return new UpdateCheckResult
                            {
                                Success = false,
                                CurrentVersion = currentVer,
                                ErrorMessageEn = "GitHub release response could not be parsed.",
                                ErrorMessageMm = "GitHub မှ ဗားရှင်းအချက်အလက်ကို ဖတ်ရှု၍မရပါ။"
                            };
                        }

                        UpdateManifest? manifest = null;
                        var assets = relDto.Assets ?? new List<GitHubReleaseAssetDto>();
                        var manifestAsset = assets.FirstOrDefault(a =>
                            string.Equals(a.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl));

                        if (manifestAsset != null)
                        {
                            string manifestJson = await client.GetStringAsync(manifestAsset.BrowserDownloadUrl!).ConfigureAwait(false);
                            manifest = ParseManifestJson(manifestJson);
                        }

                        if (manifest == null)
                        {
                            string tagVer = NormalizeVersionString(relDto.TagName ?? relDto.Name ?? currentVer);
                            var setupAsset = assets.FirstOrDefault(a =>
                                a.Name != null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                            var zipAsset = assets.FirstOrDefault(a =>
                                a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                            var dbAsset = assets.FirstOrDefault(a =>
                                string.Equals(a.Name, "mlc_thatpon.db", StringComparison.OrdinalIgnoreCase));

                            manifest = new UpdateManifest
                            {
                                Version = tagVer,
                                TagName = relDto.TagName ?? FormatShortDisplayVersion(tagVer),
                                PublishedUtc = relDto.PublishedAt ?? string.Empty,
                                SetupExeFileName = setupAsset?.Name ?? "MyanmarSpellChecker-Setup.exe",
                                SetupExeBytes = setupAsset?.Size ?? 0,
                                SetupExeDownloadUrl = setupAsset?.BrowserDownloadUrl ?? string.Empty,
                                EasySetupZipFileName = zipAsset?.Name ?? "MyanmarSpellChecker-EasySetup-OfficeSuite.zip",
                                EasySetupZipBytes = zipAsset?.Size ?? 0,
                                EasySetupZipDownloadUrl = zipAsset?.BrowserDownloadUrl ?? string.Empty,
                                DatabaseFileName = dbAsset?.Name ?? "mlc_thatpon.db",
                                DatabaseBytes = dbAsset?.Size ?? 0,
                                DatabaseDownloadUrl = dbAsset?.BrowserDownloadUrl ?? string.Empty,
                                ReleaseNotesEn = relDto.Body ?? string.Empty,
                                ReleaseNotesMm = relDto.Body ?? string.Empty
                            };
                        }

                        string latestVer = NormalizeVersionString(manifest.Version);
                        bool newer = IsNewerVersion(latestVer, currentVer);
                        bool dictOnly = newer && IsDictionaryOnlyUpdate(manifest);
                        bool sigOk = VerifyManifestRsaSignature(manifest);

                        RecordCheckTimestamp(newer ? latestVer : string.Empty);

                        return new UpdateCheckResult
                        {
                            Success = true,
                            IsUpdateAvailable = newer,
                            IsDictionaryOnlyUpdate = dictOnly,
                            SignatureVerified = sigOk,
                            CurrentVersion = currentVer,
                            LatestVersion = latestVer,
                            ReleasePageUrl = !string.IsNullOrWhiteSpace(relDto.HtmlUrl) ? relDto.HtmlUrl! : ReleasesWebUrl,
                            Manifest = manifest
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = currentVer,
                    ErrorMessageEn = "Could not connect to GitHub Releases (" + ex.Message + "). If you are offline, use 'Update from Local File'.",
                    ErrorMessageMm = "GitHub သို့ ချိတ်ဆက်၍မရပါ (" + ex.Message + ")။ အင်တာနက်မရှိပါက 'Update from Local File' ဖြင့် USB/ဖိုင်မှတစ်ဆင့် အဆင့်မြှင့်နိုင်ပါသည်။"
                };
            }
        }

        private static void RecordCheckTimestamp(string cachedNewVersion)
        {
            var prefs = LoadPreferences();
            prefs.LastCheckedUtcTicks = DateTime.UtcNow.Ticks;
            prefs.AvailableCachedVersion = cachedNewVersion;
            SavePreferences(prefs);
        }

        public static async Task DownloadVerifiedAssetAsync(
            string downloadUrl,
            string destinationFilePath,
            string? expectedSha256,
            Action<long, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("Download URL is empty.", nameof(downloadUrl));

            string? dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tempDownload = destinationFilePath + ".part";
            if (File.Exists(tempDownload))
            {
                try { File.Delete(tempDownload); } catch { }
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = CreateGitHubHttpClient(TimeSpan.FromMinutes(6)))
            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var netStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var outStream = new FileStream(tempDownload, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    byte[] buffer = new byte[65536];
                    long downloaded = 0;
                    int read;
                    while ((read = await netStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await outStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        downloaded += read;
                        progressCallback?.Invoke(downloaded, totalBytes);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualSha = ComputeFileSha256(tempDownload);
                if (!string.Equals(actualSha, expectedSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tempDownload); } catch { }
                    throw new CryptographicException(
                        $"SHA-256 verification failed! Expected {expectedSha256}, got {actualSha}. Aborting update for security.");
                }
            }

            if (File.Exists(destinationFilePath))
                File.Delete(destinationFilePath);
            File.Move(tempDownload, destinationFilePath);
        }

        public static LocalPackageInspectionResult InspectLocalUpdatePackage(string filePath, string? decryptionPassword = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new LocalPackageInspectionResult
                {
                    IsValid = false,
                    ErrorMessageEn = "Selected update file does not exist.",
                    ErrorMessageMm = "ရွေးချယ်ထားသော ဖိုင်ကို မတွေ့ရှိပါ။"
                };
            }

            var fi = new FileInfo(filePath);
            string ext = fi.Extension.ToLowerInvariant();
            string sha256 = ComputeFileSha256(filePath);
            string pwd = string.IsNullOrWhiteSpace(decryptionPassword) ? DefaultDbPassword : decryptionPassword!;

            try
            {
                if (ext == ".db")
                {
                    if (fi.Length < 5_000_000)
                    {
                        return new LocalPackageInspectionResult
                        {
                            IsValid = false,
                            ErrorMessageEn = "Dictionary database file is too small (< 5 MB).",
                            ErrorMessageMm = "အဘိဓာန်ဒေတာဘေ့စ်ဖိုင် အရွယ်အစား ငယ်လွန်းနေပါသည် (< 5 MB)။"
                        };
                    }

                    var repo = new EncryptedDictionaryRepository(filePath);
                    var trie = repo.LoadIntoTrie(pwd);
                    if (trie.WordCount < 50_000)
                    {
                        return new LocalPackageInspectionResult
                        {
                            IsValid = false,
                            ErrorMessageEn = $"Database contains only {trie.WordCount:N0} words (< 50,000 minimum).",
                            ErrorMessageMm = $"အဘိဓာန်တွင် စကားလုံး {trie.WordCount:N0} လုံးသာ ပါရှိနေပါသည် (အနည်းဆုံး ၅၀,၀၀၀ လိုအပ်သည်)။"
                        };
                    }

                    return new LocalPackageInspectionResult
                    {
                        IsValid = true,
                        Kind = LocalUpdatePackageKind.DictionaryDatabaseOnly,
                        PackageVersion = GetCurrentInstalledVersion(),
                        FileSha256 = sha256,
                        FileBytes = fi.Length,
                        VerifiedHeadwordCount = trie.WordCount,
                        IsDictionaryOnlyChange = true
                    };
                }

                if (ext == ".zip")
                {
                    return InspectZipArchiveFile(filePath, sha256, fi.Length);
                }

                if (ext == ".exe")
                {
                    string exeVer = NormalizeVersionString(FileVersionInfo.GetVersionInfo(filePath).FileVersion ?? string.Empty);
                    if (fi.Length < 16_384)
                    {
                        return new LocalPackageInspectionResult
                        {
                            IsValid = false,
                            ErrorMessageEn = "Selected .exe is not a valid Myanmar Spell Checker Setup package.",
                            ErrorMessageMm = "ရွေးချယ်ထားသော .exe ဖိုင်သည် မှန်ကန်သော Setup ဖိုင် မဟုတ်ပါ။"
                        };
                    }

                    string? prodName = FileVersionInfo.GetVersionInfo(filePath).ProductName;
                    if (string.IsNullOrWhiteSpace(prodName) || prodName!.IndexOf("Myanmar", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return new LocalPackageInspectionResult
                        {
                            IsValid = false,
                            ErrorMessageEn = "Selected .exe is not signed/built as Myanmar Official Spell Checker Suite.",
                            ErrorMessageMm = "ရွေးချယ်ထားသော .exe ဖိုင်သည် Myanmar Official Spell Checker Setup ဖိုင် မဟုတ်ပါ။"
                        };
                    }

                    return new LocalPackageInspectionResult
                    {
                        IsValid = true,
                        Kind = LocalUpdatePackageKind.SingleFileSetupExe,
                        PackageVersion = exeVer,
                        FileSha256 = sha256,
                        FileBytes = fi.Length,
                        IsDictionaryOnlyChange = false
                    };
                }

                return new LocalPackageInspectionResult
                {
                    IsValid = false,
                    ErrorMessageEn = "Unsupported file type. Select MyanmarSpellChecker-Setup.exe, EasySetup.zip, or mlc_thatpon.db.",
                    ErrorMessageMm = "ဖိုင်အမျိုးအစား မမှန်ကန်ပါ။ Setup.exe၊ EasySetup.zip သို့မဟုတ် mlc_thatpon.db ဖိုင်ကို ရွေးချယ်ပါ။"
                };
            }
            catch (Exception ex)
            {
                return new LocalPackageInspectionResult
                {
                    IsValid = false,
                    ErrorMessageEn = "Package inspection failed: " + ex.Message,
                    ErrorMessageMm = "ဖိုင်စစ်ဆေးရာတွင် အမှားတွေ့ရှိပါသည် - " + ex.Message
                };
            }
        }

        private static LocalPackageInspectionResult InspectZipArchiveFile(string zipPath, string sha256, long length)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var coreEntry = FindZipEntryByFileName(archive, "MyanmarSpellChecker.Core.dll");
                var wordEntry = FindZipEntryByFileName(archive, "MyanmarSpellChecker.WordAddIn.dll");
                var excelEntry = FindZipEntryByFileName(archive, "MyanmarSpellChecker.ExcelAddIn.dll");
                var dbEntry = FindZipEntryByFileName(archive, "mlc_thatpon.db");

                if (coreEntry == null || wordEntry == null || excelEntry == null || dbEntry == null || dbEntry.Length < 5_000_000)
                {
                    return new LocalPackageInspectionResult
                    {
                        IsValid = false,
                        ErrorMessageEn = "ZIP archive is missing required Add-in DLLs or full mlc_thatpon.db (>= 5 MB).",
                        ErrorMessageMm = "ZIP ဖိုင်အတွင်း လိုအပ်သော Add-in DLL များ သို့မဟုတ် mlc_thatpon.db မပြည့်စုံပါ။"
                    };
                }

                string tempCore = Path.Combine(Path.GetTempPath(), "msc_inspect_" + Guid.NewGuid().ToString("N") + ".dll");
                try
                {
                    coreEntry.ExtractToFile(tempCore, overwrite: true);
                    string pkgVer = NormalizeVersionString(FileVersionInfo.GetVersionInfo(tempCore).FileVersion ?? "1.0.0.0");
                    return new LocalPackageInspectionResult
                    {
                        IsValid = true,
                        Kind = LocalUpdatePackageKind.EasySetupZipBundle,
                        PackageVersion = pkgVer,
                        FileSha256 = sha256,
                        FileBytes = length,
                        IsDictionaryOnlyChange = false
                    };
                }
                finally
                {
                    try { if (File.Exists(tempCore)) File.Delete(tempCore); } catch { }
                }
            }
        }

        private static ZipArchiveEntry? FindZipEntryByFileName(ZipArchive archive, string fileName)
        {
            return archive.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Zero-restart live dictionary database update: verifies the database, atomically updates per-user storage,
        /// and triggers the active Office host's <c>HotSwapTrie</c> callback immediately.
        /// </summary>
        public static int ApplyDictionaryHotSwap(
            string verifiedDbSourcePath,
            string? activeHostDbPath,
            string? decryptionPassword,
            Action<MyanmarTrie, string>? hotSwapCallback)
        {
            string pwd = string.IsNullOrWhiteSpace(decryptionPassword) ? DefaultDbPassword : decryptionPassword!;
            var checkRepo = new EncryptedDictionaryRepository(verifiedDbSourcePath);
            MyanmarTrie verifiedTrie = checkRepo.LoadIntoTrie(pwd);
            if (verifiedTrie.WordCount < 50_000)
                throw new InvalidDataException($"Refusing incomplete dictionary ({verifiedTrie.WordCount} headwords).");

            string localRoot = GetLocalDataRootDir();
            string appDir = GetInstalledAppDir();
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(appDir);

            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(localRoot, "mlc_thatpon.db"),
                Path.Combine(appDir, "mlc_thatpon.db")
            };
            if (!string.IsNullOrWhiteSpace(activeHostDbPath))
                targetPaths.Add(Path.GetFullPath(activeHostDbPath!));

            string primaryAppliedPath = verifiedDbSourcePath;
            foreach (string target in targetPaths)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(verifiedDbSourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    {
                        primaryAppliedPath = target;
                        continue;
                    }

                    string? parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    string tmp = target + ".upd_tmp";
                    File.Copy(verifiedDbSourcePath, tmp, overwrite: true);
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(tmp, target);
                    primaryAppliedPath = target;
                }
                catch
                {
                    // ProgramFiles path may be read-only for non-admin; per-user %LocalAppData% paths succeed
                }
            }

            RecordCheckTimestamp(string.Empty);
            hotSwapCallback?.Invoke(verifiedTrie, primaryAppliedPath);
            return verifiedTrie.WordCount;
        }

        /// <summary>
        /// Stages a full Setup.exe or EasySetup.zip into <c>%LocalAppData%\MyanmarSpellChecker\staging\</c>
        /// and launches a gentle background applier that waits until Word, Excel, and PowerPoint close naturally.
        /// Never force-terminates user Office sessions.
        /// </summary>
        public static string StageFullUpdateAndLaunchDeferredApplier(string sourceFilePath, LocalUpdatePackageKind kind)
        {
            string stagingRoot = Path.Combine(GetLocalDataRootDir(), "staging", "upd_" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(stagingRoot);

            if (kind == LocalUpdatePackageKind.SingleFileSetupExe)
            {
                string stagedExe = Path.Combine(stagingRoot, "MyanmarSpellChecker-Setup.exe");
                File.Copy(sourceFilePath, stagedExe, overwrite: true);

                var psi = new ProcessStartInfo
                {
                    FileName = stagedExe,
                    Arguments = "--apply-staged \"" + stagingRoot + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = stagingRoot
                };
                Process.Start(psi);
                RecordCheckTimestamp(string.Empty);
                return stagingRoot;
            }

            if (kind == LocalUpdatePackageKind.EasySetupZipBundle)
            {
                ZipFile.ExtractToDirectory(sourceFilePath, stagingRoot);

                // Prefer bundled Setup.exe if present; otherwise fall back to installed Setup.exe or PowerShell Easy-Setup.ps1 waiter
                string bundledExe = Path.Combine(stagingRoot, "MyanmarSpellChecker-Setup.exe");
                string installedExe = Path.Combine(GetLocalDataRootDir(), "MyanmarSpellChecker-Setup.exe");
                if (!File.Exists(bundledExe) && File.Exists(installedExe))
                {
                    File.Copy(installedExe, bundledExe, overwrite: true);
                }

                if (File.Exists(bundledExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = bundledExe,
                        Arguments = "--apply-staged \"" + stagingRoot + "\"",
                        UseShellExecute = false,
                        WorkingDirectory = stagingRoot
                    };
                    Process.Start(psi);
                }
                else
                {
                    string waiterPs1 = Path.Combine(stagingRoot, "_apply_when_office_closed.ps1");
                    string easyPs1 = Path.Combine(stagingRoot, "Easy-Setup.ps1");
                    string psContent =
                        "\uFEFFAdd-Type -AssemblyName System.Windows.Forms\r\n" +
                        "while (@(Get-Process -Name 'WINWORD','EXCEL','POWERPNT' -ErrorAction SilentlyContinue).Count -gt 0) { Start-Sleep -Milliseconds 900 }\r\n" +
                        "& powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File '" + easyPs1.Replace("'", "''") + "' -Silent\r\n" +
                        "[void][System.Windows.Forms.MessageBox]::Show('Myanmar Spell Checker update installed successfully! You can reopen Word, Excel, or PowerPoint now.', 'Myanmar Official Spell Checker', 'OK', 'Information')\r\n";
                    File.WriteAllText(waiterPs1, psContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                    string psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                    var psi = new ProcessStartInfo
                    {
                        FileName = File.Exists(psExe) ? psExe : "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File \"" + waiterPs1 + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = stagingRoot
                    };
                    Process.Start(psi);
                }

                RecordCheckTimestamp(string.Empty);
                return stagingRoot;
            }

            throw new InvalidOperationException("Unsupported package kind for full staging: " + kind);
        }

        private static HttpClient CreateGitHubHttpClient(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            string ver = GetCurrentInstalledVersion();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyanmarSpellChecker-Updater", ver));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }
    }
}
