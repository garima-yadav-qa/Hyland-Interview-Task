using Microsoft.Extensions.Configuration;

namespace EcommerceTests
{
    /// <summary>
    /// Strongly-typed view over appsettings.json. Loaded once per test in [SetUp].
    /// Environment variables override file values, which is convenient for CI overrides
    /// like a per-build database host.
    /// </summary>
    public class TestConfig
    {
        public ApiConfig Api { get; set; } = new();
        public UIConfig UI { get; set; } = new();
        public DatabaseConfig Database { get; set; } = new();
        public TestDataConfig TestData { get; set; } = new();
        public CleanupConfig Cleanup { get; set; } = new();

        public static TestConfig Load()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables();

            var config = new TestConfig();
            builder.Build().GetSection("TestConfiguration").Bind(config);
            return config;
        }

        public class ApiConfig
        {
            public string BaseUrl { get; set; }
            public int Timeout { get; set; }
            public int RetryCount { get; set; }
        }

        public class UIConfig
        {
            public string BaseUrl { get; set; }
            public string Browser { get; set; }
            public bool Headless { get; set; }
            public int ImplicitWait { get; set; }
            public int PageLoadTimeout { get; set; }
            public bool ScreenshotOnFailure { get; set; }
        }

        public class DatabaseConfig
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public string Database { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public int ConnectionTimeout { get; set; }
        }

        public class TestDataConfig
        {
            public decimal ProductPrice { get; set; }
            public int ExpectedDiscountPercentage { get; set; }
            public decimal ExpectedDiscountAmount { get; set; }
            public decimal ExpectedFinalPrice { get; set; }
        }

        public class CleanupConfig
        {
            public bool Enabled { get; set; }
            public bool DeleteTestOrders { get; set; }
            public bool DeleteTestPromotions { get; set; }
        }
    }
}
