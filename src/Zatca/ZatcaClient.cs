using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zatca.Models;

namespace Zatca
{
    /// <summary>
    /// Client for ZATCA's Fatoora Phase 2 APIs: EGS onboarding (compliance and
    /// production CSIDs) and invoice clearance / reporting.
    /// <para>
    /// Bring your own <see cref="HttpClient"/>; the client only adds paths,
    /// headers and auth. All calls are async and honor cancellation.
    /// </para>
    /// <para>
    /// Onboarding moves through credential sets: start with no credentials,
    /// call <see cref="RequestComplianceCsIdAsync"/>, then
    /// <see cref="WithCredentials"/> to switch to the compliance set for the
    /// compliance checks and the production CSID call, then switch again to the
    /// production set for reporting and clearance.
    /// </para>
    /// </summary>
    public class ZatcaClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly HttpMethod PatchMethod = new HttpMethod("PATCH");

        private readonly ZatcaClientOptions _options;
        private readonly HttpClient _http;

        public ZatcaClient(ZatcaClientOptions options)
            : this(options, new HttpClient())
        {
        }

        public ZatcaClient(ZatcaClientOptions options, HttpClient httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Returns a client sharing the same <see cref="HttpClient"/> but using
        /// a different CSID credential set (e.g. compliance vs production).
        /// </summary>
        public ZatcaClient WithCredentials(string binarySecurityToken, string secret)
        {
            return new ZatcaClient(new ZatcaClientOptions
            {
                BaseUrl = _options.BaseUrl,
                BinarySecurityToken = binarySecurityToken,
                Secret = secret
            }, _http);
        }

        // ---------------- Onboarding ----------------

        /// <summary>
        /// Step 1 of onboarding: submits the base64-encoded CSR and the OTP from
        /// the Fatoora portal and returns the compliance CSID, its secret and
        /// the request id. No auth headers are needed for this call.
        /// <c>POST {base}/compliance</c> with headers <c>OTP</c> and
        /// <c>Accept-Version: V2</c>.
        /// </summary>
        /// <param name="csrBase64">Base64 of the PEM-encoded CSR.</param>
        /// <param name="otp">OTP from the Fatoora portal ("123345" on the sandbox).</param>
        public async Task<CsIdResponse> RequestComplianceCsIdAsync(
            string csrBase64, string otp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(csrBase64))
                throw new ArgumentException("CSR is required.", nameof(csrBase64));
            if (string.IsNullOrWhiteSpace(otp))
                throw new ArgumentException("OTP is required.", nameof(otp));

            var url = _options.BaseUrl.TrimEnd('/') + "/compliance";
            var body = JsonSerializer.Serialize(new { csr = csrBase64 }, JsonOptions);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("OTP", otp);
                request.Headers.Add("Accept-Version", "V2");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var responseBody = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CsIdResponse>(responseBody, JsonOptions);
            }
        }

        /// <summary>
        /// Step 2 of onboarding: submits sample documents for the compliance
        /// checks (one per invoice type declared in the CSR).
        /// <c>POST {base}/compliance/invoices</c> with Basic auth using the
        /// <b>compliance</b> CSID credentials.
        /// </summary>
        public Task<ZatcaSubmissionResult> SubmitComplianceInvoiceAsync(
            InvoiceSubmission submission, CancellationToken cancellationToken = default)
        {
            return PostInvoiceAsync("/compliance/invoices", submission, null, cancellationToken);
        }

        /// <summary>
        /// Step 3 of onboarding: exchanges the compliance request id for the
        /// production CSID. <c>POST {base}/production/csids</c> with Basic auth
        /// using the <b>compliance</b> CSID credentials.
        /// </summary>
        public async Task<CsIdResponse> RequestProductionCsIdAsync(
            string complianceRequestId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(complianceRequestId))
                throw new ArgumentException("Compliance request id is required.", nameof(complianceRequestId));

            var url = _options.BaseUrl.TrimEnd('/') + "/production/csids";
            var body = JsonSerializer.Serialize(
                new { compliance_request_id = complianceRequestId }, JsonOptions);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("Authorization", _options.BuildBasicAuthHeader());
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var responseBody = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CsIdResponse>(responseBody, JsonOptions);
            }
        }

        /// <summary>
        /// Renews an expiring production CSID with a fresh CSR and a new OTP.
        /// <c>PATCH {base}/production/csids</c> with Basic auth using the
        /// current <b>production</b> CSID credentials.
        /// </summary>
        public async Task<CsIdResponse> RenewProductionCsIdAsync(
            string csrBase64, string otp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(csrBase64))
                throw new ArgumentException("CSR is required.", nameof(csrBase64));
            if (string.IsNullOrWhiteSpace(otp))
                throw new ArgumentException("OTP is required.", nameof(otp));

            var url = _options.BaseUrl.TrimEnd('/') + "/production/csids";
            var body = JsonSerializer.Serialize(new { csr = csrBase64 }, JsonOptions);

            using (var request = new HttpRequestMessage(PatchMethod, url))
            {
                request.Headers.Add("Authorization", _options.BuildBasicAuthHeader());
                request.Headers.Add("OTP", otp);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var responseBody = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CsIdResponse>(responseBody, JsonOptions);
            }
        }

        // ---------------- Clearance / reporting ----------------

        /// <summary>
        /// Reports a simplified (B2C) invoice. The invoice is issued to the
        /// customer immediately and must be reported within 24 hours.
        /// <c>POST {base}/invoices/reporting/single</c> with
        /// <c>Clearance-Status: 0</c> and Basic auth using the
        /// <b>production</b> CSID credentials.
        /// </summary>
        public Task<ZatcaSubmissionResult> ReportInvoiceAsync(
            InvoiceSubmission submission, CancellationToken cancellationToken = default)
        {
            return PostInvoiceAsync("/invoices/reporting/single", submission, "0", cancellationToken);
        }

        /// <summary>
        /// Clears a standard (B2B) invoice. The invoice must be cleared by ZATCA
        /// <b>before</b> it is shared with the buyer.
        /// <c>POST {base}/invoices/clearance/single</c> with
        /// <c>Clearance-Status: 1</c> and Basic auth using the
        /// <b>production</b> CSID credentials.
        /// </summary>
        public Task<ZatcaSubmissionResult> ClearInvoiceAsync(
            InvoiceSubmission submission, CancellationToken cancellationToken = default)
        {
            return PostInvoiceAsync("/invoices/clearance/single", submission, "1", cancellationToken);
        }

        // ---------------- internals ----------------

        private async Task<ZatcaSubmissionResult> PostInvoiceAsync(
            string path, InvoiceSubmission submission, string clearanceStatus,
            CancellationToken cancellationToken)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (string.IsNullOrWhiteSpace(submission.InvoiceHash))
                throw new ArgumentException("InvoiceHash is required.", nameof(submission));
            if (string.IsNullOrWhiteSpace(submission.Uuid))
                throw new ArgumentException("Uuid is required.", nameof(submission));
            if (string.IsNullOrWhiteSpace(submission.InvoiceBase64))
                throw new ArgumentException("InvoiceBase64 (signed XML) is required.", nameof(submission));

            var url = _options.BaseUrl.TrimEnd('/') + path;
            var body = JsonSerializer.Serialize(submission, JsonOptions);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("Authorization", _options.BuildBasicAuthHeader());
                if (clearanceStatus != null)
                    request.Headers.Add("Clearance-Status", clearanceStatus);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var responseBody = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<ZatcaSubmissionResult>(responseBody, JsonOptions);
            }
        }

        private async Task<string> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new ZatcaApiException((int)response.StatusCode, body);
                return body;
            }
        }
    }
}
