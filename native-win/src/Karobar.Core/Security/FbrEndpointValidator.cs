using System;
using System.Net;

namespace Karobar.Core.Security;

public static class FbrEndpointValidator
{
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var clean = host.Trim().Trim('[', ']').TrimEnd('.');
        var pctIdx = clean.IndexOf('%');
        if (pctIdx >= 0)
        {
            clean = clean[..pctIdx];
        }

        if (string.Equals(clean, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(clean, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            return IPAddress.IsLoopback(ip);
        }

        return false;
    }

    public static bool IsAllowedFbrEndpointUrl(
        string? rawUrl,
        string? mode,
        out Uri? validatedUri,
        out string? errorMessage)
    {
        validatedUri = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            errorMessage = "FBR endpoint URL is invalid.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errorMessage = "FBR endpoint URL must not contain embedded credentials.";
            return false;
        }

        var normalizedMode = (mode ?? "digital_invoicing").Trim().ToLowerInvariant();
        if (normalizedMode == "tier1_pos_ims")
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Local IMS endpoint must use http:// or https://.";
                return false;
            }

            if (!IsLoopbackHost(uri.Host))
            {
                errorMessage = "Tier-1 POS IMS endpoint must target localhost / loopback (127.0.0.1) only.";
                return false;
            }

            validatedUri = uri;
            return true;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "FBR cloud endpoint URL must use HTTPS.";
            return false;
        }

        if (uri.HostNameType != UriHostNameType.Dns)
        {
            errorMessage = "FBR cloud endpoint URL must use an official *.fbr.gov.pk domain.";
            return false;
        }

        var host = uri.Host.TrimEnd('.');
        var isOfficialFbrDomain = string.Equals(host, "fbr.gov.pk", StringComparison.OrdinalIgnoreCase) ||
                                  host.EndsWith(".fbr.gov.pk", StringComparison.OrdinalIgnoreCase);
        if (!isOfficialFbrDomain)
        {
            errorMessage = "FBR cloud endpoint URL must belong to the official fbr.gov.pk domain.";
            return false;
        }

        if (uri.Port != 443 && uri.Port != 8244)
        {
            errorMessage = "FBR cloud endpoint URL must use standard HTTPS port 443 or FBR POS port 8244.";
            return false;
        }

        validatedUri = uri;
        return true;
    }
}
