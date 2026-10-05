using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MarketMage.Models;

namespace MarketMage.Services;

public sealed class UniversalisService : IDisposable
{
    private readonly HttpClient httpClient;
    public UniversalisService(HttpClient? client = null)
    {
        httpClient = client ?? new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarketMage/0.2");
    }

    public async Task<IReadOnlyList<MarketPriceSnapshot>> GetSnapshotsAsync(
        string scope, IReadOnlyCollection<uint> itemIds, bool hq, bool listings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Select a world or data center.", nameof(scope));
        var result = new List<MarketPriceSnapshot>();
        foreach (var batch in itemIds.Distinct().Chunk(50))
        {
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{string.Join(',', batch)}?listings={(listings ? 100 : 0)}&entries={(listings ? 0 : 20)}&hq={hq.ToString().ToLowerInvariant()}";
            for (var attempt = 0; ; attempt++)
            {
                using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 30)), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                result.AddRange(MarketParser.Parse(json, batch, scope, hq));
                break;
            }
        }
        return result;
    }
    public void Dispose() => httpClient.Dispose();
}
