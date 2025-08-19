using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Rent.Motorcycle.Domain.Entities;
using Rent.Motorcycle.Domain.Enums;
using Rent.Motorcycle.Domain.ValueObjects;
using Rent.Motorcycle.Infra;
using Rent.Motorcycle.Infra.Data;
using Rent.Motorcycle.Infra.Storage;
using Rent.Motorcycle.API;

using Rent.Motorcycle.Infra.Messaging;
using Rent.Motorcycle.Infra.Messaging.Events;
using Microsoft.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfra(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Sistema de Manutenção de Motos",
        Version = "v1"
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RentDbContext>();
    var retries = 0;
    const int maxRetries = 30;

    while (true)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            break;
        }
        catch (Exception ex) when (retries < maxRetries)
        {
            retries++;
            Console.WriteLine($"[EF EnsureCreated] Retries {retries}/{maxRetries}: {ex.Message}...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sistema de Manutenção de Motos v1");
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    c.RoutePrefix = string.Empty;
    c.DefaultModelsExpandDepth(-1);
});

var motorcyle = app.MapGroup("/motos").WithTags("motos");

// POST /motos
motorcyle.MapPost("", async Task<IResult> (
    MotoCreateDto dto, RentDbContext db, IEventBus bus, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.id) || string.IsNullOrWhiteSpace(dto.model) || string.IsNullOrWhiteSpace(dto.plate))
        return Results.BadRequest(new MessageDto("Dados inválidos"));

    var normalizedPlate = Motorcycle.NormalizePlate(dto.plate);

    var existsPlate = await db.Motorcycles.AnyAsync(m => m.Plate == normalizedPlate, ct);
    if (existsPlate) return TypedResults.Conflict();

    try
    {
        var moto = Motorcycle.Create(dto.id, dto.year, dto.model, normalizedPlate);
        db.Motorcycles.Add(moto);
        await db.SaveChangesAsync(ct);

        var evt = new MotorcycleRegistered(moto.Id, moto.Year, moto.Model, moto.Plate, DateTimeOffset.UtcNow);
        await bus.PublishAsync("motorcycle.registered", evt, ct);

        return TypedResults.Created($"/motos/{moto.Id}",
            new MotoVm(moto.Id, moto.Year, moto.Model, moto.Plate));
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new MessageDto( "Dados inválidos"));
    }

})
.WithSummary("Cadastrar uma nova moto")
.WithOpenApi(op =>
{
    op.Responses["201"] = new OpenApiResponse
    {
        Description = "Moto cadastrada",
    };

    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };
    return op;
})
.Produces<RiderVm>(StatusCodes.Status201Created, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");


// GET /motos
motorcyle.MapGet("", async (string? plate, RentDbContext db, CancellationToken ct) =>
{
    var q = db.Motorcycles.AsQueryable();
    if (!string.IsNullOrWhiteSpace(plate))
        q = q.Where(m => m.Plate == Motorcycle.NormalizePlate(plate));

    var list = await q.Select(m => new MotoVm(m.Id, m.Year, m.Model, m.Plate)).ToListAsync(ct);
    return Results.Ok(list);
})
.WithSummary("Consultar motos existentes")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Lista de motos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["id"]    = new OpenApiString("moto123"),
                    ["year"]  = new OpenApiInteger(2020),
                    ["model"] = new OpenApiString("Mottu Sport"),
                    ["plate"] = new OpenApiString("CDX-0101")
                }
            }
        }
    };

    return op;
})
.Produces<IEnumerable<MotoVm>>(StatusCodes.Status200OK);


// PUT /motos/{id}/placa
motorcyle.MapPut("/{id}/placa", async Task<IResult> (
    string id,
    ChangePlateDto dto,
    RentDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.plate))
        return Results.BadRequest(new MessageDto("Dados inválidos"));

    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    if (m is null)
        return Results.NotFound(new MessageDto("Moto não encontrada"));

    var normalized = Motorcycle.NormalizePlate(dto.plate);
    var plateInUse = await db.Motorcycles.AnyAsync(x => x.Plate == normalized && x.Id != id, ct);
    if (plateInUse)
        return Results.Conflict();

    try
    {
        m.ChangePlate(dto.plate);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MessageDto("Placa modificada com sucesso"));
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new MessageDto("Dados inválidos"));
    }
})
.WithSummary("Modificar a placa de uma moto")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Placa modificada com sucesso",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Placa modificada com sucesso")
                }
            }
        }
    };

    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };

    return op;
})
.Produces<MessageDto>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");

