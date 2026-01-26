# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# copy csproj first for restore caching
COPY *.sln ./
COPY Mep1.Erp.Api/*.csproj Mep1.Erp.Api/
COPY Mep1.Erp.Core/*.csproj Mep1.Erp.Core/
COPY Mep1.Erp.Infrastructure/*.csproj Mep1.Erp.Infrastructure/
# add any other project folders you reference (Desktop, TimesheetWeb, etc) ONLY if the API depends on them
# COPY Mep1.Erp.Application/*.csproj Mep1.Erp.Application/

RUN dotnet restore Mep1.Erp.Api/Mep1.Erp.Api.csproj

# copy everything and publish
COPY . .
RUN dotnet publish Mep1.Erp.Api/Mep1.Erp.Api.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mep1.Erp.Api.dll"]