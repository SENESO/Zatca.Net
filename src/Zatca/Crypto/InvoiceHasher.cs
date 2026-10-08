using System;
using System.Security.Cryptography;
using System.Text;

namespace Zatca.Crypto
{
    /// <summary>
    /// SHA-256 hashing helpers. ZATCA hashes the canonical signed invoice XML
    /// with SHA-256 to produce the invoice hash submitted to the API and
    /// embedded in the QR code (tag 6).
    /// </summary>
    public static class InvoiceHasher
    {
        /// <summary>Computes the SHA-256 digest of <paramref name="data"/>.</summary>
        public static byte[] ComputeHash(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        /// <summary>Computes the SHA-256 digest of a UTF-8 string.</summary>
        public static byte[] ComputeHash(string utf8Text)
        {
            if (utf8Text == null) throw new ArgumentNullException(nameof(utf8Text));
            return ComputeHash(Encoding.UTF8.GetBytes(utf8Text));
        }

        /// <summary>SHA-256 digest as lowercase hex (64 chars). This is the format
        /// ZATCA expects for the <c>invoiceHash</c> API field and the PIH chain.</summary>
        public static string ComputeHashHex(byte[] data)
        {
            var hash = ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>SHA-256 digest as base64.</summary>
        public static string ComputeHashBase64(byte[] data)
        {
            return Convert.ToBase64String(ComputeHash(data));
        }
    }
}
