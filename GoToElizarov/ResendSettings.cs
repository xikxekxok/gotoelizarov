namespace GoToElizarov;

public record ResendSettings
{
    public long SourceChannelId { get; set; }
    public long TargetChannelId { get; set; }
    public List<string> SearchRegexes { get; set; }
}