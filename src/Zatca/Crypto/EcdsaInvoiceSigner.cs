using System;
using System.Security.Cryptography;

namespace Zatca.Crypto
{
    /// <summary>
    /// ECDSA invoice-hash signer. Takes ownership of the <see cref="ECDsa"/>
    /// instance passed in (it is disposed with the signer).
    /// </summary>
    public sealed class EcdsaInvoiceSigner : IInvoiceSigner
    {
        private readonly ECDsa _ecdsa;
        private bool _disposed;

        public EcdsaInvoiceSigner(ECDsa ecdsa)
        {
            _ecdsa = ecdsa ?? throw new ArgumentNullException(nameof(ecdsa));
        }

        /// <summary>
        /// Creates a signer backed by a fresh secp256k1 key pair.
        /// Requires a runtime/OS where secp256k1 is available (e.g. Linux with
        /// OpenSSL, or recent Windows via CNG). Throws
        /// <see cref="PlatformNotSupportedException"/> with guidance otherwise —
        /// in that case construct <see cref="EcdsaInvoiceSigner"/> with your own
        /// <see cref="ECDsa"/> instance.
        /// </summary>
        public static EcdsaInvoiceSigner CreateSecp256k1()
        {
            try
            {
                var curve = ECCurve.CreateFromFriendlyName("secP256k1");
                return new EcdsaInvoiceSigner(ECDsa.Create(curve));
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is CryptographicException)
            {
                throw new PlatformNotSupportedException(
                    "secp256k1 is not available on this platform/runtime. " +
                    "On Linux it needs OpenSSL with secp256k1 support. " +
                    "Alternatively, build your own ECDsa instance and pass it to " +
                    "new EcdsaInvoiceSigner(ecdsa).", ex);
            }
        }

        /// <inheritdoc />
        public byte[] SignHash(byte[] hash)
        {
            ThrowIfDisposed();
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            if (hash.Length != 32)
                throw new ArgumentException("Hash must be the 32-byte SHA-256 digest.", nameof(hash));

            // ZATCA's QR tag 7 carries raw r‖s (64 bytes, IEEE P1363).
            // .NET Core 3.0+ returns that format by default; normalize a DER
            // sequence just in case (e.g. .NET Framework behavior).
            var sig = _ecdsa.SignHash(hash);
            return sig.Length == 64 ? sig : DerToRaw64(sig);
        }

        /// <inheritdoc />
        public bool VerifyHash(byte[] hash, byte[] rawSignature)
        {
            ThrowIfDisposed();
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            if (rawSignature == null) throw new ArgumentNullException(nameof(rawSignature));
            if (rawSignature.Length != 64)
                throw new ArgumentException("Signature must be the raw 64-byte r‖s form.", nameof(rawSignature));

            // Match SignHash: P1363 by default, DER as fallback.
            if (_ecdsa.VerifyHash(hash, rawSignature))
                return true;
            try { return _ecdsa.VerifyHash(hash, RawToDer64(rawSignature)); }
            catch (CryptographicException) { return false; }
        }

