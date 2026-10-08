using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Zatca.Crypto;
using Zatca.Models;
using Zatca.Qr;
using Zatca.Validation;

namespace Zatca.IntegrationTests
{
    /// <summary>
    /// Integration tests for the Zatca SDK.
    /// <para>
    /// Scope note: this SDK deliberately covers only the honest primitives —
    /// TLV/QR generation, invoice hashing, ECDSA signing, and the Fatoora
    /// onboarding/clearance/reporting HTTP API. It does not generate UBL 2.1
    /// XML, XAdES signatures, or CSRs, so these tests do not pretend to: the
    /// end-to-end test exercises the highest-level real behavior available
    /// (XML hash → ECDSA sign → verify → QR payload → decode), and the
    /// sandbox tests run the SDK's HTTP client against ZATCA's real sandbox
    /// with integrator-supplied credentials and artifacts.
    /// </para>
    /// <para>
    /// Sandbox HTTP tests never fail when credentials are missing: they call
    /// <see cref="RequireEnv"/> first, which calls <c>Assert.Ignore</c> and
    /// names the missing variable. No secrets are hardcoded anywhere in this
    /// fixture.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ZatcaIntegrationTests
    {
        private const string SandboxUrlEnv = "ZATCA_SANDBOX_URL";
        private const string SandboxOtpEnv = "ZATCA_SANDBOX_OTP";
        private const string SandboxCsrEnv = "ZATCA_SANDBOX_CSR_BASE64";
        private const string SandboxInvoiceEnv = "ZATCA_SANDBOX_INVOICE_XML_BASE64";
        private const string SandboxInvoiceUuidEnv = "ZATCA_SANDBOX_INVOICE_UUID";

        // Sample seller/invoice data used by the always-run end-to-end test.
        // The VAT number is ZATCA's well-known documentation test number.
        private const string SellerName = "Test Company";
        private const string VatNumber = "399999999900003";
        private const string Timestamp = "2026-10-08T12:00:00Z";
        private const string InvoiceTotal = "115.00";
        private const string VatTotal = "15.00";

        private static readonly HttpClient SharedHttp = new HttpClient();

        private static string RequireEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                Assert.Ignore("Integration test skipped: set the " + name + " environment variable to run it.");
            return value;
        }

        private static string SandboxUrl()
        {
            var url = Environment.GetEnvironmentVariable(SandboxUrlEnv);
            return string.IsNullOrWhiteSpace(url)
                ? ZatcaClientOptions.SandboxBaseUrl
                : url.TrimEnd('/');
        }

        /// <summary>
        /// Always runs (no credentials): the full in-SDK pipeline end to end —
        /// hash a sample invoice XML, sign the hash with a real secp256k1 key,
        /// verify the signature, export the public key, build the QR payload,
        /// decode it back and check every tag.
        /// </summary>
        [Test]
        public void InvoicePipeline_HashSignQrVerify_RoundTripsEndToEnd()
        {
            Assert.IsTrue(SaudiVatValidator.IsValid(VatNumber), "Sanity check on the test VAT number.");

            var invoiceXml =
                "<Invoice>" +
                "<Seller>" + SellerName + "</Seller>" +
                "<VatNumber>" + VatNumber + "</VatNumber>" +
                "<Timestamp>" + Timestamp + "</Timestamp>" +
                "<TotalWithVat>" + InvoiceTotal + "</TotalWithVat>" +
                "<VatTotal>" + VatTotal + "</VatTotal>" +
                "</Invoice>";

            // Hash the invoice bytes, exactly as the API's invoiceHash field expects (hex).
            var invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);
            var hash = InvoiceHasher.ComputeHash(invoiceBytes);
            Assert.AreEqual(32, hash.Length);
            var hashHex = InvoiceHasher.ComputeHashHex(invoiceBytes);
            Assert.AreEqual(64, hashHex.Length);

