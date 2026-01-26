# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore caching
COPY *.sln ./
COPY Mep1.Erp.TimesheetWeb/*.csproj Mep1.Erp.TimesheetWeb/
COPY Mep1.Erp.Core/*.csproj Mep1.Erp.Core/
# add other referenced projects if TimesheetWeb depends on them
# COPY Mep1.Erp.Infrastructure/*.csproj Mep1.Erp.Infrastructure/
# COPY Mep1.Erp.Application/*.csproj Mep1.Erp.Application/

RUN dotnet restore Mep1.Erp.TimesheetWeb/Mep1.Erp.TimesheetWeb.csproj

# copy and publish
COPY . ./
RUN dotnet publish Mep1.Erp.TimesheetWeb/Mep1.Erp.TimesheetWeb.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:5292
EXPOSE 5292

ENTRYPOINT ["dotnet", "Mep1.Erp.TimesheetWeb.dll"]