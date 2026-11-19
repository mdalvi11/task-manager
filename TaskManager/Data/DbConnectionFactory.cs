using System.Data;
using Microsoft.Data.SqlClient;

namespace TaskManager.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public sealed class SqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                                                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing in appsettings.json.");
    
    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}