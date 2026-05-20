using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stocky.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiSqlUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CREATE USER … FROM EXTERNAL PROVIDER cannot run inside a transaction.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-stocky-prod-api-sql')
                BEGIN
                    CREATE USER [id-stocky-prod-api-sql] FROM EXTERNAL PROVIDER;
                    ALTER ROLE db_datareader ADD MEMBER [id-stocky-prod-api-sql];
                    ALTER ROLE db_datawriter ADD MEMBER [id-stocky-prod-api-sql];
                END
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-stocky-prod-api-sql')
                    DROP USER [id-stocky-prod-api-sql];
                """, suppressTransaction: true);
        }
    }
}
