using System;

namespace Zatca
{
    /// <summary>
    /// Thrown when the ZATCA API answers with a non-success HTTP status.
    /// </summary>
    public class ZatcaApiException : Exception
    {
        /// <summary>HTTP status code returned by ZATCA.</summary>
        public int StatusCode { get; }

        /// <summary>Raw response body returned by ZATCA (may contain error details).</summary>
        public string ResponseBody { get; }

        public ZatcaApiException(int statusCode, string responseBody)
            : base("ZATCA API error " + statusCode + ": " + Truncate(responseBody))
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<empty response>";
            return value.Length <= 300 ? value : value.Substring(0, 300) + "...";
        }
    }

    /// <summary>
    /// Thrown when a <see cref="Models.TaxInvoice"/> fails local validation
    /// (inconsistent totals, missing buyer on a standard invoice, bad VAT number, ...).
    /// </summary>
    public class ZatcaValidationException : Exception
    {
        public ZatcaValidationException(string message) : base(message)
        {
        }
    }
}
