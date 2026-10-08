using System;

namespace Zatca
{
    /// <summary>
    /// Configuration for <see cref="ZatcaClient"/>.
    /// </summary>
    public class ZatcaClientOptions
    {
        /// <summary>ZATCA developer-portal sandbox (no real credentials needed; OTP is fixed).</summary>
        public const string SandboxBaseUrl = "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal";

        /// <summary>ZATCA simulation environment (pre-production validation).</summary>
        public const string SimulationBaseUrl = "https://gw-fatoora.zatca.gov.sa/e-invoicing/simulation";

        /// <summary>ZATCA production environment.</summary>
        public const string ProductionBaseUrl = "https://gw-fatoora.zatca.gov.sa/e-invoicing/core";

        /// <summary>
        /// API base URL. Defaults to the sandbox.
        /// Point at <see cref="SimulationBaseUrl"/> or <see cref="ProductionBaseUrl"/>
        /// when you move past development.
        /// </summary>
        public string BaseUrl { get; set; } = SandboxBaseUrl;

        /// <summary>
        /// The <c>binarySecurityToken</c> returned by the compliance or production
        /// CSID APIs. Used as the username of HTTP Basic auth.
        /// </summary>
        public string BinarySecurityToken { get; set; }

        /// <summary>
        /// The <c>secret</c> returned alongside the CSID. Used as the password
        /// of HTTP Basic auth.
        /// </summary>
        public string Secret { get; set; }

        internal string BuildBasicAuthHeader()
        {
            if (string.IsNullOrWhiteSpace(BinarySecurityToken) || string.IsNullOrWhiteSpace(Secret))
                throw new InvalidOperationException(
                    "BinarySecurityToken and Secret are required. " +
                    "Obtain them via RequestComplianceCsIdAsync / RequestProductionCsIdAsync first, " +
                    "or use WithCredentials() to switch credential sets between onboarding steps.");

            var raw = BinarySecurityToken + ":" + Secret;
            return "Basic " + Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(raw));
        }
    }
}
