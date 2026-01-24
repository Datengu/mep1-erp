# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore (copy csproj first for caching)
COPY *.sln ./
COPY Mep1.Erp.TimesheetWeb/*.csproj Mep1.Erp.TimesheetWeb/
COPY Mep1.Erp.Core/*.csproj Mep1.Erp.Core/
# If TimesheetWeb references other projects, add them here too, e.g.:
# COPY Mep1.Erp.Application/*.csproj Mep1.Erp.Application/
# COPY Mep1.Erp.Infrastructure/*.csproj Mep1.Erp.Infrastructure/

RUN dotnet restore Mep1.Erp.TimesheetWeb/Mep1.Erp.TimesheetWeb.csproj

# copy everything and publish
COPY . .
RUN dotnet publish Mep1.Erp.TimesheetWeb/Mep1.Erp.TimesheetWeb.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mep1.Erp.TimesheetWeb.dll"]
