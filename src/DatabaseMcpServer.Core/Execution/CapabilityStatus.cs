namespace DatabaseMcpServer.Helpers;

internal enum CapabilityStatus { Unsupported, Supported, Partial, ProviderDependent, ProviderDependentUnverified }

internal static class CapabilityStatusNames
{
    public static string Name(this CapabilityStatus status) => status switch
    {
        CapabilityStatus.Unsupported => "unsupported",
        CapabilityStatus.Supported => "supported",
        CapabilityStatus.Partial => "partial",
        CapabilityStatus.ProviderDependent => "provider-dependent",
        CapabilityStatus.ProviderDependentUnverified => "provider-dependent-unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
