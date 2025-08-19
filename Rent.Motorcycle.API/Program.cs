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
motorcyle.MapPost("", async Task<Results<Created<MotoVm>, BadRequest<object>, Conflict>> (
    MotoCreateDto dto, RentDbContext db, IEventBus bus, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.id) || string.IsNullOrWhiteSpace(dto.model) || string.IsNullOrWhiteSpace(dto.plate))
        return TypedResults.BadRequest(BadRequestMsg());

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
    catch (ArgumentException ex) { return TypedResults.BadRequest(BadRequestMsg(ex.Message)); }
})
.WithSummary("Cadastrar uma nova moto")
.WithDescription("Cria um registro de motocicleta.")
.Produces<MotoVm>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status409Conflict)
.Produces<object>(StatusCodes.Status400BadRequest);

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
.WithDescription("Retorna a lista de motos, com filtro opcional por placa.")
.Produces<IEnumerable<MotoVm>>(StatusCodes.Status200OK);


// PUT /motos/{id}/placa
motorcyle.MapPut("/{id}/placa", async Task<Results<NoContent, NotFound, Conflict, BadRequest<object>>> (
    string id, ChangePlateDto dto, RentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.plate))
        return TypedResults.BadRequest(BadRequestMsg("Campo 'placa' é obrigatório."));

    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    if (m is null) return TypedResults.NotFound();

    var normalized = Motorcycle.NormalizePlate(dto.plate);
    var plateInUse = await db.Motorcycles.AnyAsync(x => x.Plate == normalized && x.Id != id, ct);
    if (plateInUse) return TypedResults.Conflict();

    try
    {
        m.ChangePlate(dto.plate);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
    catch (ArgumentException ex)
    {
        return TypedResults.BadRequest(BadRequestMsg(ex.Message));
    }
})
.WithSummary("Modificar a placa de uma moto")
.WithDescription("Atualiza a placa da moto informada.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)
.Produces<object>(StatusCodes.Status400BadRequest);

// GET /motos/{id}
motorcyle.MapGet("/{id}", async (string id, RentDbContext db, CancellationToken ct) =>
{
    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    return m is null ? Results.NotFound() : Results.Ok(new MotoVm(m.Id, m.Year, m.Model, m.Plate));
})
.WithSummary("Consultar moto por id")
.WithDescription("Retorna os dados de uma moto específica.")
.Produces<MotoVm>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);


// DELETE /motos/{id}
motorcyle.MapDelete("/{id}", async Task<Results<NoContent, NotFound, BadRequest<object>>> (
    string id, RentDbContext db, CancellationToken ct) =>
{
    var m = await db.Motorcycles.FindAsync(new object[] { id }, ct);
    if (m is null) return TypedResults.NotFound();

    if (m.HasRentals)
        return TypedResults.BadRequest(BadRequestMsg("Cannot delete a motorcycle with existing rentals."));

    db.Motorcycles.Remove(m);
    await db.SaveChangesAsync(ct);
    return TypedResults.NoContent();
})
.WithSummary("Remover uma moto")
.WithDescription("Exclui o registro de uma moto.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces<object>(StatusCodes.Status400BadRequest);


// ===== Agrupamento "entregadores" =====
var riders = app.MapGroup("/entregadores").WithTags("entregadores");

// POST /entregadores
riders.MapPost("", async Task<Results<Created<RiderVm>, Conflict, BadRequest<object>>> (
     RiderCreateDto dto, RentDbContext db, CancellationToken ct) =>
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

        return TypedResults.Created($"/entregadores/{rider.Id}",
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
    catch (InvalidOperationException) { return TypedResults.Conflict(); }
    catch (ArgumentException)      { return TypedResults.BadRequest(BadRequestMsg()); }
})
.WithSummary("Cadastrar entregador")
.WithDescription("Cria um entregador (delivery rider).")
.Produces<RiderVm>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status409Conflict)
.Produces<object>(StatusCodes.Status400BadRequest);

// POST /entregadores/{id}/cnh
riders.MapPost("/{id}/cnh", async Task<Results<NoContent, NotFound, BadRequest<object>>> (
    string id,
    CnhUploadBody body,
    IStorageService storage,
    RentDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.imagem_cnh))
        return TypedResults.BadRequest(BadRequestMsg("Campo 'imagem_cnh' é obrigatório."));

    var raw = body.imagem_cnh.Trim();
    var commaIdx = raw.IndexOf(',');
    var payload = commaIdx >= 0 ? raw[(commaIdx + 1)..] : raw;

    byte[] bytes;
    try { bytes = Convert.FromBase64String(payload); }
    catch { return TypedResults.BadRequest(BadRequestMsg("Campo 'imagem_cnh' não é um base64 válido.")); }

    var rider = await db.DeliveryRiders.FindAsync(new object[] { id }, ct);
    if (rider is null) return TypedResults.NotFound();

    await using var ms = new MemoryStream(bytes);
    var fileName = $"cnh_{id}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png"; // padrão .png
    var path = await storage.SaveAsync(ms, fileName, ct);

    rider.UpdateCNHImage(path);
    await db.SaveChangesAsync(ct);

    return TypedResults.NoContent();
})
.WithSummary("Enviar foto da CNH")
.WithDescription("Recebe JSON com 'imagem_cnh' em base64 (pode ser data URL).")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces<object>(StatusCodes.Status400BadRequest);