// GET /motos/{id}
motorcyle.MapGet("/{id}", async Task<IResult> (string id, RentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new MessageDto("Request mal formada"));

    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    if (m is null)
        return Results.NotFound(new MessageDto("Moto não encontrada"));

    return Results.Ok(new MotoVm(m.Id, m.Year, m.Model, m.Plate));
})
.WithSummary("Consultar motos existentes por id")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Detalhes da moto",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["id"]    = new OpenApiString("moto123"),
                    ["year"]  = new OpenApiInteger(2020),
                    ["model"] = new OpenApiString("Mottu Sport"),
                    ["plate"] = new OpenApiString("CDX-0101")
                }
            }
        }
    };

    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Request mal formada",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Request mal formada")
                }
            }
        }
    };

    op.Responses["404"] = new OpenApiResponse
    {
        Description = "Moto não encontrada",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Moto não encontrada")
                }
            }
        }
    };

    return op;
})
.Produces<MotoVm>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json")
.Produces<MessageDto>(StatusCodes.Status404NotFound, "application/json");

// DELETE /motos/{id}
motorcyle.MapDelete("/{id}", async Task<IResult> (
    string id, RentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new MessageDto("Id inválido"));

    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    if (m is null)
        return Results.BadRequest(new MessageDto("Id não encontrado"));

    var hasRentals = await db.Rentals.AnyAsync(r => r.IdMotorcycle == id, ct);
    if (hasRentals)
        return Results.BadRequest(new MessageDto("Moto com locação ativa"));

    db.Motorcycles.Remove(m);

    try
    {
        await db.SaveChangesAsync(ct);
        return Results.Ok(new MessageDto("Moto removida com sucesso"));
    }
    catch (DbUpdateException)
    {
        return Results.BadRequest(new MessageDto("Dados inválidos"));
    }
})
.WithSummary("Remover uma moto")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Moto removida com sucesso",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Moto removida com sucesso")
                }
            }
        }
    };
    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };

    return op;
})
.Produces<MessageDto>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");


var riders = app.MapGroup("/entregadores").WithTags("entregadores");

// POST /entregadores
riders.MapPost("", async Task<IResult> (RiderCreateDto dto, RentDbContext db, CancellationToken ct) =>
{
    try
    {
        var cnh = CNH.Create(ParseCnhType(dto.cnhType), dto.cnhNumber, dto.cnhImage);
        var normalizedCnpj = NormalizeCnpj(dto.cnpj);

        var rider = DeliveryRider.Register(
            dto.id,
            normalizedCnpj,
            dto.name,
            dto.birthDate,
            cnh,
            cnpjToCheck => db.DeliveryRiders.Any(r => r.CNPJ == cnpjToCheck),
            cnhToCheck  => db.DeliveryRiders.Any(r => r.Cnh.CnhNumber == cnhToCheck),
            DateTimeOffset.UtcNow
        );

        db.DeliveryRiders.Add(rider);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/entregadores/{rider.Id}",
            new RiderVm(
                rider.Id,
                rider.Name,
                rider.CNPJ,
                rider.BirthDate,
                rider.Cnh.CnhNumber,
                rider.Cnh.Type.ToString().Replace("APlusB", "A+B"),
                rider.Cnh.CnhImageUrl
            ));
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new MessageDto("Dados inválidos"));
    }
})
.WithSummary("Cadastrar entregador")
.WithOpenApi(op =>
{
    op.Responses.TryAdd("201", new OpenApiResponse { Description = "Entregador criado" });
    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };
    return op;
})
.Produces<RiderVm>(StatusCodes.Status201Created, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");

// POST /entregadores/{id}/cnh
riders.MapPost("/{id}/cnh", async Task<IResult> (
    string id,
    CnhUploadBody body,
    IStorageService storage,
    RentDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.imagem_cnh))
        return TypedResults.BadRequest(new MessageDto("Campo 'imagem_cnh' é obrigatório."));

    var raw = body.imagem_cnh.Trim();
    var commaIdx = raw.IndexOf(',');
    var payload = commaIdx >= 0 ? raw[(commaIdx + 1)..] : raw;

    byte[] bytes;
    try { bytes = Convert.FromBase64String(payload); }
    catch { return TypedResults.BadRequest(new MessageDto("Campo 'imagem_cnh' não é um base64 válido.")); }

    var rider = await db.DeliveryRiders.FindAsync(new object[] { id }, ct);
    if (rider is null)
        return TypedResults.NotFound(new MessageDto("Entregador não encontrado."));

    await using var ms = new MemoryStream(bytes);
    var fileName = $"cnh_{id}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png"; // padrão .png
    var path = await storage.SaveAsync(ms, fileName, ct);

    rider.UpdateCNHImage(path);

    db.DeliveryRiders.Update(rider);

    await db.SaveChangesAsync(ct);

    return TypedResults.Ok(new MessageDto("Imagem da CNH atualizada com sucesso."));
})
.WithSummary("Enviar foto da CNH")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Upload realizado com sucesso",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject { ["mensagem"] = new OpenApiString("Imagem da CNH atualizada com sucesso.") }
            }
        }
    };

    op.Responses["404"] = new OpenApiResponse
    {
        Description = "Entregador não encontrado",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject { ["mensagem"] = new OpenApiString("Entregador não encontrado.") }
            }
        }
    };

    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject { ["mensagem"] = new OpenApiString("Campo 'imagem_cnh' é obrigatório.") }
            }
        }
    };

    return op;
})
.Produces<MessageDto>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status404NotFound, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");


