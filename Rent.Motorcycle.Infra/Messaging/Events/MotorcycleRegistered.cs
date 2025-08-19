namespace Rent.Motorcycle.Infra.Messaging.Events;

public sealed record MotorcycleRegistered(
    string MotorcycleId,
    int Year,
    string Model,
    string Plate,
    DateTimeOffset OccurredAt);
