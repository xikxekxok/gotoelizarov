namespace GoToElizarov;

public record TelegramSettings
{
    public int ApiId { get; set; }
    public string ApiHash { get; set; }
    public string PhoneNumber { get; set; }
    public string SessionFileDirectory { get; set; }
}