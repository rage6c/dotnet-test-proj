# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ParquetLoader/ParquetLoader.csproj ParquetLoader/
COPY ParquetLoaderApi/ParquetLoaderApi.csproj ParquetLoaderApi/
RUN dotnet restore ParquetLoaderApi/ParquetLoaderApi.csproj

COPY ParquetLoader/ ParquetLoader/
COPY ParquetLoaderApi/ ParquetLoaderApi/
RUN dotnet publish ParquetLoaderApi/ParquetLoaderApi.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production \
    ParquetLoader__BaseParquetPath=/base-parquet-path \
    LOG_FILE=/logs/dotnet-test-proj.log

EXPOSE 8080
VOLUME ["/logs", "/base-parquet-path"]

COPY --from=build /app/publish .
COPY ParquetLoaderApi/entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh \
    && mkdir -p /logs /base-parquet-path

ENTRYPOINT ["/entrypoint.sh"]
