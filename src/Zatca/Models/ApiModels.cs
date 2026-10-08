using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Zatca.Models
{
    /// <summary>
    /// Response of the Compliance CSID and Production CSID APIs.
    /// </summary>
    public class CsIdResponse
    {
        /// <summary>The cryptographic stamp identifier (used as Basic-auth username).</summary>
        [JsonPropertyName("binarySecurityToken")]
        public string BinarySecurityToken { get; set; }

        /// <summary>The secret paired with the CSID (used as Basic-auth password).</summary>
        [JsonPropertyName("secret")]
        public string Secret { get; set; }

        /// <summary>
        /// Request id linking the compliance CSID to the later production CSID call.
        /// Pass it as <c>compliance_request_id</c> to <c>POST /production/csids</c>.
        /// </summary>
        [JsonPropertyName("requestID")]
        public string RequestId { get; set; }
    }

    /// <summary>
    /// A single message inside ZATCA's validation results.
    /// </summary>
    public class ZatcaMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// ZATCA's validation outcome for a submitted invoice.
    /// </summary>
    public class ValidationResults
    {
        [JsonPropertyName("infoMessages")]
        public List<ZatcaMessage> InfoMessages { get; set; }

        [JsonPropertyName("warningMessages")]
        public List<ZatcaMessage> WarningMessages { get; set; }

        [JsonPropertyName("errorMessages")]
        public List<ZatcaMessage> ErrorMessages { get; set; }

        /// <summary>PASS, WARNING or FAIL.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// Result of reporting (simplified) or clearing (standard) an invoice.
    /// </summary>
    public class ZatcaSubmissionResult
    {
        /// <summary>Set on reporting calls: e.g. "REPORTED".</summary>
        [JsonPropertyName("reportingStatus")]
        public string ReportingStatus { get; set; }

        /// <summary>Set on clearance calls: e.g. "CLEARED".</summary>
        [JsonPropertyName("clearanceStatus")]
        public string ClearanceStatus { get; set; }

        [JsonPropertyName("validationResults")]
        public ValidationResults ValidationResults { get; set; }

        /// <summary>
        /// True when ZATCA accepted the invoice (reported/cleared and not failed validation).
        /// Note: a validation status of WARNING still counts as accepted.
        /// </summary>
        [JsonIgnore]
        public bool IsAccepted =>
            (ReportingStatus == "REPORTED" || ClearanceStatus == "CLEARED") &&
            (ValidationResults == null || ValidationResults.Status != "FAIL");
    }

    /// <summary>
    /// The payload sent to the reporting / clearance / compliance-invoice endpoints.
    /// </summary>
    public class InvoiceSubmission
    {
        /// <summary>
        /// Hex-encoded SHA-256 of the canonical signed invoice XML
        /// (see <see cref="Crypto.InvoiceHasher"/>).
        /// </summary>
        [JsonPropertyName("invoiceHash")]
        public string InvoiceHash { get; set; }

        /// <summary>UUID (v4) embedded in the invoice XML.</summary>
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        /// <summary>Base64 of the signed UBL 2.1 invoice XML.</summary>
        [JsonPropertyName("invoice")]
        public string InvoiceBase64 { get; set; }
    }
}
