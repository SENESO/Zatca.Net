using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Zatca.Qr;

namespace Zatca.Tests
{
    [TestFixture]
    public class QrCodeGeneratorTests
    {
        // Independently computed reference vectors (separate Python implementation).
        private const string Phase1Expected =
            "AQxUZXN0IENvbXBhbnkCDzM5OTk5OTk5OTkwMDAwMwMUMjAyNi0xMC0wOFQxMjowMDowMFoEBjExNS4wMAUFMTUuMDA=";

        private const string NineTagExpected =
            "AQxUZXN0IENvbXBhbnkCDzM5OTk5OTk5OTkwMDAwMwMUMjAyNi0xMC0wOFQxMjowMDowMFoEBjExNS4wMAUFMTUuMDAGICkhsNGJcbM8V/SioBo6ayvynjRlpoGiZgVlEyU7G0c4B0AAAQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyAhIiMkJSYnKCkqKywtLi8wMTIzNDU2Nzg5Ojs8PT4/CEEEAQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyAhIiMkJSYnKCkqKywtLi8wMTIzNDU2Nzg5Ojs8PT4/QAkg4K/Nv2rUrfVmxXLX98NNTfuF5RIrunUNajpYQvkV05s=";

        [Test]
        public void GeneratePhase1_MatchesReferenceVector()
        {
            var actual = QrCodeGenerator.GeneratePhase1(
                "Test Company", "399999999900003", "2026-10-08T12:00:00Z", "115.00", "15.00");

            Assert.AreEqual(Phase1Expected, actual);
        }

        [Test]
        public void Generate_NineTags_MatchesReferenceVector()
        {
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes("invoice-xml-bytes"));

            var sig = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
            var pub = new byte[65];
            pub[0] = 0x04;
            for (int i = 1; i < 65; i++) pub[i] = (byte)i;

            byte[] stamp;
            using (var sha = SHA256.Create())
                stamp = sha.ComputeHash(Encoding.UTF8.GetBytes("stamp"));

            var actual = QrCodeGenerator.Generate(new QrData
            {
                SellerName = "Test Company",
                VatNumber = "399999999900003",
                Timestamp = "2026-10-08T12:00:00Z",
                InvoiceTotalWithVat = "115.00",
                VatTotal = "15.00",
                InvoiceHash = hash,
                EcdsaSignature = sig,
                PublicKey = pub,
                StampSignature = stamp
            });

            Assert.AreEqual(NineTagExpected, actual);
        }

        [Test]
        public void Decode_RoundTripsAllFields()
        {
            var fields = QrCodeGenerator.Decode(NineTagExpected);

            Assert.AreEqual(9, fields.Count);
            Assert.AreEqual("Test Company", fields[0].ValueText);
            Assert.AreEqual("399999999900003", fields[1].ValueText);
            Assert.AreEqual("2026-10-08T12:00:00Z", fields[2].ValueText);
            Assert.AreEqual("115.00", fields[3].ValueText);
            Assert.AreEqual("15.00", fields[4].ValueText);

            for (int i = 0; i < 9; i++)
                Assert.AreEqual(i + 1, fields[i].Tag, "tag order");

            Assert.AreEqual(32, fields[5].Value.Length); // invoice hash
            Assert.AreEqual(64, fields[6].Value.Length); // signature
            Assert.AreEqual(65, fields[7].Value.Length); // public key
            Assert.AreEqual(32, fields[8].Value.Length); // stamp signature
        }

        [Test]
        public void Generate_ArabicSellerName_UsesUtf8ByteLength()
        {
            // "شركة الاختبار" is 12 chars but 25 UTF-8 bytes — length must be 25.
            var payload = QrCodeGenerator.GeneratePhase1(
                "شركة الاختبار", "399999999900003", "2026-10-08T12:00:00Z", "115.00", "15.00");

            var fields = QrCodeGenerator.Decode(payload);
            Assert.AreEqual("شركة الاختبار", fields[0].ValueText);
            Assert.AreEqual(25, fields[0].Value.Length);
        }

        [Test]
        public void Generate_FieldOver255Bytes_Throws()
        {
            var tooLong = new string('x', 300);
            Assert.Throws<ArgumentException>(() =>
                QrCodeGenerator.GeneratePhase1(tooLong, "399999999900003",
                    "2026-10-08T12:00:00Z", "115.00", "15.00"));
        }

        [Test]
        public void Generate_MissingRequiredField_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                QrCodeGenerator.GeneratePhase1(null, "399999999900003",
                    "2026-10-08T12:00:00Z", "115.00", "15.00"));
        }

        [Test]
        public void Decode_InvalidBase64_Throws()
        {
            Assert.Throws<ArgumentException>(() => QrCodeGenerator.Decode("not-base64!!!"));
        }

        [Test]
        public void Decode_TruncatedPayload_Throws()
        {
            // valid base64, but a tag+length header with no value bytes
            Assert.Throws<ArgumentException>(() => QrCodeGenerator.Decode("AQI="));
        }
    }
}
