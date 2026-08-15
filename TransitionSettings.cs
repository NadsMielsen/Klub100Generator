namespace Klub100Generator;

public enum TransitionMode
{
    None,
    SingleFile,
    RandomFolder
}

public class TransitionSettings
{
    public TransitionMode Mode { get; set; } = TransitionMode.None;
    public string? SingleFilePath { get; set; }
    public string? FolderPath { get; set; }
    public bool AddAtStart { get; set; }
    public bool AddAtEnd { get; set; }
}
