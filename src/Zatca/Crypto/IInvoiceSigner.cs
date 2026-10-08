using System;

namespace Zatca.Crypto
{
    /// <summary>
    /// Signs invoice hashes with ECDSA on the secp256k1 curve, per ZATCA's
    /// cryptographic requirements (ECDSA, 256-bit key).
    /// <para>
    /// Bring your own <see cref="System.Security.Cryptography.ECDsa"/> instance —
    /// the SDK never generates or stores your private key. Use
    /// <see cref="EcdsaInvoiceSigner.CreateSecp256k1"/> for a convenient factory
    /// on supported platforms.
    /// </para>
    /// <para>
    /// Scope note: this signs the <b>hash bytes</b> and returns the raw 64-byte
    /// r‖s signature ZATCA expects in QR tag 7. Building the full XAdES-BES
    /// enveloped signature inside UBL 2.1 XML is the integrator's responsibility
    /// (see README).
    /// </para>
    /// </summary>
    public interface IInvoiceSigner : IDisposable
    {
        /// <summary>
        /// Signs a 32-byte SHA-256 hash. Returns the raw 64-byte r‖s signature
        /// (IEEE P1363 format) used in QR tag 7.
        /// </summary>
        byte[] SignHash(byte[] hash);

        /// <summary>
        /// Verifies a raw 64-byte r‖s signature against a 32-byte hash.
        /// </summary>
        bool VerifyHash(byte[] hash, byte[] rawSignature);

        /// <summary>
        /// Exports the public key in uncompressed form (65 bytes: 0x04 ‖ X ‖ Y),
        /// suitable for QR tag 8.
        /// </summary>
        byte[] ExportPublicKeyUncompressed();
    }
}
