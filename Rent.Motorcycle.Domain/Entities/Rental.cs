using System;
using Rent.Motorcycle.Domain.Enums;
using Rent.Motorcycle.Domain.ValueObjects;

namespace Rent.Motorcycle.Domain.Entities
{
    public sealed class Rental
    {
        public int Id { get; private set; } = default!;
        public string IdMotorcycle { get; private set; } = default!;
        public string IdDeliveryRider { get; private set; } = default!;
        public DateTimeOffset ExpectedEndDate { get; private set; }
        public DateTimeOffset StartDate { get; private set; }
        public DateTimeOffset EndDate { get; private set; }
        public RentalPlan Plan { get; private set; }
        public decimal Total { get; private set; }
        public decimal LateExtraDailyFee { get; private set; } = 50m;
        public bool Active { get; private set; }
        public string Identifier => $"locacao{Id}";
        public decimal DailyPrice => GetDailyPrice(Plan);
        public DateTimeOffset ReturnDate => EndDate;
        private Rental() { }

        public static Rental Create(
            string riderId,
            string motorcycleId,
            DateTimeOffset startDate,
            DateTimeOffset endDate,
            DateTimeOffset expectedEndDate,
            RentalPlan plan)
        {
            if (string.IsNullOrWhiteSpace(riderId))
                throw new ArgumentException("Rider Id is necessary.");
            if (string.IsNullOrWhiteSpace(motorcycleId))
                throw new ArgumentException("Motorcycle Id is necessary.");
            if (endDate < startDate)
                throw new ArgumentException("End date is invalid.");
            if (expectedEndDate < startDate)
                throw new ArgumentException("Expected end date is invalid.");

            var rental = new Rental
            {
                IdDeliveryRider = riderId,
                IdMotorcycle = motorcycleId,
                StartDate = startDate,
                EndDate = endDate,
                ExpectedEndDate = expectedEndDate,
                Plan = plan,
                Total = 0m,
                Active = true
            };
            var pb   = rental.CalculatePreview(expectedEndDate);
            rental.Total  = pb.Total;

            return rental;
        }

        public PriceBreakdown CalculatePreview(DateTimeOffset returnInstant)
        {

            static DateTimeOffset DayStartUtc(DateTimeOffset dto)
                => new DateTimeOffset(dto.UtcDateTime.Date, TimeSpan.Zero);

            var start       = DayStartUtc(StartDate);
            var expectedEnd = DayStartUtc(ExpectedEndDate);
            var ret         = DayStartUtc(returnInstant);

            if (ret < start)
                throw new ArgumentOutOfRangeException(nameof(returnInstant), "Return date must be >= start date.");

            var planDays          = GetTotalDays(Plan);
            var usedDaysInclusive = (int)(ret - start).TotalDays + 1;

            var dailyPrice     = GetDailyPrice(Plan);
            var usedWithinPlan = Math.Min(usedDaysInclusive, planDays);
            var baseValue      = usedWithinPlan * dailyPrice;

            var unusedDays = 0;
            var penalty    = 0m;
            if (ret < expectedEnd)
            {
                unusedDays = planDays - usedWithinPlan;
                var penaltyRate = GetEarlyReturnPenaltyRate(Plan);
                penalty = unusedDays > 0 ? unusedDays * dailyPrice * penaltyRate : 0m;
            }

            var extraDays = 0;
            var extras    = 0m;
            if (ret > expectedEnd)
            {
                extraDays = (int)(ret - expectedEnd).TotalDays;
                extras = extraDays * LateExtraDailyFee;
            }

            return PriceBreakdown.Create(
                usedDays:   usedDaysInclusive,
                unusedDays: unusedDays,
                extraDays:  extraDays,
                dailyPrice: dailyPrice,
                baseValue:  baseValue,
                penalty:    penalty,
                extras:     extras
            );
        }

        public PriceBreakdown InformReturn(DateTimeOffset returnInstant)
        {
            if (!Active)
                throw new InvalidOperationException("Rental is already closed.");

            var pb = CalculatePreview(returnInstant);
            EndDate = returnInstant;
            Total   = pb.Total;
            Active  = false;
            return pb;
        }

        public static int GetTotalDays(RentalPlan plan) => (int)plan;

        public static decimal GetDailyPrice(RentalPlan plan) => plan switch
        {
            RentalPlan.Days7  => 30m,
            RentalPlan.Days15 => 28m,
            RentalPlan.Days30 => 22m,
            RentalPlan.Days45 => 20m,
            RentalPlan.Days50 => 18m,
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };

        public static decimal GetEarlyReturnPenaltyRate(RentalPlan plan) => plan switch
        {
            RentalPlan.Days7  => 0.20m,
            RentalPlan.Days15 => 0.40m,
            _ => 0m
        };
    }
}
