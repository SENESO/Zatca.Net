using System;
using System.Collections.Generic;
using System.Text;

namespace Zatca.Qr
{
    /// <summary>
    /// Input for the ZATCA QR code, per the Electronic Invoice Security Features
    /// Implementation Standards (TLV fields).
    /// Tags 1–5 are UTF-8 strings; tags 6–9 are raw cryptographic bytes.
    /// </summary>
    public class QrData
    {
        /// <summary>Tag 1: seller's name.</summary>
        public string SellerName { get; set; }

        /// <summary>Tag 2: seller's VAT registration number.</summary>
        public string VatNumber { get; set; }

        /// <summary>Tag 3: invoice timestamp, ISO-8601 (e.g. 2022-02-21T12:13:57Z).</summary>
        public string Timestamp { get; set; }

        /// <summary>Tag 4: invoice total including VAT (e.g. "115.00").</summary>
        public string InvoiceTotalWithVat { get; set; }

        /// <summary>Tag 5: VAT total (e.g. "15.00").</summary>
        public string VatTotal { get; set; }

        /// <summary>Tag 6: SHA-256 hash of the invoice XML (32 bytes).</summary>
        public byte[] InvoiceHash { get; set; }

        /// <summary>Tag 7: ECDSA signature of the XML hash (64 bytes, raw r‖s).</summary>
        public byte[] EcdsaSignature { get; set; }

        /// <summary>Tag 8: ECDSA public key extracted from the signing private key.</summary>
        public byte[] PublicKey { get; set; }

        /// <summary>
        /// Tag 9: for simplified invoices — ECDSA signature of the cryptographic
        /// stamp issued by ZATCA's technical CA.
        /// </summary>
        public byte[] StampSignature { get; set; }
    }

    /// <summary>
    /// One decoded TLV field.
    /// </summary>
    public class QrField
    {
        public int Tag { get; set; }
        public byte[] Value { get; set; }

        /// <summary>Value interpreted as UTF-8 text (use for tags 1–5).</summary>
        public string ValueText => Value == null ? null : Encoding.UTF8.GetString(Value);
    }

    /// <summary>
    /// Builds and decodes ZATCA QR code payloads: base64-encoded TLV
    /// (tag-length-value) buffers.
    /// <para>
    /// Each field is: 1-byte tag + 1-byte length (byte count of the UTF-8 value,
    /// max 255) + value bytes. The concatenated buffer is base64-encoded and
    /// rendered as the QR image by the integrator.
    /// </para>
    /// </summary>
    public static class QrCodeGenerator
    {
        /// <summary>
        /// Generates the base64 TLV payload for tags 1–5 (Phase 1 style, no
        /// cryptographic stamp fields).
        /// </summary>
        public static string GeneratePhase1(
            string sellerName, string vatNumber, string timestamp,
            string invoiceTotalWithVat, string vatTotal)
        {
            return Generate(new QrData
            {
                SellerName = sellerName,
                VatNumber = vatNumber,
                Timestamp = timestamp,
                InvoiceTotalWithVat = invoiceTotalWithVat,
                VatTotal = vatTotal
            });
        }

        /// <summary>
        /// Generates the base64 TLV payload for the given fields. Tags 1–5 are
        /// required; tags 6–9 are included when their byte arrays are provided.
        /// </summary>
        public static string Generate(QrData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var fields = new List<byte[]>
            {
                EncodeTextField(1, data.SellerName, nameof(data.SellerName)),
                EncodeTextField(2, data.VatNumber, nameof(data.VatNumber)),
                EncodeTextField(3, data.Timestamp, nameof(data.Timestamp)),
                EncodeTextField(4, data.InvoiceTotalWithVat, nameof(data.InvoiceTotalWithVat)),
                EncodeTextField(5, data.VatTotal, nameof(data.VatTotal))
            };

            if (data.InvoiceHash != null) fields.Add(EncodeBinaryField(6, data.InvoiceHash));
            if (data.EcdsaSignature != null) fields.Add(EncodeBinaryField(7, data.EcdsaSignature));
            if (data.PublicKey != null) fields.Add(EncodeBinaryField(8, data.PublicKey));
            if (data.StampSignature != null) fields.Add(EncodeBinaryField(9, data.StampSignature));

            int total = 0;
            foreach (var f in fields) total += f.Length;

            var buffer = new byte[total];
            int pos = 0;
            foreach (var f in fields)
            {
                Buffer.BlockCopy(f, 0, buffer, pos, f.Length);
                pos += f.Length;
            }

            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// Decodes a base64 TLV payload back into its fields. Useful for
        /// verification and debugging.
        /// </summary>
        public static IReadOnlyList<QrField> Decode(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                throw new ArgumentException("Base64 payload is required.", nameof(base64));

            byte[] buffer;
            try
            {
                buffer = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Not a valid base64 payload.", nameof(base64), ex);
            }

            var fields = new List<QrField>();
            int pos = 0;
            while (pos < buffer.Length)
            {
                if (pos + 2 > buffer.Length)
                    throw new ArgumentException("Truncated TLV field header.", nameof(base64));

                int tag = buffer[pos++];
                int length = buffer[pos++];

                if (pos + length > buffer.Length)
                    throw new ArgumentException("Truncated TLV field value.", nameof(base64));

                var value = new byte[length];
                Buffer.BlockCopy(buffer, pos, value, 0, length);
                pos += length;

                fields.Add(new QrField { Tag = tag, Value = value });
            }

            if (fields.Count == 0)
                throw new ArgumentException("Payload contains no TLV fields.", nameof(base64));

            return fields;
        }

        private static byte[] EncodeTextField(int tag, string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("QR field '" + name + "' (tag " + tag + ") is required.");

            // Length counts UTF-8 bytes, not chars (Arabic names are multi-byte).
            var bytes = Encoding.UTF8.GetBytes(value);
            return EncodeField(tag, bytes);
        }

        private static byte[] EncodeBinaryField(int tag, byte[] value)
        {
            if (value == null || value.Length == 0)
                throw new ArgumentException("QR binary field (tag " + tag + ") must not be empty.");
            return EncodeField(tag, value);
        }

        private static byte[] EncodeField(int tag, byte[] value)
        {
            if (value.Length > 255)
                throw new ArgumentException(
                    "QR field (tag " + tag + ") is " + value.Length +
                    " bytes; the TLV length is a single byte (max 255).");

            var field = new byte[2 + value.Length];
            field[0] = (byte)tag;
            field[1] = (byte)value.Length;
            Buffer.BlockCopy(value, 0, field, 2, value.Length);
            return field;
        }
    }
}
