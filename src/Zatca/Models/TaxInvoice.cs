using System;
using System.Collections.Generic;
using Zatca.Validation;

namespace Zatca.Models
{
    /// <summary>
    /// ZATCA Phase 2 invoice kinds.
    /// <list type="bullet">
    /// <item>Standard (B2B): must be <b>cleared</b> by ZATCA before sharing with the buyer.</item>
    /// <item>Simplified (B2C): issued to the customer immediately, <b>reported</b> within 24 hours.</item>
    /// </list>
    /// </summary>
    public enum ZatcaInvoiceType
    {
        Standard,
        Simplified
    }

    /// <summary>
    /// Seller or buyer party on an invoice.
    /// </summary>
    public class Party
    {
        /// <summary>Registered name (Arabic recommended for the seller).</summary>
        public string Name { get; set; }

        /// <summary>15-digit VAT registration number (must start and end with 3).</summary>
        public string VatNumber { get; set; }

        /// <summary>Commercial registration number.</summary>
        public string CommercialRegistrationNumber { get; set; }

        public string Street { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }

        /// <summary>ISO country code, e.g. "SA".</summary>
        public string CountryCode { get; set; } = "SA";
    }

    /// <summary>
    /// One line item on the invoice.
    /// </summary>
    public class InvoiceLine
    {
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPriceExcludingVat { get; set; }

        /// <summary>VAT rate in percent, e.g. 15 for the standard Saudi rate.</summary>
        public decimal VatRatePercent { get; set; }

        public decimal LineTotalExcludingVat { get; set; }
        public decimal VatAmount { get; set; }
        public decimal LineTotalIncludingVat { get; set; }
    }

    /// <summary>
    /// Domain model of a ZATCA Phase 2 tax invoice (standard or simplified).
    /// This is the business shape of the invoice; serializing it to signed
    /// UBL 2.1 XML is intentionally out of scope (see README).
    /// </summary>
    public class TaxInvoice
    {
        public ZatcaInvoiceType InvoiceType { get; set; }

        /// <summary>UUID v4, also embedded in the invoice XML.</summary>
        public string Uuid { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Invoice counter (ICV): strictly sequential per EGS unit, never reused.
        /// </summary>
        public int InvoiceCounter { get; set; }

        /// <summary>
        /// Previous invoice hash (PIH): hex SHA-256 of the previous invoice's XML.
        /// For the very first invoice use the well-known zero hash
        /// ("0000...0", 64 zeros).
        /// </summary>
        public string PreviousInvoiceHash { get; set; }

        public DateTimeOffset IssueDateTime { get; set; } = DateTimeOffset.UtcNow;

        public Party Seller { get; set; }
        public Party Buyer { get; set; }

        public List<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

        public decimal TotalExcludingVat { get; set; }
        public decimal TotalVat { get; set; }
        public decimal TotalIncludingVat { get; set; }

        /// <summary>
        /// Validates internal consistency. Throws <see cref="ZatcaValidationException"/>
        /// on the first problem found.
        /// </summary>
        public void Validate()
        {
            if (Seller == null)
                throw new ZatcaValidationException("Seller is required.");

            if (string.IsNullOrWhiteSpace(Seller.Name))
                throw new ZatcaValidationException("Seller name is required.");

            if (!SaudiVatValidator.IsValid(Seller.VatNumber))
                throw new ZatcaValidationException(
                    "Seller VAT number '" + Seller.VatNumber + "' is not a valid Saudi VAT number " +
                    "(15 digits, starts and ends with 3).");

            if (InvoiceType == ZatcaInvoiceType.Standard)
            {
                if (Buyer == null || string.IsNullOrWhiteSpace(Buyer.Name))
                    throw new ZatcaValidationException(
                        "Buyer is required on a standard (B2B) invoice.");

                if (!SaudiVatValidator.IsValid(Buyer.VatNumber))
                    throw new ZatcaValidationException(
                        "Buyer VAT number '" + (Buyer == null ? null : Buyer.VatNumber) + "' is not valid " +
                        "— a standard invoice requires the buyer's VAT number.");
            }

            if (!Guid.TryParse(Uuid, out _))
                throw new ZatcaValidationException("Uuid '" + Uuid + "' is not a valid UUID.");

            if (InvoiceCounter < 1)
                throw new ZatcaValidationException("InvoiceCounter (ICV) must be a positive sequential number.");

            if (Lines == null || Lines.Count == 0)
                throw new ZatcaValidationException("At least one invoice line is required.");

            const decimal tolerance = 0.01m;
            decimal linesEx = 0m, linesVat = 0m, linesInc = 0m;

            foreach (var line in Lines)
            {
                if (line.Quantity <= 0)
                    throw new ZatcaValidationException("Line quantity must be positive.");
                if (line.VatRatePercent < 0 || line.VatRatePercent > 100)
                    throw new ZatcaValidationException("Line VAT rate must be between 0 and 100.");

                linesEx += line.LineTotalExcludingVat;
                linesVat += line.VatAmount;
                linesInc += line.LineTotalIncludingVat;
            }

            if (Math.Abs(linesEx - TotalExcludingVat) > tolerance)
                throw new ZatcaValidationException(
                    "TotalExcludingVat does not match the sum of line totals.");
            if (Math.Abs(linesVat - TotalVat) > tolerance)
                throw new ZatcaValidationException(
                    "TotalVat does not match the sum of line VAT amounts.");
            if (Math.Abs(linesInc - TotalIncludingVat) > tolerance)
                throw new ZatcaValidationException(
                    "TotalIncludingVat does not match the sum of line totals including VAT.");
            if (Math.Abs(TotalExcludingVat + TotalVat - TotalIncludingVat) > tolerance)
                throw new ZatcaValidationException(
                    "TotalExcludingVat + TotalVat must equal TotalIncludingVat.");
        }
    }
}
