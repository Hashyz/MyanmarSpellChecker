using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace MyanmarSpellChecker.Core.Security
{
    /// <summary>
    /// Handles RSA-2048 digital signature verification and AES-256 database key unwrapping for offline .lic files.
    /// Uses 100% Microsoft-signed .NET Framework 4.8 assemblies (zero 3rd-party dependencies).
    /// </summary>
    public static class LicenseService
    {
        private static readonly byte[] EnvelopeSalt = Encoding.UTF8.GetBytes("MM-GOV-THATPON-2026-SALT");

        /// <summary>
        /// Generates a new RSA-2048 Private/Public XML keypair for the vendor License Generator tool.
        /// </summary>
        public static (string PrivateKeyXml, string PublicKeyXml) GenerateRsaKeyPair()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false;
                return (rsa.ToXmlString(true), rsa.ToXmlString(false));
            }
        }

        /// <summary>
        /// Creates a cryptographically signed .lic JSON string embedding the encrypted database key.
        /// </summary>
        public static string CreateSignedLicenseJson(
            LicensePayload payload,
            string databasePassword,
            string rsaPrivateKeyXml)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(databasePassword)) throw new ArgumentException("Database password is required.");

            payload.EncryptedDatabaseKeyBase64 = EncryptDatabaseKey(databasePassword, payload.LicenseId);

            byte[] payloadBytes = SerializeJsonToBytes(payload);

            byte[] signatureBytes;
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(rsaPrivateKeyXml);
                signatureBytes = rsa.SignData(payloadBytes, CryptoConfig.MapNameToOID("SHA256")!);
            }

            var signedFile = new SignedLicenseFile
            {
                PayloadJsonBase64 = Convert.ToBase64String(payloadBytes),
                RsaSignatureBase64 = Convert.ToBase64String(signatureBytes)
            };

            return Encoding.UTF8.GetString(SerializeJsonToBytes(signedFile));
        }

        /// <summary>
        /// Validates a signed .lic JSON string against the RSA-2048 Public Key and current Machine ID / Ministry Domain.
        /// </summary>
        public static LicenseValidationResult ValidateLicenseJson(
            string signedLicenseJson,
            string rsaPublicKeyXml,
            string? currentMachineIdOverride = null,
            string? currentDomainOverride = null)
        {
            try
            {
                var signedFile = DeserializeJson<SignedLicenseFile>(Encoding.UTF8.GetBytes(signedLicenseJson));
                if (signedFile == null ||
                    string.IsNullOrWhiteSpace(signedFile.PayloadJsonBase64) ||
                    string.IsNullOrWhiteSpace(signedFile.RsaSignatureBase64))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        StatusMessage = "လိုင်စင်ဖိုင် (.lic) တည်ဆောက်ပုံ မှားယွင်းနေပါသည်။"
                    };
                }

                byte[] payloadBytes = Convert.FromBase64String(signedFile.PayloadJsonBase64);
                byte[] signatureBytes = Convert.FromBase64String(signedFile.RsaSignatureBase64);

                bool signatureValid;
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(rsaPublicKeyXml);
                    signatureValid = rsa.VerifyData(payloadBytes, CryptoConfig.MapNameToOID("SHA256")!, signatureBytes);
                }

                if (!signatureValid)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        StatusMessage = "လိုင်စင် ဒစ်ဂျစ်တယ်လက်မှတ် (RSA-2048 Signature) မမှန်ကန်ပါ။"
                    };
                }

                var payload = DeserializeJson<LicensePayload>(payloadBytes);
                if (payload == null)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        StatusMessage = "လိုင်စင်အချက်အလက် ဖတ်ရှု၍မရပါ။"
                    };
                }

                if (DateTime.UtcNow > payload.ExpiresUtc)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        StatusMessage = $"လိုင်စင်သက်တမ်း ကုန်ဆုံးနေပါသည် ({payload.ExpiresUtc:yyyy-MM-dd})။"
                    };
                }

                string machineId = currentMachineIdOverride ?? HardwareFingerprint.GetMachineId();
                string domain = currentDomainOverride ?? HardwareFingerprint.GetCurrentDomainOrWorkgroup();

                if (payload.Mode == LicenseMode.SingleMachine)
                {
                    if (!string.Equals(payload.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LicenseValidationResult
                        {
                            IsValid = false,
                            StatusMessage = $"ဤလိုင်စင်သည် စက်အမှတ် '{payload.MachineId}' အတွက်သာဖြစ်ပြီး လက်ရှိစက်အမှတ် '{machineId}' နှင့် မကိုက်ညီပါ။"
                        };
                    }
                }
                else if (payload.Mode == LicenseMode.MinistryEnterprise)
                {
                    bool domainAllowed = payload.AllowedDomain == "*" ||
                        (!string.IsNullOrWhiteSpace(payload.AllowedDomain) &&
                         string.Equals(payload.AllowedDomain, domain, StringComparison.OrdinalIgnoreCase));

                    bool machineInList = payload.AllowedMachineIds != null &&
                        payload.AllowedMachineIds.Any(m => string.Equals(m, machineId, StringComparison.OrdinalIgnoreCase));

                    if (!domainAllowed && !machineInList)
                    {
                        return new LicenseValidationResult
                        {
                            IsValid = false,
                            StatusMessage = "ဤစက်သည် ဝန်ကြီးဌာန Enterprise လိုင်စင်စာရင်းတွင် မပါဝင်ပါ။"
                        };
                    }
                }

                string dbKey = DecryptDatabaseKey(payload.EncryptedDatabaseKeyBase64, payload.LicenseId);

                return new LicenseValidationResult
                {
                    IsValid = true,
                    StatusMessage = $"လိုင်စင် အတည်ပြုပြီး ({payload.MinistryName})",
                    MinistryName = payload.MinistryName,
                    SqlCipherDatabaseKey = dbKey,
                    Payload = payload
                };
            }
            catch (Exception ex)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    StatusMessage = $"လိုင်စင်စစ်ဆေးရာတွင် အမှားတွေ့ရှိပါသည် - {ex.Message}"
                };
            }
        }

        private static byte[] SerializeJsonToBytes<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(ms, obj);
                return ms.ToArray();
            }
        }

        private static T? DeserializeJson<T>(byte[] utf8Bytes) where T : class
        {
            using (var ms = new MemoryStream(utf8Bytes))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(ms) as T;
            }
        }

        private static string EncryptDatabaseKey(string plainKey, string licenseId)
        {
            using (var derive = new Rfc2898DeriveBytes(licenseId, EnvelopeSalt, 10000, HashAlgorithmName.SHA256))
            using (var aes = Aes.Create())
            {
                aes.Key = derive.GetBytes(32);
                aes.IV = derive.GetBytes(16);
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(plainKey);
                    cs.Write(bytes, 0, bytes.Length);
                    cs.FlushFinalBlock();
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private static string DecryptDatabaseKey(string encryptedBase64, string licenseId)
        {
            byte[] cipherBytes = Convert.FromBase64String(encryptedBase64);
            using (var derive = new Rfc2898DeriveBytes(licenseId, EnvelopeSalt, 10000, HashAlgorithmName.SHA256))
            using (var aes = Aes.Create())
            {
                aes.Key = derive.GetBytes(32);
                aes.IV = derive.GetBytes(16);
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(cipherBytes, 0, cipherBytes.Length);
                    cs.FlushFinalBlock();
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }
    }
}
