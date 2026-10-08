using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Zatca.Models;

namespace Zatca.Tests
{
    [TestFixture]
    public class ZatcaClientTests
    {
        private const string AcceptedBody =
            @"{ ""reportingStatus"": ""REPORTED"",
                ""validationResults"": { ""status"": ""PASS"", ""infoMessages"": [],
                    ""warningMessages"": [], ""errorMessages"": [] } }";

        private const string ClearedBody =
            @"{ ""clearanceStatus"": ""CLEARED"",
                ""validationResults"": { ""status"": ""WARNING"",
                    ""warningMessages"": [ { ""type"": ""WARNING"", ""code"": ""X"", ""category"": ""C"",
                        ""message"": ""m"", ""status"": ""WARNING"" } ],
                    ""infoMessages"": [], ""errorMessages"": [] } }";

        private static ZatcaClientOptions ProdOptions() => new ZatcaClientOptions
        {
            BinarySecurityToken = "prod-token",
            Secret = "prod-secret"
        };

        private static InvoiceSubmission Submission() => new InvoiceSubmission
        {
            InvoiceHash = new string('a', 64),
            Uuid = Guid.NewGuid().ToString(),
            InvoiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<xml/>"))
        };

        private class CapturingHandler : HttpMessageHandler
        {
            private readonly Queue<string> _bodies;
            private readonly HttpStatusCode _status;
            public List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

            public CapturingHandler(IEnumerable<string> bodies, HttpStatusCode status = HttpStatusCode.OK)
            {
                _bodies = new Queue<string>(bodies);
                _status = status;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var captured = new CapturedRequest
                {
                    Method = request.Method.Method,
                    Url = request.RequestUri.ToString(),
                    Body = request.Content == null
                        ? null
                        : await request.Content.ReadAsStringAsync()
                };
                foreach (var h in request.Headers)
                    captured.Headers[h.Key] = string.Join(",", h.Value);
                Requests.Add(captured);

                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_bodies.Dequeue(), Encoding.UTF8, "application/json")
                };
            }
        }

        private class CapturedRequest
        {
            public string Method;
            public string Url;
            public string Body;
            public Dictionary<string, string> Headers =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string ExpectedBasic(string token, string secret) =>
            "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(token + ":" + secret));

        [Test]
        public async Task ReportInvoiceAsync_PostsToReportingEndpoint()
        {
            var handler = new CapturingHandler(new[] { AcceptedBody });
            var client = new ZatcaClient(ProdOptions(), new HttpClient(handler));

            var result = await client.ReportInvoiceAsync(Submission());

            Assert.AreEqual(1, handler.Requests.Count);
            var req = handler.Requests[0];
            Assert.AreEqual("POST", req.Method);
            Assert.IsTrue(req.Url.EndsWith("/invoices/reporting/single"), req.Url);
            Assert.AreEqual("0", req.Headers["Clearance-Status"]);
            Assert.AreEqual(ExpectedBasic("prod-token", "prod-secret"), req.Headers["Authorization"]);

            using (var doc = JsonDocument.Parse(req.Body))
            {
                Assert.AreEqual(64, doc.RootElement.GetProperty("invoiceHash").GetString().Length);
                Assert.IsTrue(doc.RootElement.TryGetProperty("uuid", out _));
                Assert.IsTrue(doc.RootElement.TryGetProperty("invoice", out _));
            }

            Assert.AreEqual("REPORTED", result.ReportingStatus);
            Assert.AreEqual("PASS", result.ValidationResults.Status);
            Assert.IsTrue(result.IsAccepted);
        }

        [Test]
        public async Task ClearInvoiceAsync_PostsToClearanceEndpoint()
        {
            var handler = new CapturingHandler(new[] { ClearedBody });
            var client = new ZatcaClient(ProdOptions(), new HttpClient(handler));

            var result = await client.ClearInvoiceAsync(Submission());

            var req = handler.Requests[0];
            Assert.IsTrue(req.Url.EndsWith("/invoices/clearance/single"), req.Url);
            Assert.AreEqual("1", req.Headers["Clearance-Status"]);
            Assert.AreEqual("CLEARED", result.ClearanceStatus);
            Assert.IsTrue(result.IsAccepted, "WARNING still counts as accepted");
            Assert.AreEqual(1, result.ValidationResults.WarningMessages.Count);
        }

        [Test]
        public async Task RequestComplianceCsIdAsync_SendsOtpAndVersionHeaders()
        {
            var handler = new CapturingHandler(new[]
            {
                @"{ ""binarySecurityToken"": ""ccsid"", ""secret"": ""s"", ""requestID"": ""req-1"" }"
            });
            var client = new ZatcaClient(new ZatcaClientOptions(), new HttpClient(handler));

            var csid = await client.RequestComplianceCsIdAsync("Y3Ny", "123345");

            var req = handler.Requests[0];
            Assert.IsTrue(req.Url.EndsWith("/compliance"), req.Url);
            Assert.AreEqual("123345", req.Headers["OTP"]);
            Assert.AreEqual("V2", req.Headers["Accept-Version"]);

            using (var doc = JsonDocument.Parse(req.Body))
                Assert.AreEqual("Y3Ny", doc.RootElement.GetProperty("csr").GetString());

            Assert.AreEqual("ccsid", csid.BinarySecurityToken);
            Assert.AreEqual("s", csid.Secret);
            Assert.AreEqual("req-1", csid.RequestId);
        }

        [Test]
        public async Task RequestProductionCsIdAsync_SendsComplianceRequestId()
        {
            var handler = new CapturingHandler(new[]
            {
                @"{ ""binarySecurityToken"": ""pcsid"", ""secret"": ""ps"", ""requestID"": ""req-2"" }"
            });
            var complianceClient = new ZatcaClient(new ZatcaClientOptions
            {
                BinarySecurityToken = "ccsid",
                Secret = "cs"
            }, new HttpClient(handler));

            var pcsid = await complianceClient.RequestProductionCsIdAsync("req-1");

            var req = handler.Requests[0];
            Assert.IsTrue(req.Url.EndsWith("/production/csids"), req.Url);
            Assert.AreEqual(ExpectedBasic("ccsid", "cs"), req.Headers["Authorization"]);

            using (var doc = JsonDocument.Parse(req.Body))
                Assert.AreEqual("req-1", doc.RootElement.GetProperty("compliance_request_id").GetString());

            Assert.AreEqual("pcsid", pcsid.BinarySecurityToken);
        }

        [Test]
        public async Task SubmitComplianceInvoiceAsync_UsesComplianceEndpoint()
        {
            var handler = new CapturingHandler(new[] { AcceptedBody });
            var complianceClient = new ZatcaClient(new ZatcaClientOptions
            {
                BinarySecurityToken = "ccsid",
                Secret = "cs"
            }, new HttpClient(handler));

            await complianceClient.SubmitComplianceInvoiceAsync(Submission());

            var req = handler.Requests[0];
            Assert.IsTrue(req.Url.EndsWith("/compliance/invoices"), req.Url);
            Assert.AreEqual(ExpectedBasic("ccsid", "cs"), req.Headers["Authorization"]);
            Assert.IsFalse(req.Headers.ContainsKey("Clearance-Status"));
        }

        [Test]
        public void WithCredentials_SwitchesCredentialSet()
        {
            var handler = new CapturingHandler(new[] { AcceptedBody });
            var client = new ZatcaClient(new ZatcaClientOptions(), new HttpClient(handler));
            var prod = client.WithCredentials("prod-token", "prod-secret");

            prod.ReportInvoiceAsync(Submission()).GetAwaiter().GetResult();

            Assert.AreEqual(ExpectedBasic("prod-token", "prod-secret"),
                handler.Requests[0].Headers["Authorization"]);
        }

        [Test]
        public void ApiError_ThrowsZatcaApiException()
        {
            var handler = new CapturingHandler(
                new[] { @"{ ""error"": ""bad"" }" }, HttpStatusCode.BadRequest);
            var client = new ZatcaClient(ProdOptions(), new HttpClient(handler));

            var ex = Assert.ThrowsAsync<ZatcaApiException>(() =>
                client.ReportInvoiceAsync(Submission()));

            Assert.AreEqual(400, ex.StatusCode);
            Assert.IsTrue(ex.ResponseBody.Contains("bad"));
        }

        [Test]
        public void MissingCredentials_Throws()
        {
            var client = new ZatcaClient(new ZatcaClientOptions()); // no token/secret
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.ReportInvoiceAsync(Submission()));
        }

        [Test]
        public void MissingBaseUrl_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ZatcaClient(new ZatcaClientOptions { BaseUrl = " " }));
        }
    }
}
