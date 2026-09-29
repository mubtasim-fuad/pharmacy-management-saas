FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Pharmacy.Api.csproj backend/
RUN dotnet restore backend/Pharmacy.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/Pharmacy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pharmacy.Api.dll"]
