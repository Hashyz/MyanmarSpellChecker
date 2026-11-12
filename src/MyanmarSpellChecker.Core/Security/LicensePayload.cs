using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MyanmarSpellChecker.Core.Security
{
    public enum LicenseMode
    {
        /// <summary>
        /// Locked to a single computer's hardware MachineId.
        /// </summary>
        SingleMachine = 0,

        /// <summary>
        /// Enterprise GPO bundle locked to an Active Directory Domain or list of Ministry MachineIds.
        /// </summary>
        MinistryEnterprise = 1
    }

    [DataContract]
    public sealed class LicensePayload
    {
        [DataMember]
        public string LicenseId { get; set; } = Guid.NewGuid().ToString("N");

        [DataMember]
        public string MinistryName { get; set; } = string.Empty;

        [DataMember]
        public LicenseMode Mode { get; set; } = LicenseMode.SingleMachine;

        /// <summary>
        /// Required when Mode == SingleMachine.
        /// </summary>
        [DataMember]
        public string MachineId { get; set; } = string.Empty;

        /// <summary>
        /// Optional Active Directory domain lock for MinistryEnterprise GPO deployment (or "*" for site-wide).
        /// </summary>
        [DataMember]
        public string AllowedDomain { get; set; } = string.Empty;

        /// <summary>
        /// Optional list of authorized MachineIds in a MinistryEnterprise batch license.
        /// </summary>
        [DataMember]
        public List<string> AllowedMachineIds { get; set; } = new List<string>();

        [DataMember]
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

        [DataMember]
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddYears(5);

        /// <summary>
        /// AES-256 encrypted Database Password (unwrapped only after RSA-2048 signature verification).
        /// </summary>
        [DataMember]
        public string EncryptedDatabaseKeyBase64 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class SignedLicenseFile
    {
        [DataMember]
        public string PayloadJsonBase64 { get; set; } = string.Empty;

        [DataMember]
        public string RsaSignatureBase64 { get; set; } = string.Empty;
    }

    public sealed class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public string? MinistryName { get; set; }
        public string? SqlCipherDatabaseKey { get; set; }
        public LicensePayload? Payload { get; set; }
    }
}
