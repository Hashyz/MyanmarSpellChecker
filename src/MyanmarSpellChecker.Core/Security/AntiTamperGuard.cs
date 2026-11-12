using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace MyanmarSpellChecker.Core.Security
{
    /// <summary>
    /// Protects the compiled .NET assemblies and SQLCipher dictionary key against static string extraction
    /// (dnSpy / ILSpy #US heap inspection) and runtime tampering.
    /// </summary>
    public static class AntiTamperGuard
    {
        // XOR-masked byte shards so no plaintext SQLCipher fallback key appears in the IL #US string table
        private static readonly byte[] ShardA =
        {
            0x17, 0x17, 0x77, 0x1D, 0x15, 0x0C, 0x77, 0x0E,
            0x12, 0x1B, 0x0E, 0x0A, 0x15, 0x14, 0x77, 0x09
        };

        private static readonly byte[] ShardB =
        {
            0x0B, 0x16, 0x19, 0x13, 0x0A, 0x12, 0x1F, 0x08,
            0x77, 0x68, 0x6A, 0x68, 0x6C, 0x7B
        };

        private const byte MaskByte = 0x5A;

        /// <summary>
        /// Reconstructs the fallback SQLCipher key in memory without exposing a literal string in metadata.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ReconstructProtectedFallbackKey()
        {
            byte[] buffer = new byte[ShardA.Length + ShardB.Length];
            try
            {
                for (int i = 0; i < ShardA.Length; i++)
                {
                    buffer[i] = (byte)(ShardA[i] ^ MaskByte);
                }
                for (int j = 0; j < ShardB.Length; j++)
                {
                    buffer[ShardA.Length + j] = (byte)(ShardB[j] ^ MaskByte);
                }
                return Encoding.ASCII.GetString(buffer);
            }
            finally
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Checks whether a managed or native debugger is attached to inspect memory or extract the dictionary.
        /// </summary>
        public static bool IsDebuggerDetected()
        {
            return Debugger.IsAttached || Debugger.IsLogging();
        }

        /// <summary>
        /// Computes the SHA-256 hash of the executing Core assembly for integrity verification.
        /// </summary>
        public static string ComputeAssemblySha256(Assembly? assembly = null)
        {
            try
            {
                Assembly target = assembly ?? typeof(AntiTamperGuard).Assembly;
                string location = target.Location;
                if (string.IsNullOrEmpty(location) || !File.Exists(location))
                    return string.Empty;

                using (var stream = File.OpenRead(location))
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

