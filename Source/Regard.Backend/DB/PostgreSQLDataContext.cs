using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Regard.Backend.DB
{
    public class PostgreSQLDataContext : DataContext
    {
        public PostgreSQLDataContext(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(Configuration?.GetConnectionString("Postgres"));
        }
    }

    public class PostgreSQLDataContextFactory : IDesignTimeDbContextFactory<PostgreSQLDataContext>
    {
        public PostgreSQLDataContext CreateDbContext(string[] args)
        {
            var dict = new Dictionary<string, string>
            {
                {
                    "ConnectionStrings:Postgres",
                    "User ID=root;Password=myPassword;Host=localhost;Port=5432;Database=myDataBase;Pooling=true;Min Pool Size=0;Max Pool Size=100;Connection Lifetime=0;"
                }
            };

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();

            return new PostgreSQLDataContext(config);
        }
    }
}
