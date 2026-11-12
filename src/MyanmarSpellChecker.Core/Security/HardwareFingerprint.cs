using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MyanmarSpellChecker.Core.Security
{
    /// <summary>
    /// Generates a deterministic offline hardware fingerprint (Motherboard UUID + CPU ID + OS MachineGuid)
    /// for B2G Government Ministry node-locked and enterprise licensing.
    /// </summary>
    public static class HardwareFingerprint
    {
        private static string? _cachedMachineId;

        /// <summary>
        /// Returns a formatted 16-character hardware Machine ID (e.g. "MMGOV-4A8F-91C2-77E0").
        /// </summary>
        public static string GetMachineId()
        {
            if (!string.IsNullOrEmpty(_cachedMachineId))
                return _cachedMachineId!;

            string mbUuid = QueryWmiProperty("Win32_ComputerSystemProduct", "UUID");
            string cpuId = QueryWmiProperty("Win32_Processor", "ProcessorId");
            string machineGuid = GetRegistryMachineGuid();

            string rawComposite = $"{mbUuid}|{cpuId}|{machineGuid}".ToUpperInvariant();
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawComposite));
                string hex = BitConverter.ToString(hash).Replace("-", "");
                _cachedMachineId = $"MMGOV-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}";
                return _cachedMachineId;
            }
        }

        public static string GetCurrentDomainOrWorkgroup()
        {
            try
            {
                return Environment.UserDomainName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string QueryWmiProperty(string wmiClass, string propertyName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}"))
                {
                    foreach (ManagementBaseObject obj in searcher.Get())
                    {
                        string? val = obj[propertyName]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val!;
                    }
                }
            }
            catch
            {
                // Fallback gracefully if WMI service is restricted
            }
            return "FALLBACK-" + Environment.MachineName;
        }

        private static string GetRegistryMachineGuid()
        {
            try
            {
                using (RegistryKey? localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey? cryptoKey = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    string? guid = cryptoKey?.GetValue("MachineGuid")?.ToString();
                    if (!string.IsNullOrWhiteSpace(guid))
                        return guid!;
                }
            }
            catch
            {
                // Ignore
            }
            return Environment.MachineName;
        }
    }
}

