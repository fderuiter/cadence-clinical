using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Notifications.Infrastructure;

public sealed class VapidPushService : IVapidPushService
{
    private readonly HttpClient _httpClient;
    private readonly NotificationsDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<VapidPushService> _logger;
    private readonly string _publicKey;

    public VapidPushService(
        HttpClient httpClient,
        NotificationsDbContext dbContext,
        IOptions<VapidOptions> vapidOptions,
        IClock clock,
        ILogger<VapidPushService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;

        _publicKey = GetOrGeneratePublicKey(vapidOptions.Value);
    }

    public string GetPublicKey() => _publicKey;

    public async Task<PushDispatchResult> SendNotificationAsync(
        PushSubscription subscription,
        WebPushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!subscription.IsActive)
        {
            return new PushDispatchResult(false, (int)HttpStatusCode.Gone, "Subscription is inactive", IsExpired: true);
        }

        try
        {
            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Attach VAPID Authorization header
            request.Headers.Add("TTL", "86400");
            request.Headers.Add("Urgency", "high");
            request.Headers.Authorization = new AuthenticationHeaderValue("vapid", $"t=eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJjYWRlbmNlIiwic3ViIjoibWFpbHRvOnN1cHBvcnRAY2FkZW5jZS5jbGluaWNhbCJ9.sig, k={_publicKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            int statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully delivered push notification to subject {SubjectId}", subscription.SubjectId);
                return new PushDispatchResult(true, statusCode);
            }

            if (response.StatusCode == HttpStatusCode.Gone || response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Push endpoint expired or revoked for subject {SubjectId}. Marking inactive.", subscription.SubjectId);
                subscription.IsActive = false;
                subscription.UpdatedAt = _clock.UtcNow;
                subscription.ReasonForChange = "Push endpoint expired or revoked (HTTP 410/404)";

                await _dbContext.SaveChangesAsync(cancellationToken);
                return new PushDispatchResult(false, statusCode, "Push endpoint expired or revoked", IsExpired: true);
            }

            string responseError = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Push dispatch failed with status code {StatusCode}: {Error}", statusCode, responseError);
            return new PushDispatchResult(false, statusCode, responseError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception dispatching push notification to subject {SubjectId}", subscription.SubjectId);
            return new PushDispatchResult(false, 500, ex.Message);
        }
    }

    private static string GetOrGeneratePublicKey(VapidOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicKey))
        {
            return options.PublicKey;
        }

        // Generate a deterministic default VAPID key for dev/test environment
        byte[] dummyPublicKeyBytes = new byte[65];
        dummyPublicKeyBytes[0] = 0x04; // Uncompressed EC point format
        byte[] seed = Encoding.UTF8.GetBytes("CadenceVapidPublicKeySeed2026SecureKey!");
        Array.Copy(seed, 0, dummyPublicKeyBytes, 1, Math.Min(seed.Length, 64));

        return Base64UrlEncode(dummyPublicKeyBytes);
    }

    public static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
