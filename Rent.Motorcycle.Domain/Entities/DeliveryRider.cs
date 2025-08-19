using System;
using System.Collections.Generic;
using System.Linq;
using Rent.Motorcycle.Domain.Abstractions;
using Rent.Motorcycle.Domain.Enums;
using Rent.Motorcycle.Domain.ValueObjects;

namespace Rent.Motorcycle.Domain.Entities
{
    public sealed class DeliveryRider : Entity
    {
        public string CNPJ { get; private set; } = default!;
        public string Name { get; private set; } = default!;
        public DateTimeOffset BirthDate { get; private set; }
        public CNH Cnh { get; private set; } = default!;

        private readonly List<Rental> _rentals = new();
        public IReadOnlyCollection<Rental> Rentals => _rentals.AsReadOnly();

        private DeliveryRider() { }

        public static DeliveryRider Register(
            string id,
            string cnpj,
            string name,
            DateTimeOffset birthDate,
            CNH cnh,
            Func<string, bool> cnpjExists,
            Func<string, bool> cnhNumberExists,
            DateTimeOffset? createdAt = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id is required.", nameof(id));

            var normalizedCnpj = NormalizeCnpj(cnpj);
            if (string.IsNullOrWhiteSpace(normalizedCnpj)) throw new ArgumentException("CNPJ is required.", nameof(cnpj));
            if (string.IsNullOrWhiteSpace(name))           throw new ArgumentException("Name is required.", nameof(name));
            if (cnh is null)                                throw new ArgumentNullException(nameof(cnh));
            if (!IsMotorcycleEligible(cnh))                 throw new ArgumentException("CNH type not eligible for motorcycle.", nameof(cnh));

            if (cnpjExists?.Invoke(normalizedCnpj) == true)          throw new InvalidOperationException("CNPJ already in use.");
            if (cnhNumberExists?.Invoke(cnh.CnhNumber.Trim()) == true) throw new InvalidOperationException("CNH number already in use.");

            var rider = new DeliveryRider
            {
                Id        = id.Trim(),
                CNPJ      = normalizedCnpj,
                Name      = name.Trim(),
                BirthDate = birthDate,
                Cnh       = cnh,
                Active    = true,
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow
            };

            return rider;
        }

        public bool VerifyCNH(CNH cnh) => IsMotorcycleEligible(cnh);

        public void Rename(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
            Name = name.Trim();
            Touch();
        }

        public void UpdateCNH(CNH cnh)
        {
            if (cnh is null) throw new ArgumentNullException(nameof(cnh));
            if (!IsMotorcycleEligible(cnh)) throw new ArgumentException("CNH type not eligible for motorcycle.", nameof(cnh));
            Cnh = cnh;
            Touch();
        }

        public void UpdateCNHImage(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) throw new ArgumentException("Image url is required.", nameof(imageUrl));
            var lower = imageUrl.Trim().ToLowerInvariant();
            if (!(lower.EndsWith(".png") || lower.EndsWith(".bmp"))) throw new ArgumentException("Only PNG or BMP are allowed.", nameof(imageUrl));

            Cnh = CNH.Create(Cnh.Type, Cnh.CnhNumber, imageUrl.Trim());
            Touch();
        }

        public Rental StartRental(
            string idMotorcycle,
            DateTimeOffset startDate,
            DateTimeOffset endDate,
            DateTimeOffset expectedEndDate,
            RentalPlan plan)
        {
            if (!Active)
                throw new InvalidOperationException("Inactive rider cannot start rental.");

            if (!IsMotorcycleEligible(Cnh))
                throw new InvalidOperationException("Rider CNH not eligible for motorcycle.");

            if (_rentals.Any(r => r.Active))
                throw new InvalidOperationException("There is already an open rental.");

            var rental = Rental.Create(
                riderId:         this.Id,
                motorcycleId:    idMotorcycle,
                startDate:       startDate,
                endDate:         endDate,
                expectedEndDate: expectedEndDate,
                plan:            plan
            );

            _rentals.Add(rental);
            return rental;
        }

        public decimal PreviewRental(DateTimeOffset returnDate)
        {
            var open = _rentals.LastOrDefault(r => r.Active);
            if (open is null)
                throw new InvalidOperationException("No open rental to preview.");

            if (returnDate < open.StartDate)
                throw new ArgumentException("Return date cannot be before rental start date.", nameof(returnDate));

            return open.CalculatePreview(returnDate).Total;
        }

        private static bool IsMotorcycleEligible(CNH cnh)
            => cnh.Type is CNHType.A or CNHType.APlusB;

        private static string NormalizeCnpj(string cnpj)
            => new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
    }
}
