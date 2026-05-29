using Npgsql;

namespace EcommerceTests.Helpers
{
    /// <summary>
    /// Test-side database access. Opens a single connection lazily and reuses it across queries
    /// within a test. Not thread-safe — each test fixture instance owns its own helper.
    /// </summary>
    public class DatabaseHelper : IDisposable
    {
        private readonly string _connectionString;
        private NpgsqlConnection _connection;

        public DatabaseHelper(string host, int port, string database, string username, string password)
        {
            _connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = database,
                Username = username,
                Password = password,
                Timeout = 10,
                CommandTimeout = 10
            }.ConnectionString;
        }

        public void Connect()
        {
            if (_connection?.State == System.Data.ConnectionState.Open) return;

            _connection = new NpgsqlConnection(_connectionString);
            _connection.Open();
        }

        public void Disconnect()
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }

        public Order GetOrderById(string orderId)
        {
            EnsureConnected();

            using var cmd = new NpgsqlCommand(
                @"SELECT order_id, customer_email, original_amount, discount_amount,
                         final_amount, promotion_code, status, created_at
                  FROM orders
                  WHERE order_id = @orderId",
                _connection);
            cmd.Parameters.AddWithValue("orderId", orderId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new Order
            {
                OrderId = reader.GetString(0),
                CustomerEmail = reader.GetString(1),
                OriginalAmount = reader.GetDecimal(2),
                DiscountAmount = reader.GetDecimal(3),
                FinalAmount = reader.GetDecimal(4),
                PromotionCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            };
        }

        public AuditLog GetAuditLogByOrderId(string orderId)
        {
            EnsureConnected();

            using var cmd = new NpgsqlCommand(
                @"SELECT audit_id, promotion_id, order_id, discount_applied, used_at
                  FROM promotion_audit_log
                  WHERE order_id = @orderId",
                _connection);
            cmd.Parameters.AddWithValue("orderId", orderId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new AuditLog
            {
                AuditId = reader.GetInt32(0),
                PromotionId = reader.GetString(1),
                OrderId = reader.GetString(2),
                DiscountApplied = reader.GetDecimal(3),
                UsedAt = reader.GetDateTime(4)
            };
        }

        public void DeleteOrder(string orderId)
        {
            EnsureConnected();

            // promotion_audit_log has ON DELETE CASCADE for order_id, so this cleans both rows.
            using var cmd = new NpgsqlCommand(
                "DELETE FROM orders WHERE order_id = @orderId",
                _connection);
            cmd.Parameters.AddWithValue("orderId", orderId);
            cmd.ExecuteNonQuery();
        }

        public bool VerifyOrderTotals(string orderId, decimal expectedOriginal,
            decimal expectedDiscount, decimal expectedFinal)
        {
            var order = GetOrderById(orderId);
            if (order == null) return false;

            // One-cent tolerance absorbs any rounding the DB or API might apply without
            // hiding a real bug — a real bug would be a difference of dollars, not cents.
            const decimal tolerance = 0.01m;

            return Math.Abs(order.OriginalAmount - expectedOriginal) <= tolerance
                && Math.Abs(order.DiscountAmount - expectedDiscount) <= tolerance
                && Math.Abs(order.FinalAmount - expectedFinal) <= tolerance;
        }

        private void EnsureConnected()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                Connect();
        }

        public void Dispose() => Disconnect();
    }

    public class Order
    {
        public string OrderId { get; set; }
        public string CustomerEmail { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string PromotionCode { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AuditLog
    {
        public int AuditId { get; set; }
        public string PromotionId { get; set; }
        public string OrderId { get; set; }
        public decimal DiscountApplied { get; set; }
        public DateTime UsedAt { get; set; }
    }
}
