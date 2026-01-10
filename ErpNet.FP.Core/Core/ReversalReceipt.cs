namespace ErpNet.FP.Core
{
    using System.Linq;
    using System.Runtime.Serialization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    /// Reversal Reason
    /// </summary>
    public enum ReversalReason
    {
        [EnumMember(Value = "operator-error")]
        OperatorError = 1,
        [EnumMember(Value = "refund")]
        Refund = 2,
        [EnumMember(Value = "tax-base-reduction")]
        TaxBaseReduction = 3,
        [EnumMember(Value = "taxbase-reduction")]
        OldCompatibleTaxBaseReduction = 3   // must be same as TaxBaseReduction
    }

    /// <summary>
    /// Represents one Receipt, which can be printed on a fiscal printer.
    /// </summary>
    public class ReversalReceipt : Receipt
    {
        public string ReceiptNumber { get; set; } = string.Empty;
        public System.DateTime ReceiptDateTime { get; set; }
        public string FiscalMemorySerialNumber { get; set; } = string.Empty;

        // Invoice-specific properties for reversal
        public string InvoiceNumber { get; set; } = string.Empty;
        public string InvoiceUniqueSaleNumber { get; set; } = string.Empty;
        public System.DateTime? InvoiceDateTime { get; set; }
        public string InvoiceFiscalMemorySerialNumber { get; set; } = string.Empty;

        [JsonConverter(typeof(StringEnumConverter))]
        public ReversalReason Reason { get; set; } = ReversalReason.OperatorError;

        public ReversalReceipt CloneReceipt(Receipt receipt)
        {
            if (receipt.Items != null)
            {
                Items = receipt.Items.ToList();
            }
            if (receipt.Payments != null)
            {
                Payments = receipt.Payments.ToList();
            }
            UniqueSaleNumber = receipt.UniqueSaleNumber;
            return this;
        }
    }
}