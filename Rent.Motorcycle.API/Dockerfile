FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Rent.Motorcycle.sln ./
COPY Rent.Motorcycle.API/Rent.Motorcycle.API.csproj Rent.Motorcycle.API/
COPY Rent.Motorcycle.Infra/Rent.Motorcycle.Infra.csproj Rent.Motorcycle.Infra/
COPY Rent.Motorcycle.Domain/Rent.Motorcycle.Domain.csproj Rent.Motorcycle.Domain/

RUN dotnet restore Rent.Motorcycle.sln

COPY . .
RUN dotnet publish Rent.Motorcycle.API/Rent.Motorcycle.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV STORAGE_ROOT=/app/storage
RUN mkdir -p /app/storage
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rent.Motorcycle.API.dll"]
