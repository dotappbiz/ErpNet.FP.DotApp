using System;
using ErpNet.FP.Core;

public class ReceiptOptions
{
    public bool IsInvoice { get; set; }
    public string? TillNumber { get; set; }
    public string? Unp { get; set; }

    public StornoOptions? Storno { get; set; } // NEW
}

public class StornoOptions
{
    // E / R / T (Operator error / Refund / Tax base reduction)
    public ReversalReason Reason { get; set; } // или string/enum отделно, виж т.2

    // За фактура (кредитно) – оригинална фактура
    public string? OriginalInvoiceNumber { get; set; }   // InvNum

    // За връзка към оригинален документ (ако го имате)
    public string? OriginalDocNo { get; set; }           // DocNo
    public string? OriginalUNP { get; set; }             // StUNP
    public DateTime? OriginalDateTime { get; set; }      // StDT
    public string? OriginalFMNumber { get; set; }        // StFMIN

    public string? ReasonText { get; set; }              // “ВРЪЩАНЕ!” / “refund”
}
