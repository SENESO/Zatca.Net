# Integration Testing

The integration tests live in `test/Zatca.IntegrationTests` and are part of
`Zatca.sln`. They cover two layers:

1. **Always-run end-to-end tests** (no credentials, no network): the highest
   level of real behavior the SDK offers — hash a sample invoice XML with
   `InvoiceHasher`, sign it with a freshly generated secp256k1 key via
   `EcdsaInvoiceSigner`, verify the signature, build the full QR payload with
   `QrCodeGenerator` (tags 1-8), decode it back and assert every tag.
2. **Sandbox HTTP tests** (opt-in): the SDK's `ZatcaClient` against ZATCA's
   real developer-portal sandbox. These are gated behind environment
   variables and are *skipped* (not failed) when a variable is missing.

Scope honesty: this SDK does not generate UBL 2.1 XML, XAdES signatures, or
CSRs — those remain the integrator's responsibility — so the sandbox tests
take the CSR and a signed invoice XML as *inputs* rather than pretending to
produce them.

## Environment variables

| Variable | Required for | Notes |
|---|---|---|
| `ZATCA_SANDBOX_URL` | all sandbox tests | Optional. Overrides the base URL; defaults to `https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal`. |
| `ZATCA_SANDBOX_OTP` | compliance CSID + compliance invoice tests | The OTP shown in the Fatoora portal when onboarding an EGS unit on the sandbox. |
| `ZATCA_SANDBOX_CSR_BASE64` | all sandbox HTTP tests | Base64 of the PEM-encoded CSR for your EGS unit (same CSR rules as the Fatoora developer documentation). |
| `ZATCA_SANDBOX_INVOICE_XML_BASE64` | compliance invoice test | Base64 of a signed invoice XML whose CSR matches `ZATCA_SANDBOX_CSR_BASE64`. The test computes the SHA-256 `invoiceHash` from these bytes. |
| `ZATCA_SANDBOX_INVOICE_UUID` | compliance invoice test | The UUID (v4) embedded in the invoice XML above. |

No secrets are hardcoded in the test code. Treat the OTP and CSR as
credentials: export them in your shell for the test run only, do not commit
them.

## Where to get sandbox credentials

1. Read the Fatoora developer documentation on the ZATCA developer portal:
   <https://zatca.gov.sa/en/E-Invoicing/Pages/default.aspx>
2. Onboard an EGS (E-Invoicing Generation Solution) unit on the Fatoora
   **developer-portal sandbox** (`gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal`).
   The portal shows you an OTP for the CSR submission step.
3. Generate a CSR for your EGS unit following ZATCA's CSR requirements
   (secp256k1 key, required DN fields including the EGS serial number), then
   export it: `ZATCA_SANDBOX_CSR_BASE64` is the base64 of the PEM text.
4. Produce one signed invoice XML (simplified or standard) whose signing
   certificate matches that CSR; export it base64-encoded as
   `ZATCA_SANDBOX_INVOICE_XML_BASE64` and its UUID as
   `ZATCA_SANDBOX_INVOICE_UUID`.

## How to run

```bash
# Unit-style end-to-end tests only (no credentials, no network):
dotnet test test/Zatca.IntegrationTests -c Release

# Including the sandbox tests:
export ZATCA_SANDBOX_OTP=...
export ZATCA_SANDBOX_CSR_BASE64=...
export ZATCA_SANDBOX_INVOICE_XML_BASE64=...
export ZATCA_SANDBOX_INVOICE_UUID=...
dotnet test test/Zatca.IntegrationTests -c Release
```

Skipped tests are reported as "Skipped" with the name of the missing
variable. A sandbox rejection (wrong OTP, invalid CSR) surfaces as
`ZatcaApiException` with the HTTP status code and ZATCA's response body.

## What the tests cover

| Test | Type | Covers |
|---|---|---|
| `InvoicePipeline_HashSignQrVerify_RoundTripsEndToEnd` | always-run | XML → SHA-256 hash → real secp256k1 sign → verify → public-key export → QR tags 1-8 → decode → tag-by-tag assertion |
| `QrPhase1_Payload_DecodesToOriginalFields` | always-run | Phase 1 (tags 1-5) QR payload generation/decoding |
| `Sandbox_ComplianceCsid_OnboardingReturnsCredentials` | sandbox | `POST /compliance` returns `binarySecurityToken`, `secret`, `requestID` |
| `Sandbox_ComplianceInvoice_SubmissionReturnsValidationResults` | sandbox | compliance CSID → `POST /compliance/invoices` returns structured validation results |
| `Sandbox_ComplianceCsid_WrongOtp_IsRejected` | sandbox | negative path: bad OTP → `ZatcaApiException` with 4xx status |
