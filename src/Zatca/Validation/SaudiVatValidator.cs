namespace Zatca.Validation
{
    /// <summary>
    /// Validates Saudi VAT registration numbers: 15 digits, starting and
    /// ending with 3 (e.g. the sandbox test VAT 399999999900003).
    /// </summary>
    public static class SaudiVatValidator
    {
        public static bool IsValid(string vatNumber)
        {
            if (string.IsNullOrWhiteSpace(vatNumber)) return false;
            if (vatNumber.Length != 15) return false;

            foreach (char c in vatNumber)
                if (c < '0' || c > '9') return false;

            return vatNumber[0] == '3' && vatNumber[14] == '3';
        }
    }
}