            // Sign with a real secp256k1 key and verify the signature.
            using (var signer = EcdsaInvoiceSigner.CreateSecp256k1())
            {
                var signature = signer.SignHash(hash);
                Assert.AreEqual(64, signature.Length, "QR tag 7 must be raw 64-byte r‖s.");
                Assert.IsTrue(signer.VerifyHash(hash, signature));

                var publicKey = signer.ExportPublicKeyUncompressed();
                Assert.AreEqual(65, publicKey.Length);
                Assert.AreEqual(0x04, publicKey[0]);

                // Build the full QR payload (tags 1-8) and decode it back.
                var payload = QrCodeGenerator.Generate(new QrData
                {
                    SellerName = SellerName,
                    VatNumber = VatNumber,
                    Timestamp = Timestamp,
                    InvoiceTotalWithVat = InvoiceTotal,
                    VatTotal = VatTotal,
                    InvoiceHash = hash,
                    EcdsaSignature = signature,
                    PublicKey = publicKey
                    // Tag 9 (stamp signature) is issued by ZATCA's technical CA
                    // during onboarding; the SDK has nothing to produce here.
                    // The binary-field encode/decode path is the same as tags 6-8.
                });

                var fields = QrCodeGenerator.Decode(payload);
                Assert.AreEqual(8, fields.Count);

                Assert.AreEqual(SellerName, fields.Single(f => f.Tag == 1).ValueText);
                Assert.AreEqual(VatNumber, fields.Single(f => f.Tag == 2).ValueText);
                Assert.AreEqual(Timestamp, fields.Single(f => f.Tag == 3).ValueText);
                Assert.AreEqual(InvoiceTotal, fields.Single(f => f.Tag == 4).ValueText);
                Assert.AreEqual(VatTotal, fields.Single(f => f.Tag == 5).ValueText);
                CollectionAssert.AreEqual(hash, fields.Single(f => f.Tag == 6).Value);
                CollectionAssert.AreEqual(signature, fields.Single(f => f.Tag == 7).Value);
                CollectionAssert.AreEqual(publicKey, fields.Single(f => f.Tag == 8).Value);
            }
        }

        /// <summary>
        /// Always runs (no credentials): the Phase 1 (tags 1-5) QR payload —
        /// what every ZATCA invoice carries regardless of Phase 2 onboarding —
        /// decodes back to the original fields.
        /// </summary>
        [Test]
        public void QrPhase1_Payload_DecodesToOriginalFields()
        {
            var payload = QrCodeGenerator.GeneratePhase1(
                SellerName, VatNumber, Timestamp, InvoiceTotal, VatTotal);

            var fields = QrCodeGenerator.Decode(payload);
            Assert.AreEqual(5, fields.Count);
            for (int i = 1; i <= 5; i++)
                Assert.AreEqual(i, fields[i - 1].Tag, "Tags must appear in order 1-5.");

            Assert.AreEqual(SellerName, fields[0].ValueText);
            Assert.AreEqual(VatNumber, fields[1].ValueText);
            Assert.AreEqual(Timestamp, fields[2].ValueText);
            Assert.AreEqual(InvoiceTotal, fields[3].ValueText);
            Assert.AreEqual(VatTotal, fields[4].ValueText);
        }

        /// <summary>
        /// Step 1 of sandbox onboarding: submits the CSR and OTP and expects
        /// compliance CSID credentials back. Skipped unless ZATCA_SANDBOX_OTP
        /// and ZATCA_SANDBOX_CSR_BASE64 are set. ZATCA_SANDBOX_URL optionally
        /// overrides the sandbox base URL (defaults to the SDK's sandbox
        /// constant).
        /// </summary>
        [Test]
        public async Task Sandbox_ComplianceCsid_OnboardingReturnsCredentials()
        {
            var otp = RequireEnv(SandboxOtpEnv);
            var csrBase64 = RequireEnv(SandboxCsrEnv);

            var client = new ZatcaClient(
                new ZatcaClientOptions { BaseUrl = SandboxUrl() }, SharedHttp);

            var csid = await client.RequestComplianceCsIdAsync(csrBase64, otp);

            Assert.IsNotNull(csid);
            Assert.IsFalse(string.IsNullOrWhiteSpace(csid.BinarySecurityToken));
            Assert.IsFalse(string.IsNullOrWhiteSpace(csid.Secret));
            Assert.IsFalse(string.IsNullOrWhiteSpace(csid.RequestId),
                "requestID links this compliance CSID to the production CSID call.");
        }

        /// <summary>
        /// Steps 1-2 of sandbox onboarding: obtains a compliance CSID, then
        /// submits an integrator-supplied signed invoice XML for the
        /// compliance checks and expects a structured validation result back.
        /// Skipped unless ZATCA_SANDBOX_OTP, ZATCA_SANDBOX_CSR_BASE64,
        /// ZATCA_SANDBOX_INVOICE_XML_BASE64 and ZATCA_SANDBOX_INVOICE_UUID are
        /// set. The SDK does not generate signed UBL XML — that remains the
        /// integrator's responsibility, which is why the invoice is an input.
        /// </summary>
        [Test]
        public async Task Sandbox_ComplianceInvoice_SubmissionReturnsValidationResults()
        {
            var otp = RequireEnv(SandboxOtpEnv);
            var csrBase64 = RequireEnv(SandboxCsrEnv);
            var invoiceBase64 = RequireEnv(SandboxInvoiceEnv);
            var uuid = RequireEnv(SandboxInvoiceUuidEnv);

            var client = new ZatcaClient(
                new ZatcaClientOptions { BaseUrl = SandboxUrl() }, SharedHttp);

            var compliance = await client.RequestComplianceCsIdAsync(csrBase64, otp);
            var complianceClient = client.WithCredentials(
                compliance.BinarySecurityToken, compliance.Secret);

            var invoiceBytes = Convert.FromBase64String(invoiceBase64);
            var submission = new InvoiceSubmission
            {
                InvoiceHash = InvoiceHasher.ComputeHashHex(invoiceBytes),
                Uuid = uuid,
                InvoiceBase64 = invoiceBase64
            };

            var result = await complianceClient.SubmitComplianceInvoiceAsync(submission);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.ValidationResults,
                "ZATCA should return validation results for the compliance check.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ValidationResults.Status));
        }

        /// <summary>
        /// Negative path: a wrong OTP must be rejected by the sandbox with a
        /// <see cref="ZatcaApiException"/> (non-2xx). Skipped unless
        /// ZATCA_SANDBOX_CSR_BASE64 is set. The wrong OTP is deliberately
        /// trivial and hardcoded — it is not a credential.
        /// </summary>
        [Test]
        public void Sandbox_ComplianceCsid_WrongOtp_IsRejected()
        {
            var csrBase64 = RequireEnv(SandboxCsrEnv);

            var client = new ZatcaClient(
                new ZatcaClientOptions { BaseUrl = SandboxUrl() }, SharedHttp);

            var ex = Assert.ThrowsAsync<ZatcaApiException>(async () =>
                await client.RequestComplianceCsIdAsync(csrBase64, "000000"));

            Assert.IsTrue(ex.StatusCode >= 400 && ex.StatusCode < 500,
                "Expected a 4xx rejection, got " + ex.StatusCode + ".");
        }
    }
}
