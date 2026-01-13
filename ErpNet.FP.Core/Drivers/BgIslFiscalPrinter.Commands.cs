namespace ErpNet.FP.Core.Drivers
{
    using System;
    using System.Globalization;
    using System.Text;

    public abstract partial class BgIslFiscalPrinter : BgFiscalPrinter
    {
        protected const byte
            CommandGetStatus = 0x4a,
            CommandGetDeviceInfo = 0x5a,
            CommandMoneyTransfer = 0x46,
            CommandOpenFiscalReceipt = 0x30,
            CommandCloseFiscalReceipt = 0x38,
            CommandAbortFiscalReceipt = 0x3c,
            CommandFiscalReceiptTotal = 0x35,
            CommandFiscalReceiptComment = 0x36,
            CommandFiscalReceiptSale = 0x31,
            CommandPrintDailyReport = 0x45,
            CommandGetDateTime = 0x3e,
            CommandSetDateTime = 0x3d,
            CommandGetReceiptStatus = 0x4c,
            CommandGetLastDocumentNumber = 0x71,
            CommandGetTaxIdentificationNumber = 0x63,
            CommandPrintLastReceiptDuplicate = 0x6D,
            CommandSubtotal = 0x33,
            CommandReadLastReceiptQRCodeData = 0x74,
            CommandGetInvoiceRange = 0x42,
            CommandOpenStornoReceipt = 0x2E,
            CommandToPinpad = 0x37;
            
            // 066_info_Get_InvoiceRange
            


        // Error for payment with pinpad when transaction may be successful in pinpad, but unsuccessful in fiscal device
        protected const string DatecsPinpadErrorUnfinishedTransaction = "-111560";
        // Pinpad commands – option ‘13’ - After error (-111560) by "CommandFiscalReceiptTotal" and do "Print pinpad receipt"
        protected const string DatecsFinalizePinpadTransactionAndPrintReceipt = "13\t1\t";
        // Pinpad commands – option ‘15’ - Print receipt for pinpad after successful transaction
        protected const string DatecsXPinpadPrintReceipt = "15\t";
        // Pinpad commands – option ‘5’ - End of day from pinpad
        protected const string DatecsXPinpadEndOfDay = "5\t";
        // Pinpad commands – option ‘6’ - Report from pinpad
        protected const string DatecsXPinpadReportFromPinpad = "6\t";

        protected const byte CommandPrintCustomerInformation = 0x39; // 57 dec

        // dotapp >>
        protected virtual char GetStornoTypeLetter(ReversalReason reason)
        {
            return reason switch
            {
                ReversalReason.OperatorError => 'E',
                ReversalReason.Refund => 'R',
                ReversalReason.TaxBaseReduction => 'T',
                _ => 'E'
            };
        }

        protected static string FormatStornoDateTime(DateTime dt) => dt.ToString("ddMMyyHHmmss", CultureInfo.InvariantCulture);



public virtual (string, DeviceStatus) OpenStornoInvoiceReceipt(
    StornoOptions storno,
    string uniqueSaleNumber,
    string operatorId,
    string operatorPassword,
    string? tillNumber)
{
    // =====================================================================
    // HARD-CODED TEST SWITCH (за да докажем, че 2E може да мине изобщо)
    // =====================================================================
    const bool USE_HARDCODED_TEST_PAYLOAD = false;
    if (USE_HARDCODED_TEST_PAYLOAD)
    {
        // Формат 2E:
        // Op,Pwd,Till/ , I<InvNum>// , <UNP>/ , <StType><DocNo>/ , <StUNP>,<StDT>,<StFMIN>/#Reason
        var hardcoded =
            "20,9999,1/," +
            "I0054//," +
            "DT511780-4444-0080001/," +
            "R0001000/," +
            "DT511780-4444-0080000,100126225003,02978964/#refund";

        System.Diagnostics.Debug.WriteLine($"[DOTAPP] 2E HARDCODED payload => {hardcoded}");
        Console.WriteLine($"[DOTAPP] 2E HARDCODED payload => {hardcoded}");

        return Request(CommandOpenStornoReceipt, hardcoded);
    }

    // =====================================================================
    // NORMAL (динамично) построяване по протокола
    // =====================================================================

    System.Diagnostics.Debug.WriteLine("[DOTAPP] OpenStornoInvoiceReceipt() ENTERED");
    Console.WriteLine("[DOTAPP] OpenStornoInvoiceReceipt() ENTERED");

    var op = string.IsNullOrEmpty(operatorId)
        ? Options.ValueOrDefault("Administrator.ID", "20")
        : operatorId;

    var pwd = string.IsNullOrEmpty(operatorPassword)
        ? Options.ValueOrDefault("Administrator.Password", "9999").WithMaxLength(Info.OperatorPasswordMaxLength)
        : operatorPassword.WithMaxLength(Info.OperatorPasswordMaxLength);

    var till = string.IsNullOrWhiteSpace(tillNumber)
        ? Options.ValueOrDefault("Till.Number", "1")
        : tillNumber.Trim();

    // DocNo is REQUIRED (2E format has <StType><DocNo>)
    if (string.IsNullOrWhiteSpace(storno.OriginalDocNo))
    {
        var st = new DeviceStatus();
        st.AddError("E403", "OriginalDocNo is required for storno invoice (2E).");
        return ("", st);
    }

    // 1) Invoice number (InvNum) – по протокол е 4 цифри.
    // От твоето: "0000000054" -> "0054"
    string NormalizeInvNum(string? inv)
    {
        var s = (inv ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "";

        // Вземи само цифрите (ако има други символи)
        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
            return "";

        // ПО ПРОТОКОЛ: 4 цифри → взимаме последните 4
        if (digits.Length > 4)
            digits = digits.Substring(digits.Length - 4);

        return digits.PadLeft(4, '0');
    }

    var invNum = NormalizeInvNum(storno.OriginalInvoiceNumber);

    // 2) UNP на новия сторно документ (протокол: <UNP>)
    // В твоя API това е receipt.UniqueSaleNumber / uniqueSaleNumber.
    var unpNew = (uniqueSaleNumber ?? "").Trim();

    // 3) Storno type letter: E/R/T
    var stType = GetStornoTypeLetter(storno.Reason); // E/R/T

    // 4) DocNo (глобален документ, който сторнираш) – протокол казва до 11 символа.
    string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    var docNo = Cut(storno.OriginalDocNo.Trim(), 11);

    // 5) Original date/time: DDMMYYhhmmss
    var stDT = storno.OriginalDateTime.HasValue
        ? storno.OriginalDateTime.Value.ToString("ddMMyyHHmmss", CultureInfo.InvariantCulture)
        : "";

    // 6) Optional block StUNP, StDT, StFMIN се подава като 3 последователни параметъра
    var hasOrigTriple =
        !string.IsNullOrWhiteSpace(storno.OriginalUNP) &&
        !string.IsNullOrWhiteSpace(stDT) &&
        !string.IsNullOrWhiteSpace(storno.OriginalFMNumber);

    var sb = new StringBuilder();

    // <OpNum>,<Password>,<TillNum>
    sb.Append(op).Append(',')
      .Append(pwd).Append(',')
      .Append(till);

    // "/,"  (важно: точно така)
    sb.Append("/,");

    // <Invoice><InvNum>//  (Invoice='I' само ако имаме invNum)
    if (!string.IsNullOrWhiteSpace(invNum))
        sb.Append('I').Append(invNum);

    // Важно: винаги има "//" след invoice-part-а
    sb.Append("//,");

    // <UNP>  (ако е празен – пак ще има празен параметър, но структурата остава валидна)
    if (!string.IsNullOrWhiteSpace(unpNew))
        sb.Append(unpNew);

    // "/,"  (край на UNP секцията)
    sb.Append("/,");

    // <StType><DocNo>/   (важно: DocNo завършва със '/')
    sb.Append(stType).Append(docNo).Append('/');

    // [,<StUNP>,<StDT>,<StFMIN>]
    if (hasOrigTriple)
    {
        sb.Append(',')
          .Append(storno.OriginalUNP!.Trim())
          .Append(',')
          .Append(stDT)
          .Append(',')
          .Append(storno.OriginalFMNumber!.Trim());
    }

    // [/#Reason] или [#Reason] – по-стабилно е "/#"
    if (!string.IsNullOrWhiteSpace(storno.ReasonText))
    {
        sb.Append("/#")
          .Append(storno.ReasonText.WithMaxLength(30));
    }

    var payload = sb.ToString();

    // Debug
    System.Diagnostics.Debug.WriteLine($"[DOTAPP] 2E payload => {payload}");
    Console.WriteLine($"[DOTAPP] 2E payload => {payload}");

    // Ако искаш и HEX:
    // var bytes = Encoding.ASCII.GetBytes(payload);
    // System.Diagnostics.Debug.WriteLine("[DOTAPP] 2E payload bytes (ASCII HEX) => " + BitConverter.ToString(bytes));

    return Request(CommandOpenStornoReceipt, payload);
}



// public virtual (string, DeviceStatus) OpenStornoInvoiceReceipt(
//     StornoOptions storno,
//     string uniqueSaleNumber,
//     string operatorId,
//     string operatorPassword,
//     string? tillNumber)
// {
//     Console.WriteLine("[DOTAPP] OpenStornoInvoiceReceipt() ENTERED");

//     var op = string.IsNullOrEmpty(operatorId)
//         ? Options.ValueOrDefault("Administrator.ID", "20")
//         : operatorId;

//     var pwd = string.IsNullOrEmpty(operatorPassword)
//         ? Options.ValueOrDefault("Administrator.Password", "9999").WithMaxLength(Info.OperatorPasswordMaxLength)
//         : operatorPassword;

//     var till = string.IsNullOrWhiteSpace(tillNumber)
//         ? Options.ValueOrDefault("Till.Number", "1")
//         : tillNumber;

//     // По 2E: DocNo е задължителен (<StType><DocNo>)
//     if (string.IsNullOrWhiteSpace(storno.OriginalDocNo))
//     {
//         var st = new DeviceStatus();
//         st.AddError("E403", "OriginalDocNo is required for storno (2E).");
//         return ("", st);
//     }

//     var invNum = (storno.OriginalInvoiceNumber ?? "").Trim(); // InvNum (invoice number)
//     var docNo = storno.OriginalDocNo.Trim();                  // DocNo (global doc no)

//     var stType = GetStornoTypeLetter(storno.Reason); // E/R/T

//     // 2E изисква: DDMMYYhhmmss
//     var stDT = storno.OriginalDateTime.HasValue
//         ? storno.OriginalDateTime.Value.ToString("ddMMyyHHmmss", CultureInfo.InvariantCulture)
//         : "";

//     var sb = new StringBuilder();

//     // <OpNum>,<Password>,<TillNum>/
//     sb.Append(op).Append(',')
//       .Append(pwd).Append(',')
//       .Append(till)
//       .Append('/'); // <-- ЗАДЪЛЖИТЕЛНО по протокол

//     // [,<Invoice><InvNum>/]  (Invoice='I', InvNum=номер на фактурата)
//     // Забележка: секцията започва директно с "I", НЕ с запетая
//     if (!string.IsNullOrWhiteSpace(invNum))
//         sb.Append('I').Append(invNum).Append('/');

//     // [,<UNP>/]  (нов UNP на сторното)
//     if (!string.IsNullOrWhiteSpace(uniqueSaleNumber))
//         sb.Append(',').Append(uniqueSaleNumber).Append('/');

//     // ,<StType><DocNo>
//     sb.Append(',').Append(stType).Append(docNo);

//     // [,<StUNP>,<StDT>,<StFMIN>]
//     if (!string.IsNullOrWhiteSpace(storno.OriginalUNP)
//         && !string.IsNullOrWhiteSpace(stDT)
//         && !string.IsNullOrWhiteSpace(storno.OriginalFMNumber))
//     {
//         sb.Append(',').Append(storno.OriginalUNP)
//           .Append(',').Append(stDT)
//           .Append(',').Append(storno.OriginalFMNumber);
//     }

//     // /[#<StornoReason>]
//     sb.Append('/'); // <-- ЗАДЪЛЖИТЕЛЕН разделител към секцията за причина
//     if (!string.IsNullOrWhiteSpace(storno.ReasonText))
//         sb.Append('#').Append(storno.ReasonText.WithMaxLength(30));

//     // Лог
//     var payload = sb.ToString();
//     Console.WriteLine($"[DOTAPP] 2E payload => {payload}");

//     // Ако искаш 1:1 тест с “идеалния” пример, временно можеш да хардкоднеш payload тук:
//     // payload = "20,9999,1/I0000000054/,DT511780-4444-0080001/,R0001000,DT511780-4444-0080000,100126225003,02978964/#refund";

//     return Request(CommandOpenStornoReceipt, payload);
// }








        // dotapp <<

        public virtual (string, DeviceStatus) PrintCustomerInformation(ErpNet.FP.Core.ClientInfo info)
        {
            string Safe(string? s) => string.IsNullOrEmpty(s) ? " " : s;
            string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

            var hasVat = !string.IsNullOrWhiteSpace(info.TaxNo);

            // NAV логиката: _04 ако няма VAT, _07 ако има VAT
            // Тук подаваме под-команда като "4" / "7" (ако принтерът ви иска "04"/"07", ще го коригираме след тест)
            var subCode = hasVat ? "7" : "4";

            var eikType = Cut(CopyOrDefault(info.EIKType, "^"), 1);
            var eik = Cut(CopyOrDefault(info.EIK, "999999999"), 14);

            var seller = Cut(Safe(info.SellerName), 26);
            var receiver = Cut(Safe(info.ReceiverName), 26);
            var client = Cut(Safe(info.ClientName), 26);

            var sb = new System.Text.StringBuilder()
                .Append('\t').Append(subCode)
                .Append('\t').Append(eikType)
                .Append('\t').Append(eik)
                .Append('\t').Append(seller)
                .Append('\t').Append(receiver)
                .Append('\t').Append(client);

            if (hasVat)
            {
                var taxNo = Cut(Safe(info.TaxNo), 14);
                var addr1 = Cut(Safe(info.Address1), 28);
                var addr2 = Cut(Safe(info.Address2), 28);

                sb.Append('\t').Append(taxNo)
                .Append('\t').Append(addr1)
                .Append('\t').Append(addr2);
            }

            return Request(CommandPrintCustomerInformation, sb.ToString());

            static string CopyOrDefault(string? value, string def) => string.IsNullOrWhiteSpace(value) ? def : value!;
        }

        public virtual (string, DeviceStatus) OpenReceipt(ErpNet.FP.Core.Receipt receipt)
        {
            return OpenReceipt(receipt.UniqueSaleNumber, receipt.Operator, receipt.OperatorPassword);
        }

        public override string GetReversalReasonText(ReversalReason reversalReason)
        {
            return reversalReason switch
            {
                ReversalReason.OperatorError => "1",
                ReversalReason.Refund => "0",
                ReversalReason.TaxBaseReduction => "2",
                _ => "1",
            };
        }

        public virtual (string, DeviceStatus) GetStatus()
        {
            return Request(CommandGetStatus);
        }

        public virtual (string, DeviceStatus) GetTaxIdentificationNumber()
        {
            return Request(CommandGetTaxIdentificationNumber);
        }

        public virtual (string, DeviceStatus) GetLastDocumentNumber(string closeReceiptResponse)
        {
            return Request(CommandGetLastDocumentNumber);
        }
        public virtual (string, DeviceStatus) SubtotalChangeAmount(Decimal amount)
        {
            return Request(CommandSubtotal, $"10;{amount.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        public virtual (decimal?, DeviceStatus) GetReceiptAmount()
        {
            decimal? receiptAmount = null;

            var (receiptStatusResponse, deviceStatus) = Request(CommandGetReceiptStatus, "T");
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading last receipt status");
                return (null, deviceStatus);
            }

            var fields = receiptStatusResponse.Split(',');
            if (fields.Length < 3)
            {
                deviceStatus.AddInfo($"Error occured while parsing last receipt status");
                deviceStatus.AddError("E409", "Wrong format of receipt status");
                return (null, deviceStatus);
            }

            try
            {
                var amountString = fields[2];
                if (amountString.Length > 0)
                {
                    switch (amountString[0])
                    {
                        case '+':
                            receiptAmount = decimal.Parse(amountString.Substring(1), CultureInfo.InvariantCulture) / 100m;
                            break;
                        case '-':
                            receiptAmount = -decimal.Parse(amountString.Substring(1), CultureInfo.InvariantCulture) / 100m;
                            break;
                        default:
                            if (amountString.Contains("."))
                            {
                                receiptAmount = decimal.Parse(amountString, CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                receiptAmount = decimal.Parse(amountString, CultureInfo.InvariantCulture) / 100m;
                            }
                            break;
                    }
                }

            }
            catch (Exception e)
            {
                deviceStatus = new DeviceStatus();
                deviceStatus.AddInfo($"Error occured while parsing the amount of last receipt status");
                deviceStatus.AddError("E409", e.Message);
                return (null, deviceStatus);
            }

            return (receiptAmount, deviceStatus);
        }


        public virtual (string, DeviceStatus) MoneyTransfer(decimal amount)
        {
            return Request(CommandMoneyTransfer, amount.ToString("F2", CultureInfo.InvariantCulture));
        }

        public virtual (string, DeviceStatus) SetDeviceDateTime(DateTime dateTime)
        {
            return Request(CommandSetDateTime, dateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture));
        }

        public virtual (string, DeviceStatus) GetFiscalMemorySerialNumber()
        {
            var (rawDeviceInfo, deviceStatus) = GetRawDeviceInfo();
            var fields = rawDeviceInfo.Split(',');
            if (fields != null && fields.Length > 0)
            {
                return (fields[^1], deviceStatus);
            }
            else
            {
                deviceStatus.AddInfo($"Error occured while reading device info");
                deviceStatus.AddError("E409", $"Wrong number of fields");
                return (string.Empty, deviceStatus);
            }
        }

        public virtual (System.DateTime?, DeviceStatus) GetDateTime()
        {
            var (dateTimeResponse, deviceStatus) = Request(CommandGetDateTime);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading current date and time");
                return (null, deviceStatus);
            }


            if (DateTime.TryParseExact(dateTimeResponse,
                "dd-MM-yy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime1))
            {
                return (dateTime1, deviceStatus);
            }
            else if (DateTime.TryParseExact(dateTimeResponse,
                "dd.MM.yy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime2))
            {
                return (dateTime2, deviceStatus);
            }
            else
            {
                deviceStatus.AddInfo($"Error occured while parsing current date and time");
                deviceStatus.AddError("E409", $"Wrong format of date and time");
                return (null, deviceStatus);
            }
        }

        public virtual (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword)
        {
            var header = string.Join(",",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "0000").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    uniqueSaleNumber
                });
            return Request(CommandOpenFiscalReceipt, header);
        }

        public virtual (string, DeviceStatus) OpenReversalReceipt(
            ReversalReason reason,
            string receiptNumber,
            System.DateTime receiptDateTime,
            string fiscalMemorySerialNumber,
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword)
        {
            // Protocol: {ClerkNum},{Password},{UnicSaleNum}[{Tab}{Refund}{Reason},{DocLink},{DocLinkDT}{Tab}{FiskMem}
            var headerData = new StringBuilder()
                .Append(
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Administrator.ID", "20")
                        :
                        operatorId
                )
                .Append(',')
                .Append(
                    String.IsNullOrEmpty(operatorPassword) ?
                        Options.ValueOrDefault("Administrator.Password", "9999").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword
                )
                .Append(',')
                .Append(uniqueSaleNumber)
                .Append('\t')
                .Append('R')
                .Append(GetReversalReasonText(reason))
                .Append(',')
                .Append(receiptNumber)
                .Append(',')
                .Append(receiptDateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(fiscalMemorySerialNumber);

            return Request(CommandOpenFiscalReceipt, headerData.ToString());
        }

        public virtual (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0,
            decimal priceModifierValue = 0,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int ItemCode = 999)
        {
            var itemData = new StringBuilder();
            if (department <= 0) {
                itemData
                    .Append(itemText.WithMaxLength(Info.ItemTextMaxLength))
                    .Append('\t').Append(GetTaxGroupText(taxGroup))
                    .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture));
            }
            else
            {
                itemData
                    .Append(itemText.WithMaxLength(Info.ItemTextMaxLength))
                    .Append('\t').Append(department).Append('\t')
                    .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture));
            }
            if (quantity != 0)
            {
                itemData
                    .Append('*')
                    .Append(quantity.ToString(CultureInfo.InvariantCulture));
            }
            if (priceModifierType != PriceModifierType.None)
            {
                itemData
                    .Append(
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.SurchargePercent
                        ? ',' : '$')
                    .Append((
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.DiscountAmount
                        ? -priceModifierValue : priceModifierValue).ToString("F2", CultureInfo.InvariantCulture));
            }
            return Request(CommandFiscalReceiptSale, itemData.ToString());
        }

        public virtual (string, DeviceStatus) AddComment(string text)
        {
            return Request(
                CommandFiscalReceiptComment,
                text.WithMaxLength(Info.CommentTextMaxLength)
            );
        }

        public virtual (string, DeviceStatus) CloseReceipt()
        {
            return Request(CommandCloseFiscalReceipt);
        }

        public virtual (string, DeviceStatus) AbortReceipt()
        {
            return Request(CommandAbortFiscalReceipt);
        }

        public virtual (string, DeviceStatus) FullPayment()
        {
            return Request(CommandFiscalReceiptTotal, "\t");
        }

        public virtual (string, DeviceStatus) AddPayment(decimal amount, PaymentType paymentType)
        {
            var paymentData = new StringBuilder()
                .Append('\t')
                .Append(GetPaymentTypeText(paymentType))
                .Append(amount.ToString("F2", CultureInfo.InvariantCulture));
            return Request(CommandFiscalReceiptTotal, paymentData.ToString());
        }

        public virtual (string, DeviceStatus) PrintDailyReport(bool zeroing = true)
        {
            if (zeroing)
            {
                return Request(CommandPrintDailyReport);
            }
            else
            {
                return Request(CommandPrintDailyReport, "2");
            }
        }

        public virtual (string, DeviceStatus) GetLastReceiptQRCodeData()
        {
            return Request(CommandReadLastReceiptQRCodeData);
        }

        public virtual (string, DeviceStatus) GetRawDeviceInfo()
        {
            return Request(CommandGetDeviceInfo, "1");
        }
    }
}
