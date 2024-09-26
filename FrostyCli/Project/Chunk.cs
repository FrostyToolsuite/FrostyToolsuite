namespace FrostyCli.Project;

public class Chunk : Asset
{
    public int H32 { get; set; } = 0;
    public int FirstMip { get; set; } = -1;
}