        /// <inheritdoc />
        public byte[] ExportPublicKeyUncompressed()
        {
            ThrowIfDisposed();
            var p = _ecdsa.ExportParameters(false);
            var key = new byte[65];
            key[0] = 0x04;
            Buffer.BlockCopy(LeftPad(p.Q.X), 0, key, 1, 32);
            Buffer.BlockCopy(LeftPad(p.Q.Y), 0, key, 33, 32);
            return key;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ecdsa.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EcdsaInvoiceSigner));
        }

        private static byte[] LeftPad(byte[] value)
        {
            if (value == null) throw new CryptographicException("EC parameters missing.");
            if (value.Length == 32) return value;
            if (value.Length > 32) throw new CryptographicException("EC coordinate too large.");
            var padded = new byte[32];
            Buffer.BlockCopy(value, 0, padded, 32 - value.Length, value.Length);
            return padded;
        }

        // ---- Minimal DER codec (256-bit curve signatures only) ----

        internal static byte[] DerToRaw64(byte[] der)
        {
            if (der == null) throw new ArgumentNullException(nameof(der));
            int pos = 0;

            ExpectByte(der, ref pos, 0x30); // SEQUENCE
            int seqLen = ReadLength(der, ref pos);
            if (seqLen != der.Length - pos)
                throw new CryptographicException("Invalid DER signature: bad sequence length.");

            ExpectByte(der, ref pos, 0x02); // INTEGER r
            byte[] r = ReadInteger(der, ref pos);

            ExpectByte(der, ref pos, 0x02); // INTEGER s
            byte[] s = ReadInteger(der, ref pos);

            if (pos != der.Length)
                throw new CryptographicException("Invalid DER signature: trailing bytes.");

            var raw = new byte[64];
            Buffer.BlockCopy(r, 0, raw, 32 - r.Length, r.Length);
            Buffer.BlockCopy(s, 0, raw, 64 - s.Length, s.Length);
            return raw;
        }

        internal static byte[] RawToDer64(byte[] raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Length != 64)
                throw new ArgumentException("Raw signature must be 64 bytes (r‖s).", nameof(raw));

            byte[] r = EncodeInteger(raw, 0);
            byte[] s = EncodeInteger(raw, 32);
            var der = new byte[2 + r.Length + s.Length];
            der[0] = 0x30;
            der[1] = (byte)(r.Length + s.Length); // always < 128 for 256-bit curves
            Buffer.BlockCopy(r, 0, der, 2, r.Length);
            Buffer.BlockCopy(s, 0, der, 2 + r.Length, s.Length);
            return der;
        }

        private static void ExpectByte(byte[] der, ref int pos, byte expected)
        {
            if (pos >= der.Length || der[pos] != expected)
                throw new CryptographicException("Invalid DER signature: unexpected structure.");
            pos++;
        }

        private static int ReadLength(byte[] der, ref int pos)
        {
            if (pos >= der.Length)
                throw new CryptographicException("Invalid DER signature: truncated length.");
            int first = der[pos++];
            if (first < 0x80) return first;
            int count = first & 0x7F;
            if (count == 0 || count > 2 || pos + count > der.Length)
                throw new CryptographicException("Invalid DER signature: bad long-form length.");
            int len = 0;
            for (int i = 0; i < count; i++)
                len = (len << 8) | der[pos++];
            return len;
        }

        private static byte[] ReadInteger(byte[] der, ref int pos)
        {
            int len = ReadLength(der, ref pos);
            if (len < 1 || len > 33 || pos + len > der.Length)
                throw new CryptographicException("Invalid DER signature: bad integer length.");

            int start = pos;
            int end = pos + len;
            while (start < end - 1 && der[start] == 0x00) start++; // strip sign byte(s)

            int outLen = end - start;
            if (outLen > 32)
                throw new CryptographicException("Invalid DER signature: integer too large.");

            var result = new byte[outLen];
            Buffer.BlockCopy(der, start, result, 0, outLen);
            pos = end;
            return result;
        }

        private static byte[] EncodeInteger(byte[] raw, int offset)
        {
            int start = offset;
            int end = offset + 32;
            while (start < end - 1 && raw[start] == 0x00) start++; // strip leading zeros

            int len = end - start;
            bool needsZero = (raw[start] & 0x80) != 0; // keep it positive
            var enc = new byte[2 + len + (needsZero ? 1 : 0)];
            enc[0] = 0x02;
            enc[1] = (byte)(len + (needsZero ? 1 : 0));
            if (needsZero)
            {
                enc[2] = 0x00;
                Buffer.BlockCopy(raw, start, enc, 3, len);
            }
            else
            {
                Buffer.BlockCopy(raw, start, enc, 2, len);
            }
            return enc;
        }
    }
}