var rentals = app.MapGroup("/locação").WithTags("locação");

// POST /locacao
rentals.MapPost("", async Task<IResult> (
    RentalCreateDto dto, RentDbContext db, CancellationToken ct) =>
{
    var rider = await db.DeliveryRiders.FindAsync(new object[] { dto.riderId }, ct);
    if (rider is null)
        return TypedResults.BadRequest(new MessageDto("Entregador inexistente."));

    var moto = await db.Motorcycles.FindAsync(new object[] { dto.motorcycleId }, ct);
    if (moto is null)
        return TypedResults.BadRequest(new MessageDto("Moto inexistente."));

    try
    {
        var plan = ParsePlan(dto.plan);

        var rental = Rental.Create(
            riderId: dto.riderId,
            motorcycleId: dto.motorcycleId,
            startDate: dto.startDate,
            endDate: dto.endDate,
            expectedEndDate: dto.expectedEndDate,
            plan: plan
        );

        db.Rentals.Add(rental);
        await db.SaveChangesAsync(ct);

        var vm = new RentalCreateDto(
            riderId: rental.IdDeliveryRider,
            motorcycleId: rental.IdMotorcycle,
            startDate: rental.StartDate,
            endDate: rental.EndDate,
            expectedEndDate: rental.ExpectedEndDate,
            plan: (int)rental.Plan
        );

        return TypedResults.Created($"/locacao/{rental.Id}", vm);
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new MessageDto("Dados inválidos"));
    }
})
.WithSummary("Alugar uma moto")
.WithOpenApi(op =>
{
    op.Responses.TryAdd("201", new OpenApiResponse { Description = "Moto cadastrada" });
    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };
    return op;
})
.Produces<RiderVm>(StatusCodes.Status201Created, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");


// GET /locacao/{id}
rentals.MapGet("/{id}", async (string identifier, RentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(identifier))
        return Results.BadRequest(new MessageDto("Parâmetro 'Id' é obrigatório."));

    int idValue;
    if (identifier.StartsWith("locacao", StringComparison.OrdinalIgnoreCase))
    {
        var numericPart = identifier["locacao".Length..].Trim();
        if (!int.TryParse(numericPart, out idValue))
            return Results.BadRequest(new MessageDto("Identifier inválido. Use 'locacao{Id}', por exemplo: 'locacao1'."));
    }
    else
    {
        if (!int.TryParse(identifier, out idValue))
            return Results.BadRequest(new MessageDto("Identifier inválido. Use 'locacao{Id}', por exemplo: 'locacao1'."));
    }

    var r = await db.Rentals.FindAsync(new object[] { idValue }, ct);
    if (r is null)
        return Results.NotFound(new MessageDto("Locação não encontrada"));

    var vm = new RentalVm(
        identifier: r.Identifier,
        dailyPrice: r.DailyPrice,
        riderId: r.IdDeliveryRider,
        motorcycleId: r.IdMotorcycle,
        startDate: r.StartDate,
        termDate: r.ExpectedEndDate,
        expectedEndDate: r.ExpectedEndDate,
        returnDate: r.ReturnDate
    );

    return Results.Ok(vm);
})
.WithSummary("Consultar locação por id")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Detalhes da locação",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["identificador"]      = new OpenApiString("locacao1"),
                    ["valor_diaria"]      = new OpenApiDouble(10),
                    ["entregador_id"]         = new OpenApiString("entregador123"),
                    ["moto_id"]    = new OpenApiString("moto123"),
                    ["data_inicio"]       = new OpenApiString("2024-01-01T00:00:00Z"),
                    ["data_termino"]        = new OpenApiString("2024-01-07T23:59:59Z"),
                    ["data_previsao_termino"] = new OpenApiString("2024-01-07T23:59:59Z"),
                    ["data_devolucao"]      = new OpenApiString("2024-01-07T18:00:00Z")
                }
            }
        }
    };

    op.Responses["404"] = new OpenApiResponse { Description = "Dados não encontrados" };

    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Id inválido. Use 'locacao{Id}', por exemplo: 'locacao1'.")
                }
            }
        }
    };

    return op;
})
.Produces<RentalVm>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status404NotFound, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");


