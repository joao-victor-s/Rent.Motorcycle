using System;
using Rent.Motorcycle.Domain.Enums;

namespace Rent.Motorcycle.Domain.ValueObjects;

public sealed class CNH
{
    public CNHType Type { get; private set; }
    public string CnhNumber { get; private set; } = default!;
    public string? CnhImageUrl { get; private set; }

    private CNH() {}

    public static CNH Create(CNHType type, string cnhNumber, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(cnhNumber))
            throw new ArgumentException("CNH number is required.", nameof(cnhNumber));

        return new CNH
        {
            Type = type,
            CnhNumber = cnhNumber.Trim(),
            CnhImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim()
        };
    }
}

