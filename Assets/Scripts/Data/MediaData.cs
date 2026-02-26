/// <summary>
/// 비디오 미디어 메타데이터 구조체
/// </summary>
public struct MediaData
{
    public int Index;
    public string AbsolutePath;
    public bool IsValid;

    public MediaData(int index, string absolutePath, bool isValid)
    {
        Index = index;
        AbsolutePath = absolutePath;
        IsValid = isValid;
    }
}