// PUT /locacao/{id}/devolucao
rentals.MapPut("/{id}/devolucao", async Task<IResult> (
    string identifier,
    ReturnDto dto,
    RentDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(identifier))
        return Results.BadRequest(new MessageDto("Parâmetro 'identifier' é obrigatório."));

    int idValue;
    if (identifier.StartsWith("locacao", StringComparison.OrdinalIgnoreCase))
    {
        var numericPart = identifier["locacao".Length..].Trim();
        if (!int.TryParse(numericPart, out idValue))
            return Results.BadRequest(new MessageDto("Error de identificacao"));
    }
    else
    {
        if (!int.TryParse(identifier, out idValue))
            return Results.BadRequest(new MessageDto("Error de parse"));
    }

    var r = await db.Rentals.FindAsync(new object[] { idValue }, ct);
    if (r is null)
        return Results.NotFound(new MessageDto("Locação não encontrada"));

    try
    {
        var pb = r.CalculatePreview(dto.returnDate);
        return Results.Ok(new PreviewVm(
            pb.UsedDays,
            pb.UnusedDays,
            pb.ExtraDays,
            pb.DailyPrice,
            pb.BaseValue,
            pb.Penalty,
            pb.Extras,
            pb.Total
        ));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new MessageDto(ex.Message));
    }
})
.WithSummary("Informar data de devolução e calcular valor")
.WithOpenApi(op =>
{
    op.Responses["200"] = new OpenApiResponse
    {
        Description = "Data de devolução informada com sucesso"
    };


    op.Responses["400"] = new OpenApiResponse
    {
        Description = "Dados inválidos",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["mensagem"] = new OpenApiString("Dados inválidos")
                }
            }
        }
    };

    return op;
})
.Produces<PreviewVm>(StatusCodes.Status200OK, "application/json")
.Produces<MessageDto>(StatusCodes.Status400BadRequest, "application/json");

app.Run();

static CNHType ParseCnhType(string s)
{
    var t = s.Trim().ToUpperInvariant();
    return t switch
    {
        "A" => CNHType.A,
        "B" => CNHType.B,
        "A+B" or "A + B" => CNHType.APlusB,
        _ => throw new ArgumentException("Invalid CNH type")
    };
}

static RentalPlan ParsePlan(int n) => n switch
{
    7 => RentalPlan.Days7,
    15 => RentalPlan.Days15,
    30 => RentalPlan.Days30,
    45 => RentalPlan.Days45,
    50 => RentalPlan.Days50,
    _ => throw new ArgumentException("Invalid plan")
};

static string NormalizeCnpj(string cnpj) => new string((cnpj ?? "").Where(char.IsDigit).ToArray());
