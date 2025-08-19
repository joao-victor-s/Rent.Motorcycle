using System.Text.Json.Serialization;

namespace Rent.Motorcycle.API;

public record MotoCreateDto(
    [property: JsonPropertyName("identificador")] string id,
    [property: JsonPropertyName("ano")] int year,
    [property: JsonPropertyName("modelo")] string model,
    [property: JsonPropertyName("placa")] string plate);

public record MotoVm(
    [property: JsonPropertyName("identificador")] string id,
    [property: JsonPropertyName("ano")] int year,
    [property: JsonPropertyName("modelo")] string model,
    [property: JsonPropertyName("placa")] string plate);

public record ChangePlateDto(
    [property: JsonPropertyName("placa")] string plate
    );
public record RiderCreateDto(
    [property: JsonPropertyName("identificador")] string id,
    [property: JsonPropertyName("nome")] string name,
    [property: JsonPropertyName("cnpj")] string cnpj,
    [property: JsonPropertyName("data_nascimento")] DateTimeOffset birthDate,
    [property: JsonPropertyName("numero_cnh")] string cnhNumber,
    [property: JsonPropertyName("tipo_cnh")] string cnhType,
    [property: JsonPropertyName("imagem_cnh")] string? cnhImage);

public record RiderVm(
    [property: JsonPropertyName("identificador")] string id,
    [property: JsonPropertyName("nome")] string name,
    [property: JsonPropertyName("cnpj")] string cnpj,
    [property: JsonPropertyName("data_nascimento")] DateTimeOffset birthDate,
    [property: JsonPropertyName("numero_cnh")] string cnhNumber,
    [property: JsonPropertyName("tipo_cnh")] string cnhType,
    [property: JsonPropertyName("imagem_cnh")] string? cnhImage);
public record CnhUploadBody(
    [property: JsonPropertyName("imagem_cnh")] string imagem_cnh
    );

public record MotorcycleVm(
    [property: JsonPropertyName("identificador")] string id,
    [property: JsonPropertyName("ano")] int year,
    [property: JsonPropertyName("modelo")] string model,
    [property: JsonPropertyName("placa")] string plate,
    [property: JsonPropertyName("possui_locacoes")] bool hasRentals);

public record MotorcycleIdVm(
    [property: JsonPropertyName("identificador")] string id);

public record RentalCreateDto(
    [property: JsonPropertyName("entregador_id")] string riderId,
    [property: JsonPropertyName("moto_id")] string motorcycleId,
    [property: JsonPropertyName("data_inicio")] DateTimeOffset startDate,
    [property: JsonPropertyName("data_termino")] DateTimeOffset endDate,
    [property: JsonPropertyName("data_previsao_termino")] DateTimeOffset expectedEndDate,
    [property: JsonPropertyName("plano")] int plan
    );

public record RentalVm(
    [property: JsonPropertyName("identificador")] string identifier,
    [property: JsonPropertyName("valor_diaria")] decimal dailyPrice,
    [property: JsonPropertyName("entregador_id")] string riderId,
    [property: JsonPropertyName("moto_id")] string motorcycleId,
    [property: JsonPropertyName("data_inicio")] DateTimeOffset startDate,
    [property: JsonPropertyName("data_termino")] DateTimeOffset termDate,
    [property: JsonPropertyName("data_previsao_termino")] DateTimeOffset expectedEndDate,
    [property: JsonPropertyName("data_devolucao")] DateTimeOffset returnDate
    );

public record PreviewVm(
    [property: JsonPropertyName("dias_usados")] int usedDays,
    [property: JsonPropertyName("dias_nao_usados")] int unusedDays,
    [property: JsonPropertyName("dias_extras")] int extraDays,
    [property: JsonPropertyName("preco_diaria")] decimal dailyPrice,
    [property: JsonPropertyName("valor_base")] decimal baseValue,
    [property: JsonPropertyName("multa")] decimal penalty,
    [property: JsonPropertyName("extras")] decimal extras,
    [property: JsonPropertyName("total")] decimal total);

public record ReturnDto(
    [property: JsonPropertyName("data_retorno")] DateTimeOffset returnDate);



public sealed record MessageDto(string mensagem);

