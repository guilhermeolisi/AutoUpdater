namespace AutoUpdaterModel;

public static class Connectivity
{
    /// <summary>
    /// Returns true if the given URL responds with a 2xx-3xx status within
    /// the timeout. Unlike ICMP ping, this works through corporate firewalls
    /// and captive portals, and proves the actual update endpoint is reachable
    /// (not just "the internet exists somewhere").
    /// </summary>
    public static bool IsEndpointReachable(string url, int timeoutMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);

            // Some endpoints reject HEAD with 405; fall through to a ranged GET in that case.
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var getResp = client.Send(getReq, HttpCompletionOption.ResponseHeadersRead);
                return (int)getResp.StatusCode >= 200 && (int)getResp.StatusCode < 400;
            }

            return (int)response.StatusCode >= 200 && (int)response.StatusCode < 400;
        }
        catch
        {
            return false;
        }
    }
}
