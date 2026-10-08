using System;
using NUnit.Framework;
using Zatca.Crypto;

namespace Zatca.Tests
{
    [TestFixture]
    public class InvoiceHasherTests
    {
        [Test]
        public void ComputeHashHex_KnownVector_Abc()
        {
            // Well-known SHA-256 test vector.
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                InvoiceHasher.ComputeHashHex(new byte[] { (byte)'a', (byte)'b', (byte)'c' }));
        }

        [Test]
        public void ComputeHashHex_KnownVector_Empty()
        {
            Assert.AreEqual(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                InvoiceHasher.ComputeHashHex(new byte[0]));
        }

        [Test]
        public void ComputeHash_Returns32Bytes()
        {
            Assert.AreEqual(32, InvoiceHasher.ComputeHash("invoice-xml").Length);
        }

        [Test]
        public void ComputeHash_IsDeterministic()
        {
            var a = InvoiceHasher.ComputeHashHex("same-input");
            var b = InvoiceHasher.ComputeHashHex("same-input");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ComputeHashBase64_DecodesTo32Bytes()
        {
            var raw = Convert.FromBase64String(InvoiceHasher.ComputeHashBase64(new byte[] { 1, 2, 3 }));
            Assert.AreEqual(32, raw.Length);
        }

        [Test]
        public void ComputeHash_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => InvoiceHasher.ComputeHash((byte[])null));
        }
    }
}
