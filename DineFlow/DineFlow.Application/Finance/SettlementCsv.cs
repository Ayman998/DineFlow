using System.Globalization;
using System.Text;
using DineFlow.Domain.Finance;

namespace DineFlow.Application.Finance;

/// <summary>One row of a settlement report, before it becomes part of a <see cref="Settlement"/>.</summary>
public sealed record SettlementRow(int Line, string BatchRef, DateOnly PayoutDate, DateOnly TransactionDate, SettlementLineKind Kind,
    string? OrderId, string? Reference, string? RefundId, long AmountMinor, long FeeMinor, string? Currency);

/// <summary>
/// Reads an acquirer settlement report (CSV). Jordanian acquirers send these as files, with their own
/// column names, so headers are matched by common names, case-insensitively:
/// batch (batch, batch id, payout id), payout date (payout date, settlement date, value date),
/// transaction date, type (type, transaction type), order id (order id, merchant order id),
/// reference, refund id, amount (amount, gross, gross amount), fee (fee, fees, commission, mdr)
/// and currency. Amounts are in major units, e.g. 12.500 for JOD; dates are yyyy-MM-dd or dd/MM/yyyy.
/// </summary>
public static class SettlementCsv
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["batch"] = ["batch", "batch id", "batch ref", "batch reference", "payout id", "payout reference", "settlement id"],
        ["payout"] = ["payout date", "settlement date", "value date", "paid on"],
        ["date"] = ["transaction date", "txn date", "date", "trans date"],
        ["type"] = ["type", "transaction type", "txn type"],
        ["order"] = ["order id", "merchant order id", "order", "order ref"],
        ["reference"] = ["reference", "merchant reference", "ref"],
        ["refund"] = ["refund id", "refund ref", "refund reference"],
        ["amount"] = ["amount", "gross", "gross amount", "transaction amount"],
        ["fee"] = ["fee", "fees", "commission", "mdr", "fee amount"],
        ["currency"] = ["currency", "ccy"]
    };

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "yyyyMMdd"];

    /// <summary>Parses the report. Throws <see cref="FormatException"/> naming the line and column of the first problem.</summary>
    public static IReadOnlyList<SettlementRow> Parse(string text, string currency)
    {
        var lines = SplitRows(text).Where(r => r.Fields.Any(f => f.Length > 0)).ToList();
        if (lines.Count < 2) throw new FormatException("The file has no rows under the header.");

        var header = lines[0].Fields.Select(h => h.Trim().ToLowerInvariant().Replace('_', ' ')).ToList();
        int? Column(string key) => Aliases[key].Select(a => header.IndexOf(a)).Where(i => i >= 0).Select(i => (int?)i).FirstOrDefault();
        int Required(string key) => Column(key) ?? throw new FormatException($"The file needs a \"{Aliases[key][0]}\" column.");

        var (batch, payout, date, amount) = (Required("batch"), Required("payout"), Column("date"), Required("amount"));
        var (type, order, reference, refund, fee, ccy) = (Column("type"), Column("order"), Column("reference"), Column("refund"), Column("fee"), Column("currency"));
        if (order is null && reference is null) throw new FormatException("The file needs an \"order id\" or \"reference\" column.");

        var digits = currency is "KWD" or "BHD" or "OMR" or "JOD" or "TND" or "LYD" or "IQD" ? 3 : 2;
        var rows = new List<SettlementRow>();
        foreach (var (number, fields) in lines.Skip(1))
        {
            string? Cell(int? i) => i is { } index && index < fields.Count && fields[index].Trim().Length > 0 ? fields[index].Trim() : null;
            string Need(int i, string name) => Cell(i) ?? throw new FormatException($"Line {number}: \"{name}\" is empty.");

            var payoutDate = ParseDate(Need(payout, "payout date"), number);
            var kind = ParseKind(Cell(type), number);
            var rowCurrency = Cell(ccy)?.ToUpperInvariant();
            if (rowCurrency is not null && rowCurrency != currency)
                throw new FormatException($"Line {number}: the currency is {rowCurrency}, but this venue uses {currency}.");

            rows.Add(new SettlementRow(number, Need(batch, "batch"), payoutDate, Cell(date) is { } d ? ParseDate(d, number) : payoutDate, kind,
                Cell(order), Cell(reference)?.ToUpperInvariant(), Cell(refund), ParseAmount(Need(amount, "amount"), digits, number),
                Cell(fee) is { } f ? ParseAmount(f, digits, number) : 0, rowCurrency));
        }
        return rows;
    }

    private static SettlementLineKind ParseKind(string? text, int line) => text?.Trim().ToUpperInvariant() switch
    {
        null or "PAYMENT" or "SALE" or "PURCHASE" or "CAPTURE" or "PAY" => SettlementLineKind.Payment,
        "REFUND" or "CREDIT" or "RETURN" => SettlementLineKind.Refund,
        "FEE" or "ADJUSTMENT" or "CHARGE" => SettlementLineKind.Adjustment,
        var other => throw new FormatException($"Line {line}: \"{other}\" isn't a type DineFlow knows (use payment, refund or fee).")
    };

    /// <summary>"12.500" → 12500 for a 3-decimal currency. Signs are ignored: the type says which way money goes.</summary>
    private static long ParseAmount(string text, int digits, int line)
    {
        var clean = text.Replace(",", "").Replace(" ", "").TrimStart('-', '+');
        if (!decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Line {line}: \"{text}\" isn't an amount.");
        var minor = value * (decimal)Math.Pow(10, digits);
        if (minor != decimal.Truncate(minor)) throw new FormatException($"Line {line}: \"{text}\" has more decimals than the currency allows.");
        return (long)minor;
    }

    private static DateOnly ParseDate(string text, int line)
    {
        var value = text.Trim();
        if (value.Length > 10 && value[10] is 'T' or ' ') value = value[..10]; // "2026-10-25T00:00:00" or "2026-10-25 00:00"
        return DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException($"Line {line}: \"{text}\" isn't a date (use yyyy-MM-dd).");
    }

    /// <summary>CSV rows with quoted fields ("a, b"), doubled quotes ("") and line numbers; commas or semicolons as separators.</summary>
    private static IEnumerable<(int Number, List<string> Fields)> SplitRows(string text)
    {
        var separator = text.Split('\n')[0].Count(c => c == ';') > text.Split('\n')[0].Count(c => c == ',') ? ';' : ',';
        var field = new StringBuilder();
        var fields = new List<string>();
        var (inQuotes, line, start) = (false, 1, 1);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else { if (c == '\n') line++; field.Append(c); }
            }
            else if (c == '"') inQuotes = true;
            else if (c == separator) { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                fields.Add(field.ToString().TrimEnd('\r'));
                yield return (start, fields);
                (fields, field, start) = ([], new StringBuilder(), ++line);
            }
            else field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString().TrimEnd('\r'));
            yield return (start, fields);
        }
    }
}
