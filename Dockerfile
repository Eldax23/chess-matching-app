# Build and run the API in a container (Render, Fly.io, a VM, ...).
# Configuration comes from environment variables, e.g.
#   ConnectionStrings__DefaultConnection, Jwt__SecretKey, Storage__Provider
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ChessApp.Backend.csproj ./
RUN dotnet restore ChessApp.Backend.csproj
COPY . .
RUN dotnet publish ChessApp.Backend.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Staging
# Most hosts say which port to listen on through PORT
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet ChessApp.Backend.dll"]
