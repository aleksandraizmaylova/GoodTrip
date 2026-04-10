// Database/DbConnector.cs
using Npgsql;
using System.Data;

namespace TourismApp.Database
{

    public interface IDbConnector
    {
        Task<NpgsqlConnection> GetConnectionAsync();
        Task<T?> ExecuteScalarAsync<T>(string sql, Dictionary<string, object>? parameters = null);
        Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null);
        Task<List<T>> QueryAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new();
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new();
        Task<T> QuerySingleAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new();
        Task<T> ExecuteInTransactionAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action);
    }

    public class DbConnector : IDbConnector, IDisposable
    {
        private readonly string _connectionString;
        private NpgsqlConnection? _connection;
        private bool _disposed;

        public DbConnector(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<NpgsqlConnection> GetConnectionAsync()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new NpgsqlConnection(_connectionString);
                await _connection.OpenAsync();
            }
            return _connection;
        }

        public async Task<T?> ExecuteScalarAsync<T>(string sql, Dictionary<string, object>? parameters = null)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(sql, connection);
                
                AddParameters(command, parameters);
                
                var result = await command.ExecuteScalarAsync();
                
                if (result == null || result == DBNull.Value)
                    return default;
                    
                return (T)Convert.ChangeType(result, typeof(T));
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка выполнения запроса: {ex.Message}\nSQL: {sql}", ex);
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(sql, connection);
                
                AddParameters(command, parameters);
                
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка выполнения запроса: {ex.Message}\nSQL: {sql}", ex);
            }
        }

        public async Task<List<T>> QueryAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
        {
            var results = new List<T>();
            
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(sql, connection);
                
                AddParameters(command, parameters);
                
                await using var reader = await command.ExecuteReaderAsync();
                var properties = typeof(T).GetProperties();
                
                while (await reader.ReadAsync())
                {
                    var item = MapToObject<T>(reader, properties);
                    results.Add(item);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка выполнения запроса: {ex.Message}\nSQL: {sql}", ex);
            }
            
            return results;
        }


        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
        {
            var results = await QueryAsync<T>(sql, parameters);
            return results.FirstOrDefault();
        }


        public async Task<T> QuerySingleAsync<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
        {
            var results = await QueryAsync<T>(sql, parameters);
            return results.Single();
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                var result = await action(connection, transaction);
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private void AddParameters(NpgsqlCommand command, Dictionary<string, object>? parameters)
        {
            if (parameters == null) return;
            
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }
        }

        private T MapToObject<T>(NpgsqlDataReader reader, System.Reflection.PropertyInfo[] properties) where T : new()
        {
            var item = new T();
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var value = reader.GetValue(i);
                
                if (value == DBNull.Value) continue;
                
                var property = properties.FirstOrDefault(p => 
                    string.Equals(p.Name, columnName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, ToPascalCase(columnName), StringComparison.OrdinalIgnoreCase));
                
                if (property != null && property.CanWrite)
                {
                    try
                    {
                        if (property.PropertyType == typeof(DateTime) && value is DateTime dateTimeValue)
                        {
                            property.SetValue(item, DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc));
                        }
                        else if (property.PropertyType.IsGenericType && 
                                 property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
                            property.SetValue(item, Convert.ChangeType(value, underlyingType!));
                        }
                        else
                        {
                            property.SetValue(item, Convert.ChangeType(value, property.PropertyType));
                        }
                    }
                    catch
                    {
                      
                    }
                }
            }
            
            return item;
        }


        private string ToPascalCase(string snakeCase)
        {
            return string.Concat(snakeCase.Split('_')
                .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}