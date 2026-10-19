using Xunit;

namespace Classify8.Tests
{
    // データファイル(settings.json など)の保存先を差し替えて動くテストは、並列実行しない
    [CollectionDefinition("DataFiles", DisableParallelization = true)]
    public class DataFilesCollection { }

    // UIテストは実際のマウス・キーボードを操作するため、他のテストと並列実行しない
    [CollectionDefinition("UI", DisableParallelization = true)]
    public class UiCollection { }
}
