using System;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Zatca.Crypto;

namespace Zatca.Tests
{
    [TestFixture]
    public class EcdsaInvoiceSignerTests
    {
        private static byte[] TestHash()
        {
            return InvoiceHasher.ComputeHash("test-invoice-bytes");
        }

        [Test]
        public void SignHash_Returns64ByteRawSignature_AndVerifies()
        {
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var hash = TestHash();
                var sig = signer.SignHash(hash);

                Assert.AreEqual(64, sig.Length, "raw r‖s must be 64 bytes");
                Assert.IsTrue(signer.VerifyHash(hash, sig));
            }
        }

        [Test]
        public void VerifyHash_TamperedSignature_Fails()
        {
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var hash = TestHash();
                var sig = signer.SignHash(hash);
                sig[10] ^= 0xFF;

                Assert.IsFalse(signer.VerifyHash(hash, sig));
            }
        }

        [Test]
        public void VerifyHash_WrongHash_Fails()
        {
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var sig = signer.SignHash(TestHash());
                Assert.IsFalse(signer.VerifyHash(InvoiceHasher.ComputeHash("other"), sig));
            }
        }

        [Test]
        public void ExportPublicKeyUncompressed_Is65BytesWith04Prefix()
        {
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var pub = signer.ExportPublicKeyUncompressed();
                Assert.AreEqual(65, pub.Length);
                Assert.AreEqual(0x04, pub[0]);
            }
        }

        [Test]
        public void Signatures_DifferPerCall_ButAllVerify()
        {
            // ECDSA uses a fresh random nonce per signature (not deterministic).
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var hash = TestHash();
                var s1 = signer.SignHash(hash);
                var s2 = signer.SignHash(hash);

                Assert.IsTrue(signer.VerifyHash(hash, s1));
                Assert.IsTrue(signer.VerifyHash(hash, s2));
            }
        }

        [Test]
        public void SignHash_WrongHashLength_Throws()
        {
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                Assert.Throws<ArgumentException>(() => signer.SignHash(new byte[16]));
            }
        }

        [Test]
        public void DerCodec_RoundTrips()
        {
            // Fixed raw signature exercising both branches: r needs a DER sign
            // byte (high bit set), s does not.
            var raw = new byte[64];
            raw[0] = 0x80;
            for (int i = 1; i < 64; i++) raw[i] = (byte)i;

            var der = EcdsaInvoiceSigner.RawToDer64(raw);
            var back = EcdsaInvoiceSigner.DerToRaw64(der);

            Assert.IsTrue(raw.SequenceEqual(back));
        }

        [Test]
        public void DerCodec_RejectsGarbage()
        {
            Assert.Throws<CryptographicException>(() =>
                EcdsaInvoiceSigner.DerToRaw64(new byte[] { 1, 2, 3 }));
        }
    }
}
