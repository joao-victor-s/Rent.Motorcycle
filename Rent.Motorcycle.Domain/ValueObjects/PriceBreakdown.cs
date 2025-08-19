using System;

namespace Rent.Motorcycle.Domain.ValueObjects
{
    public sealed class PriceBreakdown
    {
        public int UsedDays { get; private set; }
        public int UnusedDays { get; private set; }
        public int ExtraDays { get; private set; }
        public decimal DailyPrice { get; private set; }
        public decimal BaseValue { get; private set; }
        public decimal Penalty { get; private set; }
        public decimal Extras { get; private set; }
        public decimal Total { get; private set; }

        private PriceBreakdown() { }

        private PriceBreakdown(
            int usedDays,
            int unusedDays,
            int extraDays,
            decimal dailyPrice,
            decimal baseValue,
            decimal penalty,
            decimal extras)
        {
            if (usedDays < 0) throw new ArgumentOutOfRangeException(nameof(usedDays));
            if (unusedDays < 0) throw new ArgumentOutOfRangeException(nameof(unusedDays));
            if (extraDays < 0) throw new ArgumentOutOfRangeException(nameof(extraDays));
            if (dailyPrice < 0) throw new ArgumentOutOfRangeException(nameof(dailyPrice));
            if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));
            if (penalty < 0) throw new ArgumentOutOfRangeException(nameof(penalty));
            if (extras < 0) throw new ArgumentOutOfRangeException(nameof(extras));

            UsedDays = usedDays;
            UnusedDays = unusedDays;
            ExtraDays = extraDays;
            DailyPrice = dailyPrice;
            BaseValue = baseValue;
            Penalty = penalty;
            Extras = extras;
            Total = baseValue + penalty + extras;
        }

        public static PriceBreakdown Create(
            int usedDays,
            int unusedDays,
            int extraDays,
            decimal dailyPrice,
            decimal baseValue,
            decimal penalty,
            decimal extras)
        {
            return new PriceBreakdown(
                usedDays,
                unusedDays,
                extraDays,
                dailyPrice,
                baseValue,
                penalty,
                extras
            );
        }
    }
}
