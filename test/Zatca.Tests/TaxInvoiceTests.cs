using System;
using System.Collections.Generic;
using NUnit.Framework;
using Zatca.Models;

namespace Zatca.Tests
{
    [TestFixture]
    public class TaxInvoiceTests
    {
        private static TaxInvoice ValidSimplified()
        {
            return new TaxInvoice
            {
                InvoiceType = ZatcaInvoiceType.Simplified,
                InvoiceCounter = 1,
                PreviousInvoiceHash = new string('0', 64),
                Seller = new Party
                {
                    Name = "شركة الاختبار",
                    VatNumber = "399999999900003",
                    CommercialRegistrationNumber = "1010000000",
                    City = "Riyadh"
                },
                Lines = new List<InvoiceLine>
                {
                    new InvoiceLine
                    {
                        Description = "Item A",
                        Quantity = 2,
                        UnitPriceExcludingVat = 50,
                        VatRatePercent = 15,
                        LineTotalExcludingVat = 100,
                        VatAmount = 15,
                        LineTotalIncludingVat = 115
                    }
                },
                TotalExcludingVat = 100,
                TotalVat = 15,
                TotalIncludingVat = 115
            };
        }

        [Test]
        public void Validate_HappyPath_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ValidSimplified().Validate());
        }

        [Test]
        public void Validate_TotalsMismatch_Throws()
        {
            var inv = ValidSimplified();
            inv.TotalVat = 99;
            Assert.Throws<ZatcaValidationException>(() => inv.Validate());
        }

        [Test]
        public void Validate_StandardRequiresBuyer()
        {
            var inv = ValidSimplified();
            inv.InvoiceType = ZatcaInvoiceType.Standard;
            var ex = Assert.Throws<ZatcaValidationException>(() => inv.Validate());
            StringAssert.Contains("Buyer", ex.Message);
        }

        [Test]
        public void Validate_StandardWithBuyerVat_Passes()
        {
            var inv = ValidSimplified();
            inv.InvoiceType = ZatcaInvoiceType.Standard;
            inv.Buyer = new Party { Name = "Buyer Co", VatNumber = "300000000000003" };
            Assert.DoesNotThrow(() => inv.Validate());
        }

        [Test]
        public void Validate_BadSellerVat_Throws()
        {
            var inv = ValidSimplified();
            inv.Seller.VatNumber = "123";
            Assert.Throws<ZatcaValidationException>(() => inv.Validate());
        }

        [Test]
        public void Validate_ZeroCounter_Throws()
        {
            var inv = ValidSimplified();
            inv.InvoiceCounter = 0;
            Assert.Throws<ZatcaValidationException>(() => inv.Validate());
        }

        [Test]
        public void Validate_NoLines_Throws()
        {
            var inv = ValidSimplified();
            inv.Lines = new List<InvoiceLine>();
            Assert.Throws<ZatcaValidationException>(() => inv.Validate());
        }

        [Test]
        public void Validate_BadUuid_Throws()
        {
            var inv = ValidSimplified();
            inv.Uuid = "not-a-uuid";
            Assert.Throws<ZatcaValidationException>(() => inv.Validate());
        }
    }
}
