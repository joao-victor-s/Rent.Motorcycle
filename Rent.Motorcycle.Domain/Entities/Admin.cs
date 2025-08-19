using System;
using System.Collections.Generic;
using System.Linq;
using Rent.Motorcycle.Domain.Abstractions;

using MotorcycleEntity = Rent.Motorcycle.Domain.Entities.Motorcycle;

namespace Rent.Motorcycle.Domain.Entities
{
    public sealed class Admin : Entity
    {
        private readonly List<MotorcycleEntity> _motorcycles = new();

        private Admin()
        {
            Active = true;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public static Admin Create() => new Admin();

        public IReadOnlyCollection<MotorcycleEntity> Motorcycles => _motorcycles.AsReadOnly();

        private MotorcycleRegistered RegisterMotorcycle(string id, int year, string model, string plate)
        {
            var normalized = MotorcycleEntity.NormalizePlate(plate);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("Plate cannot be empty.", nameof(plate));

            if (_motorcycles.Any(m => MotorcycleEntity.NormalizePlate(m.Plate) == normalized))
                throw new InvalidOperationException("Plate already registered.");

            var moto = MotorcycleEntity.Create(id, year, model, normalized);
            _motorcycles.Add(moto);
            Touch();

            return new MotorcycleRegistered(year, model, normalized, DateTimeOffset.UtcNow);
        }

        public void RenameMotorcycle(string motorcycleId, string newModel)
        {
            if (string.IsNullOrWhiteSpace(newModel))
                throw new ArgumentException("Model cannot be empty.", nameof(newModel));

            var moto = FindMotorcycleOrThrow(motorcycleId);
            moto.Rename(newModel);
            Touch();
        }

        public void ChangeMotorcyclePlate(string motorcycleId, string newPlate)
        {
            var normalized = MotorcycleEntity.NormalizePlate(newPlate);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("Plate cannot be empty.", nameof(newPlate));

            if (_motorcycles.Any(m => MotorcycleEntity.NormalizePlate(m.Plate) == normalized && m.Id != motorcycleId))
                throw new InvalidOperationException("Plate already registered.");

            var moto = FindMotorcycleOrThrow(motorcycleId);
            moto.ChangePlate(normalized);
            Touch();
        }

        public void DeleteMotorcycle(string motorcycleId)
        {
            var motorcycle = _motorcycles.FirstOrDefault(m => m.Id == motorcycleId);
            if (motorcycle is null)
                throw new KeyNotFoundException($"Motorcycle {motorcycleId} not found.");

            if (motorcycle.HasRentals)
                throw new InvalidOperationException("Cannot delete a motorcycle with existing rentals.");

            _motorcycles.Remove(motorcycle);
            Touch();
        }

        private MotorcycleEntity FindMotorcycleOrThrow(string motorcycleId)
        {
            var moto = _motorcycles.FirstOrDefault(m => m.Id == motorcycleId);
            if (moto is null)
                throw new KeyNotFoundException($"Motorcycle {motorcycleId} not found.");
            return moto;
        }

        public sealed record MotorcycleRegistered(int Year, string Model, string Plate, DateTimeOffset OccurredAt);
    }
}
