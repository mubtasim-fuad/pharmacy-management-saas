FROM node:24-bookworm-slim AS web-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY backend/Pharmacy.Api.csproj backend/
RUN dotnet restore backend/Pharmacy.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/Pharmacy.Api.csproj -c Release -o /publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api-build /publish ./
COPY --from=web-build /src/frontend/dist/pharmacy/browser ./wwwroot
USER $APP_UID
EXPOSE 10000
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet Pharmacy.Api.dll"]
