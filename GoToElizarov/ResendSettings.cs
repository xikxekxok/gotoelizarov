namespace GoToElizarov;

public record ResendSettings
{
    public string SourceChannel { get; set; }
    public long TargetChannelId { get; set; }
    public List<string> SearchRegexes { get; set; }
}