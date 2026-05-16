using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stocky.Api.Data;

public class StockyDbContextFactory : IDesignTimeDbContextFactory<StockyDbContext>
{
    public StockyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseSqlServer("Server=localhost;Database=stocky;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        return new StockyDbContext(options);
    }
}
