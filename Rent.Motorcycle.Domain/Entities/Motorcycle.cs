using System;
using System.Text.RegularExpressions;
using Rent.Motorcycle.Domain.Abstractions;

namespace Rent.Motorcycle.Domain.Entities
{
    public sealed class Motorcycle : Entity
    {
        public int Year { get; private set; }
        public string Model { get; private set; } = default!;
        public string Plate { get; private set; } = default!;
        public bool HasRentals { get; private set; }

        private Motorcycle() { }

        public static Motorcycle Create(string id, int year, string model, string plate)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id is required.", nameof(id));

            var currentYear = DateTime.UtcNow.Year;
            if (year < 1900 || year > currentYear + 1)
                throw new ArgumentException("Invalid year.", nameof(year));

            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(plate))
                throw new ArgumentException("Plate is required.", nameof(plate));

            var normalizedPlate = NormalizePlate(plate);
            ValidatePlate(normalizedPlate);

            return new Motorcycle
            {
                Id = id.Trim(),
                Year = year,
                Model = model.Trim(),
                Plate = normalizedPlate,
                HasRentals = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public void Rename(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model is required.", nameof(model));

            Model = model.Trim();
            Touch();
        }

        public static string NormalizePlate(string plate)
              => new string((plate ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        public void ChangePlate(string newPlate)
        {
            if (string.IsNullOrWhiteSpace(newPlate))
                throw new ArgumentException("Plate is required.", nameof(newPlate));

            var normalized = NormalizePlate(newPlate);

            // Se não mudou, não mexe em Plate (mas pode sair sem atualizar UpdatedAt)
            if (Plate == normalized)
                return;

            Plate = normalized;
            UpdatedAt = DateTimeOffset.UtcNow;   // <- marca a atualização
        }

        public void MarkAsRented()
        {
            if (!HasRentals)
            {
                HasRentals = true;
                Touch();
            }
        }

        public void MarkAsNotRented()
        {
            if (HasRentals)
            {
                HasRentals = false;
                Touch();
            }
        }

        private static void ValidatePlate(string plate)
        {
            if (plate.Length != 7)
                throw new ArgumentException("Plate must have 7 characters after normalization.", nameof(plate));

            var mercosul = new Regex(@"^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$", RegexOptions.Compiled);
            if (!mercosul.IsMatch(plate))
                throw new ArgumentException("Plate format is invalid (Mercosul).", nameof(plate));
        }
    }
}
