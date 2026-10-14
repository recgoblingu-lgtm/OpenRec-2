namespace OpenRec_2;

public sealed class ServerOptions
{
    public string Name { get; set; } = "OpenRec-2";
    public string Version { get; set; } = "0.1.0";
    public string SupportedRecRoomVersion { get; set; } = "20170626_EA";
    public string CdnBaseUri { get; set; } = "http://localhost:5229/";
    public string CloudRegion { get; set; } = "us";
    public int MaxRequestBodySize { get; set; } = 10485760;
    public bool EnableCors { get; set; } = true;
}
