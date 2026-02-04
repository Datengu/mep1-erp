using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mep1.Erp.Infrastructure
{
    public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // We only support Postgres now (staging/prod)
            var cs =
                Environment.GetEnvironmentVariable("ConnectionStrings__ErpDb")
                ?? Environment.GetEnvironmentVariable("MEP1_ERP_DB_CONNECTION");

            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException(
                    "No DB connection string found. Set ConnectionStrings__ErpDb (or MEP1_ERP_DB_CONNECTION).");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(cs);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}