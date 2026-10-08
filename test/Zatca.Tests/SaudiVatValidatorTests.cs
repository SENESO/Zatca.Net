using NUnit.Framework;
using Zatca.Validation;

namespace Zatca.Tests
{
    [TestFixture]
    public class SaudiVatValidatorTests
    {
        [Test]
        public void Valid_SandboxTestVat() =>
            Assert.IsTrue(SaudiVatValidator.IsValid("399999999900003"));

        [Test]
        public void Valid_GenericVat() =>
            Assert.IsTrue(SaudiVatValidator.IsValid("300000000000003"));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("39999999990000")]    // 14 digits
        [TestCase("3999999999000003")]  // 16 digits
        [TestCase("199999999900003")]  // doesn't start with 3
        [TestCase("399999999900001")]  // doesn't end with 3
        [TestCase("39999999990000A")]  // non-digit
        public void Invalid_VatNumbers(string vat) =>
            Assert.IsFalse(SaudiVatValidator.IsValid(vat));
    }
}
