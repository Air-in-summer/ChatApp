namespace MultiRoomChatWebApp.Server.Shared.Options;

/// <summary>
/// Cau hinh HTTP security headers, dac biet la CSP cho browser client.
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public bool CspEnabled { get; set; } = true;

    public bool CspReportOnly { get; set; } = true;

    public string[] AdditionalConnectSrc { get; set; } = [];

    public string[] AdditionalImgSrc { get; set; } = [];

    public string[] AdditionalMediaSrc { get; set; } = [];

    public string[] FrameAncestors { get; set; } = ["'none'"];
}