var rentals = app.MapGroup("/locação").WithTags("locação");

// POST /locacao
rentals.MapPost("", async Task<Results<Created<RentalCreateDto>, BadRequest<object>, Conflict>> (
    RentalCreateDto dto, RentDbContext db, CancellationToken ct) =>
{
    var rider = await db.DeliveryRiders.FindAsync(new object[] { dto.riderId }, ct);
    if (rider is null)
        return TypedResults.BadRequest(BadRequestMsg("Entregador inexistente."));

    var moto = await db.Motorcycles.FindAsync(new object[] { dto.motorcycleId }, ct);
    if (moto is null)
        return TypedResults.BadRequest(BadRequestMsg("Moto inexistente."));

    try
    {
        var plan = ParsePlan(dto.plan);

        var rental = Rental.Create(
            riderId:         dto.riderId,
            motorcycleId:    dto.motorcycleId,
            startDate:       dto.startDate,
            endDate:         dto.endDate,
            expectedEndDate: dto.expectedEndDate,
            plan:            plan
        );

        db.Rentals.Add(rental);
        await db.SaveChangesAsync(ct);

        var vm = new RentalCreateDto(
            riderId:         rental.IdDeliveryRider,
            motorcycleId:    rental.IdMotorcycle,
            startDate:       rental.StartDate,
            endDate:         rental.EndDate,
            expectedEndDate: rental.ExpectedEndDate,
            plan:            (int)rental.Plan
        );

        return TypedResults.Created($"/locacao/{rental.Id}", vm);
    }
    catch (InvalidOperationException) { return TypedResults.Conflict(); }
    catch (ArgumentException ex)      { return TypedResults.BadRequest(BadRequestMsg(ex.Message)); }
})
.WithSummary("Alugar uma moto")
.WithDescription("Cria uma locação para um entregador.")
.Produces<RentalCreateDto>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status409Conflict)
.Produces<object>(StatusCodes.Status400BadRequest);


// GET /locacao/{id}
rentals.MapGet("/{id:int}", async (int id, RentDbContext db, CancellationToken ct) =>
{
    var r = await db.Rentals.FindAsync(new object[] { id }, ct);
    if (r is null) return Results.NotFound();

    var vm = new RentalVm(
        identifier:       r.Identifier,
        dailyPrice:       r.DailyPrice,
        riderId:          r.IdDeliveryRider,
        motorcycleId:     r.IdMotorcycle,
        startDate:        r.StartDate,
        termDate:         r.ExpectedEndDate,
        expectedEndDate:  r.ExpectedEndDate,
        returnDate:       r.ReturnDate
    );

    return Results.Ok(vm);
})
.WithSummary("Consultar locação por id")
.WithDescription("Retorna os dados de uma locação específica.")
.Produces<RentalVm>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// PUT /locacao/{id}/devolucao
rentals.MapPut("/{id:int}/devolucao", async Task<Results<Ok<PreviewVm>, NotFound, BadRequest<object>>> (
    int id, ReturnDto dto, RentDbContext db, CancellationToken ct) =>
{
    var r = await db.Rentals.FindAsync(new object[] { id }, ct);
    if (r is null) return TypedResults.NotFound();

    try
    {
        var pb = r.CalculatePreview(dto.returnDate);
        return TypedResults.Ok(new PreviewVm(
            pb.UsedDays, pb.UnusedDays, pb.ExtraDays,
            pb.DailyPrice, pb.BaseValue, pb.Penalty, pb.Extras, pb.Total
        ));
    }
    catch (ArgumentException ex) { return TypedResults.BadRequest(BadRequestMsg(ex.Message)); }
})
.WithSummary("Informar data de devolução e calcular valor")
.WithDescription("Calcula o preview do valor total da locação ao informar a data de devolução.")
.Produces<PreviewVm>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces<object>(StatusCodes.Status400BadRequest);

app.Run();

static object BadRequestMsg(string msg = "Dados inválidos") => new { message = msg };

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
    7  => RentalPlan.Days7,
    15 => RentalPlan.Days15,
    30 => RentalPlan.Days30,
    45 => RentalPlan.Days45,
    50 => RentalPlan.Days50,
    _  => throw new ArgumentException("Invalid plan")
};

static string NormalizeCnpj(string cnpj) => new string((cnpj ?? "").Where(char.IsDigit).ToArray());
