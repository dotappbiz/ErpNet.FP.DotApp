namespace ErpNet.FP.Core
{
    public class ClientInfo
    {
        public string? EIKType { get; set; }   // '#', '*', '^'
        public string? EIK { get; set; }       // Bulstat / ID
        public string? SellerName { get; set; }
        public string? ReceiverName { get; set; }
        public string? ClientName { get; set; }
        public string? TaxNo { get; set; }     // VAT number (BG...)
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
    }
}
