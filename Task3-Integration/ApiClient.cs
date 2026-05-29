using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EcommerceTests.Helpers
{
    /// <summary>
    /// Thin wrapper around the Promotions API. Owns its own HttpClient so the lifetime
    /// is bound to the helper, not shared via DI — fine for test scope, would change for prod.
    /// </summary>
    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _writeOptions;
        private readonly JsonSerializerOptions _readOptions;

        public ApiClient(string baseUrl, string apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL is required", nameof(baseUrl));

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            // Outgoing bodies use camelCase — that's what the API expects on POST.
            _writeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Incoming GET responses are snake_case (DB-backed); JsonPropertyName attributes
            // on PromotionResponse handle the mapping.
            _readOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PromotionResponse> CreatePromotionAsync(object promotionData)
        {
            var response = await _httpClient.PostAsJsonAsync("/admin/promotions", promotionData, _writeOptions);
            await EnsureSuccessOrThrow(response, "create promotion");

            // POST returns a minimal camelCase payload: { promotionId, code, status }.
            // The full record (with discount_type, valid_until, etc.) only comes back from GET,
            // so we only populate what the create response actually gives us.
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return new PromotionResponse
            {
                PromotionId = root.GetProperty("promotionId").GetString(),
                Code = root.GetProperty("code").GetString(),
                Status = root.GetProperty("status").GetString()
            };
        }

        public async Task<PromotionResponse> GetPromotionAsync(string promotionId)
        {
            if (string.IsNullOrWhiteSpace(promotionId))
                throw new ArgumentException("Promotion ID is required", nameof(promotionId));

            var response = await _httpClient.GetAsync($"/admin/promotions/{promotionId}");
            await EnsureSuccessOrThrow(response, $"fetch promotion {promotionId}");

            return await response.Content.ReadFromJsonAsync<PromotionResponse>(_readOptions);
        }

        public async Task DeletePromotionAsync(string promotionId)
        {
            if (string.IsNullOrWhiteSpace(promotionId))
                throw new ArgumentException("Promotion ID is required", nameof(promotionId));

            var response = await _httpClient.DeleteAsync($"/admin/promotions/{promotionId}");

            // A missing promotion at cleanup time is fine — someone (a previous run, a parallel
            // teardown) already deleted it. Don't throw on the test path.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;

            await EnsureSuccessOrThrow(response, $"delete promotion {promotionId}");
        }

        private static async Task EnsureSuccessOrThrow(HttpResponseMessage response, string action)
        {
            if (response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Failed to {action}. Status: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        public void Dispose() => _httpClient?.Dispose();
    }

    public class PromotionResponse
    {
        [JsonPropertyName("promotion_id")]
        public string PromotionId { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("discount_type")]
        public string DiscountType { get; set; }

        [JsonPropertyName("discount_value")]
        public int DiscountValue { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
